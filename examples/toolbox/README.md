# toolbox: a registry described by files

A registry is generated state: a folder of files with random names and an index. A **toolbox** is the readable description
of one, a folder you keep in Git. These two tools turn one into the other.

```
my-toolbox/
  tools/
    Greet.cs
    Shout.cs          first line: // depends: Greet
  docs/
    howto/toolbox-conventions.md
```

- **`ApplyToolbox`** adds or updates every tool of `tools/`, and imports every document of `docs/`. It can be run again
  and again: a tool whose code has not changed is left alone, and a tool is updated when a tool it embeds has changed
  (the copies it carries would otherwise be out of date). With `dryRun`, it only reports what would change.
- **`ExportToolbox`** goes the other way: it writes a registry as a toolbox folder. Data that tools stored is not exported.

## Try it

1. Add `harness/ImportDocuments.cs` with `AddMethod`.
2. Add `ApplyToolbox.cs` with `AddMethod`, with `dependencies` set to `["ImportDocuments"]`.
3. Add `ExportToolbox.cs` with `AddMethod`.
4. Call `ApplyToolbox` with the full path of the `sample` folder next to this file, first with `dryRun` set to true, then for real.
   Then call `Shout`.

`sample/` holds two tools and one document. In a release archive, this folder is next to the executable
(`GetInstallDirectory` tells where).

## Read this before applying a folder

Applying a toolbox **compiles and installs the code it contains**, with the permissions of the server. Only apply folders you
wrote or have read. Keep your own toolboxes in a private repository, and keep your client's approval on for `AddMethod`,
`UpdateMethod` and `CallMethod`: this tool asks the registry to do the same things, so what it does is subject to the same care.
