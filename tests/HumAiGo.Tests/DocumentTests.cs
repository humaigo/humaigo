using System.Text.Json;
using Xunit;

namespace HumAiGo.Tests;

/// <summary>
/// Documents: Markdown guidance kept as data under keys that start with doc:, found through tags,
/// filters, and the index at the end of GetHowTo.
/// </summary>
public class DocumentTests
{
    private static JsonElement Tags(params string[] tags) => JsonSerializer.SerializeToElement(tags);

    private static string Set(TempRegistry t, string key, string description, params string[] tags) =>
        t.Call("SetData", ("key", key), ("value", "# " + key), ("description", description), ("tags", Tags(tags)));

    private static List<string> Keys(string json)
    {
        using var document = JsonDocument.Parse(json);
        return document.RootElement.EnumerateArray().Select(entry => entry.GetProperty("key").GetString()!).ToList();
    }

    [Fact]
    public void Tags_are_stored_with_the_entry_and_can_be_listed()
    {
        using var t = new TempRegistry();

        Assert.StartsWith("Data stored", Set(t, "doc:skills/a", "Skill A", "kind:skill", "topic:x"));

        using var listed = JsonDocument.Parse(t.Call("ListAllData", ("tag", "kind:skill")));
        var entry = Assert.Single(listed.RootElement.EnumerateArray());
        Assert.Equal("doc:skills/a", entry.GetProperty("key").GetString());
        Assert.Equal(new[] { "kind:skill", "topic:x" }, entry.GetProperty("tags").EnumerateArray().Select(tag => tag.GetString()));
    }

    [Fact]
    public void Tags_that_belong_to_the_system_are_refused_and_nothing_is_stored()
    {
        using var t = new TempRegistry();

        var result = Set(t, "doc:trick", "Looks like a method", "kind:skill", "type:method");

        Assert.Contains("reserved", result);
        Assert.Contains("No data found", t.Call("GetData", ("key", "doc:trick")));
    }

    [Fact]
    public void An_overlong_tag_is_refused()
    {
        using var t = new TempRegistry();

        Assert.Contains("at most 64", Set(t, "doc:long", "Too long", new string('a', 65)));
    }

    [Fact]
    public void Writing_an_entry_again_replaces_its_tags()
    {
        using var t = new TempRegistry();
        Set(t, "doc:a", "First", "kind:skill");

        Set(t, "doc:a", "Second", "kind:howto");

        Assert.Empty(Keys(t.Call("ListAllData", ("tag", "kind:skill"))));
        Assert.Equal(new[] { "doc:a" }, Keys(t.Call("ListAllData", ("tag", "kind:howto"))));
    }

    [Fact]
    public void Entries_can_be_filtered_by_prefix_and_by_tag_together()
    {
        using var t = new TempRegistry();
        Set(t, "doc:skills/one", "One", "kind:skill");
        Set(t, "doc:howto/two", "Two", "kind:howto");
        Set(t, "doc:skills/three", "Three", "kind:howto");
        t.Call("SetData", ("key", "reading"), ("value", "12"));

        Assert.Equal(4, Keys(t.Call("ListAllData")).Count);
        Assert.Equal(3, Keys(t.Call("ListAllData", ("prefix", "doc:"))).Count);
        Assert.Equal(new[] { "doc:skills/one", "doc:skills/three" }, Keys(t.Call("ListAllData", ("prefix", "doc:skills/"))));
        Assert.Equal(new[] { "doc:skills/three" }, Keys(t.Call("ListAllData", ("prefix", "doc:skills/"), ("tag", "kind:howto"))));
    }

    [Fact]
    public void The_how_to_ends_with_the_index_of_the_documents()
    {
        using var t = new TempRegistry();

        Assert.Contains("DOCUMENTS IN THIS REGISTRY\nNone yet.", t.Call("GetHowTo"));

        Set(t, "doc:skills/b", "Skill B", "kind:skill");
        Set(t, "doc:agents/a", "Agent A", "kind:agent", "topic:review");
        t.Call("SetData", ("key", "not-a-document"), ("value", "x"), ("description", "Plain data"));
        t.Call("AddMethod", ("methodCode", "[McpMethod(\"A tool\")]\npublic int Tool(int n) => n;"));

        var howTo = t.Call("GetHowTo");

        Assert.Contains("DOCUMENTS IN THIS REGISTRY (2)", howTo);
        Assert.Contains("- doc:agents/a: Agent A [kind:agent, topic:review]", howTo);
        Assert.Contains("- doc:skills/b: Skill B [kind:skill]", howTo);
        Assert.True(howTo.IndexOf("doc:agents/a", StringComparison.Ordinal) < howTo.IndexOf("doc:skills/b", StringComparison.Ordinal),
            "The index is sorted by key.");
        Assert.DoesNotContain("not-a-document", howTo);
        Assert.DoesNotContain("method:Tool", howTo);
    }

    [Fact]
    public void The_index_of_a_large_registry_is_capped()
    {
        using var t = new TempRegistry();
        for (var i = 0; i < 60; i++)
            Set(t, $"doc:n{i:00}", $"Document {i}", "kind:howto");

        var howTo = t.Call("GetHowTo");

        Assert.Contains("DOCUMENTS IN THIS REGISTRY (60)", howTo);
        Assert.Contains("doc:n49", howTo);
        Assert.DoesNotContain("doc:n50", howTo);
        Assert.Contains("and 10 more", howTo);
    }

    [Fact]
    public void A_document_is_guidance_and_can_still_be_removed_like_any_data()
    {
        using var t = new TempRegistry();
        Set(t, "doc:howto/x", "How to X", "kind:howto");

        Assert.Contains("Data removed", t.Call("RemoveData", ("key", "doc:howto/x")));
        Assert.Contains("None yet", t.Call("GetHowTo"));
    }
}
