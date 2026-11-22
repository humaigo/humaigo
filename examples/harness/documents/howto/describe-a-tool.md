---
description: How to write the description of a tool and of its parameters so that an assistant uses it correctly
tags: kind:howto, topic:design
---
# Describe a tool

The description is the only thing an assistant reads when it chooses a tool.

- Start with what the tool does, in one sentence: "Returns the current temperature for a latitude and a longitude".
- Say what it needs and what it returns, with units.
- Say what it can change. A tool that only reads says nothing about it; a tool that writes says so.
- Describe every parameter with `[System.ComponentModel.Description("...")]`: its meaning, its unit, its allowed values.
- Name the tool for what the person wants, not for how it works.
- Give a parameter a default value when a sensible one exists.
