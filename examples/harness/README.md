# harness: guidance that stays with the tools

A registry can hold **documents**: Markdown texts, kept as data under keys that start with `doc:`, that tell the
assistant how to work with that toolbox. This folder shows the idea with five documents and one tool that loads them.

| Document | Kind | What it is |
| --- | --- | --- |
| `agents/tool-reviewer.md` | `kind:agent` | A role: review the code against the rules before adding a tool |
| `skills/add-api-tool.md` | `kind:skill` | A procedure: turn a web API into tools |
| `security/tool-rules.md` | `kind:security` | The rules every tool must respect |
| `howto/verify-a-tool.md` | `kind:howto` | How to check a tool before saying it works |
| `howto/describe-a-tool.md` | `kind:howto` | How to write a description an assistant can use |

## Load them

1. Add `ImportDocuments.cs` with `AddMethod`.
2. Call `ImportDocuments` with the full path of the `documents` folder: `examples/harness/documents` inside the install directory (`GetInstallDirectory` tells where), or in your clone of the repository.
3. Call `GetHowTo`. The five documents are listed at the end, each with its description and tags.

From then on, `GetHowTo` is the entry point: the assistant reads it at the start of a conversation, sees which documents
exist, and reads the one that applies with `GetData("doc:skills/add-api-tool")` before it acts. A standing instruction
such as "before you add or change a tool, follow the documents that `GetHowTo` lists" is enough to make it work.

`ListAllData(prefix: "doc:", tag: "kind:security")` lists the documents of one kind.

## Write your own

A document is a Markdown file. It may start with a front matter block, which `ImportDocuments` reads:

```
---
description: One line for the index
tags: kind:skill, topic:anything
---
# Title

The text.
```

The key is `doc:` plus the path of the file without `.md`. Kinds are a convention, not a list the server knows:
`kind:agent`, `kind:skill`, `kind:security` and `kind:howto` are suggestions. Tags that start with `type:` are reserved
by the system.

You can also write a document directly, with `SetData`, with no file at all:

```
SetData("doc:howto/release", "# Release\n...", "How we release", ["kind:howto"])
```

## Guidance, not enforcement

A document tells an assistant what to do. It does not stop it from doing something else, and an assistant that can call
`SetData` can rewrite a document. A rule that must hold needs a place the assistant cannot write to, and a check that
runs before a tool is added: neither exists in HumAiGo, and each project that needs them can decide how to add them.
