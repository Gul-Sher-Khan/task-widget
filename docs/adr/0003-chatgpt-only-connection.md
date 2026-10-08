# ChatGPT is the only Connection

Task Widget turns Captures into Tasks only through the user's ChatGPT account (Sign in with ChatGPT). There is no OpenAI-compatible API-key Connection (Groq, OpenRouter or custom), and no fallback when ChatGPT is rate-limited or signed out. Sign in with ChatGPT already works end to end, and one path means one request adapter, one prompt to tune and one set of failures to design for.

## Consequences

- Using the app needs a ChatGPT plan that allows plan usage in open-source apps (Go, Plus or Pro; Free is unverified). Users without one can't use it.
- When ChatGPT is rate-limited or signed out, Captures wait (they are never lost) until it works again; nothing else is tried.
- Supersedes the earlier draft decision "One active Connection, no automatic failover", which assumed a second, API-key Connection.
