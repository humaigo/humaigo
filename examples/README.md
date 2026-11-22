# Examples

Each file here is the code of one tool, exactly as you would pass it to `AddMethod`. They are compiled and run by
the test suite (`tests/HumAiGo.Tests/ExamplesTests.cs`), so they stay correct.

In a release archive this folder sits next to the executable: `GetInstallDirectory` gives its location.

To use one, give its content to your assistant and ask it to add it, for example:

> Add this tool with AddMethod, then call it for latitude 46.95 and longitude 7.45: *(paste the file)*

## `weather-open-meteo/`: an API in one request

`GetCurrentWeather.cs` calls the free [Open-Meteo](https://open-meteo.com/) API, which needs no key. It shows a tool
that reaches the Internet, and a tool whose parameters are described for the assistant.

## `dependencies/`: reusing tools by copy

`Add.cs` and `Multiply.cs` are small tools. `AddThenMultiply.cs` uses both. Add the first two, then add the third with
`dependencies` set to `["Add", "Multiply"]`: their code is copied into it, so it stays self-contained.

Change `Add` afterwards and run `CheckOutdatedDependencies`: it reports that `AddThenMultiply` holds an older copy.

## `capitalization/`: what one conversation leaves for the next

`RecordTemperature.cs` reads the current temperature and stores it with `SetData`. `TemperatureTrend.cs` reads what was
stored and reports the minimum, maximum, average and change.

1. In a first conversation, add both tools and call `RecordTemperature` once.
2. Later, in a new conversation, call `RecordTemperature` again, then `TemperatureTrend`.

The tools and the readings are still there: nothing is added by hand, and nothing starts again from zero.

A note on the code: a tool is a method of a class that derives from the base methods, so it can call `SetData`,
`GetData` and `ListAllData` directly, as these two do.

## `harness/`: guidance that stays with the tools

Five Markdown documents (a role, a skill, security rules and two how-tos) and `ImportDocuments.cs`, a tool that loads
them into the data store. `GetHowTo` then lists them, so the assistant knows what guidance exists. See
[`harness/README.md`](harness/README.md).
