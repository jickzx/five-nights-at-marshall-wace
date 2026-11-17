// Structured, allow-listed logging. Only the fields below are ever written: no bodies, generated text,
// tokens, keys, Authorization headers or raw IP addresses.
export interface LogFields {
  event: string;
  route?: string;
  status?: number;
  ms?: number;
  cache?: boolean;
  provider?: 'openRouter' | 'elevenlabs' | 'mock';
  provider_status?: number;
  code?: string;
  chars?: number;
  bytes?: number;
  sess?: string; // short salted hash, never the id or token
  feature?: string;
}

export type LogSink = (line: string) => void;

const ALLOWED = new Set<keyof LogFields>([
  'event', 'route', 'status', 'ms', 'cache', 'provider', 'provider_status', 'code', 'chars', 'bytes', 'sess', 'feature',
]);
const SAFE_TOKEN = /^[a-z0-9_:./-]{1,64}$/i;

export class Logger {
  readonly sink: LogSink;
  constructor(sink: LogSink = (l) => process.stdout.write(l + '\n')) {
    this.sink = sink;
  }

  info(fields: LogFields): void {
    const out: Record<string, string | number | boolean> = { t: new Date().toISOString() };
    for (const [k, v] of Object.entries(fields)) {
      if (!ALLOWED.has(k as keyof LogFields) || v === undefined) continue;
      if (typeof v === 'string') {
        // strings are enum-like labels only; anything else is dropped rather than risk logging content
        if (SAFE_TOKEN.test(v)) out[k] = v;
      } else if (typeof v === 'number' && Number.isFinite(v)) out[k] = Math.round(v);
      else if (typeof v === 'boolean') out[k] = v;
    }
    this.sink(JSON.stringify(out));
  }
}
