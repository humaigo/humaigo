[McpMethod("Imports the Markdown documents of a folder into the data store, as doc: entries with their description and tags. A file may start with a front matter block that holds a description and tags")]
public string ImportDocuments(
    [System.ComponentModel.Description("Full path of the folder that holds the .md files. Sub-folders become part of the key")] string folder)
{
    if (!System.IO.Directory.Exists(folder))
        return "Folder not found: " + folder;

    var imported = new List<string>();
    var files = System.IO.Directory.GetFiles(folder, "*.md", System.IO.SearchOption.AllDirectories)
        .OrderBy(file => file, StringComparer.Ordinal);

    foreach (var file in files)
    {
        var lines = System.IO.File.ReadAllText(file).Replace("\r\n", "\n").Split('\n');
        var description = "";
        var tags = new List<string>();
        var bodyStart = 0;

        // An optional front matter block: the lines between two lines that hold only ---
        if (lines[0].Trim() == "---")
        {
            var end = Array.FindIndex(lines, 1, line => line.Trim() == "---");
            if (end > 0)
            {
                foreach (var line in lines.Skip(1).Take(end - 1))
                {
                    var colon = line.IndexOf(':');
                    if (colon <= 0) continue;

                    var name = line.Substring(0, colon).Trim();
                    var value = line.Substring(colon + 1).Trim();
                    if (name == "description")
                        description = value;
                    else if (name == "tags")
                        tags = value.Split(',').Select(tag => tag.Trim()).Where(tag => tag.Length > 0).ToList();
                }
                bodyStart = end + 1;
            }
        }

        var body = string.Join("\n", lines.Skip(bodyStart)).TrimStart('\n');

        // Without a description in the front matter, the first heading of the document is used.
        if (description.Length == 0)
            description = lines.Skip(bodyStart).Select(line => line.Trim()).FirstOrDefault(line => line.StartsWith("# "))?.Substring(2) ?? "";

        var relative = System.IO.Path.GetRelativePath(folder, file).Replace('\\', '/');
        var key = "doc:" + relative.Substring(0, relative.Length - ".md".Length);

        // SetData is a base method: a dynamic method can call it directly.
        var result = SetData(key, body, description, tags);
        if (!result.StartsWith("Data stored"))
            return "Stopped at " + key + ": " + result;

        imported.Add(key);
    }

    return "Imported " + imported.Count + " document(s) from " + folder + ": " + string.Join(", ", imported);
}
