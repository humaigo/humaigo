// depends: Greet
[McpMethod("Returns a greeting for a name, in capital letters")]
public string Shout([System.ComponentModel.Description("Who to greet")] string name = "world") => Greet(name).ToUpperInvariant();
