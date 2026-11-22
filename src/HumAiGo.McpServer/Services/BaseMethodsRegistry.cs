using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;

using HumAiGo.McpServer.Models;

namespace HumAiGo.McpServer.Services.Methods;

/// <summary>
/// The base methods: the tools every HumAiGo server has. They cannot be changed or removed by the assistant.
/// Every dynamic method derives from this class, so dynamic code can call them too.
/// </summary>
public class BaseMethodsRegistry
{
    private readonly McpMethodRegistry _mcpMethodRegistry;

    public BaseMethodsRegistry(McpMethodRegistry mcpMethodRegistry)
    {
        _mcpMethodRegistry = mcpMethodRegistry;
    }

    #region Help

    [McpMethod("Returns how to use this server: how to add, call, reuse and remove tools, where data goes, and the index of the documents kept in this registry")]
    public string GetHowTo()
    {
        const string instructions = """
            HUMAIGO - HOW TO USE

            HumAiGo lets you add tools to this server by writing C# code. A tool you add is compiled at once, saved,
            and can be called right away. It is still there in the next conversation.

            CREATE A TOOL: AddMethod(methodCode)
            - The code is C# (.NET 8): one public method, preceded by [McpMethod("what it does")].
            - System, System.Collections.Generic and System.Linq are already imported. For anything else use fully qualified names
              (System.Text.StringBuilder, not StringBuilder): no other using directive is added for you.
            - Describe a parameter with [System.ComponentModel.Description("text")] placed before its type.
            - A parameter with a default value is optional for the caller.
            - The method name must be unique in the registry.
            Example:
            [McpMethod("Adds two integers")]
            public int Sum(int a, int b) => a + b;

            EACH TOOL IS COMPILED ON ITS OWN
            A compilation error in one tool never affects another. Code is saved only after it compiled, so a failed
            AddMethod leaves the registry as it was. The error is returned: fix the code and try again.

            CALL A TOOL
            A tool you add can be called at once with CallMethod(methodName, argumentsJson), because clients usually do not
            refresh their tool list during a conversation. GetAvailableMethods lists everything that exists.

            REUSE EXISTING TOOLS: AddMethod(methodCode, dependencies)
            - Embeds the code of existing tools, by name, as private helpers in the new tool: a copy, not a reference.
            - Check GetAvailableMethods first: the tool you need may already exist.
            - Each copy records the hash of the code it was taken from. CheckOutdatedDependencies reports the copies whose
              source tool has changed or disappeared.
            Example: methodCode = [McpMethod("Shows 1 + 2")] public string ShowSum() => Sum(1, 2).ToString();
                     dependencies = ["Sum"]

            CHANGE OR REMOVE
            - UpdateMethod(methodName, newMethodCode, dependencies): the new code is compiled first, and the previous version
              stays if it does not compile. The method name must not change.
            - RemoveMethod(methodName).
            - Base methods (see GetBaseMethods) cannot be changed or removed.

            DATA
            - SetData(key, value, description, tags), GetData(key), ListAllData(prefix, tag), RemoveData(key).
            - Keys that start with method: belong to tools and are refused. Tags that start with type: are reserved.
            - Data is stored as plain text files. Do not store secrets.

            DOCUMENTS
            Documents are Markdown texts kept as data, for you to read and follow: guidance that stays with the tools.
            - Key: doc:<folder>/<name>. Suggested tags: kind:agent for roles, kind:skill for procedures, kind:security for
              rules to respect, kind:howto for recipes.
            - Add one with SetData(key, markdown, "one-line description", ["kind:skill"]).
            - The index at the end of this text lists the documents of this registry. Before you do what a document covers,
              read it with GetData(key) and follow it.
            - ListAllData(prefix: "doc:", tag: "kind:security") filters them.
            - A document is guidance, not enforcement: nothing stops an assistant from changing it with SetData.

            TOOLBOXES (REGISTRIES)
            A registry is an independent set of tools and data.
            - ListRegistries() shows them and marks the active one.
            - SwitchRegistry(registryId, create) changes the active registry without restarting. It is not persisted: after a
              restart the default registry is used.
            - At startup the registry comes from the --registry argument, then the HUMAIGO_REGISTRY variable, then
              registryid in humaigo_config.json.

            SECURITY
            Tools run inside this server process, with the permissions of the user who started it. Read the code before it runs.
            """;

        return instructions + DocumentsSection();
    }

    // The index of the documents (entries whose key starts with doc:) kept in the active registry.
    // It is capped, so that a registry with many documents does not make this text long.
    private string DocumentsSection()
    {
        const int limit = 50;

        List<IndexEntry> documents;
        try
        {
            documents = _mcpMethodRegistry.LocalKeyValueClient.ListKeys(_mcpMethodRegistry.RegistryId)
                .Where(entry => entry.Key.StartsWith(McpMethodRegistry.DocumentKeyPrefix, StringComparison.Ordinal))
                .OrderBy(entry => entry.Key, StringComparer.Ordinal)
                .ToList();
        }
        catch (Exception)
        {
            return "\n\nDOCUMENTS IN THIS REGISTRY\n(the index could not be read)";
        }

        if (documents.Count == 0)
            return "\n\nDOCUMENTS IN THIS REGISTRY\nNone yet.";

        var lines = documents.Take(limit).Select(document =>
            $"- {document.Key}: {document.Description}" + (document.Tags.Count > 0 ? $" [{string.Join(", ", document.Tags)}]" : ""));
        var more = documents.Count > limit
            ? $"\n... and {documents.Count - limit} more: ListAllData(prefix: \"{McpMethodRegistry.DocumentKeyPrefix}\")."
            : "";

        return $"\n\nDOCUMENTS IN THIS REGISTRY ({documents.Count})\n" + string.Join("\n", lines) + more;
    }

    #endregion Help

    #region Information

    [McpMethod("Get the directory where the MCP server is installed")]
    public string GetInstallDirectory() => _mcpMethodRegistry.AssemblyDirectory;

    [McpMethod("Get actual references that could be used for dynamic compilation")]
    public string GetRegistryReferenceMetaData()
    {
        var items = _mcpMethodRegistry.DynamicRegistryMetadataReferences
            .Select(reference => new
            {
                AssemblyName = Path.GetFileNameWithoutExtension(reference.Display),
                Path = reference.Display,
                Exists = File.Exists(reference.Display)
            })
            .OrderBy(item => item.AssemblyName)
            .ToList();

        return JsonSerializer.Serialize(items, new JsonSerializerOptions { WriteIndented = true });
    }

    [McpMethod("List all base methods (tools) that cannot be modified via registry")]
    public string GetBaseMethods()
    {
        var names = string.Join(Environment.NewLine, _mcpMethodRegistry.BaseMethodsName.Select(name => "- " + name));

        return "BASE METHODS (cannot be modified or removed):" + Environment.NewLine + names + Environment.NewLine + Environment.NewLine +
               "Everything else is a dynamic method created with AddMethod. Use GetAvailableMethods to see the current registry.";
    }

    #endregion Information

    #region Methods

    // Adds a C# method to the registry, given by the user or written by the assistant.
    // Each method is compiled in its own file and stored only once it compiled.
    // The dependencies, given by name, are copied in as private helpers, not referenced.
    [McpMethod("Adds a new method to the registry by compiling C# code dynamically, in isolation from all other methods. Optionally embeds existing methods (by name) as private helper copies")]
    public string AddMethod(
        [Description("C# source of one public method, preceded by an McpMethod attribute that carries its description. Use fully qualified type names")] string methodCode,
        [Description("Names of existing methods to embed as private helper copies")] List<string>? dependencies = null)
    {
        try
        {
            // 1. Resolve the requested dependencies, by copy and never by reference.
            var resolved = new List<(string Name, MethodEnvelope Envelope)>();
            foreach (var name in dependencies ?? new List<string>())
            {
                var envelope = _mcpMethodRegistry.GetMethodEnvelope(name);
                if (envelope == null)
                    return $"Dependency '{name}' not found in registry. Use GetAvailableMethods to see what exists.";
                resolved.Add((name, envelope));
            }

            // 2. Generate the self-contained file. Nothing is written to the store at this stage.
            var fileCode = _mcpMethodRegistry.GenerateMethodFileCode(methodCode, resolved);

            // 3. Compile it on its own: no other method can be affected.
            var (assembly, compileError) = _mcpMethodRegistry.CompileCode(fileCode);
            if (assembly == null)
                return string.Join(Environment.NewLine, "Compilation failed :", compileError);

            // 4. Register the method from the compiled assembly.
            var (addedCount, methodName) = _mcpMethodRegistry.AddMethodsToDomainFromAssembly(assembly);
            if (addedCount == 0)
            {
                var existingName = _mcpMethodRegistry.PeekMethodName(assembly);
                if (string.IsNullOrEmpty(existingName))
                    return "Compilation succeeded but no [McpMethod] attribute was found on the method.";
                return $"A method named '{existingName}' already exists in the registry. Use UpdateMethod to change it, or RemoveMethod first.";
            }

            // 5. Store it. This only happens after success, so a failed attempt leaves nothing behind.
            _mcpMethodRegistry.SetMethodContent(methodName, new MethodEnvelope
            {
                RawCode = methodCode,
                FileCode = fileCode,
                SelfHash = _mcpMethodRegistry.ComputeHash(methodCode),
                Dependencies = resolved.ToDictionary(d => d.Name, d => d.Envelope.SelfHash)
            });

            // 6. Tell the client that the tool list changed.
            _mcpMethodRegistry.NotifyToolsListChanged();

            var summary = resolved.Count > 0
                ? $" (embedding {resolved.Count} dependency method(s) as private helper(s): {string.Join(", ", resolved.Select(d => d.Name))})"
                : "";

            return $"Successfully added {methodName} to registry{summary}. Use GetAvailableMethods to see updated list or CallMethod('{methodName}', {{...}}) to execute directly.";
        }
        catch (Exception ex)
        {
            return $"Error: {ex.Message}";
        }
    }

    // The tool list, as text. Clients usually do not refresh tools/list after AddMethod, so the assistant reads it here.
    [McpMethod("Returns the current list of available methods (MCP tools) with their schemas")]
    public string GetAvailableMethods()
    {
        var methods = _mcpMethodRegistry.Methods;
        var lines = methods.Select(kvp => $"- {kvp.Key}: {kvp.Value.GetCustomAttribute<McpMethodAttribute>()?.Description}");

        return $"Available tools ({methods.Count}):\n" + string.Join("\n", lines);
    }

    // Calls any method of the registry. It exists because clients usually do not refresh their tool list
    // during a conversation, so a method added a moment ago is not yet a tool of its own for them.
    [McpMethod("Calls any available registry method dynamically")]
    public string CallMethod(
        [Description("Name of the method to call")] string methodName,
        [Description("JSON object with the arguments, by parameter name. Use an empty object when there are none")] string argumentsJson = "{}")
    {
        try
        {
            if (!_mcpMethodRegistry.Methods.ContainsKey(methodName))
                return $"Method '{methodName}' not found. Available methods: {string.Join(", ", _mcpMethodRegistry.Methods.Keys)}";

            JsonElement arguments;
            try
            {
                arguments = JsonSerializer.Deserialize<JsonElement>(argumentsJson);
            }
            catch (JsonException ex)
            {
                return $"Invalid JSON in argumentsJson: {ex.Message}";
            }

            var result = _mcpMethodRegistry.Invoke(methodName, arguments);

            return result switch
            {
                null => "null",
                string text => text,
                _ => JsonSerializer.Serialize(result, new JsonSerializerOptions
                {
                    WriteIndented = true,
                    PropertyNamingPolicy = JsonNamingPolicy.CamelCase
                })
            };
        }
        catch (ArgumentException ex)
        {
            return $"Parameter error: {ex.Message}";
        }
        catch (TargetInvocationException ex)
        {
            // The method itself threw. Report its own message, not the one of the reflection wrapper.
            return $"Error calling method '{methodName}': {ex.InnerException?.Message ?? ex.Message}";
        }
        catch (Exception ex)
        {
            return $"Error calling method '{methodName}': {ex.Message}";
        }
    }

    // Removes a dynamic method: it leaves memory, and its store entry is deleted. No other method is touched.
    [McpMethod("Removes a dynamic method from the registry")]
    public string RemoveMethod([Description("Name of the dynamic method to remove")] string methodName)
    {
        try
        {
            if (_mcpMethodRegistry.BaseMethodsName.Contains(methodName))
                return $"'{methodName}' is a base method and cannot be removed.";

            if (!_mcpMethodRegistry.Methods.ContainsKey(methodName))
                return $"Method '{methodName}' not found in registry.";

            _mcpMethodRegistry.RemoveMethodFromDomain(methodName);
            _mcpMethodRegistry.DeleteMethodContent(methodName);
            _mcpMethodRegistry.NotifyToolsListChanged();

            return $"Method '{methodName}' successfully removed.";
        }
        catch (Exception ex)
        {
            return $"Error removing method: {ex.Message}";
        }
    }

    // Replaces an existing method. The new version, and its dependencies, is compiled and checked BEFORE anything is
    // replaced. If it does not compile, the previous version stays active.
    [McpMethod("Safely updates an existing dynamic method: compiles the new version (with optional dependencies) in isolation and only replaces it if compilation succeeds")]
    public string UpdateMethod(
        [Description("Name of the existing dynamic method")] string methodName,
        [Description("New C# source of the method. It must keep the same method name")] string newMethodCode,
        [Description("Names of existing methods to embed as private helper copies")] List<string>? dependencies = null)
    {
        try
        {
            if (_mcpMethodRegistry.BaseMethodsName.Contains(methodName))
                return $"'{methodName}' is a base method and cannot be updated.";

            if (!_mcpMethodRegistry.Methods.ContainsKey(methodName))
                return $"Method '{methodName}' not found. Use AddMethod to create it.";

            var resolved = new List<(string Name, MethodEnvelope Envelope)>();
            foreach (var name in dependencies ?? new List<string>())
            {
                var envelope = _mcpMethodRegistry.GetMethodEnvelope(name);
                if (envelope == null)
                    return $"Dependency '{name}' not found in registry.";
                resolved.Add((name, envelope));
            }

            var fileCode = _mcpMethodRegistry.GenerateMethodFileCode(newMethodCode, resolved);

            var (assembly, compileError) = _mcpMethodRegistry.CompileCode(fileCode);
            if (assembly == null)
                return string.Join(Environment.NewLine, "Update failed - compilation error:", compileError, "The previous version remains active.");

            var newName = _mcpMethodRegistry.PeekMethodName(assembly);
            if (string.IsNullOrEmpty(newName))
                return "Update failed: no [McpMethod] attribute found in the new code. The previous version remains active.";

            if (newName != methodName)
                return $"Update rejected: new code defines method '{newName}', but '{methodName}' was expected. Renaming via UpdateMethod is not supported - the previous version remains active.";

            // It compiled and the name matches: replacing is now safe.
            _mcpMethodRegistry.RemoveMethodFromDomain(methodName);
            _mcpMethodRegistry.AddMethodsToDomainFromAssembly(assembly);

            _mcpMethodRegistry.SetMethodContent(methodName, new MethodEnvelope
            {
                RawCode = newMethodCode,
                FileCode = fileCode,
                SelfHash = _mcpMethodRegistry.ComputeHash(newMethodCode),
                Dependencies = resolved.ToDictionary(d => d.Name, d => d.Envelope.SelfHash)
            });
            _mcpMethodRegistry.NotifyToolsListChanged();

            return $"Method '{methodName}' successfully updated.";
        }
        catch (Exception ex)
        {
            return $"Update failed: {ex.Message}";
        }
    }

    // Compiles and loads every dynamic method again from the store. Useful to recover without restarting.
    // A method that cannot be loaded is reported and skipped: it never prevents the others from loading.
    [McpMethod("Force reload of all dynamic methods from the data store, recompiling each one independently")]
    public string ReloadRegistry()
    {
        try
        {
            var (reloadedCount, errors) = _mcpMethodRegistry.ReloadAllMethodsFromStore();
            _mcpMethodRegistry.NotifyToolsListChanged();

            if (errors.Count == 0)
                return $"Registry reloaded successfully. {reloadedCount} method(s) loaded from data store.";

            return $"Registry reloaded with issues. {reloadedCount} method(s) loaded successfully.\nSkipped due to errors:\n" + string.Join("\n", errors);
        }
        catch (Exception ex)
        {
            return $"Reload failed: {ex.Message}";
        }
    }

    // Reports the embedded (copied) dependencies whose source method changed or disappeared.
    [McpMethod("Checks all dynamic methods for embedded dependencies that have since been updated or removed (version drift detection)")]
    public string CheckOutdatedDependencies()
    {
        try
        {
            var issues = _mcpMethodRegistry.CheckOutdatedDependencies();
            if (issues.Count == 0)
                return "All embedded dependencies are up to date.";

            return "Outdated or missing embedded dependencies found:\n" + string.Join("\n", issues);
        }
        catch (Exception ex)
        {
            return $"Error checking dependencies: {ex.Message}";
        }
    }

    #endregion Methods

    #region Registries

    // Lists the registries (toolboxes): the sub-folders of data/. The active one is marked.
    [McpMethod("Lists the registries (toolboxes) of this server with the number of methods and data entries in each. The active registry is marked")]
    public string ListRegistries()
    {
        try
        {
            var active = _mcpMethodRegistry.RegistryId;
            var items = _mcpMethodRegistry.GetRegistryIds().Select(id =>
            {
                var counts = _mcpMethodRegistry.CountEntries(id);
                return new { id, active = id == active, readable = counts.Readable, methods = counts.Methods, data = counts.Data };
            }).ToList();

            return JsonSerializer.Serialize(items, _mcpMethodRegistry.JsonOptions);
        }
        catch (Exception ex)
        {
            return $"Error listing registries: {ex.Message}";
        }
    }

    // Changes the active registry without restarting: the dynamic methods of the previous registry are dropped and
    // those of the new one are compiled and loaded. The choice is not persisted: at the next start the default registry
    // (--registry argument, HUMAIGO_REGISTRY variable or configuration) is used again.
    [McpMethod("Switches the active registry (toolbox) without restarting: dynamic methods of the previous registry are unloaded and those of the new one are loaded. The choice is not persisted. Use create=true to create a registry that does not exist yet")]
    public string SwitchRegistry(
        [Description("Registry id: 1 to 64 lowercase letters, digits, - or _")] string registryId,
        [Description("True to create the registry when it does not exist")] bool create = false)
    {
        try
        {
            if (!McpMethodRegistry.IsValidRegistryId(registryId))
                return "Invalid registry id. Use 1 to 64 lowercase letters, digits, '-' or '_'.";

            if (registryId == _mcpMethodRegistry.RegistryId)
                return $"Registry '{registryId}' is already active.";

            var existing = _mcpMethodRegistry.GetRegistryIds();
            if (!existing.Contains(registryId))
            {
                if (!create)
                    return $"Registry '{registryId}' does not exist. Existing registries: {string.Join(", ", existing)}. Use create=true to create it.";

                _mcpMethodRegistry.CreateRegistry(registryId);
            }

            var previous = _mcpMethodRegistry.RegistryId;
            var (loadedCount, errors) = _mcpMethodRegistry.SwitchRegistry(registryId);
            _mcpMethodRegistry.NotifyToolsListChanged();

            var message = $"Active registry: '{registryId}' (was '{previous}'). {loadedCount} dynamic method(s) loaded.";
            if (errors.Count > 0)
                message += "\nSkipped due to errors:\n" + string.Join("\n", errors);

            return message + "\nNote: the client's native tool list is not refreshed during a conversation; use GetAvailableMethods and CallMethod.";
        }
        catch (Exception ex)
        {
            return $"Error switching registry: {ex.Message}";
        }
    }

    #endregion Registries

    #region Data

    [McpMethod("Retrieves the path of the registry index")]
    public string GetRegistryPath() =>
        _mcpMethodRegistry.LocalKeyValueClient.GetRegistryIndexPath(_mcpMethodRegistry.RegistryId);

    [McpMethod("Retrieves stored value by key")]
    public string GetData([Description("Key of the stored value")] string key)
    {
        try
        {
            var entry = _mcpMethodRegistry.LocalKeyValueClient.Get(_mcpMethodRegistry.RegistryId, key);
            if (entry == null)
                return $"No data found for key: {key}";

            return JsonSerializer.Serialize(entry, _mcpMethodRegistry.JsonOptions);
        }
        catch (Exception ex)
        {
            return $"Error reading data: {ex.Message}";
        }
    }

    [McpMethod("Stores a value for a given key, with a description and optional tags")]
    public string SetData(
        [Description("Key of the value. Keys starting with method: are reserved")] string key,
        [Description("Text value to store")] string value,
        [Description("Optional short description")] string description = "",
        [Description("Optional tags, for example kind:skill. Tags starting with type: are reserved. They replace the tags the entry had")] List<string>? tags = null)
    {
        try
        {
            if (McpMethodRegistry.IsReservedKey(key))
                return $"Key '{key}' is reserved for dynamic methods. Use AddMethod or UpdateMethod to manage methods.";

            var cleanTags = (tags ?? new List<string>())
                .Select(tag => tag?.Trim() ?? "")
                .Where(tag => tag.Length > 0)
                .Distinct()
                .ToList();

            var reserved = cleanTags.FirstOrDefault(McpMethodRegistry.IsReservedTag);
            if (reserved != null)
                return $"Tag '{reserved}' is reserved: tags that start with type: are used by the system.";

            var tooLong = cleanTags.FirstOrDefault(tag => tag.Length > 64);
            if (tooLong != null)
                return "A tag has at most 64 characters.";

            _mcpMethodRegistry.LocalKeyValueClient.Set(_mcpMethodRegistry.RegistryId, key, value, description, cleanTags);

            return $"Data stored successfully for key: {key}";
        }
        catch (Exception ex)
        {
            return $"Error storing data: {ex.Message}";
        }
    }

    [McpMethod("Lists the stored key-value pairs, with their description and tags. Optional filters: a key prefix (for example doc:) and a tag")]
    public string ListAllData(
        [Description("Only the keys that start with this prefix. Empty for all")] string prefix = "",
        [Description("Only the entries that carry this tag. Empty for all")] string tag = "")
    {
        try
        {
            IEnumerable<IndexEntry> entries = _mcpMethodRegistry.LocalKeyValueClient.ListKeys(_mcpMethodRegistry.RegistryId);

            if (!string.IsNullOrEmpty(prefix))
                entries = entries.Where(entry => entry.Key.StartsWith(prefix, StringComparison.Ordinal));
            if (!string.IsNullOrEmpty(tag))
                entries = entries.Where(entry => entry.Tags.Contains(tag));

            return JsonSerializer.Serialize(entries.ToList(), _mcpMethodRegistry.JsonOptions);
        }
        catch (Exception ex)
        {
            return $"Error listing data: {ex.Message}";
        }
    }

    [McpMethod("Removes a stored key-value pair. Keys starting with method: are reserved for dynamic methods (use RemoveMethod)")]
    public string RemoveData([Description("Key to remove. Keys starting with method: are reserved")] string key)
    {
        try
        {
            if (McpMethodRegistry.IsReservedKey(key))
                return $"Key '{key}' is reserved for dynamic methods. Use RemoveMethod to remove a method.";

            var entries = _mcpMethodRegistry.LocalKeyValueClient.ListKeys(_mcpMethodRegistry.RegistryId);
            if (!entries.Any(entry => entry.Key == key))
                return $"No data found for key: {key}";

            _mcpMethodRegistry.LocalKeyValueClient.Delete(_mcpMethodRegistry.RegistryId, key);
            return $"Data removed for key: {key}";
        }
        catch (Exception ex)
        {
            return $"Error removing data: {ex.Message}";
        }
    }

    #endregion Data
}
