// Test harness: a real local HTTP server around the service, with an injected provider fetch that records
// every call and never reaches the network. No test calls a paid endpoint.
import type { AddressInfo } from 'node:net';
import { loadConfig, type Config } from '../src/config.ts';
import { Logger } from '../src/log.ts';
import { createApp } from '../src/server.ts';
import { VoiceService } from '../src/service.ts';
import type { Fetch } from '../src/providers/common.ts';

export const SECRETS = {
  game: 'test-game-token-0123456789abcdef',
  session: 'test-session-secret-0123456789abcdef-0123456789',
  openRouter: 'AIzaTEST-openRouter-key-do-not-log-1234567890',
  eleven: 'sk_test_eleven_key_do_not_log_1234567890',
};

export function env(over: Record<string, string | undefined> = {}): NodeJS.ProcessEnv {
  return {
    FNMW_PROVIDER_MODE: 'live',
    FNMW_GAME_TOKENS: SECRETS.game,
    FNMW_SESSION_SECRET: SECRETS.session,
    OPENROUTER_API_KEY: SECRETS.openRouter,
    ELEVENLABS_API_KEY: SECRETS.eleven,
    FNMW_VOICE_MONSTER: 'voiceMonster01',
    FNMW_VOICE_SHIP: 'voiceShip01',
    FNMW_VOICE_NATIVE: 'voiceNative01',
    ...over,
  };
}

export interface Call { url: string; init: RequestInit; body: any; headers: Record<string, string> }
export type Responder = (call: Call, index: number) => Promise<Response> | Response;

export class FakeFetch {
  calls: Call[] = [];
  queue: Responder[] = [];
  fallback: Responder = () => new Response('no responder', { status: 500 });
  readonly fn: Fetch = async (url, init) => {
    const headers: Record<string, string> = {};
    new Headers(init.headers).forEach((v, k) => { headers[k] = v; });
    const call: Call = { url, init, headers, body: typeof init.body === 'string' ? JSON.parse(init.body) : undefined };
    this.calls.push(call);
    const r = this.queue.shift() ?? this.fallback;
    return await r(call, this.calls.length - 1);
  };
  openRouter(): Call[] { return this.calls.filter((c) => c.url.startsWith('https://openrouter.ai/')); }
  eleven(): Call[] { return this.calls.filter((c) => c.url.startsWith('https://api.elevenlabs.io/')); }
}

export function openRouterJson(obj: unknown, finishReason = 'stop'): Response {
  return new Response(JSON.stringify({ choices: [{ finish_reason: finishReason, message: { content: typeof obj === 'string' ? obj : JSON.stringify(obj) } }] }), {
    status: 200, headers: { 'content-type': 'application/json' },
  });
}

export function pcm(samples = 2400): Response {
  return new Response(Buffer.alloc(samples * 2, 1), { status: 200, headers: { 'content-type': 'audio/pcm' } });
}

/** Never resolves until the request's signal aborts (a provider that hangs). */
export function hang(call: Call): Promise<Response> {
  return new Promise((_, reject) => {
    const s = call.init.signal!;
    if (s.aborted) reject(s.reason);
    s.addEventListener('abort', () => reject(s.reason), { once: true });
  });
}

export interface Harness {
  base: string;
  svc: VoiceService;
  cfg: Config;
  fake: FakeFetch;
  logs: string[];
  clock: { t: number };
  close: () => Promise<void>;
  post: (path: string, body: unknown, token?: string, extra?: RequestInit) => Promise<Response>;
  get: (path: string, token?: string) => Promise<Response>;
  session: () => Promise<string>;
}

export async function start(over: Record<string, string | undefined> = {}, tweak?: (cfg: Config) => void): Promise<Harness> {
  const cfg = loadConfig(env(over));
  tweak?.(cfg);
  const fake = new FakeFetch();
  const logs: string[] = [];
  const clock = { t: Date.UTC(2026, 10, 15, 12, 0, 0) };
  const svc = new VoiceService({ config: cfg, fetch: fake.fn, now: () => clock.t, logger: new Logger((l) => logs.push(l)) });
  const server = createApp(svc);
  await new Promise<void>((r) => server.listen(0, '127.0.0.1', r));
  const base = `http://127.0.0.1:${(server.address() as AddressInfo).port}`;
  const post = (path: string, body: unknown, token?: string, extra: RequestInit = {}) =>
    fetch(base + path, {
      method: 'POST',
      headers: { 'content-type': 'application/json', ...(token ? { authorization: `Bearer ${token}` } : {}) },
      body: typeof body === 'string' ? body : JSON.stringify(body),
      ...extra,
    });
  const get = (path: string, token?: string) => fetch(base + path, { headers: token ? { authorization: `Bearer ${token}` } : {} });
  const session = async () => {
    const r = await post('/v1/session', { proto: 1, build: 'test' }, SECRETS.game);
    if (r.status !== 200) throw new Error('session failed ' + r.status);
    return ((await r.json()) as { session: string }).session;
  };
  return {
    base, svc, cfg, fake, logs, clock, post, get, session,
    close: () => new Promise<void>((r) => { server.closeAllConnections(); server.close(() => r()); }),
  };
}

export const monsterBody = (over: Record<string, unknown> = {}) => ({
  seq: 1, trigger: 'repeat_concept', stage: 2, concept: 'verb-final', concept_misses: 2, wrong_streak: 1, mistakes: 3,
  solved: 1, total: 5, pace: 'slow', distance: 'near', aggression: 'high', ...over,
});

export const hintBody = (over: Record<string, unknown> = {}) => ({
  seq: 1, level: 1, concept: 'verb-final', attempts: 2, target_meaning: 'Please open the door.',
  examples: [
    { meaning: 'Please close the door.', words: ['tobira', 'o', 'shimete', 'kudasai'] },
    { meaning: 'Please open the window.', words: ['mado', 'o', 'akete', 'kudasai'] },
    { meaning: 'Please eat the fish.', words: ['sakana', 'o', 'tabete', 'kudasai'] },
  ],
  tiles: ['akete', 'tobira', 'o', 'kudasai', 'shimete'],
  ...over,
});
