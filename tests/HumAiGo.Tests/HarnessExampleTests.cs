using System.Text.Json;
using System.Text.RegularExpressions;
using Xunit;

namespace HumAiGo.Tests;

/// <summary>The harness example: Markdown documents, and the tool that imports them as data.</summary>
public class HarnessExampleTests
{
    private static readonly string[] ExpectedKeys =
    {
        "doc:agents/tool-reviewer",
        "doc:howto/describe-a-tool",
        "doc:howto/verify-a-tool",
        "doc:security/tool-rules",
        "doc:skills/add-api-tool"
    };

    private static string Import(TempRegistry t)
    {
        var code = File.ReadAllText(RepoPaths.Examples("harness", "ImportDocuments.cs"));
        Assert.StartsWith("Successfully added ImportDocuments", t.Call("AddMethod", ("methodCode", code)));

        return t.Call("ImportDocuments", ("folder", RepoPaths.Examples("harness", "documents")));
    }

    private static string Body(TempRegistry t, string key)
    {
        using var stored = JsonDocument.Parse(t.Call("GetData", ("key", key)));
        return stored.RootElement.GetProperty("value").GetString()!;
    }

    [Fact]
    public void The_documents_are_imported_with_their_description_and_tags()
    {
        using var t = new TempRegistry();

        Assert.StartsWith("Imported 5 document(s)", Import(t));

        using var listed = JsonDocument.Parse(t.Call("ListAllData", ("prefix", "doc:")));
        var entries = listed.RootElement.EnumerateArray().ToDictionary(entry => entry.GetProperty("key").GetString()!);
        Assert.Equal(ExpectedKeys, entries.Keys.OrderBy(key => key, StringComparer.Ordinal));

        var rules = entries["doc:security/tool-rules"];
        Assert.Equal(new[] { "kind:security" }, rules.GetProperty("tags").EnumerateArray().Select(tag => tag.GetString()));
        Assert.False(string.IsNullOrWhiteSpace(rules.GetProperty("description").GetString()));

        var skill = entries["doc:skills/add-api-tool"];
        Assert.Equal(new[] { "kind:skill", "topic:api" }, skill.GetProperty("tags").EnumerateArray().Select(tag => tag.GetString()));
    }

    [Fact]
    public void An_imported_document_is_readable_without_its_front_matter_and_is_listed_in_the_how_to()
    {
        using var t = new TempRegistry();
        Import(t);

        var body = Body(t, "doc:security/tool-rules");
        Assert.StartsWith("# Rules for tools", body);
        Assert.DoesNotContain("description:", body);

        var howTo = t.Call("GetHowTo");
        Assert.Contains("DOCUMENTS IN THIS REGISTRY (5)", howTo);
        Assert.Contains("- doc:skills/add-api-tool: The procedure to turn a public web API into one or more tools [kind:skill, topic:api]", howTo);
    }

    [Fact]
    public void Importing_twice_updates_the_documents_instead_of_duplicating_them()
    {
        using var t = new TempRegistry();
        Import(t);

        var second = t.Call("ImportDocuments", ("folder", RepoPaths.Examples("harness", "documents")));

        Assert.StartsWith("Imported 5 document(s)", second);
        using var listed = JsonDocument.Parse(t.Call("ListAllData", ("prefix", "doc:")));
        Assert.Equal(5, listed.RootElement.GetArrayLength());
    }

    [Fact]
    public void The_documents_only_refer_to_documents_that_exist()
    {
        using var t = new TempRegistry();
        Import(t);

        foreach (var key in ExpectedKeys)
        {
            foreach (Match reference in Regex.Matches(Body(t, key), @"doc:[a-z0-9/-]+"))
                Assert.Contains(reference.Value, ExpectedKeys);
        }
    }

    [Fact]
    public void A_missing_folder_is_reported()
    {
        using var t = new TempRegistry();
        var code = File.ReadAllText(RepoPaths.Examples("harness", "ImportDocuments.cs"));
        t.Call("AddMethod", ("methodCode", code));

        Assert.Contains("Folder not found", t.Call("ImportDocuments", ("folder", Path.Combine(Path.GetTempPath(), "humaigo-no-such-folder"))));
    }
}
