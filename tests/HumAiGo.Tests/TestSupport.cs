using System.Diagnostics;
using System.Text;
using System.Text.Json;
using HumAiGo.McpServer.Models;
using HumAiGo.McpServer.Services;

namespace HumAiGo.Tests;

/// <summary>
/// A registry that lives in its own folder under data/ next to the test binaries.
/// Every folder created through this object is deleted when the test ends.
/// </summary>
public sealed class TempRegistry : IDisposable
{
    private readonly List<string> _ids = new();

    public string Id { get; }
    public McpMethodRegistry Registry { get; }

    public TempRegistry()
    {
        Id = NewId();
        _ids.Add(Id);
        Registry = Open(Id);
    }

    public static string NewId() => "t" + Guid.NewGuid().ToString("N")[..12];

    public static string DataFolder(string id) => Path.Combine(AppContext.BaseDirectory, "data", id);

    public static McpMethodRegistry Open(string id) => new(
        AppContext.BaseDirectory,
        new HumAiGoConfig { RegistryId = id },
        new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });

    /// <summary>Another registry id, whose folder is also removed at the end of the test.</summary>
    public string NewOtherId()
    {
        var id = NewId();
        _ids.Add(id);
        return id;
    }

    /// <summary>Calls a method of the registry by name, the way the MCP server does, and returns the result as text.</summary>
    public string Call(string method, params (string Name, object Value)[] args)
    {
        var parameters = args.ToDictionary(a => a.Name, a => a.Value);
        return Registry.Invoke(method, parameters)?.ToString() ?? "";
    }

    public static void DeleteFolder(string id)
    {
        try { Directory.Delete(DataFolder(id), recursive: true); }
        catch (DirectoryNotFoundException) { }
    }

    public void Dispose()
    {
        foreach (var id in _ids) DeleteFolder(id);
    }
}

/// <summary>The outcome of one run of the server executable.</summary>
public sealed record ServerRun(int ExitCode, IReadOnlyList<string> Output, string Error, bool TimedOut)
{
    /// <summary>The JSON-RPC response with the given id.</summary>
    public JsonElement Response(int id)
    {
        foreach (var line in Output)
        {
            using var doc = JsonDocument.Parse(line);
            if (doc.RootElement.TryGetProperty("id", out var idElement)
                && idElement.ValueKind == JsonValueKind.Number
                && idElement.GetInt32() == id)
                return doc.RootElement.Clone();
        }

        throw new InvalidOperationException(
            $"No response with id {id}. Output: {string.Join(" | ", Output)}. Error output: {Error}");
    }

    /// <summary>The text of the first content block of a tools/call response.</summary>
    public string ToolText(int id) =>
        Response(id).GetProperty("result").GetProperty("content")[0].GetProperty("text").GetString() ?? "";
}

/// <summary>Starts the real server executable and talks JSON-RPC to it over standard input and output.</summary>
public static class Server
{
    public static string Rpc(int id, string method, object? parameters = null) =>
        JsonSerializer.Serialize(new Dictionary<string, object?>
        {
            ["jsonrpc"] = "2.0",
            ["id"] = id,
            ["method"] = method,
            ["params"] = parameters ?? new { }
        });

    public static string ToolCall(int id, string name, object? arguments = null) =>
        Rpc(id, "tools/call", new { name, arguments = arguments ?? new { } });

    /// <summary>
    /// Runs the server with the given arguments, sends the requests, closes standard input and waits for it to exit.
    /// </summary>
    public static async Task<ServerRun> RunAsync(string[] args, IEnumerable<string> requests, int timeoutSeconds = 60)
    {
        var dll = Path.Combine(AppContext.BaseDirectory, "humaigo.dll");
        if (!File.Exists(dll))
            throw new FileNotFoundException("The server was not built next to the tests.", dll);

        var startInfo = new ProcessStartInfo("dotnet")
        {
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardInputEncoding = new UTF8Encoding(false),
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
            UseShellExecute = false,
            CreateNoWindow = true,
            WorkingDirectory = AppContext.BaseDirectory
        };
        startInfo.ArgumentList.Add(dll);
        foreach (var arg in args) startInfo.ArgumentList.Add(arg);

        using var process = Process.Start(startInfo)!;
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();

        foreach (var request in requests)
            await process.StandardInput.WriteLineAsync(request);
        process.StandardInput.Close();

        var exited = process.WaitForExit(timeoutSeconds * 1000);
        if (!exited) process.Kill(entireProcessTree: true);

        var lines = (await stdout).Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return new ServerRun(exited ? process.ExitCode : -1, lines, await stderr, !exited);
    }
}
