---
description: How a toolbox folder is laid out and applied
tags: kind:howto, topic:toolbox
---
# Toolbox conventions

A toolbox is a folder:

- `tools/Name.cs`: one tool per file, in the form AddMethod takes. A first line `// depends: A, B` embeds the tools A and B by copy.
- `docs/path/name.md`: a document, stored as `doc:path/name`, with an optional front matter block.

Apply it with ApplyToolbox. Apply it again after any change: tools that did not change are left alone, and a tool is updated when a tool it embeds changed.
Write a registry back to a folder with ExportToolbox.
