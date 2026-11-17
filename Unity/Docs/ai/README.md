# OpenRouter in the game

`Services.Ai` sends chat completions to `https://openrouter.ai/api/v1/chat/completions`. The default `openrouter/free` router selects an available free model. Overrides must use a `provider/model:free` id; requests also cap prompt and completion prices at zero.

Set `OPENROUTER_API_KEY` and optionally `OPENROUTER_MODEL` in the Editor environment, or use `openRouterApiKey` and `openRouterModel` in the git-ignored `Unity/secrets.json`. Set `FNMW_AI_DISABLED=1` to use stock text. Player builds ignore direct keys and require a proxy.


An optional `aiProxyUrl` / `FNMW_AI_PROXY_URL` receives the same chat completions body without an Authorization header and must add its own key. No generic text relay is included in the voice backend.

The wire client sends system/user messages, a standard JSON Schema response format, and a Bearer authorization header for direct calls. Only completed responses are accepted. Per-feature budgets, deadlines, cancellation, validation and stock fallbacks remain in place. The run debrief uses this client; monster dialogue and ship hints use the separate voice backend.

`AiKeyBuildGuard` still rejects additional keys placed in bundled Resources settings. Keys are never bundled into player builds.
