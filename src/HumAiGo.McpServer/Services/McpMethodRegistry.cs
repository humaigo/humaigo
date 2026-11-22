using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

using HumAiGo.McpServer.Models;
using HumAiGo.McpServer.Services.Methods;

namespace HumAiGo.McpServer.Services;

/// <summary>
/// The methods the server exposes as MCP tools: the fixed base methods, plus the dynamic methods that are compiled
/// from C# source and persisted in the local store, one entry per method.
///
/// A registry (a "toolbox") is a folder under data/ with its own methods and data. One registry is active at a time.
/// </summary>
public class McpMethodRegistry
{
    // Dynamic methods are stored under a key that starts with this prefix, and carry this tag.
    private const string MethodKeyPrefix = "method:";
    private const string MethodTag = "type:method";

    // Documents for the assistant (Markdown guidance, see GetHowTo) are stored under keys that start with this prefix.
    internal const string DocumentKeyPrefix = "doc:";

    private readonly Dictionary<string, MethodInfo> _methods = new();
    private readonly Dictionary<string, object> _instances = new();
    private readonly List<string> _baseMethodNames;
    private readonly LocalKeyValueClient _store;
    private readonly string _dataPath;
    private readonly string _assemblyDirectory;
    private readonly JsonSerializerOptions _jsonOptions;

    private string _registryId;
    private List<MetadataReference>? _references;

    public McpMethodRegistry(string assemblyDirectory, HumAiGoConfig config, JsonSerializerOptions jsonOptions)
    {
        _assemblyDirectory = assemblyDirectory;
        _jsonOptions = jsonOptions;

        _registryId = config.RegistryId;
        if (!IsValidRegistryId(_registryId))
            throw new ArgumentException($"Invalid registry id '{_registryId}': use 1 to 64 lowercase letters, digits, '-' or '_'.");

        // Every method and every piece of data lives in its own file under data/{registry}/.
        _dataPath = Path.Combine(_assemblyDirectory, "data");
        Console.Error.WriteLine($"LOCAL REGISTRY >> path : {_dataPath}");
        _store = new LocalKeyValueClient(_dataPath);
        Directory.CreateDirectory(Path.Combine(_dataPath, _registryId));

        // The base methods are the public [McpMethod] methods of BaseMethodsRegistry.
        var baseMethods = new BaseMethodsRegistry(this);
        foreach (var method in baseMethods.GetType().GetMethods())
        {
            if (method.GetCustomAttribute<McpMethodAttribute>() == null) continue;
            _methods[method.Name] = method;
            _instances[method.Name] = baseMethods;
        }
        _baseMethodNames = _methods.Keys.ToList();
        Console.Error.WriteLine($"REGISTRY BASE METHODS LOADED >> {_baseMethodNames.Count}");

        // Then every dynamic method found in the active registry, each compiled on its own.
        var (loadedCount, errors) = LoadMethodsFromStore();
        foreach (var error in errors)
            Console.Error.WriteLine("REGISTRY WARNING >> " + error);
        Console.Error.WriteLine($"DYNAMIC METHODS LOADED >> {loadedCount}");
    }

    // ----- Tool list -----

    /// <summary>
    /// The tools in the shape MCP expects for tools/list. The parameter schemas are built from the method signatures.
    /// </summary>
    public IEnumerable<object> GetToolsForMcp()
    {
        return _methods.Select(kvp =>
        {
            var attribute = kvp.Value.GetCustomAttribute<McpMethodAttribute>();
            var properties = new Dictionary<string, object>();
            var required = new List<string>();

            foreach (var parameter in kvp.Value.GetParameters())
            {
                var name = parameter.Name ?? "unknown";
                properties[name] = BuildPropertySchema(parameter);

                // Required: no default value, and not a nullable type.
                if (!parameter.IsOptional && !IsNullable(parameter.ParameterType))
                    required.Add(name);
            }

            return new
            {
                name = kvp.Key,
                description = attribute?.Description ?? "No description",
                inputSchema = new
                {
                    type = "object",
                    properties,
                    required = required.ToArray()
                }
            };
        });
    }

    // JSON schema of one parameter: its type, its description (from a Description attribute),
    // the type of the items of an array, and its default value.
    private static Dictionary<string, object> BuildPropertySchema(ParameterInfo parameter)
    {
        var type = parameter.ParameterType;
        var schema = new Dictionary<string, object> { ["type"] = GetJsonSchemaType(type) };

        var description = parameter.GetCustomAttribute<System.ComponentModel.DescriptionAttribute>()?.Description;
        if (!string.IsNullOrWhiteSpace(description))
            schema["description"] = description;

        Type? elementType = type.IsArray
            ? type.GetElementType()
            : (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(List<>)) ? type.GetGenericArguments()[0] : null;
        if (elementType != null)
            schema["items"] = new Dictionary<string, object> { ["type"] = GetJsonSchemaType(elementType) };

        var defaultValue = parameter.DefaultValue;
        if (parameter.HasDefaultValue && defaultValue != null && defaultValue != DBNull.Value
            && (defaultValue is string || defaultValue is bool || defaultValue.GetType().IsPrimitive))
            schema["default"] = defaultValue;

        return schema;
    }

    private static string GetJsonSchemaType(Type type)
    {
        if (IsNullable(type))
            type = Nullable.GetUnderlyingType(type) ?? type;

        return type switch
        {
            Type t when t == typeof(string) => "string",
            Type t when t == typeof(int) || t == typeof(long) || t == typeof(short) || t == typeof(byte) => "integer",
            Type t when t == typeof(double) || t == typeof(float) || t == typeof(decimal) => "number",
            Type t when t == typeof(bool) => "boolean",
            Type t when t.IsArray || (t.IsGenericType && t.GetGenericTypeDefinition() == typeof(List<>)) => "array",
            Type t when t.IsClass => "object",
            _ => "string"
        };
    }

    private static bool IsNullable(Type type) =>
        type.IsGenericType && type.GetGenericTypeDefinition() == typeof(Nullable<>);

    // ----- What the base methods need from the registry -----

    internal Dictionary<string, MethodInfo> Methods => _methods;
    internal LocalKeyValueClient LocalKeyValueClient => _store;
    internal string RegistryId => _registryId;
    internal JsonSerializerOptions JsonOptions => _jsonOptions;
    internal string AssemblyDirectory => _assemblyDirectory;
    internal List<string> BaseMethodsName => _baseMethodNames;

    internal List<MetadataReference> DynamicRegistryMetadataReferences
    {
        get
        {
            EnsureReferences();
            return _references!;
        }
    }

    /// <summary>Tells the client that the tool list changed. Some clients ignore it during a conversation.</summary>
    internal void NotifyToolsListChanged()
    {
        var notification = new
        {
            jsonrpc = "2.0",
            method = "notifications/tools/list_changed",
            @params = new { }
        };

        var json = JsonSerializer.Serialize(notification);

        // Standard output carries the protocol.
        Console.Error.WriteLine("NOTIFICATION >> " + json);
        Console.WriteLine(json);
    }

    // ----- Persistence: one store entry per method, written only after it compiled -----

    /// <summary>The SHA-256 of a text. It identifies one exact version of a method's code.</summary>
    internal string ComputeHash(string text)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(text ?? string.Empty));
        return Convert.ToHexString(bytes);
    }

    /// <summary>
    /// Stores the envelope of a dynamic method. Callers only do this after the method compiled,
    /// so a failed attempt never leaves anything behind.
    /// </summary>
    internal void SetMethodContent(string methodName, MethodEnvelope envelope)
    {
        var json = JsonSerializer.Serialize(envelope);
        _store.Set(_registryId, MethodKeyPrefix + methodName, json, "Dynamic method: " + methodName, new List<string> { MethodTag });
    }

    /// <summary>The stored envelope of a dynamic method, or null when it is missing or unreadable.</summary>
    internal MethodEnvelope? GetMethodEnvelope(string methodName)
    {
        var entry = _store.Get(_registryId, MethodKeyPrefix + methodName);
        if (entry == null || string.IsNullOrWhiteSpace(entry.Value))
            return null;

        try
        {
            return JsonSerializer.Deserialize<MethodEnvelope>(entry.Value);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    internal void DeleteMethodContent(string methodName) =>
        _store.Delete(_registryId, MethodKeyPrefix + methodName);

    // ----- Generating and compiling the C# of a method -----

    // Turns the code of an existing method (an [McpMethod] attribute and a public signature) into a private helper
    // that another method can embed. A plain text replacement on purpose: the generator does no full syntax analysis.
    internal string ConvertToPrivateHelper(string rawCode)
    {
        var lines = rawCode.Split('\n').Where(line => !line.TrimStart().StartsWith("[McpMethod("));
        return new Regex(@"\bpublic\b").Replace(string.Join("\n", lines), "private", 1);
    }

    /// <summary>
    /// The complete C# file of one dynamic method: a minimal class that derives from BaseMethodsRegistry, with the code of
    /// the requested dependencies copied in as private helpers (a copy, not a reference).
    /// Nothing is stored here: storing only happens after the file compiled.
    /// </summary>
    internal string GenerateMethodFileCode(string methodCode, List<(string Name, MethodEnvelope Envelope)>? dependencies = null)
    {
        static string Indent(string code) =>
            string.Join("\n", code.Split('\n').Select(line => string.IsNullOrWhiteSpace(line) ? line : "    " + line));

        var sb = new StringBuilder();
        sb.AppendLine("using HumAiGo.McpServer.Models;");
        sb.AppendLine("using HumAiGo.McpServer.Services;");
        sb.AppendLine("using HumAiGo.McpServer.Services.Methods;");
        sb.AppendLine();
        sb.AppendLine("using System;" + Environment.NewLine + "using System.Collections.Generic;" + Environment.NewLine + "using System.Linq;");
        sb.AppendLine();
        sb.AppendLine("public class DynamicMethod : BaseMethodsRegistry");
        sb.AppendLine("{");
        sb.AppendLine("    public DynamicMethod(McpMethodRegistry mcpMethodRegistry) : base(mcpMethodRegistry) { }");
        sb.AppendLine();

        if (dependencies != null)
        {
            foreach (var dependency in dependencies)
            {
                sb.AppendLine($"    // Embedded dependency: {dependency.Name} (hash at the time of the copy: {dependency.Envelope.SelfHash})");
                sb.AppendLine(Indent(ConvertToPrivateHelper(dependency.Envelope.RawCode)));
                sb.AppendLine();
            }
        }

        sb.AppendLine(Indent(methodCode));
        sb.AppendLine("}");

        return sb.ToString();
    }

    // The compilation references are the same for every method, so they are built once per process.
    private void EnsureReferences()
    {
        if (_references != null) return;

        var references = new List<MetadataReference>();
        var added = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var skippedNative = 0;
        var failed = 0;

        void AddReference(string path)
        {
            if (added.Add(path))
                references.Add(MetadataReference.CreateFromFile(path));
        }

        // Assemblies that dynamic code needs even when the server has not loaded them yet.
        foreach (var type in new[] { typeof(System.Net.Http.HttpClient), typeof(System.Diagnostics.Process), typeof(Exception) })
            AddReference(type.Assembly.Location);

        // Assemblies the server has already loaded.
        foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies().Where(a => !a.IsDynamic && !string.IsNullOrWhiteSpace(a.Location)))
        {
            try { AddReference(assembly.Location); }
            catch (Exception) { failed++; }
        }

        // The other .NET libraries next to the executable. Native libraries are skipped.
        foreach (var dllPath in Directory.EnumerateFiles(_assemblyDirectory, "*.dll"))
        {
            try
            {
                if (added.Contains(dllPath)) continue;
                AssemblyName.GetAssemblyName(dllPath);
                AddReference(dllPath);
            }
            catch (BadImageFormatException) { skippedNative++; }
            catch (Exception) { failed++; }
        }

        _references = references;
        Console.Error.WriteLine($"COMPILATION REFERENCES >> {references.Count} assemblies ({skippedNative} native skipped, {failed} failed)");
    }

    /// <summary>
    /// Compiles one method file in memory. Returns the assembly, or null and the compiler errors.
    /// </summary>
    internal (Assembly? Assembly, string Error) CompileCode(string code)
    {
        EnsureReferences();

        var compilation = CSharpCompilation.Create(
            "DynamicMethods",
            new[] { CSharpSyntaxTree.ParseText(code) },
            _references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        using var stream = new MemoryStream();
        var result = compilation.Emit(stream);

        if (!result.Success)
        {
            var errors = result.Diagnostics.Where(d => d.IsWarningAsError || d.Severity == DiagnosticSeverity.Error);
            var message = string.Join(Environment.NewLine, errors.Select(d => d.ToString()));
            Console.Error.WriteLine("COMPILATION ERROR >> " + message);
            return (null, message);
        }

        return (Assembly.Load(stream.ToArray()), string.Empty);
    }

    // A dynamic method is compiled into its own assembly, which holds one class deriving from BaseMethodsRegistry.
    // The class is found through its base type rather than its name, so methods stored under an older name still load.
    private static Type? FindDynamicType(Assembly assembly) =>
        assembly.GetTypes().FirstOrDefault(t => t.IsClass && typeof(BaseMethodsRegistry).IsAssignableFrom(t));

    private static Dictionary<string, MethodInfo> ExtractMethodsFromType(Type? type)
    {
        var methods = new Dictionary<string, MethodInfo>();
        if (type == null) return methods;

        var tools = type.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static)
            .Where(m => m.GetCustomAttribute<McpMethodAttribute>() != null);

        foreach (var method in tools)
            methods[method.Name] = method;

        return methods;
    }

    // ----- Adding, removing and reloading methods in memory -----

    /// <summary>
    /// Registers the [McpMethod] method of a compiled assembly. Returns how many were added, and their name when it is
    /// exactly one. A name that already exists is left alone.
    /// </summary>
    internal (int AddedCount, string MethodName) AddMethodsToDomainFromAssembly(Assembly assembly)
    {
        var type = FindDynamicType(assembly);
        if (type == null) return (0, string.Empty);

        var instance = Activator.CreateInstance(type, new object[] { this })
                       ?? throw new InvalidOperationException($"Could not create an instance of {type.Name}.");

        var addedCount = 0;
        var lastAdded = string.Empty;
        foreach (var method in ExtractMethodsFromType(type))
        {
            if (_methods.ContainsKey(method.Key)) continue;

            _methods[method.Key] = method.Value;
            _instances[method.Key] = instance;
            addedCount++;
            lastAdded = method.Key;
            Console.Error.WriteLine($"ADDING REGISTRY METHOD >>> {method.Key}");
        }

        return addedCount == 1 ? (1, lastAdded) : (addedCount, string.Empty);
    }

    /// <summary>Forgets a dynamic method in memory. The store is not touched.</summary>
    internal void RemoveMethodFromDomain(string methodName)
    {
        _methods.Remove(methodName);
        _instances.Remove(methodName);
    }

    /// <summary>
    /// The name of the [McpMethod] method in a compiled assembly, without registering it. A file holds one public tool;
    /// embedded dependencies are private and ignored.
    /// </summary>
    internal string PeekMethodName(Assembly assembly) =>
        ExtractMethodsFromType(FindDynamicType(assembly)).Keys.FirstOrDefault() ?? string.Empty;

    /// <summary>Forgets every dynamic method, then compiles and loads them again from the store.</summary>
    internal (int LoadedCount, List<string> Errors) ReloadAllMethodsFromStore()
    {
        foreach (var name in _methods.Keys.Where(k => !_baseMethodNames.Contains(k)).ToList())
            RemoveMethodFromDomain(name);

        return LoadMethodsFromStore();
    }

    // Loads the dynamic methods of the active registry. A method that cannot be loaded is reported and skipped,
    // it never prevents the others from loading.
    private (int LoadedCount, List<string> Errors) LoadMethodsFromStore()
    {
        var errors = new List<string>();
        var loadedCount = 0;

        foreach (var entry in _store.ListKeys(_registryId).Where(e => e.Tags.Contains(MethodTag)))
        {
            var stored = _store.Get(_registryId, entry.Key);
            if (stored == null || string.IsNullOrWhiteSpace(stored.Value))
            {
                errors.Add($"'{entry.Key}': empty content, skipped.");
                continue;
            }

            MethodEnvelope? envelope;
            try
            {
                envelope = JsonSerializer.Deserialize<MethodEnvelope>(stored.Value);
            }
            catch (JsonException ex)
            {
                errors.Add($"'{entry.Key}': invalid stored envelope - {ex.Message}");
                continue;
            }

            if (envelope == null || string.IsNullOrWhiteSpace(envelope.FileCode))
            {
                errors.Add($"'{entry.Key}': empty file code, skipped.");
                continue;
            }

            var (assembly, error) = CompileCode(envelope.FileCode);
            if (assembly == null)
            {
                errors.Add($"'{entry.Key}': compilation failed - {error}");
                continue;
            }

            AddMethodsToDomainFromAssembly(assembly);
            loadedCount++;
        }

        return (loadedCount, errors);
    }

    // ----- Dependencies embedded as copies -----

    /// <summary>
    /// Reports the embedded dependencies whose source method changed since the copy (its hash is different)
    /// or no longer exists.
    /// </summary>
    internal List<string> CheckOutdatedDependencies()
    {
        var issues = new List<string>();

        foreach (var entry in _store.ListKeys(_registryId).Where(e => e.Tags.Contains(MethodTag)))
        {
            if (!entry.Key.StartsWith(MethodKeyPrefix)) continue;
            var methodName = entry.Key.Substring(MethodKeyPrefix.Length);

            var envelope = GetMethodEnvelope(methodName);
            if (envelope == null) continue;

            foreach (var dependency in envelope.Dependencies)
            {
                var current = GetMethodEnvelope(dependency.Key);
                if (current == null)
                    issues.Add($"'{methodName}' embeds '{dependency.Key}', which no longer exists in the registry.");
                else if (current.SelfHash != dependency.Value)
                    issues.Add($"'{methodName}' embeds an outdated copy of '{dependency.Key}' (it has been updated since the copy was made). " +
                               $"Consider UpdateMethod('{methodName}', ..., dependencies:['{dependency.Key}']) to refresh it.");
            }
        }

        return issues;
    }

    // ----- Registries (toolboxes) -----

    // A registry id becomes a folder name under data/, so only safe characters are accepted:
    // lowercase letters, digits, '-' and '_'. Never a path separator or an absolute path.
    internal static bool IsValidRegistryId(string? id) =>
        !string.IsNullOrEmpty(id) && Regex.IsMatch(id, "^[a-z0-9_-]{1,64}$");

    // Keys that start with the method prefix belong to dynamic methods. They are only managed through
    // AddMethod, UpdateMethod and RemoveMethod, never through the data methods.
    internal static bool IsReservedKey(string? key) =>
        key != null && key.StartsWith(MethodKeyPrefix, StringComparison.Ordinal);

    // Tags that start with type: belong to the system (type:method marks a dynamic method). A data entry cannot carry
    // them, otherwise a document could be taken for a method when the registry is loaded.
    internal static bool IsReservedTag(string? tag) =>
        tag != null && tag.StartsWith("type:", StringComparison.Ordinal);

    // The registries are the sub-folders of data/. The file system is the only source of truth.
    internal List<string> GetRegistryIds()
    {
        if (!Directory.Exists(_dataPath)) return new List<string>();

        return Directory.GetDirectories(_dataPath)
            .Select(directory => Path.GetFileName(directory))
            .Where(name => IsValidRegistryId(name))
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToList();
    }

    internal void CreateRegistry(string id) =>
        Directory.CreateDirectory(Path.Combine(_dataPath, id));

    /// <summary>
    /// How many methods and how many data entries a registry holds. Readable is false when its index cannot be read.
    /// </summary>
    internal (int Methods, int Data, bool Readable) CountEntries(string id)
    {
        try
        {
            var entries = _store.ListKeys(id);
            var methods = entries.Count(e => e.Tags.Contains(MethodTag));
            return (methods, entries.Count - methods, true);
        }
        catch (Exception)
        {
            // An unreadable index is reported, and does not prevent listing the other registries.
            return (0, 0, false);
        }
    }

    /// <summary>
    /// Makes another registry the active one: the dynamic methods of the previous registry are dropped and those of
    /// the new one are compiled and loaded.
    /// </summary>
    internal (int LoadedCount, List<string> Errors) SwitchRegistry(string id)
    {
        // Read the new index first. If it is unreadable this throws, and nothing has changed yet.
        _store.ListKeys(id);

        _registryId = id;
        return ReloadAllMethodsFromStore();
    }

    // ----- Calling methods -----

    public bool HasMethod(string methodName) => _methods.ContainsKey(methodName);

    /// <summary>
    /// Calls a method with arguments given as a JSON object. Throws ArgumentException when an argument is missing
    /// or cannot be converted, and TargetInvocationException when the method itself throws.
    /// </summary>
    public object? Invoke(string methodName, JsonElement argumentsElement)
    {
        var arguments = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(argumentsElement.GetRawText())
                        ?? new Dictionary<string, JsonElement>();

        return Invoke(methodName, arguments.ToDictionary(pair => pair.Key, pair => (object)pair.Value));
    }

    /// <summary>
    /// Calls a method with named arguments. A parameter that has a default value in C# may be left out.
    /// </summary>
    public object? Invoke(string methodName, Dictionary<string, object> arguments)
    {
        if (!_methods.TryGetValue(methodName, out var method)) return null;
        var instance = _instances[methodName];

        var values = method.GetParameters().Select(parameter =>
        {
            if (!arguments.TryGetValue(parameter.Name!, out var raw))
            {
                if (parameter.HasDefaultValue)
                    return parameter.DefaultValue;
                throw new ArgumentException($"Missing parameter: {parameter.Name}");
            }

            try
            {
                return raw is JsonElement json
                    ? JsonSerializer.Deserialize(json.GetRawText(), parameter.ParameterType)
                    : Convert.ChangeType(raw, parameter.ParameterType);
            }
            catch (Exception ex)
            {
                throw new ArgumentException($"Cannot convert parameter '{parameter.Name}' to type {parameter.ParameterType.Name}: {ex.Message}");
            }
        }).ToArray();

        return method.Invoke(instance, values);
    }
}
