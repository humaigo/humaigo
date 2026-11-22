using ModelContextProtocol.Client;
using Xunit;

namespace HumAiGo.Tests;

// Errors, seen through the official client.
public partial class IndependentClientTests
{
    private const string BoomCode = "[McpMethod(\"Always fails\")]\npublic int Boom(int n) { throw new System.InvalidOperationException(\"boom: \" + n); }";

    [Fact]
    public async Task A_failing_tool_returns_an_error_result_the_client_can_read()
    {
        await using var client = await ConnectAsync();
        await client.CallToolAsync("AddMethod", new Dictionary<string, object?> { ["methodCode"] = BoomCode });

        var failed = await client.CallToolAsync("Boom", new Dictionary<string, object?> { ["n"] = 3 });

        Assert.True(failed.IsError);
        Assert.Equal("boom: 3", TextOf(failed));
    }

    [Fact]
    public async Task Missing_arguments_are_refused_as_a_protocol_error()
    {
        await using var client = await ConnectAsync();

        var outcome = await Record.ExceptionAsync(() => client.CallToolAsync("GetData", new Dictionary<string, object?>()).AsTask());

        Assert.NotNull(outcome);
        Assert.Contains("Missing parameter", outcome.Message);
    }
}
