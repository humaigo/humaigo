[McpMethod("Computes (a + b) * (a * b) using the Add and Multiply tools")]
public int AddThenMultiply(int a, int b)
{
    // Add and Multiply are not called through the registry: AddMethod copies their code in
    // when they are passed as dependencies, so this method is self-contained.
    return Add(a, b) * Multiply(a, b);
}
