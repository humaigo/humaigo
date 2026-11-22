# Security

Read this before you run HumAiGo. It is short, and it is the part of the project that matters most.

## What the server does, in security terms

HumAiGo is an MCP server that talks over standard input and output. It opens no network port.

An AI assistant can ask it to add a tool. A tool is C# code, and the server compiles it and runs it **inside the server
process, with the permissions of the user who started the server**. Nothing limits what that code can do: it can read
and write any file that user can, start programs, use the network, and read environment variables.

This is the point of the project, and it is also its main risk. HumAiGo gives an assistant the ability to run code on your
machine. Treat it that way.

## Who is trusted

You trust your MCP client and the assistant behind it, and you read what they ask the server to run. Most clients ask for
your approval before calling a tool, and `AddMethod` is a tool like another one: its argument is the code that will
run. Keep that approval on, and read the code in the prompt.

HumAiGo is meant for a development machine or a virtual machine you are willing to risk. It is not meant for production, for
a shared machine, or for a machine that holds things the assistant should never touch.

## Threats

| Threat | Example | Where it stands |
| --- | --- | --- |
| Prompt injection leading to code execution | A web page or a file the assistant reads tells it to add a tool that sends your files elsewhere. | **Not prevented by the server.** Your client's approval of tool calls is the control. Read the code before approving. |
| Faulty or malicious tool code | A tool deletes files, or returns wrong results. | **Not prevented.** There is no sandbox. Compiling each tool separately keeps a mistake in one tool from breaking the others; it does not limit what a tool can do. |
| Secrets in the store | An API key kept with `SetData`. | The store is made of plain text files under `data/`. **Do not keep secrets there.** |
| A registry id that leaves the data folder | An id such as `..\..\elsewhere`. | Prevented: ids are limited to lowercase letters, digits, `-` and `_`, and the store refuses path separators. Tested. |
| A tool overwritten through the data methods | `SetData("method:Name", ...)`. | Prevented: keys that start with `method:` are refused by `SetData` and `RemoveData`. Tested. |
| Two server processes damaging the store | Some clients start two instances that share one data folder. | Handled: each registry is locked while it is read or written, and an unreadable index is never overwritten. Tested. |
| Code downloaded from elsewhere | Tools fetched from a shared registry. | **Not supported in this version.** There is no remote loading. If it is ever added, it needs signatures and an explicit approval. |
| A compromised dependency | A package the server uses. | The server depends on Roslyn only. Release builds come with SHA-256 checksums. |

## What is not covered

- No sandbox and no permission system: every tool has every right the server has.
- No isolation between tools: they share one process and one level of trust.
- No log of what a tool did.
- No enforcement of the documents kept in a registry. They are guidance for the assistant, and it can rewrite them with
  `SetData`. A rule that must hold has to live where the assistant cannot write.
- No protection against an assistant that is steered by hostile text. That is a limit of the whole approach, not a
  missing feature.

## Reasonable precautions

- Run the server in a virtual machine or on a machine you accept to expose.
- Use one registry per purpose (`SwitchRegistry`), and remove tools you no longer need.
- Keep secrets out of the data store and out of tool code. Pass them in at call time if you must.
- Keep your client's per-call approval enabled, and read the code of `AddMethod` calls. If your client offers a mode that
  approves tool calls automatically, do not use it for `AddMethod` and `UpdateMethod`: their argument is the code that will
  run. Approving read-only tools automatically is a reasonable middle ground; the rest should be read.
- Never start the server through a network service, and never expose its input and output to others.
- Check the checksums of a release before running it.

## Reporting a vulnerability

Please do not open a public issue for a security problem. Use the private reporting of this repository: the **Security**
tab, then **Report a vulnerability**. Say what you found, how to reproduce it, and what you think it allows.
