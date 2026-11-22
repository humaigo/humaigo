namespace HumAiGo.Tests;

/// <summary>Finds files of the repository, such as the examples, from the folder the tests run in.</summary>
public static class RepoPaths
{
    public static string Root()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null && !File.Exists(Path.Combine(directory.FullName, "HumAiGo.sln")))
            directory = directory.Parent;

        return directory?.FullName ?? throw new DirectoryNotFoundException("HumAiGo.sln was not found above " + AppContext.BaseDirectory);
    }

    public static string Examples(params string[] parts) =>
        Path.Combine(new[] { Root(), "examples" }.Concat(parts).ToArray());
}
