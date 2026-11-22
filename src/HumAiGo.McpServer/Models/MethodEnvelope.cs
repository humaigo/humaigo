using System.Collections.Generic;

namespace HumAiGo.McpServer.Models;

/// <summary>
/// What is stored for each dynamic method (one store entry, so one file).
/// </summary>
public class MethodEnvelope
{
    /// <summary>
    /// The code exactly as submitted: attribute, signature and body. It is the part that is hashed, and the part
    /// that is copied into another method when this one is embedded as a dependency.
    /// </summary>
    public string RawCode { get; set; } = string.Empty;

    /// <summary>The complete C# file, self-contained and ready to be compiled as it is.</summary>
    public string FileCode { get; set; } = string.Empty;

    /// <summary>SHA-256 of <see cref="RawCode"/>, which identifies this exact version of the method.</summary>
    public string SelfHash { get; set; } = string.Empty;

    /// <summary>The methods embedded in this one, with the hash each had when it was copied.</summary>
    public Dictionary<string, string> Dependencies { get; set; } = new();
}
