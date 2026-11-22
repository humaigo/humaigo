using System.Text.Json;
using Xunit;

namespace HumAiGo.Tests;

/// <summary>The toolbox example: a registry described by a folder of files, applied and exported.</summary>
public class ToolboxExampleTests
{
    private static string Sample => RepoPaths.Examples("toolbox", "sample");

    private static string Code(params string[] path) => File.ReadAllText(RepoPaths.Examples(path));

    private static JsonElement Names(params string[] names) => JsonSerializer.SerializeToElement(names);

    private static JsonElement Yes => JsonSerializer.SerializeToElement(true);

    private static TempRegistry Prepared()
    {
        var t = new TempRegistry();
        Assert.StartsWith("Successfully added ImportDocuments", t.Call("AddMethod", ("methodCode", Code("harness", "ImportDocuments.cs"))));
        Assert.StartsWith("Successfully added ApplyToolbox", t.Call("AddMethod", ("methodCode", Code("toolbox", "ApplyToolbox.cs")), ("dependencies", Names("ImportDocuments"))));
        Assert.StartsWith("Successfully added ExportToolbox", t.Call("AddMethod", ("methodCode", Code("toolbox", "ExportToolbox.cs"))));
        return t;
    }

    private sealed class Folder : IDisposable
    {
        public string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "humaigo-toolbox-" + Guid.NewGuid().ToString("N"));

        public Folder() => Directory.CreateDirectory(Path);

        public void Dispose()
        {
            try { Directory.Delete(Path, true); } catch (IOException) { }
        }
    }

    private static void Copy(string from, string to)
    {
        foreach (var file in Directory.GetFiles(from, "*", SearchOption.AllDirectories))
        {
            var target = System.IO.Path.Combine(to, System.IO.Path.GetRelativePath(from, file));
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(target)!);
            File.Copy(file, target, true);
        }
    }

    private static void WriteTool(string folder, string name, string code)
    {
        Directory.CreateDirectory(System.IO.Path.Combine(folder, "tools"));
        File.WriteAllText(System.IO.Path.Combine(folder, "tools", name + ".cs"), code);
    }

    [Fact]
    public void The_sample_toolbox_adds_its_tools_and_its_document()
    {
        using var t = Prepared();

        var report = t.Call("ApplyToolbox", ("folder", Sample));

        Assert.Contains("Tools: 2 added, 0 updated, 0 unchanged, 0 failed", report);
        Assert.Contains("Documents: Imported 1 document(s)", report);
        Assert.Equal("HELLO, ADA!", t.Call("Shout", ("name", "ada")));
        Assert.Contains("doc:howto/toolbox-conventions", t.Call("GetHowTo"));
    }

    [Fact]
    public void Applying_a_toolbox_again_changes_nothing()
    {
        using var t = Prepared();
        t.Call("ApplyToolbox", ("folder", Sample));

        var report = t.Call("ApplyToolbox", ("folder", Sample));

        Assert.Contains("Tools: 0 added, 0 updated, 2 unchanged, 0 failed", report);
    }

    [Fact]
    public void A_changed_tool_also_updates_the_tools_that_embed_it()
    {
        using var t = Prepared();
        using var folder = new Folder();
        Copy(Sample, folder.Path);
        t.Call("ApplyToolbox", ("folder", folder.Path));

        var greet = System.IO.Path.Combine(folder.Path, "tools", "Greet.cs");
        File.WriteAllText(greet, File.ReadAllText(greet).Replace("Hello, ", "Hi, "));
        var report = t.Call("ApplyToolbox", ("folder", folder.Path));

        Assert.Contains("Tools: 0 added, 2 updated, 0 unchanged, 0 failed", report);
        Assert.Contains("~ Greet (updated: code changed)", report);
        Assert.Contains("~ Shout (updated: an embedded tool changed)", report);
        Assert.Equal("HI, ADA!", t.Call("Shout", ("name", "ada")));
        Assert.Equal("Hi, ada!", t.Call("Greet", ("name", "ada")));
    }

    [Fact]
    public void A_dry_run_reports_without_changing_anything()
    {
        using var t = Prepared();

        var report = t.Call("ApplyToolbox", ("folder", Sample), ("dryRun", Yes));

        Assert.Contains("Dry run, nothing was changed.", report);
        Assert.Contains("Tools: 2 added, 0 updated, 0 unchanged, 0 failed", report);
        Assert.Contains("No data found", t.Call("GetData", ("key", "method:Greet")));
        Assert.Contains("No data found", t.Call("GetData", ("key", "doc:howto/toolbox-conventions")));
    }

    [Fact]
    public void A_tool_that_embeds_a_missing_tool_is_reported_and_not_added()
    {
        using var t = Prepared();
        using var folder = new Folder();
        WriteTool(folder.Path, "Lonely", "// depends: Nope\n[McpMethod(\"Needs a tool that does not exist\")]\npublic string Lonely() => Nope();\n");

        var report = t.Call("ApplyToolbox", ("folder", folder.Path));

        Assert.Contains("1 failed", report);
        Assert.Contains("missing tool(s) to embed: Nope", report);
        Assert.Contains("No data found", t.Call("GetData", ("key", "method:Lonely")));
    }

    [Fact]
    public void Tools_that_embed_each_other_are_reported_as_a_cycle()
    {
        using var t = Prepared();
        using var folder = new Folder();
        WriteTool(folder.Path, "Ping", "// depends: Pong\n[McpMethod(\"Calls Pong\")]\npublic string Ping() => Pong();\n");
        WriteTool(folder.Path, "Pong", "// depends: Ping\n[McpMethod(\"Calls Ping\")]\npublic string Pong() => Ping();\n");

        var report = t.Call("ApplyToolbox", ("folder", folder.Path));

        Assert.Contains("0 added, 0 updated, 0 unchanged, 2 failed", report);
        Assert.Contains("cycle", report);
    }

    [Fact]
    public void A_registry_exported_to_a_folder_gives_the_same_registry_when_applied_elsewhere()
    {
        using var source = Prepared();
        source.Call("ApplyToolbox", ("folder", Sample));
        using var exported = new Folder();

        var export = source.Call("ExportToolbox", ("folder", exported.Path));

        Assert.Contains("Exported 5 tool(s) and 1 document(s)", export);
        var shout = File.ReadAllText(System.IO.Path.Combine(exported.Path, "tools", "Shout.cs"));
        Assert.StartsWith("// depends: Greet", shout);
        var document = File.ReadAllText(System.IO.Path.Combine(exported.Path, "docs", "howto", "toolbox-conventions.md"));
        Assert.Contains("tags: kind:howto, topic:toolbox", document);

        using var target = Prepared();
        var report = target.Call("ApplyToolbox", ("folder", exported.Path));

        Assert.Contains("Tools: 2 added, 0 updated, 3 unchanged, 0 failed", report);
        Assert.Contains("Documents: Imported 1 document(s)", report);
        Assert.Equal("HELLO, ADA!", target.Call("Shout", ("name", "ada")));
        Assert.Contains("Tools: 0 added, 0 updated, 5 unchanged, 0 failed", target.Call("ApplyToolbox", ("folder", exported.Path)));
    }

    [Fact]
    public void A_document_key_cannot_make_the_export_write_outside_its_folder()
    {
        using var t = Prepared();
        t.Call("SetData", ("key", "doc:../evil"), ("value", "x"), ("description", "Tries to escape"));
        using var parent = new Folder();
        var inner = System.IO.Path.Combine(parent.Path, "inner");

        var export = t.Call("ExportToolbox", ("folder", inner));

        Assert.Contains("the key would write outside the docs folder", export);
        Assert.False(File.Exists(System.IO.Path.Combine(parent.Path, "inner", "evil.md")));
        Assert.False(File.Exists(System.IO.Path.Combine(parent.Path, "evil.md")));
    }
}
