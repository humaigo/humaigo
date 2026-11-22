using System.Text.Json.Serialization;

namespace HumAiGo.McpServer.Models;

/// <summary>An outgoing JSON-RPC 2.0 message: either a result or an error.</summary>
public class JsonRpcResponse
{
    [JsonPropertyName("jsonrpc")]
    public string Jsonrpc { get; set; } = "2.0";

    [JsonPropertyName("result")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public object? Result { get; set; }

    [JsonPropertyName("error")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public JsonRpcError? Error { get; set; }

    [JsonPropertyName("id")]
    public object? Id { get; set; }

    public static JsonRpcResponse Success(object? id, object? result) =>
        new JsonRpcResponse { Id = id, Result = result };

    public static JsonRpcResponse FromError(string message, object? id = null, int code = JsonRpcErrorCodes.InternalError) =>
        new JsonRpcResponse
        {
            Id = id,
            Error = new JsonRpcError { Code = code, Message = message }
        };
}

public class JsonRpcError
{
    [JsonPropertyName("code")]
    public int Code { get; set; }

    [JsonPropertyName("message")]
    public string Message { get; set; } = "";
}

/// <summary>The error codes defined by the JSON-RPC 2.0 specification.</summary>
public static class JsonRpcErrorCodes
{
    public const int ParseError = -32700;
    public const int InvalidRequest = -32600;
    public const int MethodNotFound = -32601;
    public const int InvalidParams = -32602;
    public const int InternalError = -32603;
}
