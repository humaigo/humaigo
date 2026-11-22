# Timeline

From the first prototype to version 0.1.0. Dates are approximate until the rewrite of November 2026.

## 2025: the first version

An MCP server in C#, over standard input and output, started because an assistant in a chat application could write code
but not run it. It already had its main ideas: tools found by reflection, new tools compiled at run time with Roslyn, and a
small key-value store. One finding shaped the design from the start: clients read the tool list once, so a tool added during a
conversation is not visible to them. A `CallMethod` tool was added to reach it anyway, and it is still there.

The project was named HumAiGo. A commercial packaging was tried (a licence, a demo mode, an online store) and abandoned.
None of it is in this repository.

## 2026: ideas around it

Research notes on a related concept, a "Node" that would extend itself, with a threat model for it, developed in
conversation with another assistant. The notes are kept in this folder, and the concept is not implemented.

## November 2026: a minimal core, version by version

The aim was the smallest dynamic MCP server that lets an assistant add a tool and use it in the same conversation.

- **V3.** One store entry and one compilation per tool, so an error in one never affects another. The code is saved only
  after it compiled. A tool can embed another by copy; the hash of the source is recorded, and a changed source is reported.
- **V4.** The demo mode, the online store and the licence parameter were removed. The sources compiled cleanly.
- **V5.** `RemoveData`, the reserved `method:` prefix, and a generated class renamed `DynamicMethod`.
- **V6.** Registries (toolboxes): list them, switch between them without restarting, choose one at startup. The server now
  stops when its input closes.
- **V7.** A lock per registry, and an index that cannot be read is never overwritten. Two server processes writing 40
  entries each into one registry kept 9, 4, 18 and 14 of 80 in V6, and 80 of 80 four times in V7; in V6 almost none of the
  losses was reported. Also: cached compile references, fuller tool schemas, `ping`, and protocol version negotiation.
- **V8.** A clean repository, with English comments, tests, and the official MCP C# client used against the server.
  Request ids may be strings, errors carry the standard JSON-RPC codes, and a tool that throws is reported with `isError`.
- **V9.** Documents: Markdown guidance kept as data, with tags, filters, and an index at the end of `GetHowTo`. The examples
  ship with the executable. 67 tests.
- **0.1.0.** The first public version.

## What was learned on the way

- A write done as a temporary file and a rename, repeated quickly, looked like ransomware to a security product on the
  development machine, which stopped the server processes without a message. Writing in place, under a lock, did not.
- A first estimate of the start-up cost per tool was wrong by an order of magnitude. It was measured afterwards: about
  0.75 s fixed, then about 15 ms per tool.
- Tests that cannot fail protect nothing. Each group of tests was checked by breaking the code on purpose and watching
  a test fail.
