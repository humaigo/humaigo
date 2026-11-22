[McpMethod("Writes this registry as a toolbox folder: each tool as tools\\Name.cs, with a '// depends:' line when it embeds other tools, and each document as docs\\...md with its description and tags. Existing files are overwritten. Other data is not exported")]
public string ExportToolbox(
    [System.ComponentModel.Description("Full path of the folder to write. It is created if it does not exist")] string folder)
{
    var root = System.IO.Path.GetFullPath(folder);
    var toolsFolder = System.IO.Path.Combine(root, "tools");
    var docsFolder = System.IO.Path.Combine(root, "docs");
    var skipped = new List<string>();
    var toolCount = 0;
    var documentCount = 0;

    System.Text.Json.JsonElement? Prop(System.Text.Json.JsonElement element, string name)
    {
        if (element.ValueKind != System.Text.Json.JsonValueKind.Object) return null;
        foreach (var property in element.EnumerateObject())
            if (string.Equals(property.Name, name, StringComparison.OrdinalIgnoreCase))
                return property.Value;
        return null;
    }

    string ValueOf(string key)
    {
        var stored = GetData(key);
        if (stored.StartsWith("No data found") || stored.StartsWith("Error")) return null;
        using var wrapper = System.Text.Json.JsonDocument.Parse(stored);
        return Prop(wrapper.RootElement, "value")?.GetString();
    }

    void Write(string path, string text)
    {
        System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path));
        System.IO.File.WriteAllText(path, text, new System.Text.UTF8Encoding(false));
    }

    var methodList = ListAllData("method:");
    if (methodList.StartsWith("Error")) return methodList;
    using (var methods = System.Text.Json.JsonDocument.Parse(methodList))
    {
        foreach (var item in methods.RootElement.EnumerateArray().ToList())
        {
            var key = Prop(item, "key")?.GetString() ?? "";
            var name = key.Substring("method:".Length);
            if (!System.Text.RegularExpressions.Regex.IsMatch(name, @"^[A-Za-z_][A-Za-z0-9_]*$"))
            {
                skipped.Add(key + " (not a valid tool name)");
                continue;
            }

            var value = ValueOf(key);
            if (value == null)
            {
                skipped.Add(key + " (unreadable)");
                continue;
            }

            using var envelope = System.Text.Json.JsonDocument.Parse(value);
            var code = (Prop(envelope.RootElement, "rawCode")?.GetString() ?? "").Replace("\r\n", "\n").TrimEnd();
            var copies = new List<string>();
            var dependencies = Prop(envelope.RootElement, "dependencies");
            if (dependencies.HasValue && dependencies.Value.ValueKind == System.Text.Json.JsonValueKind.Object)
                copies.AddRange(dependencies.Value.EnumerateObject().Select(copy => copy.Name));
            if (copies.Count > 0 && !code.Split('\n').Any(line => line.TrimStart().StartsWith("// depends:")))
                code = "// depends: " + string.Join(", ", copies) + "\n" + code;

            Write(System.IO.Path.Combine(toolsFolder, name + ".cs"), code + "\n");
            toolCount++;
        }
    }

    var documentList = ListAllData("doc:");
    if (!documentList.StartsWith("Error"))
    {
        using var documents = System.Text.Json.JsonDocument.Parse(documentList);
        foreach (var item in documents.RootElement.EnumerateArray().ToList())
        {
            var key = Prop(item, "key")?.GetString() ?? "";
            var relative = key.Substring("doc:".Length);

            // A key must never write outside the docs folder.
            string target;
            try
            {
                target = System.IO.Path.GetFullPath(System.IO.Path.Combine(docsFolder, relative.Replace('/', System.IO.Path.DirectorySeparatorChar) + ".md"));
            }
            catch (Exception)
            {
                skipped.Add(key + " (not a valid file name)");
                continue;
            }
            if (relative.Length == 0 || !target.StartsWith(docsFolder + System.IO.Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            {
                skipped.Add(key + " (the key would write outside the docs folder)");
                continue;
            }

            var body = ValueOf(key);
            if (body == null)
            {
                skipped.Add(key + " (unreadable)");
                continue;
            }

            var description = (Prop(item, "description")?.GetString() ?? "").Replace("\r", " ").Replace("\n", " ").Trim();
            var tags = new List<string>();
            var tagsProperty = Prop(item, "tags");
            if (tagsProperty.HasValue && tagsProperty.Value.ValueKind == System.Text.Json.JsonValueKind.Array)
                tags.AddRange(tagsProperty.Value.EnumerateArray().Select(tag => tag.GetString() ?? "").Where(tag => tag.Length > 0));

            var text = body.Replace("\r\n", "\n").TrimEnd() + "\n";
            if (description.Length > 0 || tags.Count > 0)
            {
                var header = new List<string> { "---" };
                if (description.Length > 0) header.Add("description: " + description);
                if (tags.Count > 0) header.Add("tags: " + string.Join(", ", tags));
                header.Add("---");
                text = string.Join("\n", header) + "\n" + text;
            }

            Write(target, text);
            documentCount++;
        }
    }

    return "Exported " + toolCount + " tool(s) and " + documentCount + " document(s) to " + root
        + (skipped.Count > 0 ? "\nSkipped:\n  " + string.Join("\n  ", skipped) : "");
}
