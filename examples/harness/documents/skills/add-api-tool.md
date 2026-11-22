---
description: The procedure to turn a public web API into one or more tools
tags: kind:skill, topic:api
---
# Add a tool for a web API

Use this when you are asked to "use this API" or to "add a tool for" some web service.

1. Call GetAvailableMethods. If a tool already covers the need, use it or update it instead of adding another.
2. Read the documentation of the API or its OpenAPI description. Do not guess endpoints or fields.
3. Prefer an API that needs no key. If a key is needed, ask the person how to give it at call time. Never write it in
   the code or in the data store.
4. Add one tool per useful action, not one per endpoint, and name it for what the person wants (`GetCurrentWeather`),
   not for the URL.
5. Describe the tool and every parameter, with units. See `doc:howto/describe-a-tool`.
6. In the code, use HttpClient with a timeout, check the status of the response, and return a short text. Do not return
   the whole response.
7. Review the code with `doc:agents/tool-reviewer`, add the tool, then check it with `doc:howto/verify-a-tool`.
