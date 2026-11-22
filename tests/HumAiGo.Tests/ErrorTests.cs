using Xunit;

namespace HumAiGo.Tests;

public class ErrorTests
{
    [Fact]
    public void A_method_that_throws_is_reported_with_its_own_message()
    {
        using var t = new TempRegistry();
        t.Call("AddMethod", ("methodCode", """
            [McpMethod("Always fails")]
            public int Boom(int n) { throw new System.InvalidOperationException("boom: " + n); }
            """));

        var result = t.Call("CallMethod", ("methodName", "Boom"), ("argumentsJson", "{\"n\": 2}"));

        Assert.Contains("boom: 2", result);
        Assert.DoesNotContain("target of an invocation", result);
    }
}
