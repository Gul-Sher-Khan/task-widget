# Sign in with ChatGPT for an open-source desktop app

Research for issue #2 (child of map #1). Researched 2026-10-06. Sources are OpenAI developer docs, OpenAI Help Center, OpenAI's terms, OpenAI's OIDC discovery document, the official OpenAI devkit repo, Microsoft/Electron docs, and the OpenRouter and Groq docs and APIs. Where a claim could not be verified from a primary source it is marked **Unverified**.

## Short answer

Yes. OpenAI ships a flow built for this case: "ChatGPT plan usage in your open-source app". It needs no client secret and no OpenAI-issued client ID in the source. Each user's first sign-in dynamically registers a client bound to that user and workspace. The app opens the system browser and receives the code on a `127.0.0.1` loopback callback with PKCE. It then calls the public Responses API (`POST https://api.openai.com/v1/responses`) with the user's OAuth access token, and usage counts against that user's ChatGPT plan. Constraints that matter for Task Widget:

- Plan usage needs a paid plan. The developer docs say Plus and Pro; the Help Center now also names Go. Free-tier access through open-source tools is ambiguous (see Eligibility).
- The Responses API is restricted on this route: `stream: true` and `store: false` are required, and `temperature` and `max_output_tokens` are rejected. Models are account-specific and must be listed at runtime.
- The terms forbid charging users for plan usage. Tokens must be stored locally under the user's control, and the plan may only be used for the connected app.
- OpenAI's devkit SDK (`@siwc/local`, `@siwc/react`) is under a **noncommercial** license. To keep Task Widget under a normal open-source license, implement the documented HTTP flow ourselves rather than vendoring the devkit.

## Eligibility

| Question | Finding | Source |
|---|---|---|
| Which plans can spend plan usage? | Developer docs: "Eligible ChatGPT Plus and Pro users can use their ChatGPT plan for AI requests in participating apps." | [Quickstart](https://developers.openai.com/siwc/quickstart) |
| | Help Center (updated ~2026-10-05): plan usage "in participating commercial apps and sites is only available with Go, Plus, and Pro", and "Supported open-source tools remain available to all ChatGPT users." | [Help: Using your ChatGPT plan in other apps](https://help.openai.com/en/articles/20001542-using-your-chatgpt-plan-in-other-apps-and-sites) |
| Free users in open-source tools? | **Unverified / ambiguous.** "Remain available to all ChatGPT users" could mean Free users can spend plan usage in OSS tools, or only that they can sign in. The developer docs only name Plus and Pro. Test with a Free account before promising it. The app must handle `subscription_sharing_user_not_eligible` (403) anyway. | Help page above; [Errors and recovery](https://developers.openai.com/siwc/token-sharing-open-source/errors-and-recovery) |
| Individuals vs. developers | No developer approval is needed for OSS apps: "ChatGPT plan usage is available to all open-source partners and selected private clients." Identity-only sign-in for commercial sites is a limited trial/waitlist. Paid or remotely hosted apps must use an [interest form](https://developers.openai.com/siwc/token-sharing-open-source). | [Quickstart](https://developers.openai.com/siwc/quickstart), [Overview](https://developers.openai.com/siwc/token-sharing-open-source), [Request a client ID](https://developers.openai.com/siwc/request-client-id) |
| Business / Enterprise workspaces | Identity sign-in is available to Enterprise users, subject to org admin policy. The client is bound to the workspace chosen at registration. **Unverified:** whether Business/Enterprise/Edu workspaces may spend plan usage through an OSS client. Only Go/Plus/Pro are named. | [Help: Sign in with ChatGPT](https://help.openai.com/en/articles/20001410-sign-in-with-chatgpt), [Overview](https://developers.openai.com/siwc/token-sharing-open-source) |
| Region | A direct-route 403 can mean "permitted serving region" blocked admission. No region list is published (**Unverified**). | [Errors and recovery](https://developers.openai.com/siwc/token-sharing-open-source/errors-and-recovery) |

## Client registration for a redistributed open-source app

- **No shipped client ID or secret.** The first authorization request uses the literal `client_id=dynamic_agent_client`, plus `agent_name_hint` (our app name, e.g. `Task Widget`) and `ext_agent_host_id`. After consent, the callback returns an issued `client_id` (`oaiapp_...`) "bound to the authenticated user and the workspace selected during registration". The app saves it and reuses it for later sign-ins and refreshes. "This direct flow needs neither a client secret nor a partner API key." So the public source contains only the constant `dynamic_agent_client` and the app name, and there is nothing secret to leak. ([Registration and sign-in](https://developers.openai.com/siwc/token-sharing-open-source/sign-in), [Overview](https://developers.openai.com/siwc/token-sharing-open-source))
- **Registration is per user and workspace, done automatically.** One issued client per (user, workspace) can serve several hosts. Each install generates its own stable, opaque **host ID** before the first sign-in: `urn:uuid:<uuidv4>`, or preferably an RFC 9278 JWK thumbprint URI, or `did:key`. It is not a credential and must not be user-identifying. ([Overview](https://developers.openai.com/siwc/token-sharing-open-source))
- The [Request a client ID](https://developers.openai.com/siwc/request-client-id) page is a commercial waitlist and does not apply to the OSS flow.
- Multiple accounts: keep one credential record per issued client ID. Never mix one registration's client ID with another's tokens. ([Accounts and sessions](https://developers.openai.com/siwc/token-sharing-open-source/profiles-and-sessions))

## Native-app OAuth flow

From [Registration and sign-in](https://developers.openai.com/siwc/token-sharing-open-source/sign-in) and the live [OIDC discovery document](https://auth.openai.com/.well-known/openid-configuration):

1. Start a listener on `http://127.0.0.1:<port>/auth/callback`. Only the port may vary between attempts. Use `127.0.0.1`, not `localhost`, and keep the path exactly `/auth/callback`. Start the listener before opening the browser.
2. Generate fresh `state`, OIDC `nonce`, and a PKCE verifier (`S256` only) for each attempt.
3. Open the **system browser** at `https://auth.openai.com/api/accounts/authorize` with:
   - `response_type=code`
   - `scope=openid profile email offline_access resource.invoke chatgpt.tokens.use.direct`
   - `resource=https://api.openai.com/v1`
   - `redirect_uri`, `state`, `nonce`, `code_challenge`, `code_challenge_method=S256`
   - `client_id`: `dynamic_agent_client` plus `agent_name_hint` on first registration; the saved issued ID afterwards
   - `ext_agent_host_id`
   - optional `id_token_hint`/`login_hint` on reauthorization (these skip the account picker)
4. The callback returns `code`, `state`, `scope`, and on a new registration the issued `client_id`. Save the client ID before exchanging the code. `error=access_denied` means consent was declined.
5. POST form-encoded `grant_type=authorization_code` to `https://auth.openai.com/api/accounts/oauth/token` with the issued `client_id`, `code`, `code_verifier`, the same `redirect_uri`, and `resource`. No secret is sent; discovery lists token auth method `none`.
6. Validate the ID token (RS256, JWKS at `https://auth.openai.com/.well-known/jwks.json`) against issuer, audience (the issued client ID), expiry, and nonce. Then **check the granted scopes include `chatgpt.tokens.use.direct`**. A valid ID token alone does not authorize plan usage.

**Device code flow is not available.** Discovery advertises only the `authorization_code` and `refresh_token` grants and has no `device_authorization_endpoint`, and the docs describe only the loopback flow. For a desktop app this does not matter. (The [Self-hosted VMs](https://developers.openai.com/siwc/token-sharing-open-source/self-hosted-vms) guide confirms this: headless hosts sign in locally and copy the credential file.)

## Tokens: lifetime, refresh, revocation

From the [Token reference](https://developers.openai.com/siwc/token-sharing-open-source/token-reference) and [Accounts and sessions](https://developers.openai.com/siwc/token-sharing-open-source/profiles-and-sessions):

- Access token: **1 hour** (`expires_in: 3600`). It is a JWT with `aud=https://api.openai.com/v1`.
- Refresh token: **30 days, rotating**. Each refresh returns a new refresh token with a fresh 30 days, with no fixed cap on successive refreshes. A user who opens the app at least once every 30 days stays signed in indefinitely.
- Refresh: POST `grant_type=refresh_token`, the issued `client_id`, `refresh_token`, and `resource=https://api.openai.com/v1` to the token endpoint, omitting `scope`. Replace the access token, expiry, scopes, and refresh token together. **Serialize refreshes**, because rotating tokens race. Errors such as `invalid_grant`, `refresh_token_expired`, or `refresh_token_reused` mean clearing the tokens and repeating OAuth with the saved client ID.
- Sign-out: POST `token=<refresh>`, `token_type_hint=refresh_token`, and `client_id` to `https://auth.openai.com/api/accounts/oauth/revoke`. Keep the client-ID mapping and the host ID for the next sign-in.
- OpenAI does not notify the app when the user disconnects it in ChatGPT settings. The app learns this from a failed request or refresh.

## Secure token storage on Windows

- OpenAI requires protected local storage that is atomic and owner-only, and never browser storage, logs, or source control. The SIWC Terms add: "Any persistent storage of Authentication Tokens must be local and under the user's control." ([Registration and sign-in §5](https://developers.openai.com/siwc/token-sharing-open-source/sign-in), [SIWC Terms §1](https://openai.com/policies/sign-in-with-chatgpt-terms/))
- OpenAI's own SDK requires an OS-backed encryption provider and has no plaintext fallback. Its example uses Electron `safeStorage`. ([devkit docs/security.md](https://github.com/openai/sign-in-with-chatgpt-devkit/blob/main/docs/security.md))
- On Windows, Electron `safeStorage` is DPAPI ([Electron docs](https://www.electronjs.org/docs/latest/api/safe-storage)). Without Electron, call DPAPI directly: [`CryptProtectData`](https://learn.microsoft.com/en-us/windows/win32/api/dpapi/nf-dpapi-cryptprotectdata) with no `CRYPTPROTECT_LOCAL_MACHINE`, so only the same Windows user can decrypt, usually only on the same machine. Write the ciphertext atomically to a file under `%LOCALAPPDATA%\TaskWidget\`. DPAPI does not protect against other processes running as the same user.
- Windows Credential Manager (`CredWrite`) is a poor fit for the whole record. A generic credential blob is capped at `CRED_MAX_CREDENTIAL_BLOB_SIZE` = 5×512 = **2,560 bytes** ([CREDENTIALW](https://learn.microsoft.com/en-us/windows/win32/api/wincred/ns-wincred-credentialw)), and the record holds an ID token, an access-token JWT, and a refresh token. A DPAPI-encrypted file is simpler. Store the host ID in a separate unencrypted file; it is not a secret.

## Models and endpoints reachable

From [Models and inference](https://developers.openai.com/siwc/token-sharing-open-source/models-and-inference) and [Preview limitations](https://developers.openai.com/siwc/token-sharing-open-source/preview-limitations):

- **Supported endpoints:** `GET https://api.openai.com/v1/models`, which returns an account-specific catalog (filter `visibility == "list"`, show `display_name`, send `slug`), and `POST https://api.openai.com/v1/responses`. Other routes return `subscription_sharing_route_not_supported`. Chat Completions, Files, and transcription are not supported. The docs say not to use ChatGPT `backend-api`.
- **Request constraints that affect our one-call Capture → Tasks design:**
  - `stream: true` and `store: false` are required on every request.
  - Put the system prompt in `instructions` or a developer message. `role: "system"` items are rejected.
  - Not accepted: `temperature`, `top_p`, `max_output_tokens`, `metadata`, `user`, `previous_response_id`, `background`, `truncation`, and others.
  - Hosted tools (file search, code interpreter, image generation, MCP) are unsupported. Function tools are allowed when grouped in namespaces.
  - Text and image inputs work. Audio input and transcription do not, which is fine because dictation happens outside the app (Wispr Flow).
- **Which models, and which is cheapest: Unverified.** The docs' only example slug is `gpt-6.1-sol`. The catalog depends on the account, and OpenAI publishes no list of the mini/nano models exposed on this route, nor any per-model usage weighting ("Usage rates may differ between an app and ChatGPT"). The app should list models at runtime, choose a default by a preference list, and let the user override it.
- **Structured outputs (`text.format` with `json_schema`): Unverified.** It is not on the unsupported list, but no page confirms it. Test it with a real token before relying on it, and keep a tolerant JSON parser as a fallback.

## Does usage count against the user's plan? Rate limits

- Yes. "Eligible AI requests count toward the ChatGPT Work and Codex usage included in your plan." If the user has opted in, overflow can draw on ChatGPT credits; this is off by default. Plan usage never falls back silently to another billing path. ([Help: Using your ChatGPT plan](https://help.openai.com/en/articles/20001542-using-your-chatgpt-plan-in-other-apps-and-sites), [Errors and recovery](https://developers.openai.com/siwc/token-sharing-open-source/errors-and-recovery))
- **Limits are plan quotas, not API-style RPM/TPM:**
  - Plus has a five-hour usage limit shared across all apps using the plan. Pro does not have the five-hour limit. ([Accounts and sessions](https://developers.openai.com/siwc/token-sharing-open-source/profiles-and-sessions))
  - Users can set a weekly per-app cap as a percentage of their overall weekly usage in ChatGPT Settings → Usage. It is a cap, not a reserved pool. ([Help](https://help.openai.com/en/articles/20001542-using-your-chatgpt-plan-in-other-apps-and-sites), [learn.chatgpt.com](https://learn.chatgpt.com/docs/sign-in-with-chatgpt))
  - Hitting a limit returns `subscription_sharing_usage_limit_exceeded` (429), possibly mid-stream as `response.failed`. The UI should show "Manage usage" linking to ChatGPT settings ([UI/UX guidelines](https://developers.openai.com/siwc/ui-ux-guidelines)).
  - **Unverified:** OpenAI publishes no numbers for the five-hour or weekly limits on this route, and no per-request rate limit. One small Responses call per Capture should be negligible against a coding-oriented quota, but that cannot be quantified from the docs.

## Terms that affect open-source distribution

[Sign in with ChatGPT Terms](https://openai.com/policies/sign-in-with-chatgpt-terms/), dated 2026-09-29:

- Use our own app name. Do not impersonate OpenAI or another OSS project. Obtain tokens only through OpenAI's flow, and never ask for ChatGPT passwords or cookies.
- Requests must come from the user's local runtime, or a remote runtime only that user controls, and must arise from the user's activity. Background use needs express consent.
- **Connected application only:** no general-purpose API proxying for other tools.
- **No charge:** users must be able to use their plan through SIWC "without paying you or upgrading to a paid version of your application." Free open-source software meets this.
- Prohibited: pooling, reselling, or sharing usage or tokens; using one user's subscription for another user's requests; multiple accounts to bypass limits; altering or reverse-engineering the SIWC software.
- A privacy notice is required before processing personal data, and data collection must be limited to what is reasonably necessary. This feeds the open "Privacy" item on map #1.
- Branding: use the "Continue with ChatGPT" label and approved assets only ([UI/UX guidelines](https://developers.openai.com/siwc/ui-ux-guidelines)). The guidelines also ask for a one-time "You're using your ChatGPT plan" modal, a "Using ChatGPT plan" indicator, and a Manage usage link.
- OpenAI may suspend an app's SIWC access for violations.
- **Devkit license:** [`openai/sign-in-with-chatgpt-devkit`](https://github.com/openai/sign-in-with-chatgpt-devkit/blob/main/LICENSE) is under the "Sign-in with ChatGPT DevKit Noncommercial License v1.0". Distribution is allowed only for noncommercial purposes, modified files must carry the same license, and no trademark rights are granted. Software that only calls the devkit through an interface is not a Modified Work, but redistributing the devkit inside our installer still binds that part to noncommercial terms. The devkit's sample apps also target Node.js/Electron and macOS. Recommendation: implement the flow ourselves from the docs. It is small: a loopback listener, PKCE, a token POST, JWKS verification, and DPAPI.

## Fallback: OpenAI-compatible API key (OpenRouter / Groq)

Both expose OpenAI-compatible Chat Completions. Groq's base URL is `https://api.groq.com/openai/v1` ([Groq models](https://console.groq.com/docs/models)), and OpenRouter's is `https://openrouter.ai/api/v1` ([OpenRouter quickstart](https://openrouter.ai/docs/quickstart)). The fallback client therefore speaks Chat Completions, while the SIWC path speaks Responses; plan for two thin adapters.

### Groq (snapshot 2026-10-06)

Free plan limits ([Groq rate limits](https://console.groq.com/docs/rate-limits)) are per organization:

| Model | RPM | RPD | TPM | TPD | Paid price per 1M tokens (in / out) | Strict JSON schema |
|---|---|---|---|---|---|---|
| `openai/gpt-oss-20b` | 30 | 1K | 8K | 200K | $0.075 / $0.30 | yes |
| `openai/gpt-oss-120b` | 30 | 1K | 8K | 200K | $0.15 / $0.60 | yes |
| `qwen/qwen3.8-27b` (preview) | 30 | 1K | 8K | 200K | $0.80 / $4.00 | yes |

Prices are from [Groq models](https://console.groq.com/docs/models); strict-mode support is from [Groq structured outputs](https://console.groq.com/docs/structured-outputs). `llama-3.1-8b-instant` is now listed as Enterprise / contact sales, so it is not a free option.

**Pick: `openai/gpt-oss-20b`.** It is the cheapest and fastest (~1000 tok/s), supports strict `json_schema`, and its free tier of 1K requests/day is far more than a personal task list needs.

### OpenRouter (snapshot 2026-10-06)

Free-model limits ([OpenRouter limits](https://openrouter.ai/docs/api/reference/limits)) are **20 RPM**, plus **50 requests/day** if the account has bought less than 10 credits ever, or **1000/day** with 10 or more. Extra keys or accounts do not raise the limits. A negative balance can return 402 even on free models.

Free models change often. From the live [models API](https://openrouter.ai/api/v1/models), these support `structured_outputs`: `nvidia/nemotron-3-super-120b-a12b:free`, `apodex/apodex-1.1-mini:free`, `dots-studio/dots-3-note-preview:free`, `liquid/lfm-2.5-2.6b:free`, and the `openrouter/free` router, which picks a random free model. The Gemma 4 `:free` models support `response_format` only. Do not hard-code a `:free` slug; let users pick, and default to a paid slug.

Cheapest paid models with `structured_outputs` (per 1M tokens, in / out):

| Model | Price |
|---|---|
| `mistralai/mistral-nemo` | $0.019 / $0.03 |
| `openai/gpt-oss-20b` | $0.018 / $0.09 |
| `meta-llama/llama-3.1-8b-instruct` | $0.05 / $0.08 |

A Capture of a few hundred tokens costs well under $0.0001.

Privacy: OpenRouter lets users opt out of routing to providers that train on prompts, with separate settings for paid and free models ([OpenRouter logging](https://openrouter.ai/docs/guides/privacy/logging)). This is relevant to the privacy disclosure.

**Pick:**
- Default model on both providers: `openai/gpt-oss-20b`. It is the same model everywhere, so one prompt and schema works across Groq and OpenRouter.
- Free option: Groq's free tier, which has much more headroom than OpenRouter's 50/day.

## Open questions to test with a real account

1. Can a Free (or Go) account spend plan usage in an OSS client, or only sign in?
2. Which model slugs does `/v1/models` return for Plus and Pro? Is there a small, fast model suitable for Capture → Tasks?
3. Does `text.format: {type: "json_schema", strict: true}` work on this route?
4. How much of the Plus five-hour or weekly quota does one small request consume?
