[McpMethod("Returns a greeting for a name")]
public string Greet([System.ComponentModel.Description("Who to greet")] string name = "world") => "Hello, " + name + "!";
