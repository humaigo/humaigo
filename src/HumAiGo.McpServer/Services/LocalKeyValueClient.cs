using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading;

using HumAiGo.McpServer.Models;

namespace HumAiGo.McpServer.Services;

/// <summary>
/// Local key-value store: one folder per registry, one file per value, one index file per registry.
///
/// Safety rules:
/// - writes happen in place under the registry lock (see WriteFile);
/// - operations on a registry are serialized with a lock file, so two server processes sharing the same
///   data folder cannot lose each other's updates;
/// - an unreadable index is never treated as empty: an error is raised and nothing is modified. The last good
///   copy of the index is kept next to it as __index__.json.bak.
/// </summary>
public class LocalKeyValueClient
{
    private static readonly TimeSpan LockTimeout = TimeSpan.FromSeconds(10);

    private readonly string _basePath;

    public LocalKeyValueClient(string basePath)
    {
        _basePath = basePath;
        if (!Directory.Exists(_basePath))
            Directory.CreateDirectory(_basePath);
    }

    public void Set(string clientId, string key, string value, string description = "", List<string>? tags = null)
    {
        WithLock(clientId, () =>
        {
            var clientDir = GetClientDirectory(clientId);
            Directory.CreateDirectory(clientDir);

            var index = GetIndex(clientId);
            var now = DateTime.UtcNow;
            var existing = index.FirstOrDefault(e => e.Key == key);

            if (existing != null)
            {
                existing.Description = description;
                existing.Timestamp = now;
                existing.Tags = tags ?? new();
                WriteFile(Path.Combine(clientDir, existing.ValueKey), value);
            }
            else
            {
                var valueKey = Guid.NewGuid().ToString();
                WriteFile(Path.Combine(clientDir, valueKey), value);

                index.Add(new IndexEntry
                {
                    Key = key,
                    Description = description,
                    ValueKey = valueKey,
                    Timestamp = now,
                    Tags = tags ?? new()
                });
            }

            SaveIndex(clientId, index);
        });
    }

    public EntryValue? Get(string clientId, string key)
    {
        return WithLock(clientId, () =>
        {
            var index = GetIndex(clientId);
            var entry = index.FirstOrDefault(e => e.Key == key);
            if (entry == null)
                return null;

            var filePath = Path.Combine(GetClientDirectory(clientId), entry.ValueKey);
            if (!File.Exists(filePath))
                return null;

            return new EntryValue
            {
                Value = File.ReadAllText(filePath, Encoding.UTF8),
                Description = entry.Description,
                Timestamp = entry.Timestamp
            };
        });
    }

    public void Delete(string clientId, string key)
    {
        WithLock(clientId, () =>
        {
            var index = GetIndex(clientId);
            var entry = index.FirstOrDefault(e => e.Key == key);
            if (entry == null) return;

            var filePath = Path.Combine(GetClientDirectory(clientId), entry.ValueKey);
            if (File.Exists(filePath))
                File.Delete(filePath);

            index.Remove(entry);
            SaveIndex(clientId, index);
        });
    }

    public List<IndexEntry> ListKeys(string clientId)
    {
        return WithLock(clientId, () => GetIndex(clientId));
    }

    public string GetRegistryIndexPath(string clientId)
    {
        return GetIndexPath(clientId);
    }

    // ----- Internals -----

    private string GetClientDirectory(string clientId)
    {
        // Last line of defense: the registry id becomes a folder name under the data folder, never a path.
        if (string.IsNullOrEmpty(clientId)
            || clientId == "." || clientId == ".."
            || clientId.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0
            || clientId.IndexOfAny(new[] { '/', '\\' }) >= 0)
            throw new ArgumentException($"Invalid registry id '{clientId}'.");

        return Path.Combine(_basePath, clientId);
    }

    private string GetIndexPath(string clientId)
        => Path.Combine(GetClientDirectory(clientId), "__index__.json");

    private List<IndexEntry> GetIndex(string clientId)
    {
        var path = GetIndexPath(clientId);
        if (!File.Exists(path)) return new List<IndexEntry>();

        try
        {
            var json = File.ReadAllText(path, Encoding.UTF8);
            var index = JsonSerializer.Deserialize<List<IndexEntry>>(json)
                        ?? throw new JsonException("the document is empty");

            // An index written by an older version may have no tags.
            foreach (var entry in index)
                entry.Tags ??= new();

            return index;
        }
        catch (Exception ex) when (ex is JsonException || ex is NotSupportedException)
        {
            throw new InvalidDataException(
                $"The index of registry '{clientId}' is unreadable ({path}): {ex.Message}. " +
                $"The last good copy, if any, is {path}.bak. Nothing was modified.", ex);
        }
    }

    private void SaveIndex(string clientId, List<IndexEntry> index)
    {
        var path = GetIndexPath(clientId);
        var json = JsonSerializer.Serialize(index, new JsonSerializerOptions { WriteIndented = true });

        // Keep a recent good copy of the index before overwriting it (at most one copy every 30 seconds, to limit writes).
        var backupPath = path + ".bak";
        if (File.Exists(path) && (!File.Exists(backupPath) || File.GetLastWriteTimeUtc(backupPath) < DateTime.UtcNow.AddSeconds(-30)))
            File.Copy(path, backupPath, overwrite: true);

        WriteFile(path, json);
    }

    // Files are written in place, under the registry lock. A temporary file followed by a rename would be
    // crash-safe, but a burst of create/rename/replace operations looks like ransomware to some anti-ransomware
    // tools, which kill the process (observed on the development machine). Recovery relies on __index__.json.bak.
    private static void WriteFile(string path, string content)
    {
        File.WriteAllText(path, content, new UTF8Encoding(false));
    }

    private void WithLock(string clientId, Action action)
    {
        WithLock<object?>(clientId, () => { action(); return null; });
    }

    private T WithLock<T>(string clientId, Func<T> func)
    {
        // Lock file held open with FileShare.None: the operating system guarantees that only one process
        // (and only one holder) has it at a time, and releases it automatically if the process dies.
        var clientDir = GetClientDirectory(clientId);
        Directory.CreateDirectory(clientDir);
        var lockPath = Path.Combine(clientDir, "__lock__");
        var deadline = DateTime.UtcNow + LockTimeout;

        FileStream handle;
        while (true)
        {
            try
            {
                handle = new FileStream(lockPath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
                break;
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
            {
                if (DateTime.UtcNow >= deadline)
                    throw new IOException($"Registry '{clientId}' is locked by another process (waited {LockTimeout.TotalSeconds:0} s).", ex);

                Thread.Sleep(10);
            }
        }

        try
        {
            return func();
        }
        finally
        {
            handle.Dispose();
        }
    }
}
