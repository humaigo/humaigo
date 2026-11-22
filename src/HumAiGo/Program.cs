using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Unicode;

using HumAiGo;
using HumAiGo.McpServer.Models;
using HumAiGo.McpServer.Services;

// The HumAiGo MCP server.
//
// It speaks JSON-RPC 2.0 over standard input and output, one message per line. Standard output is reserved
// for the protocol: every diagnostic goes to standard error.

var baseDirectory = AppContext.BaseDirectory;
Console.Error.WriteLine($"BASE DIRECTORY >> {baseDirectory}");

Console.InputEncoding = Encoding.UTF8;
Console.OutputEncoding = Encoding.UTF8;

var jsonOptions = new JsonSerializerOptions
{
    PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    WriteIndented = false,
    Encoder = JavaScriptEncoder.Create(UnicodeRanges.All)
};

// Writes one message on standard output.
void Send(JsonRpcResponse response)
{
    var json = JsonSerializer.Serialize(response, jsonOptions);
    Console.WriteLine(json);
    Console.Error.WriteLine("OUTPUT >> " + json);
}

// The registry used at startup comes from, in this order: the --registry argument, the HUMAIGO_REGISTRY variable,
// the registryid of humaigo_config.json, and finally "default".
HumAiGoConfig LoadConfig()
{
    var config = new HumAiGoConfig();

    var configFile = Path.Combine(baseDirectory, "humaigo_config.json");
    if (File.Exists(configFile))
        config = JsonSerializer.Deserialize<HumAiGoConfig>(File.ReadAllText(configFile, Encoding.UTF8), jsonOptions) ?? config;

    string? fromArguments = null;
    for (var i = 0; i < args.Length; i++)
    {
        if (args[i] == "--registry" && i + 1 < args.Length)
            fromArguments = args[i + 1];
        else if (args[i].StartsWith("--registry="))
            fromArguments = args[i].Substring("--registry=".Length);
    }

    var requested = fromArguments ?? Environment.GetEnvironmentVariable("HUMAIGO_REGISTRY");
    if (!string.IsNullOrWhiteSpace(requested))
        config.RegistryId = requested;
    if (string.IsNullOrWhiteSpace(config.RegistryId))
        config.RegistryId = "default";

    Console.Error.WriteLine($"REGISTRY ID >> {config.RegistryId}");
    return config;
}

McpMethodRegistry registry;
try
{
    registry = new McpMethodRegistry(baseDirectory, LoadConfig(), jsonOptions);
}
catch (Exception ex)
{
    Send(JsonRpcResponse.FromError("Unhandled registry initialization error: " + ex.Message + Environment.NewLine + ex.StackTrace));
    return;
}

AppDomain.CurrentDomain.ProcessExit += (_, _) => Console.Error.WriteLine("PROCESS EXIT >>");
AppDomain.CurrentDomain.UnhandledException += (_, e) => Console.Error.WriteLine("UNHANDLED EXCEPTION >> " + e.ExceptionObject);

// The server loop: one request per line, until the client closes standard input.
while (true)
{
    var line = Console.ReadLine();
    if (line == null)
    {
        Console.Error.WriteLine("STDIN CLOSED >> end of input, stopping");
        break;
    }
    if (string.IsNullOrWhiteSpace(line))
        continue;

    Console.Error.WriteLine("INPUT >> " + line);

    JsonRpcRequest? request;
    try
    {
        request = JsonSerializer.Deserialize<JsonRpcRequest>(line, jsonOptions);
    }
    catch (JsonException ex)
    {
        Send(JsonRpcResponse.FromError("Parse error: " + ex.Message, null, JsonRpcErrorCodes.ParseError));
        continue;
    }

    if (request == null)
    {
        Send(JsonRpcResponse.FromError("Invalid request", null, JsonRpcErrorCodes.InvalidRequest));
        continue;
    }

    try
    {
        var response = McpProtocol.Handle(request, registry, jsonOptions);
        if (response != null)
            Send(response);
    }
    catch (Exception ex)
    {
        Send(JsonRpcResponse.FromError("Internal error: " + ex.Message, request.Id, JsonRpcErrorCodes.InternalError));
    }
}
