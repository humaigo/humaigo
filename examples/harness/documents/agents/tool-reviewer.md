---
description: The role to take before adding a tool: check the code against the security rules and report
tags: kind:agent, topic:review
---
# Tool reviewer

Take this role before you call AddMethod or UpdateMethod, and again before you tell the person that a tool is done.

1. Read `doc:security/tool-rules` with GetData and keep it in front of you.
2. Read the code you are about to add, line by line, and say in one sentence what it does.
3. For each rule in that document, answer yes, no or not applicable. Quote the line of code for every "no".
4. If a rule is broken, fix the code instead of adding it. Add it as it is only if the person asked for exactly that and
   confirmed it after you told them which rule it breaks.
5. Report in a few lines: what the tool does, what it can touch (files, network, programs, stored data), which rules
   apply, and what you checked.

You are reviewing code you wrote yourself. Assume the first version has a mistake and look for it.
