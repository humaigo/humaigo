using System.Text.Json;
using Xunit;

namespace HumAiGo.Tests;

/// <summary>The dynamic registry, driven through the same public entry point the MCP server uses.</summary>
public class RegistryTests
{
    private const string SquareCode = """
        [McpMethod("Returns the square of an integer")]
        public int Square(int n) => n * n;
        """;

    private const string BrokenSquareCode = """
        [McpMethod("Returns the square of an integer")]
        public int Square(int n) => n *;
        """;

    private const string CubeBehindSquareName = """
        [McpMethod("Now returns the cube")]
        public int Square(int n) => n * n * n;
        """;

    private const string OtherName = """
        [McpMethod("Another name")]
        public int Other(int n) => n;
        """;

    // ---- Adding, updating, removing methods ----

    [Fact]
    public void An_added_method_can_be_called()
    {
        using var t = new TempRegistry();

        Assert.StartsWith("Successfully added Square", t.Call("AddMethod", ("methodCode", SquareCode)));

        Assert.True(t.Registry.HasMethod("Square"));
        Assert.Equal("49", t.Call("Square", ("n", 7)));
    }

    [Fact]
    public void A_method_that_does_not_compile_changes_nothing()
    {
        using var t = new TempRegistry();
        t.Call("AddMethod", ("methodCode", SquareCode));

        var result = t.Call("AddMethod", ("methodCode", BrokenSquareCode.Replace("Square", "Broken")));

        Assert.Contains("Compilation failed", result);
        Assert.False(t.Registry.HasMethod("Broken"));
        Assert.DoesNotContain("method:Broken", t.Call("ListAllData"));
        Assert.Equal("49", t.Call("Square", ("n", 7)));
    }

    [Fact]
    public void A_method_cannot_be_added_twice()
    {
        using var t = new TempRegistry();
        t.Call("AddMethod", ("methodCode", SquareCode));

        Assert.Contains("already exists", t.Call("AddMethod", ("methodCode", SquareCode)));
    }

    [Fact]
    public void A_failed_update_keeps_the_previous_version()
    {
        using var t = new TempRegistry();
        t.Call("AddMethod", ("methodCode", SquareCode));

        var failed = t.Call("UpdateMethod", ("methodName", "Square"), ("newMethodCode", BrokenSquareCode));

        Assert.Contains("previous version remains active", failed);
        Assert.Equal("49", t.Call("Square", ("n", 7)));

        var updated = t.Call("UpdateMethod", ("methodName", "Square"), ("newMethodCode", CubeBehindSquareName));

        Assert.Contains("successfully updated", updated);
        Assert.Equal("343", t.Call("Square", ("n", 7)));
    }

    [Fact]
    public void An_update_cannot_rename_a_method()
    {
        using var t = new TempRegistry();
        t.Call("AddMethod", ("methodCode", SquareCode));

        var result = t.Call("UpdateMethod", ("methodName", "Square"), ("newMethodCode", OtherName));

        Assert.Contains("Update rejected", result);
        Assert.True(t.Registry.HasMethod("Square"));
        Assert.False(t.Registry.HasMethod("Other"));
    }

    [Fact]
    public void A_removed_method_leaves_the_registry_and_the_store()
    {
        using var t = new TempRegistry();
        t.Call("AddMethod", ("methodCode", SquareCode));

        Assert.Contains("successfully removed", t.Call("RemoveMethod", ("methodName", "Square")));

        Assert.False(t.Registry.HasMethod("Square"));
        Assert.DoesNotContain("method:Square", t.Call("ListAllData"));
    }

    [Fact]
    public void Base_methods_cannot_be_removed_or_updated()
    {
        using var t = new TempRegistry();

        Assert.Contains("base method", t.Call("RemoveMethod", ("methodName", "AddMethod")));
        Assert.Contains("base method", t.Call("UpdateMethod", ("methodName", "AddMethod"), ("newMethodCode", OtherName)));
        Assert.True(t.Registry.HasMethod("AddMethod"));
    }

    [Fact]
    public void Optional_parameters_use_their_default_value()
    {
        using var t = new TempRegistry();
        t.Call("AddMethod", ("methodCode", """
            [McpMethod("Greets someone")]
            public string Greet(string name, string greeting = "Hello") => greeting + ", " + name;
            """));

        Assert.Equal("Hello, Ada", t.Call("Greet", ("name", "Ada")));
        Assert.Equal("Hi, Ada", t.Call("Greet", ("name", "Ada"), ("greeting", "Hi")));
    }

    [Fact]
    public void Methods_are_reloaded_from_the_store_by_a_new_registry()
    {
        using var t = new TempRegistry();
        t.Call("AddMethod", ("methodCode", SquareCode));

        var second = TempRegistry.Open(t.Id);

        Assert.True(second.HasMethod("Square"));
    }

    // ---- Dependencies: copied, hashed, and checked for drift ----

    [Fact]
    public void A_dependency_is_embedded_as_a_copy_and_its_drift_is_detected()
    {
        using var t = new TempRegistry();
        t.Call("AddMethod", ("methodCode", """
            [McpMethod("Adds")]
            public int Add2(int a, int b) => a + b;
            """));
        t.Call("AddMethod", ("methodCode", """
            [McpMethod("Multiplies")]
            public int Mul2(int a, int b) => a * b;
            """));
        var dependencies = JsonSerializer.SerializeToElement(new[] { "Add2", "Mul2" });
        const string comboCode = """
            [McpMethod("Combines the two")]
            public int Combo(int a, int b) { return Add2(a, b) * Mul2(a, b); }
            """;

        Assert.StartsWith("Successfully added Combo",
            t.Call("AddMethod", ("methodCode", comboCode), ("dependencies", dependencies)));
        Assert.Equal("84", t.Call("Combo", ("a", 3), ("b", 4)));
        Assert.Contains("up to date", t.Call("CheckOutdatedDependencies"));

        // Changing the source method does not touch the copy, but the drift is reported.
        t.Call("UpdateMethod", ("methodName", "Add2"), ("newMethodCode", """
            [McpMethod("Adds, operands swapped")]
            public int Add2(int a, int b) => b + a;
            """));

        Assert.Contains("outdated copy of 'Add2'", t.Call("CheckOutdatedDependencies"));

        // Refreshing the dependent method clears the report.
        t.Call("UpdateMethod", ("methodName", "Combo"), ("newMethodCode", comboCode), ("dependencies", dependencies));

        Assert.Contains("up to date", t.Call("CheckOutdatedDependencies"));
    }

    [Fact]
    public void A_missing_dependency_is_refused()
    {
        using var t = new TempRegistry();

        var result = t.Call("AddMethod", ("methodCode", SquareCode),
            ("dependencies", JsonSerializer.SerializeToElement(new[] { "DoesNotExist" })));

        Assert.Contains("not found", result);
        Assert.False(t.Registry.HasMethod("Square"));
    }

    // ---- Data ----

    [Fact]
    public void Data_survives_a_round_trip_with_accents_and_can_be_removed()
    {
        using var t = new TempRegistry();
        const string value = "Voilà : àéèùç €";

        t.Call("SetData", ("key", "greeting"), ("value", value), ("description", "test"));
        using var stored = JsonDocument.Parse(t.Call("GetData", ("key", "greeting")));

        Assert.Equal(value, stored.RootElement.GetProperty("value").GetString());

        Assert.Contains("Data removed", t.Call("RemoveData", ("key", "greeting")));
        Assert.Contains("No data found", t.Call("GetData", ("key", "greeting")));
        Assert.Contains("No data found", t.Call("RemoveData", ("key", "greeting")));
    }

    [Fact]
    public void Keys_that_belong_to_methods_are_reserved()
    {
        using var t = new TempRegistry();
        t.Call("AddMethod", ("methodCode", SquareCode));

        Assert.Contains("reserved", t.Call("SetData", ("key", "method:Square"), ("value", "oops")));
        Assert.Contains("reserved", t.Call("RemoveData", ("key", "method:Square")));
        Assert.Equal("49", t.Call("Square", ("n", 7)));

        t.Registry.Invoke("ReloadRegistry", new Dictionary<string, object>());
        Assert.True(t.Registry.HasMethod("Square"));
    }

    // ---- Registries (toolboxes) ----

    [Theory]
    [InlineData("..\\evil")]
    [InlineData("../evil")]
    [InlineData("C:\\temp")]
    [InlineData("Upper")]
    [InlineData("with space")]
    [InlineData("")]
    public void Unsafe_registry_ids_are_refused(string id)
    {
        using var t = new TempRegistry();

        Assert.Contains("Invalid registry id", t.Call("SwitchRegistry", ("registryId", id), ("create", true)));
    }

    [Fact]
    public void A_registry_id_longer_than_64_characters_is_refused()
    {
        using var t = new TempRegistry();

        Assert.Contains("Invalid registry id", t.Call("SwitchRegistry", ("registryId", new string('a', 65))));
    }

    [Fact]
    public void An_unknown_registry_is_not_created_by_accident()
    {
        using var t = new TempRegistry();
        var other = t.NewOtherId();

        Assert.Contains("does not exist", t.Call("SwitchRegistry", ("registryId", other)));
        Assert.False(Directory.Exists(TempRegistry.DataFolder(other)));
    }

    [Fact]
    public void Registries_are_isolated_from_each_other()
    {
        using var t = new TempRegistry();
        var other = t.NewOtherId();
        t.Call("AddMethod", ("methodCode", SquareCode));
        t.Call("SetData", ("key", "note"), ("value", "in the first registry"));

        Assert.Contains("Active registry", t.Call("SwitchRegistry", ("registryId", other), ("create", true)));

        Assert.False(t.Registry.HasMethod("Square"));
        Assert.Contains("No data found", t.Call("GetData", ("key", "note")));

        Assert.Contains("Active registry", t.Call("SwitchRegistry", ("registryId", t.Id)));

        Assert.True(t.Registry.HasMethod("Square"));
        Assert.Equal("49", t.Call("Square", ("n", 7)));
        Assert.Contains("in the first registry", t.Call("GetData", ("key", "note")));
        Assert.Contains("already active", t.Call("SwitchRegistry", ("registryId", t.Id)));
    }

    [Fact]
    public void The_active_registry_is_marked_in_the_list()
    {
        using var t = new TempRegistry();
        var other = t.NewOtherId();
        t.Call("SwitchRegistry", ("registryId", other), ("create", true));

        using var list = JsonDocument.Parse(t.Call("ListRegistries"));
        var entries = list.RootElement.EnumerateArray().ToDictionary(e => e.GetProperty("id").GetString()!);

        Assert.True(entries[other].GetProperty("active").GetBoolean());
        Assert.False(entries[t.Id].GetProperty("active").GetBoolean());
    }

    [Fact]
    public void Switching_to_a_registry_with_an_unreadable_index_is_refused_and_changes_nothing()
    {
        using var t = new TempRegistry();
        var damaged = t.NewOtherId();
        Directory.CreateDirectory(TempRegistry.DataFolder(damaged));
        var indexPath = Path.Combine(TempRegistry.DataFolder(damaged), "__index__.json");
        const string corrupt = "[ {\"Key\": ";
        File.WriteAllText(indexPath, corrupt);
        t.Call("AddMethod", ("methodCode", SquareCode));

        Assert.Contains("unreadable", t.Call("SwitchRegistry", ("registryId", damaged)));

        Assert.Equal(corrupt, File.ReadAllText(indexPath));
        Assert.True(t.Registry.HasMethod("Square"));

        using var list = JsonDocument.Parse(t.Call("ListRegistries"));
        var entries = list.RootElement.EnumerateArray().ToDictionary(e => e.GetProperty("id").GetString()!);
        Assert.True(entries[t.Id].GetProperty("active").GetBoolean());
        Assert.False(entries[damaged].GetProperty("readable").GetBoolean());
    }

    // ---- Tool schemas ----

    [Fact]
    public void Tool_schemas_carry_descriptions_array_items_and_defaults()
    {
        using var t = new TempRegistry();
        var tools = t.Registry.GetToolsForMcp()
            .Select(tool => JsonSerializer.SerializeToElement(tool))
            .ToDictionary(tool => tool.GetProperty("name").GetString()!);

        var addMethod = tools["AddMethod"].GetProperty("inputSchema");
        var properties = addMethod.GetProperty("properties");

        Assert.False(string.IsNullOrWhiteSpace(properties.GetProperty("methodCode").GetProperty("description").GetString()));
        Assert.Equal("string", properties.GetProperty("dependencies").GetProperty("items").GetProperty("type").GetString());
        Assert.Equal(new[] { "methodCode" }, addMethod.GetProperty("required").EnumerateArray().Select(e => e.GetString()));

        var callMethod = tools["CallMethod"].GetProperty("inputSchema").GetProperty("properties");
        Assert.Equal("{}", callMethod.GetProperty("argumentsJson").GetProperty("default").GetString());
    }
}
