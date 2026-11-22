# Contributing

Thanks for looking. The goal of this project is to stay small: the smallest dynamic MCP server that lets an
assistant add a tool and use it in the same conversation. A change that makes it bigger needs a reason.

## Build and test

You need the .NET 8 SDK.

```
dotnet build HumAiGo.sln
dotnet test HumAiGo.sln
```

The tests take a few seconds. Some of them start the real server executable, so they are closer to real use than unit
tests; one of them uses the official MCP C# client.

To run the tests on Linux from another system, with Docker:

```
docker run --rm -v "$PWD":/src -w /src mcr.microsoft.com/dotnet/sdk:8.0 dotnet test HumAiGo.sln
```

## What a good change looks like

- It comes with a test that fails without it. The existing tests were checked the same way: break the code on purpose,
  and a test must fail.
- It keeps the rules of the store: a method is saved only after it compiled, an unreadable index is never overwritten,
  and keys that start with `method:` belong to tools.
- It does not add a dependency lightly. The server has one (Roslyn), on purpose.
- It does not weaken what is written in [SECURITY.md](SECURITY.md). If it changes what a tool can do, update that file.

## Layout

```
src/HumAiGo/            the executable: the protocol and the main loop
src/HumAiGo.McpServer/  the registry, the compiler and the store
tests/HumAiGo.Tests/    the tests
examples/               example tools, compiled and run by the tests
```

## Reporting a bug

Open an issue with what you did, what you expected, and the diagnostics the server wrote on standard error (they
start with `INPUT >>` and `OUTPUT >>`). Do not paste secrets. For a security problem, see [SECURITY.md](SECURITY.md)
instead of opening an issue.
