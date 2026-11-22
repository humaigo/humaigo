// depends: ImportDocuments
[McpMethod("Applies a toolbox folder to this registry: it adds or updates every tool of its tools folder and imports every document of its docs folder. A tool whose code and embedded copies have not changed is left alone. With dryRun, it only reports what would change")]
public string ApplyToolbox(
    [System.ComponentModel.Description("Full path of the toolbox folder. It holds tools\\*.cs, one tool per file, and docs\\**\\*.md")] string folder,
    [System.ComponentModel.Description("True to report what would change without changing anything")] bool dryRun = false)
{
    var toolsFolder = System.IO.Path.Combine(folder, "tools");
    var docsFolder = System.IO.Path.Combine(folder, "docs");
    if (!System.IO.Directory.Exists(toolsFolder) && !System.IO.Directory.Exists(docsFolder))
        return "Nothing to apply: " + folder + " has neither a tools folder nor a docs folder.";

    // The code without its "// depends:" lines and with normalized line ends, so that two versions can be compared.
    string Normalize(string code) =>
        string.Join("\n", code.Replace("\r\n", "\n").Split('\n').Where(line => !line.TrimStart().StartsWith("// depends:"))).Trim();

    System.Text.Json.JsonElement? Prop(System.Text.Json.JsonElement element, string name)
    {
        if (element.ValueKind != System.Text.Json.JsonValueKind.Object) return null;
        foreach (var property in element.EnumerateObject())
            if (string.Equals(property.Name, name, StringComparison.OrdinalIgnoreCase))
                return property.Value;
        return null;
    }

    // What the registry stores for a tool: its code, its hash, and the hashes of the copies it embeds. Null if it does not exist.
    (string Code, string Hash, Dictionary<string, string> Copies)? Stored(string name)
    {
        var entry = GetData("method:" + name);
        if (entry.StartsWith("No data found") || entry.StartsWith("Error")) return null;
        using var wrapper = System.Text.Json.JsonDocument.Parse(entry);
        var value = Prop(wrapper.RootElement, "value")?.GetString();
        if (value == null) return null;
        using var envelope = System.Text.Json.JsonDocument.Parse(value);
        var copies = new Dictionary<string, string>();
        var dependencies = Prop(envelope.RootElement, "dependencies");
        if (dependencies.HasValue && dependencies.Value.ValueKind == System.Text.Json.JsonValueKind.Object)
            foreach (var copy in dependencies.Value.EnumerateObject())
                copies[copy.Name] = copy.Value.GetString() ?? "";
        return (Prop(envelope.RootElement, "rawCode")?.GetString() ?? "", Prop(envelope.RootElement, "selfHash")?.GetString() ?? "", copies);
    }

    var tools = new List<(string Name, string Code, List<string> Depends, string File)>();
    var report = new List<string>();
    var failed = 0;

    if (System.IO.Directory.Exists(toolsFolder))
    {
        var files = System.IO.Directory.GetFiles(toolsFolder, "*.cs", System.IO.SearchOption.TopDirectoryOnly)
            .OrderBy(file => file, StringComparer.Ordinal);
        foreach (var file in files)
        {
            var code = System.IO.File.ReadAllText(file);

            // The tool is the first public method of the file: its signature starts a line.
            var signature = System.Text.RegularExpressions.Regex.Match(code, @"^[ \t]*public[ \t]+[^\r\n(]*?(\w+)[ \t]*\(", System.Text.RegularExpressions.RegexOptions.Multiline);
            if (!signature.Success)
            {
                report.Add("  ! " + System.IO.Path.GetFileName(file) + ": no public method found");
                failed++;
                continue;
            }

            var name = signature.Groups[1].Value;
            if (tools.Any(tool => tool.Name == name))
            {
                report.Add("  ! " + System.IO.Path.GetFileName(file) + ": a tool named " + name + " is already defined by another file");
                failed++;
                continue;
            }

            var depends = new List<string>();
            foreach (var line in code.Replace("\r\n", "\n").Split('\n'))
                if (line.TrimStart().StartsWith("// depends:"))
                    depends.AddRange(line.Substring(line.IndexOf(':') + 1).Split(',').Select(n => n.Trim()).Where(n => n.Length > 0));

            tools.Add((name, code, depends, file));
        }
    }

    var changed = new HashSet<string>();
    var added = 0;
    var updated = 0;
    var unchanged = 0;
    var pending = tools.ToList();
    var progress = true;

    // A tool is applied after the tools it embeds, so that its copies are the current ones.
    while (pending.Count > 0 && progress)
    {
        progress = false;
        foreach (var tool in pending.ToList())
        {
            if (tool.Depends.Any(d => pending.Any(p => p.Name == d && p.Name != tool.Name)))
                continue;

            pending.Remove(tool);
            progress = true;

            var missing = tool.Depends.Where(d => Stored(d) == null && !changed.Contains(d)).ToList();
            if (missing.Count > 0)
            {
                report.Add("  ! " + tool.Name + ": missing tool(s) to embed: " + string.Join(", ", missing));
                failed++;
                continue;
            }

            var current = Stored(tool.Name);
            string why;
            if (current == null)
                why = "new";
            else if (Normalize(current.Value.Code) != Normalize(tool.Code))
                why = "code changed";
            else if (!tool.Depends.OrderBy(n => n).SequenceEqual(current.Value.Copies.Keys.OrderBy(n => n)))
                why = "embedded tools changed";
            else if (tool.Depends.Any(d => changed.Contains(d) || Stored(d)?.Hash != current.Value.Copies[d]))
                why = "an embedded tool changed";
            else
                why = "";

            if (why == "")
            {
                unchanged++;
                report.Add("  = " + tool.Name + " (unchanged)");
                continue;
            }

            if (!dryRun)
            {
                var result = current == null
                    ? AddMethod(tool.Code, tool.Depends.Count > 0 ? tool.Depends : null)
                    : UpdateMethod(tool.Name, tool.Code, tool.Depends.Count > 0 ? tool.Depends : null);

                // The registry is the judge: the tool must now be stored with this code.
                var after = Stored(tool.Name);
                if (after == null || Normalize(after.Value.Code) != Normalize(tool.Code))
                {
                    report.Add("  ! " + tool.Name + ": " + string.Join(" ", result.Split('\n').Take(3).Select(line => line.Trim())));
                    failed++;
                    continue;
                }
            }

            changed.Add(tool.Name);
            if (current == null)
            {
                added++;
                report.Add("  + " + tool.Name + " (added)");
            }
            else
            {
                updated++;
                report.Add("  ~ " + tool.Name + " (updated: " + why + ")");
            }
        }
    }

    foreach (var tool in pending)
    {
        report.Add("  ! " + tool.Name + ": cannot be ordered, the tools it embeds depend on each other in a cycle");
        failed++;
    }

    string documents;
    if (!System.IO.Directory.Exists(docsFolder))
        documents = "Documents: no docs folder.";
    else if (dryRun)
        documents = "Documents: " + System.IO.Directory.GetFiles(docsFolder, "*.md", System.IO.SearchOption.AllDirectories).Length + " file(s) would be imported.";
    else
        documents = "Documents: " + ImportDocuments(docsFolder);

    return (dryRun ? "Dry run, nothing was changed.\n" : "")
        + "Toolbox " + folder + "\n"
        + "Tools: " + added + " added, " + updated + " updated, " + unchanged + " unchanged, " + failed + " failed\n"
        + string.Join("\n", report) + (report.Count > 0 ? "\n" : "")
        + documents;
}
