// The five endpoints' logic, independent of HTTP so tests can drive it with a fake clock and fake fetch.
import { createHash, randomBytes } from 'node:crypto';
import { features, nativeVoice, VOICE_SETTINGS_VERSION, type Config, type Lang } from './config.ts';
import { SessionSigner, checkGameToken } from './auth.ts';
import { networkKey } from './client-ip.ts';
import { DailyBudget, Gate, ProviderSwitch, WindowCounter } from './limits.ts';
import { LruCache } from './cache.ts';
import { Logger } from './log.ts';
import { HINT_FACTS, FACTS_VERSION, guidedHint } from './hintfacts.ts';
import { HINT_SYSTEM, MONSTER_SYSTEM, PROMPT_VERSION, TRIGGER_MEANING } from './prompts.ts';
import { checkHint, checkMonsterLine } from './textcheck.ts';
import type { HintRequest, MonsterRequest, PronounceRequest, SessionRequest } from './validate.ts';
import { ProviderError, type Fetch } from './providers/common.ts';
import { geminiGenerate } from './providers/gemini.ts';
import { SAMPLE_RATE, synthesize, type Channel } from './providers/elevenlabs.ts';
import { mockGemini, mockTts } from './providers/mock.ts';

export type ErrorCode =
  | 'unauthorized' | 'bad_request' | 'too_large' | 'not_found' | 'rate_limited' | 'invalid_generation'
  | 'provider_unavailable' | 'provider_timeout' | 'disabled' | 'budget_exhausted';

const STATUS: Record<ErrorCode, number> = {
  unauthorized: 401, bad_request: 400, too_large: 413, not_found: 404, rate_limited: 429, invalid_generation: 502,
  provider_unavailable: 503, provider_timeout: 504, disabled: 503, budget_exhausted: 503,
};

export class ApiError extends Error {
  readonly code: ErrorCode;
  readonly retryAfterMs: number | undefined;
  constructor(code: ErrorCode, retryAfterMs?: number) {
    super(code);
    this.code = code; this.retryAfterMs = retryAfterMs;
  }
  get status(): number { return STATUS[this.code]; }
}

export type Source = 'live' | 'cache' | 'mock';
export interface SpeechRef { id: string; sample_rate: number; version: string; source: Source }

interface Session {
  sid: string;
  exp: number; // ms
  network: string;
  lastSeen: number;
  used: { monster: number; hint: number; pronounce: number; speechChars: number; speechFetches: number };
  recentLines: string[];
  activeTts: number;
}

interface CachedAudio { pcm: Buffer; mock: boolean }

interface Job {
  id: string;
  sid: string;
  contentKey: string;
  createdAt: number;
  done: Promise<void>;
  settled: boolean;
  error?: ApiError;
  source: Source;
}

export interface ServiceDeps {
  config: Config;
  fetch: Fetch;
  now?: () => number;
  logger?: Logger;
}

const randomId = (): string => randomBytes(16).toString('hex');

export class VoiceService {
  readonly cfg: Config;
  readonly #fetch: Fetch;
  readonly now: () => number;
  readonly log: Logger;
  readonly signer: SessionSigner;
  readonly #sessions = new Map<string, Session>();
  readonly #ipSessions: WindowCounter;
  readonly #ipRequests: WindowCounter;
  readonly geminiGate: Gate;
  readonly ttsGate: Gate;
  readonly dailyGemini: DailyBudget;
  readonly dailySpeech: DailyBudget;
  readonly geminiSwitch: ProviderSwitch;
  readonly elevenSwitch: ProviderSwitch;
  readonly #hintCache: LruCache<{ text: string; kind: 'generated' | 'guided'; rows: number[] }>;
  readonly speechCache: LruCache<CachedAudio>;
  readonly #jobs = new Map<string, Job>();
  readonly #inflight = new Map<string, Promise<void>>();

  constructor(deps: ServiceDeps) {
    this.cfg = deps.config;
    this.#fetch = deps.fetch;
    this.now = deps.now ?? Date.now;
    this.log = deps.logger ?? new Logger();
    const L = this.cfg.limits;
    this.signer = new SessionSigner(this.cfg.sessionSecret);
    this.#ipSessions = new WindowCounter(L.sessionsPerIpPerWindow, L.ipWindowS * 1000, this.now);
    this.#ipRequests = new WindowCounter(L.requestsPerIpPerWindow, L.ipWindowS * 1000, this.now);
    this.geminiGate = new Gate(L.geminiConcurrency);
    this.ttsGate = new Gate(L.ttsConcurrency);
    this.dailyGemini = new DailyBudget(L.daily.geminiCalls, this.now);
    this.dailySpeech = new DailyBudget(L.daily.speechChars, this.now);
    this.geminiSwitch = new ProviderSwitch(L.providerDisableS * 1000, this.now);
    this.elevenSwitch = new ProviderSwitch(L.providerDisableS * 1000, this.now);
    this.#hintCache = new LruCache({ maxEntries: L.hintCacheEntries, maxBytes: 4 * 1024 * 1024, ttlMs: L.hintCacheTtlS * 1000, now: this.now, size: (v) => v.text.length * 2 + 64 });
    this.speechCache = new LruCache({ maxEntries: 4096, maxBytes: L.speechCacheBytes, ttlMs: L.speechCacheTtlS * 1000, now: this.now, size: (v) => v.pcm.length + 64 });
  }

  get mock(): boolean { return this.cfg.mode === 'mock'; }
  get jobCount(): number { return this.#jobs.size; }
  get sessionCount(): number { return this.#sessions.size; }

  // ---------------------------------------------------------------- limits shared by every route

  /** Per-IP request limit (every route but /healthz). */
  admit(ip: string): void {
    const tag = this.signer.ipTag(networkKey(ip));
    if (!this.#ipRequests.take(tag)) throw new ApiError('rate_limited', this.#ipRequests.retryAfterMs(tag));
  }

  // ---------------------------------------------------------------- sessions

  #pruneSessions(): void {
    const now = this.now(), idle = this.cfg.limits.sessionIdleS * 1000;
    for (const [sid, s] of this.#sessions)
      if (s.exp <= now || now - s.lastSeen >= idle) this.#sessions.delete(sid);
  }

  createSession(gameToken: string | undefined, ip: string, _req: SessionRequest) {
    if (!checkGameToken(gameToken, this.cfg.gameTokens)) throw new ApiError('unauthorized');
    const tag = this.signer.ipTag(networkKey(ip));
    if (!this.#ipSessions.take(tag)) throw new ApiError('rate_limited', this.#ipSessions.retryAfterMs(tag));
    const now = this.now();
    this.#pruneSessions();
    let fromNetwork = 0;
    for (const s of this.#sessions.values()) if (s.network === tag) fromNetwork++;
    if (fromNetwork >= this.cfg.limits.maxSessionsPerIp) throw new ApiError('rate_limited', 60_000);
    if (this.#sessions.size >= this.cfg.limits.maxSessions) throw new ApiError('rate_limited', 60_000);
    const sid = this.signer.newSessionId();
    const ttl = this.cfg.limits.sessionTtlS;
    const exp = now + ttl * 1000;
    this.#sessions.set(sid, { sid, exp, network: tag, lastSeen: now, used: { monster: 0, hint: 0, pronounce: 0, speechChars: 0, speechFetches: 0 }, recentLines: [], activeTts: 0 });
    this.log.info({ event: 'session', sess: this.signer.logTag(sid) });
    return {
      session: this.signer.sign(sid, Math.floor(exp / 1000), ip),
      expires_in_s: ttl,
      mode: this.cfg.mode,
      config_version: this.cfg.configVersion,
      features: this.features(),
    };
  }

  features() {
    const f = features(this.cfg);
    return {
      monster: f.monster && !this.geminiSwitch.off && !this.elevenSwitch.off,
      hint: f.hint && !this.geminiSwitch.off,
      hint_speech: f.hint_speech && !this.geminiSwitch.off && !this.elevenSwitch.off,
      pronounce: f.pronounce && !this.elevenSwitch.off,
    };
  }

  session(token: string | undefined, ip: string): Session {
    this.#pruneSessions();
    const claims = this.signer.verify(token, ip, Math.floor(this.now() / 1000));
    const s = claims ? this.#sessions.get(claims.sid) : undefined;
    if (!s || s.exp <= this.now()) throw new ApiError('unauthorized');
    s.lastSeen = this.now();
    return s;
  }

  #quota(s: Session, what: 'monster' | 'hint' | 'pronounce'): void {
    if (s.used[what] >= this.cfg.limits.perSession[what]) throw new ApiError('rate_limited');
    s.used[what]++;
  }

  // ---------------------------------------------------------------- Gemini

  async #generate(kind: 'monster' | 'hint', user: Record<string, unknown>, schema: object, signal: AbortSignal): Promise<unknown> {
    if (this.geminiSwitch.off) throw new ApiError('disabled');
    if (!this.geminiGate.tryEnter()) throw new ApiError('rate_limited', 500);
    const deadline = this.cfg.limits.geminiDeadlineMs[kind];
    const t0 = this.now();
    try {
      if (this.mock) return await withDeadline((sig) => mockGemini(kind, user, this.cfg.mockFault, sig), deadline, signal);
      // reserve a call plus its one possible retry; with only one left, the retry is skipped
      const reserved = this.dailyGemini.reserve(2) ? 2 : this.dailyGemini.reserve(1) ? 1 : 0;
      if (reserved === 0) throw new ApiError('budget_exhausted');
      let calls = 0;
      const counting: Fetch = (url, init) => { calls++; return this.#fetch(url, init); };
      try {
        return await geminiGenerate(
          { fetch: counting, key: this.cfg.geminiKey!, model: this.cfg.geminiModel, maxBytes: this.cfg.limits.maxGeminiBytes },
          { system: kind === 'monster' ? MONSTER_SYSTEM : HINT_SYSTEM, user, schema, temperature: kind === 'monster' ? 0.9 : 0.4, deadlineMs: deadline },
          this.now, signal, reserved === 2,
        );
      } finally {
        const unused = reserved - Math.min(calls, reserved);
        if (unused > 0) this.dailyGemini.refund(unused);
      }
    } catch (e) {
      throw this.#providerError(e, 'gemini', kind);
    } finally {
      this.geminiGate.leave();
      this.log.info({ event: 'provider', provider: this.mock ? 'mock' : 'gemini', feature: kind, ms: this.now() - t0 });
    }
  }

  #providerError(e: unknown, provider: 'gemini' | 'elevenlabs', feature: string): ApiError {
    if (e instanceof ApiError) return e;
    if (!(e instanceof ProviderError)) return new ApiError('provider_unavailable');
    this.log.info({ event: 'provider_error', provider: this.mock ? 'mock' : provider, provider_status: e.status, code: e.code, feature });
    switch (e.code) {
      case 'timeout': return new ApiError('provider_timeout');
      case 'rate_limited': return new ApiError('rate_limited', 2000);
      case 'auth':
        (provider === 'gemini' ? this.geminiSwitch : this.elevenSwitch).trip();
        return new ApiError('disabled');
      case 'invalid': return new ApiError(provider === 'gemini' ? 'invalid_generation' : 'provider_unavailable');
      default: return new ApiError('provider_unavailable');
    }
  }

  // ---------------------------------------------------------------- speech jobs

  #voiceFor(channel: Channel, lang: string): string | undefined {
    if (this.mock) return 'mock';
    if (channel === 'monster') return this.cfg.voices.monster;
    if (channel === 'ship') return this.cfg.voices.ship;
    return nativeVoice(this.cfg, lang as Lang);
  }

  #pruneJobs(): void {
    const ttl = this.cfg.limits.speechJobTtlS * 1000, now = this.now();
    for (const [k, j] of this.#jobs) if (now - j.createdAt >= ttl) this.#jobs.delete(k);
  }

  /**
   * Starts (or reuses) speech for validated text. Completed records may be reclaimed under pressure.
   * The work is not tied to the HTTP request that asked for it: a client that got its text and now fetches the
   * audio must find the job still running. Bounded by the TTS gate, a per-session cap and the job table size.
   */
  startSpeech(s: Session, channel: Channel, lang: string, text: string): SpeechRef {
    const voice = this.#voiceFor(channel, lang);
    if (!voice || this.elevenSwitch.off) throw new ApiError('disabled');
    this.#pruneJobs();
    const contentKey = createHash('sha256')
      .update(JSON.stringify([this.cfg.mode, this.cfg.ttsModel, voice, VOICE_SETTINGS_VERSION, channel, lang, text]))
      .digest('hex');
    const version = contentKey.slice(0, 16);
    const id = randomId();
    const chars = [...text].length;

    const cached = this.speechCache.get(contentKey);
    let owned = 0;
    let oldestSettled: Job | undefined;
    let oldestOwned: Job | undefined;
    for (const job of this.#jobs.values()) {
      if (job.sid === s.sid) {
        if (job.contentKey === contentKey && !job.error && (!job.settled || cached)) {
          if (cached) job.source = cached.mock ? 'mock' : 'cache';
          return { id: job.id, sample_rate: SAMPLE_RATE, version, source: job.source };
        }
        owned++;
        if (job.settled && (!oldestOwned || job.createdAt < oldestOwned.createdAt)) oldestOwned = job;
      }
      if (job.settled && (!oldestSettled || job.createdAt < oldestSettled.createdAt)) oldestSettled = job;
    }
    if (owned >= this.cfg.limits.maxSpeechJobsPerSession) {
      if (!oldestOwned) throw new ApiError('rate_limited', 5000);
      this.#jobs.delete(oldestOwned.id);
    }
    if (this.#jobs.size >= this.cfg.limits.maxSpeechJobs) {
      if (!oldestSettled) throw new ApiError('rate_limited', 5000);
      this.#jobs.delete(oldestSettled.id);
    }
    if (cached) {
      const source: Source = cached.mock ? 'mock' : 'cache';
      this.#jobs.set(id, { id, sid: s.sid, contentKey, createdAt: this.now(), done: Promise.resolve(), settled: true, source });
      return { id, sample_rate: SAMPLE_RATE, version, source };
    }

    let done = this.#inflight.get(contentKey);
    const source: Source = this.mock ? 'mock' : 'live';
    if (!done) {
      if (s.activeTts >= this.cfg.limits.ttsPerSessionConcurrency) throw new ApiError('rate_limited', 1000);
      if (s.used.speechChars + chars > this.cfg.limits.perSession.speechChars) throw new ApiError('rate_limited');
      if (!this.mock && !this.dailySpeech.reserve(chars)) throw new ApiError('budget_exhausted');
      if (!this.ttsGate.tryEnter()) {
        if (!this.mock) this.dailySpeech.refund(chars);
        throw new ApiError('rate_limited', 1000);
      }
      s.used.speechChars += chars;
      s.activeTts++;
      const t0 = this.now();
      const work = this.mock
        ? withDeadline((sig) => mockTts(text, this.cfg.mockFault, sig), this.cfg.limits.ttsDeadlineMs)
        : synthesize(
            { fetch: this.#fetch, key: this.cfg.elevenKey!, model: this.cfg.ttsModel, maxBytes: this.cfg.limits.maxAudioBytes },
            { voiceId: voice, text, lang, channel, deadlineMs: this.cfg.limits.ttsDeadlineMs },
          );
      done = work.then(
        (pcm) => {
          if (pcm.length === 0 || pcm.length % 2 !== 0 || pcm.length > this.cfg.limits.maxAudioBytes) throw new ProviderError('invalid', 200);
          this.speechCache.set(contentKey, { pcm, mock: this.mock });
          this.log.info({ event: 'tts', provider: this.mock ? 'mock' : 'elevenlabs', chars, bytes: pcm.length, ms: this.now() - t0 });
        },
      ).catch((e: unknown) => {
        // nothing was billed for an HTTP error or a refused connection: give the characters back
        if (e instanceof ProviderError && e.unbilled) {
          s.used.speechChars = Math.max(0, s.used.speechChars - chars);
          if (!this.mock) this.dailySpeech.refund(chars);
        }
        throw this.#providerError(e, 'elevenlabs', channel);
      }).finally(() => {
        this.ttsGate.leave();
        s.activeTts = Math.max(0, s.activeTts - 1);
        this.#inflight.delete(contentKey);
      });
      this.#inflight.set(contentKey, done);
    }
    const job: Job = { id, sid: s.sid, contentKey, createdAt: this.now(), done: Promise.resolve(), settled: false, source };
    job.done = done.catch((e: unknown) => { job.error = e instanceof ApiError ? e : new ApiError('provider_unavailable'); })
      .finally(() => { job.settled = true; });
    this.#jobs.set(id, job);
    return { id, sample_rate: SAMPLE_RATE, version, source };
  }

  /** Waits (bounded) for a job this session owns. A client going away stops the wait, never the job. */
  async fetchSpeech(s: Session, id: string, signal: AbortSignal): Promise<{ pcm: Buffer; source: Source }> {
    this.#pruneJobs();
    const job = this.#jobs.get(id);
    if (!job || job.sid !== s.sid) throw new ApiError('not_found');
    if (s.used.speechFetches >= this.cfg.limits.perSession.speechFetches) throw new ApiError('rate_limited');
    s.used.speechFetches++;
    let timer: NodeJS.Timeout | undefined;
    const waited = await Promise.race([
      job.done.then(() => 'done' as const),
      new Promise<'timeout'>((r) => { timer = setTimeout(() => r('timeout'), this.cfg.limits.speechWaitMs); }),
      new Promise<'aborted'>((r) => signal.addEventListener('abort', () => r('aborted'), { once: true })),
    ]);
    clearTimeout(timer);
    if (waited === 'timeout') throw new ApiError('provider_timeout');
    if (waited === 'aborted') throw new ApiError('provider_unavailable');
    if (job.error) throw job.error;
    const audio = this.speechCache.get(job.contentKey);
    if (!audio) throw new ApiError('not_found');
    return { pcm: audio.pcm, source: audio.mock ? 'mock' : job.source };
  }

  #trySpeech(s: Session, channel: Channel, lang: string, text: string): SpeechRef | null {
    try { return this.startSpeech(s, channel, lang, text); }
    catch (e) {
      if (e instanceof ApiError) { this.log.info({ event: 'speech_skipped', code: e.code, feature: channel }); return null; }
      throw e;
    }
  }

  // ---------------------------------------------------------------- monster

  async monsterLine(s: Session, req: MonsterRequest, signal: AbortSignal) {
    if (!this.features().monster) throw new ApiError('disabled');
    this.#quota(s, 'monster');
    const user = { ...req, what_happened: TRIGGER_MEANING[req.trigger] };
    const schema = { type: 'object', properties: { line: { type: 'string' } }, required: ['line'] };
    const out = await this.#generate('monster', user, schema, signal);
    if (typeof out !== 'object' || out === null || Array.isArray(out) || Object.keys(out).length !== 1 || typeof (out as { line?: unknown }).line !== 'string')
      throw new ApiError('invalid_generation');
    const line = (out as { line: string }).line.trim();
    const v = checkMonsterLine(line, s.recentLines);
    if (!v.ok) { this.log.info({ event: 'rejected', feature: 'monster', code: v.reason }); throw new ApiError('invalid_generation'); }
    s.recentLines.push(line);
    if (s.recentLines.length > 5) s.recentLines.shift();
    return { id: randomId(), text: line, source: (this.mock ? 'mock' : 'live') as Source, speech: this.#trySpeech(s, 'monster', 'en', line) };
  }

  // ---------------------------------------------------------------- hints

  async hint(s: Session, req: HintRequest, signal: AbortSignal) {
    if (!this.features().hint) throw new ApiError('disabled');
    this.#quota(s, 'hint');
    // the key covers the whole validated request (not a client-chosen puzzle id) and every version that shapes the output
    const { seq: _seq, attempts: _attempts, ...context } = req;
    const key = createHash('sha256').update(JSON.stringify([PROMPT_VERSION, FACTS_VERSION, this.cfg.mode, this.cfg.geminiModel, context])).digest('hex');
    let entry = this.#hintCache.get(key);
    let source: Source = this.mock ? 'mock' : 'cache';
    if (!entry) {
      source = this.mock ? 'mock' : 'live';
      const facts = HINT_FACTS.concepts[req.concept];
      const user = {
        level: req.level,
        concept: req.concept,
        approved_rule: facts.facts,
        look_at: facts.focus,
        target_meaning: req.target_meaning,
        rows: req.examples.map((e, i) => ({ row: i + 1, meaning: e.meaning, words: e.words })),
      };
      const schema = {
        type: 'object',
        properties: {
          rows: { type: 'array', items: { type: 'integer', enum: [1, 2, 3] }, minItems: 1, maxItems: 2 },
          hint: { type: 'string' },
        },
        required: ['rows', 'hint'],
      };
      const out = await this.#generate('hint', user, schema, signal);
      const o = out as { rows?: unknown; hint?: unknown };
      if (typeof out !== 'object' || out === null || Array.isArray(out) || Object.keys(out).some((k) => k !== 'rows' && k !== 'hint'))
        throw new ApiError('invalid_generation');
      const rows = Array.isArray(o.rows) ? o.rows : [];
      if (rows.length < 1 || rows.length > 2 || !rows.every((r) => r === 1 || r === 2 || r === 3) || new Set(rows).size !== rows.length)
        throw new ApiError('invalid_generation');
      const a = rows[0] as number, b = (rows[1] as number | undefined) ?? (a % 3) + 1;
      const puzzleWords = [...req.tiles, ...req.examples.flatMap((e) => e.words)];
      const text = typeof o.hint === 'string' ? o.hint.trim() : '';
      const v = checkHint(text, puzzleWords);
      if (!v.ok) this.log.info({ event: 'rejected', feature: 'hint', code: v.reason });
      entry = v.ok ? { text, kind: 'generated', rows: [a, b] } : { text: guidedHint(req.level, req.concept, a, b), kind: 'guided', rows: [a, b] };
      this.#hintCache.set(key, entry);
    }
    const speech = this.features().hint_speech ? this.#trySpeech(s, 'ship', 'en', entry.text) : null;
    return { id: randomId(), text: entry.text, kind: entry.kind, rows: entry.rows, source, speech };
  }

  // ---------------------------------------------------------------- pronunciation

  pronounce(s: Session, req: PronounceRequest) {
    if (!this.features().pronounce) throw new ApiError('disabled');
    this.#quota(s, 'pronounce');
    const speech = this.startSpeech(s, 'native', req.lang, req.text);
    return { id: randomId(), source: speech.source, speech };
  }
}

/** Runs work with a deadline (and an optional outer signal); the work gets a signal that fires on either. */
async function withDeadline<T>(work: (signal: AbortSignal) => Promise<T>, ms: number, outer?: AbortSignal): Promise<T> {
  const ctl = new AbortController();
  const timer = setTimeout(() => ctl.abort(new ProviderError('timeout')), ms);
  const onOuter = () => ctl.abort(new ProviderError('aborted'));
  outer?.addEventListener('abort', onOuter, { once: true });
  try {
    return await Promise.race([
      work(ctl.signal),
      new Promise<never>((_, reject) => ctl.signal.addEventListener('abort', () => reject(ctl.signal.reason), { once: true })),
    ]);
  } finally {
    clearTimeout(timer);
    outer?.removeEventListener('abort', onOuter);
  }
}
