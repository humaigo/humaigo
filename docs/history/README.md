# History

This folder keeps how HumAiGo came to be. It is not part of the software.

## What is here

- [`TIMELINE.md`](TIMELINE.md): the versions, one after the other, with what changed and what was measured.
- [`HUMAIGO_BIBLE_V0.5.md`](HUMAIGO_BIBLE_V0.5.md): research notes, in French, written as a notebook. It is a snapshot of
  28 September 2026. It follows a related idea, a "Node" that extends itself, and the threat model that idea would need.
  The notes use named roles for the people and assistants involved, and a vocabulary of their own.

The conversations with AI assistants that these notes come from are not published.

## How to read it

HumAiGo was designed and built by its author in conversation with AI assistants. From the third version on, the server
itself was the means: tools added to it read and wrote its sources, ran the builds and the tests, and published the next
version, with the author reading, restarting and deciding.

**The ideas in the notes are not implemented here.** HumAiGo keeps a human in the loop and loads no code from elsewhere.
A Node that extends itself without a human, and tools shared between peers, are explored in the notes as questions, with
their risks. The notes themselves say that the general implementation and the shared, long-running forms should wait for a
separate decision, and should not be published before the questions about their security have been explored. This
repository follows that plan: see [SECURITY.md](../../SECURITY.md) for what HumAiGo does and does not protect.
