// OpenRouter chat completions. Only free models are accepted, even when called outside loadConfig.
import { callProvider, errorForStatus, ProviderError, readLimited, sleep, type Fetch } from './common.ts';

export const OPENROUTER_ORIGIN = 'https://openrouter.ai';
export function isFreeModel(model: string): boolean {
  return model === 'openrouter/free' || /^[a-zA-Z0-9_.-]+\/[a-zA-Z0-9_.-]+:free$/.test(model);
}
export interface OpenRouterDeps { fetch: Fetch; key: string; model: string; maxBytes: number }
export interface OpenRouterCall { system: string; user: unknown; schema: object; temperature: number; deadlineMs: number }

export function openRouterRequest(deps: OpenRouterDeps, call: OpenRouterCall): { url: string; init: RequestInit } {
  if (!isFreeModel(deps.model)) throw new ProviderError('invalid');
  return {
    url: `${OPENROUTER_ORIGIN}/api/v1/chat/completions`,
    init: {
      method: 'POST',
      headers: { 'content-type': 'application/json', authorization: `Bearer ${deps.key}` },
      body: JSON.stringify({
        model: deps.model,
        messages: [{ role: 'system', content: call.system }, { role: 'user', content: JSON.stringify(call.user) }],
        response_format: { type: 'json_schema', json_schema: { name: 'game_reply', strict: true, schema: call.schema } },
        provider: { require_parameters: true, max_price: { prompt: 0, completion: 0 } },
        max_tokens: 200,
        temperature: call.temperature,
      }),
    },
  };
}

async function once(deps: OpenRouterDeps, call: OpenRouterCall, deadlineMs: number, signal?: AbortSignal): Promise<unknown> {
  const { url, init } = openRouterRequest(deps, call);
  const parsed = await callProvider(deps.fetch, url, init, deadlineMs, signal, async (res) => {
    if (res.status !== 200) {
      await res.body?.cancel().catch(() => {});
      throw errorForStatus(res.status);
    }
    const raw = await readLimited(res, deps.maxBytes);
    try { return JSON.parse(raw.toString('utf8')); } catch { throw new ProviderError('invalid', 200); }
  });
  if (parsed?.error) {
    const code = parsed.error.code;
    throw typeof code === 'number' ? errorForStatus(code) : new ProviderError('invalid', 200);
  }
  const choice = parsed?.choices?.[0];
  if (choice?.finish_reason !== 'stop' || typeof choice.message?.content !== 'string') throw new ProviderError('invalid', 200);
  try { return JSON.parse(choice.message.content); } catch { throw new ProviderError('invalid', 200); }
}

/** One call, plus one retry on 429/500/503 when the deadline still allows it. */
export async function openRouterGenerate(deps: OpenRouterDeps, call: OpenRouterCall, now: () => number, signal?: AbortSignal, allowRetry = true): Promise<unknown> {
  const start = now();
  try {
    return await once(deps, call, call.deadlineMs, signal);
  } catch (e) {
    if (!allowRetry || !(e instanceof ProviderError) || !e.retryable || (e.status !== 429 && e.status !== 500 && e.status !== 503)) throw e;
    const wait = 250 + Math.floor(Math.random() * 250);
    const left = call.deadlineMs - (now() - start) - wait;
    if (left < 500) throw e;
    await sleep(wait, signal);
    return await once(deps, call, left, signal);
  }
}
