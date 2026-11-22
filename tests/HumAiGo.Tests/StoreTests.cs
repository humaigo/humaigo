using HumAiGo.McpServer.Services;
using Xunit;

namespace HumAiGo.Tests;

/// <summary>The local key-value store: one file per value, one index per registry.</summary>
public class StoreTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "humaigo-store-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); }
        catch (DirectoryNotFoundException) { }
    }

    [Fact]
    public void A_value_can_be_stored_updated_and_deleted()
    {
        var client = new LocalKeyValueClient(_dir);

        client.Set("reg", "k", "one", "first");
        client.Set("reg", "k", "two", "second");

        var entries = client.ListKeys("reg");
        Assert.Single(entries);
        Assert.Equal("two", client.Get("reg", "k")!.Value);

        var valueFile = Path.Combine(_dir, "reg", entries[0].ValueKey);
        Assert.True(File.Exists(valueFile));

        client.Delete("reg", "k");

        Assert.False(File.Exists(valueFile));
        Assert.Empty(client.ListKeys("reg"));
        Assert.Null(client.Get("reg", "k"));
    }

    [Fact]
    public void Concurrent_writers_lose_no_update()
    {
        var clients = Enumerable.Range(0, 4).Select(_ => new LocalKeyValueClient(_dir)).ToArray();

        Parallel.For(0, clients.Length, c =>
        {
            for (var i = 0; i < 25; i++)
                clients[c].Set("reg", $"k{c}-{i}", "v");
        });

        Assert.Equal(100, clients[0].ListKeys("reg").Count);
    }

    [Fact]
    public void An_unreadable_index_is_never_treated_as_empty()
    {
        var client = new LocalKeyValueClient(_dir);
        Directory.CreateDirectory(Path.Combine(_dir, "reg"));
        var indexPath = Path.Combine(_dir, "reg", "__index__.json");
        const string corrupt = "[ {\"Key\": ";
        File.WriteAllText(indexPath, corrupt);

        Assert.Throws<InvalidDataException>(() => client.ListKeys("reg"));
        Assert.Throws<InvalidDataException>(() => client.Set("reg", "k", "v"));

        // Nothing was overwritten: the damaged file is exactly as it was.
        Assert.Equal(corrupt, File.ReadAllText(indexPath));
    }

    [Theory]
    [InlineData("..\\escape")]
    [InlineData("../escape")]
    [InlineData("")]
    [InlineData("..")]
    public void A_registry_id_cannot_leave_the_data_folder(string id)
    {
        var client = new LocalKeyValueClient(_dir);

        Assert.Throws<ArgumentException>(() => client.Set(id, "k", "v"));
    }
}
