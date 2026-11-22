---
description: How to check a new tool before telling the person that it works
tags: kind:howto, topic:testing
---
# Verify a tool

1. Call it with a typical input and read the result. Do not assume it is right.
2. Call it with an empty value, a very large value and a value of the wrong type, and check that the error message
   would help someone fix the call.
3. If the tool reaches the Internet, call it once for real and compare the answer with the source.
4. If the tool stores data, read the data back with GetData.
5. If something fails, fix it with UpdateMethod. The previous version stays active until the new one compiles.
6. Report what you called and what came back, in a few lines.
