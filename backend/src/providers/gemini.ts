// Gemini generateContent with structured JSON output. Host and path are constants; only the validated model id
// is interpolated. The key goes in the x-goog-api-key header, never the URL.
// Unverified live behaviour (no key was available): the header on this endpoint, responseJsonSchema,
// thinkingLevel MINIMAL on the configured model. See backend/README.md.
import { callProvider, errorForStatus, ProviderError, readLimited, sleep, type Fetch } from './common.ts';

export const GEMINI_ORIGIN = 'https://generativelanguage.googleapis.com';

export interface GeminiDeps { fetch: Fetch; key: string; model: string; maxBytes: number }
export interface GeminiCall { system: string; user: unknown; schema: object; temperature: number; deadlineMs: number }

const SAFETY = ['HARM_CATEGORY_HARASSMENT', 'HARM_CATEGORY_HATE_SPEECH', 'HARM_CATEGORY_SEXUALLY_EXPLICIT', 'HARM_CATEGORY_DANGEROUS_CONTENT']
  .map((category) => ({ category, threshold: 'BLOCK_MEDIUM_AND_ABOVE' }));

export function geminiRequest(deps: GeminiDeps, call: GeminiCall): { url: string; init: RequestInit } {
  const url = `${GEMINI_ORIGIN}/v1beta/models/${encodeURIComponent(deps.model)}:generateContent`;
  const body = {
    systemInstruction: { parts: [{ text: call.system }] },
    contents: [{ role: 'user', parts: [{ text: JSON.stringify(call.user) }] }],
    generationConfig: {
      responseMimeType: 'application/json',
      responseJsonSchema: call.schema,
      maxOutputTokens: 200,
      temperature: call.temperature,
      thinkingConfig: { thinkingLevel: 'MINIMAL' },
    },
    safetySettings: SAFETY,
  };
  return {
    url,
    init: { method: 'POST', headers: { 'content-type': 'application/json', 'x-goog-api-key': deps.key }, body: JSON.stringify(body) },
  };
}

interface GeminiResponse {
  promptFeedback?: { blockReason?: string };
  candidates?: { finishReason?: string; content?: { parts?: { text?: unknown; thought?: unknown }[] } }[];
}

function record(value: unknown): value is Record<string, unknown> {
  return typeof value === 'object' && value !== null && !Array.isArray(value);
}

function invalidKey(body: unknown): boolean {
  if (!record(body) || !record(body.error)) return false;
  if (body.error.status === 'UNAUTHENTICATED') return true;
  const details = body.error.details;
  return Array.isArray(details) && details.some((detail: unknown) => record(detail)
    && detail['@type'] === 'type.googleapis.com/google.rpc.ErrorInfo'
    && detail.domain === 'googleapis.com'
    && (detail.reason === 'API_KEY_INVALID' || detail.reason === 'API_KEY_EXPIRED'));
}

async function once(deps: GeminiDeps, call: GeminiCall, deadlineMs: number, signal?: AbortSignal): Promise<unknown> {
  const { url, init } = geminiRequest(deps, call);
  const parsed = await callProvider(deps.fetch, url, init, deadlineMs, signal, async (res) => {
    if (res.status !== 200) {
      if (res.status === 400) {
        const raw = await readLimited(res, deps.maxBytes);
        let body: unknown;
        try { body = JSON.parse(raw.toString('utf8')); } catch { throw errorForStatus(400); }
        if (invalidKey(body)) throw new ProviderError('auth', 400, true);
      } else await res.body?.cancel().catch(() => {});
      throw errorForStatus(res.status);
    }
    const raw = await readLimited(res, deps.maxBytes);
    try { return JSON.parse(raw.toString('utf8')) as GeminiResponse; } catch { throw new ProviderError('invalid', 200); }
  });
  if (parsed?.promptFeedback?.blockReason) throw new ProviderError('invalid', 200);
  const cand = parsed?.candidates?.[0];
  if (!cand || cand.finishReason !== 'STOP') throw new ProviderError('invalid', 200);
  const text = (cand.content?.parts ?? []).filter((p) => p.thought !== true && typeof p.text === 'string').map((p) => p.text as string).join('');
  try { return JSON.parse(text); } catch { throw new ProviderError('invalid', 200); }
}

/** One call, plus one retry on 429/500/503 when the deadline still allows it. */
export async function geminiGenerate(deps: GeminiDeps, call: GeminiCall, now: () => number, signal?: AbortSignal, allowRetry = true): Promise<unknown> {
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
