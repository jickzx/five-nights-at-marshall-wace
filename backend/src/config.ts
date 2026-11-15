// Environment configuration. Provider keys and voice ids live only here (never in Unity or the repository).
import { createHash, randomBytes } from 'node:crypto';
import { PROMPT_VERSION } from './prompts.ts';
import { FACTS_VERSION } from './hintfacts.ts';
import { canonicalIp } from './client-ip.ts';

export const LANGS = ['ja', 'fr', 'es', 'de', 'tr', 'zh', 'ar'] as const;
export type Lang = (typeof LANGS)[number];
export type Mode = 'live' | 'mock';
export type MockFault = 'none' | 'timeout' | '429' | '500' | 'garbage';

/** Bump when fixed voice settings change (part of every speech cache key). */
export const VOICE_SETTINGS_VERSION = '1';

export interface Limits {
  bodyBytes: number;
  sessionTtlS: number;
  sessionIdleS: number;
  maxSessionsPerIp: number;
  maxSessions: number;
  sessionsPerIpPerWindow: number;
  requestsPerIpPerWindow: number;
  ipWindowS: number;
  perSession: { monster: number; hint: number; pronounce: number; speechChars: number; speechFetches: number };
  daily: { geminiCalls: number; speechChars: number };
  geminiConcurrency: number;
  ttsConcurrency: number;
  ttsPerSessionConcurrency: number;
  speechJobTtlS: number;
  maxSpeechJobs: number;
  maxSpeechJobsPerSession: number;
  speechCacheBytes: number;
  speechCacheTtlS: number;
  hintCacheEntries: number;
  hintCacheTtlS: number;
  providerDisableS: number;
  geminiDeadlineMs: { monster: number; hint: number };
  ttsDeadlineMs: number;
  speechWaitMs: number;
  maxAudioBytes: number;
  maxGeminiBytes: number;
}

export const DEFAULT_LIMITS: Limits = {
  bodyBytes: 8192,
  sessionTtlS: 7200,
  sessionIdleS: 900,
  maxSessionsPerIp: 8,
  maxSessions: 500,
  sessionsPerIpPerWindow: 3,
  requestsPerIpPerWindow: 120,
  ipWindowS: 600,
  perSession: { monster: 40, hint: 40, pronounce: 40, speechChars: 8000, speechFetches: 200 },
  daily: { geminiCalls: 2000, speechChars: 60000 },
  geminiConcurrency: 4,
  ttsConcurrency: 2,
  ttsPerSessionConcurrency: 2,
  speechJobTtlS: 600,
  maxSpeechJobs: 256,
  maxSpeechJobsPerSession: 16,
  speechCacheBytes: 64 * 1024 * 1024,
  speechCacheTtlS: 24 * 3600,
  hintCacheEntries: 500,
  hintCacheTtlS: 24 * 3600,
  providerDisableS: 600,
  geminiDeadlineMs: { monster: 4000, hint: 3000 },
  ttsDeadlineMs: 8000,
  speechWaitMs: 7000,
  maxAudioBytes: 1_500_000,
  maxGeminiBytes: 64 * 1024,
};

export interface Voices {
  monster?: string;
  ship?: string;
  native?: string;
  nativeByLang: Partial<Record<Lang, string>>;
}

export interface Config {
  mode: Mode;
  host: string;
  port: number;
  trustedProxies: string[];
  gameTokens: string[];
  sessionSecret: Buffer;
  geminiKey?: string;
  elevenKey?: string;
  geminiModel: string;
  ttsModel: string;
  voices: Voices;
  limits: Limits;
  mockFault: MockFault;
  /** Opaque: changes whenever mode, a model, a voice or a prompt/fact/settings version changes. */
  configVersion: string;
}

export class ConfigError extends Error {}

const ID_RE = /^[A-Za-z0-9_.-]{1,64}$/;
const TOKEN_RE = /^[\x21-\x7e]{16,256}$/;

function id(env: NodeJS.ProcessEnv, name: string, fallback?: string): string | undefined {
  const v = (env[name] ?? '').trim() || fallback;
  if (v === undefined) return undefined;
  if (!ID_RE.test(v)) throw new ConfigError(`${name} must match ${ID_RE}`);
  return v;
}

function int(env: NodeJS.ProcessEnv, name: string, fallback: number, min: number, max: number): number {
  const raw = env[name];
  if (raw === undefined || raw.trim() === '') return fallback;
  const n = Number(raw);
  if (!Number.isInteger(n) || n < min || n > max) throw new ConfigError(`${name} must be an integer in ${min}..${max}`);
  return n;
}

export function loadConfig(env: NodeJS.ProcessEnv): Config {
  const modeRaw = (env.FNMW_PROVIDER_MODE ?? 'live').trim();
  if (modeRaw !== 'live' && modeRaw !== 'mock') throw new ConfigError('FNMW_PROVIDER_MODE must be live or mock');
  const mode: Mode = modeRaw;

  const bind = (env.FNMW_BIND ?? '127.0.0.1:8787').trim();
  const m = /^([0-9.]+|localhost|\[[0-9a-f:]+\]):(\d{1,5})$/i.exec(bind);
  if (!m) throw new ConfigError('FNMW_BIND must look like 127.0.0.1:8787');
  const host = m[1]!.replace(/^\[|\]$/g, '');
  const port = Number(m[2]);
  if (port > 65535) throw new ConfigError('FNMW_BIND port out of range');

  let trustedProxies: string[];
  try {
    trustedProxies = (env.FNMW_TRUSTED_PROXIES ?? '').split(',').map((s) => s.trim()).filter(Boolean).map(canonicalIp);
    if (trustedProxies.length > 16) throw new Error();
  } catch { throw new ConfigError('FNMW_TRUSTED_PROXIES must list at most 16 exact IP addresses'); }

  const gameTokens = (env.FNMW_GAME_TOKENS ?? '').split(',').map((t) => t.trim()).filter((t) => t.length > 0);
  if (gameTokens.length === 0) throw new ConfigError('FNMW_GAME_TOKENS must list at least one game token (16+ printable characters)');
  for (const t of gameTokens) if (!TOKEN_RE.test(t)) throw new ConfigError('every FNMW_GAME_TOKENS entry must be 16-256 printable characters');

  const secretRaw = env.FNMW_SESSION_SECRET ?? '';
  let sessionSecret: Buffer;
  if (Buffer.byteLength(secretRaw) >= 32) sessionSecret = Buffer.from(secretRaw);
  else if (mode === 'live') throw new ConfigError('live mode needs FNMW_SESSION_SECRET of at least 32 bytes');
  else sessionSecret = randomBytes(32); // mock: sessions last for this process only

  const geminiKey = (env.GEMINI_API_KEY ?? '').trim() || undefined;
  const elevenKey = (env.ELEVENLABS_API_KEY ?? '').trim() || undefined;
  for (const [name, v] of [['GEMINI_API_KEY', geminiKey], ['ELEVENLABS_API_KEY', elevenKey]] as const)
    if (v !== undefined && !/^[\x21-\x7e]{8,512}$/.test(v)) throw new ConfigError(`${name} has unexpected characters`);

  const voices: Voices = {
    monster: id(env, 'FNMW_VOICE_MONSTER'),
    ship: id(env, 'FNMW_VOICE_SHIP'),
    native: id(env, 'FNMW_VOICE_NATIVE'),
    nativeByLang: {},
  };
  for (const l of LANGS) {
    const v = id(env, `FNMW_VOICE_NATIVE_${l.toUpperCase()}`);
    if (v) voices.nativeByLang[l] = v;
  }

  const faultRaw = (env.FNMW_MOCK_FAULT ?? 'none').trim() || 'none';
  if (!['none', 'timeout', '429', '500', 'garbage'].includes(faultRaw)) throw new ConfigError('FNMW_MOCK_FAULT must be none, timeout, 429, 500 or garbage');
  if (faultRaw !== 'none' && mode !== 'mock') throw new ConfigError('FNMW_MOCK_FAULT is for mock mode only');

  const limits: Limits = structuredClone(DEFAULT_LIMITS);
  limits.daily.geminiCalls = int(env, 'FNMW_DAILY_GEMINI_CALLS', limits.daily.geminiCalls, 0, 1_000_000);
  limits.daily.speechChars = int(env, 'FNMW_DAILY_SPEECH_CHARS', limits.daily.speechChars, 0, 10_000_000);
  limits.perSession.speechChars = int(env, 'FNMW_SESSION_SPEECH_CHARS', limits.perSession.speechChars, 0, 1_000_000);
  limits.sessionsPerIpPerWindow = int(env, 'FNMW_SESSIONS_PER_IP', limits.sessionsPerIpPerWindow, 1, 1000);
  limits.requestsPerIpPerWindow = int(env, 'FNMW_REQUESTS_PER_IP', limits.requestsPerIpPerWindow, 1, 100_000);

  const geminiModel = id(env, 'FNMW_GEMINI_MODEL', 'gemini-3.5-flash-lite')!;
  const ttsModel = id(env, 'FNMW_TTS_MODEL', 'eleven_flash_v2_5')!;
  const configVersion = createHash('sha256')
    .update(JSON.stringify([mode, geminiModel, ttsModel, voices, PROMPT_VERSION, FACTS_VERSION, VOICE_SETTINGS_VERSION]))
    .digest('hex')
    .slice(0, 16);

  return {
    mode, host, port, trustedProxies, gameTokens, sessionSecret, geminiKey, elevenKey, geminiModel, ttsModel, voices, limits,
    mockFault: faultRaw as MockFault, configVersion,
  };
}

export interface Features { monster: boolean; hint: boolean; hint_speech: boolean; pronounce: boolean }

export function nativeVoice(cfg: Config, lang: Lang): string | undefined {
  return cfg.voices.nativeByLang[lang] ?? cfg.voices.native;
}

/** A feature whose key or voice is missing is off; mock mode needs neither. */
export function features(cfg: Config): Features {
  if (cfg.mode === 'mock') return { monster: true, hint: true, hint_speech: true, pronounce: true };
  const g = !!cfg.geminiKey, e = !!cfg.elevenKey;
  return {
    monster: g && e && !!cfg.voices.monster,
    hint: g,
    hint_speech: g && e && !!cfg.voices.ship,
    pronounce: e && LANGS.every((l) => !!nativeVoice(cfg, l)),
  };
}
