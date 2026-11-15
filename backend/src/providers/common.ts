// Shared provider plumbing: an injectable fetch, bounded body reads and a small error vocabulary.
// Provider error bodies are never read into responses or logs; only the HTTP status is kept.

export type Fetch = (url: string, init: RequestInit) => Promise<Response>;

export type ProviderErrorCode = 'timeout' | 'unavailable' | 'rate_limited' | 'auth' | 'invalid' | 'aborted' | 'too_large';

export class ProviderError extends Error {
  readonly code: ProviderErrorCode;
  readonly status: number | undefined;
  /** True when the provider certainly produced nothing billable (HTTP error, connection refused). */
  readonly unbilled: boolean;
  constructor(code: ProviderErrorCode, status?: number, unbilled = false) {
    super(code);
    this.code = code; this.status = status; this.unbilled = unbilled;
  }
  get retryable(): boolean { return this.code === 'rate_limited' || (this.code === 'unavailable' && this.status !== undefined); }
}

export function errorForStatus(status: number): ProviderError {
  if (status === 429) return new ProviderError('rate_limited', status, true);
  if (status === 401 || status === 402 || status === 403) return new ProviderError('auth', status, true);
  if (status === 400 || status === 404 || status === 422) return new ProviderError('invalid', status, true);
  return new ProviderError('unavailable', status, true);
}

/** Reads at most maxBytes of a body; larger bodies are cancelled and rejected. */
export async function readLimited(res: Response, maxBytes: number): Promise<Buffer> {
  const len = Number(res.headers.get('content-length') ?? NaN);
  if (Number.isFinite(len) && len > maxBytes) { await res.body?.cancel().catch(() => {}); throw new ProviderError('too_large', res.status); }
  if (!res.body) return Buffer.alloc(0);
  const reader = res.body.getReader();
  const chunks: Buffer[] = [];
  let total = 0;
  for (;;) {
    const { done, value } = await reader.read();
    if (done) break;
    total += value.byteLength;
    if (total > maxBytes) { await reader.cancel().catch(() => {}); throw new ProviderError('too_large', res.status); }
    chunks.push(Buffer.from(value));
  }
  return Buffer.concat(chunks, total);
}

/** Calls fetch and consumes the response inside one deadline and the caller's abort signal; maps failures. */
export async function callProvider<T>(
  fetchFn: Fetch, url: string, init: RequestInit, deadlineMs: number, outer: AbortSignal | undefined,
  consume: (res: Response) => Promise<T>,
): Promise<T> {
  const ctl = new AbortController();
  const timer = setTimeout(() => ctl.abort(new ProviderError('timeout')), Math.max(1, deadlineMs));
  const onOuter = () => ctl.abort(new ProviderError('aborted'));
  if (outer) { if (outer.aborted) onOuter(); else outer.addEventListener('abort', onOuter, { once: true }); }
  try {
    let res: Response;
    try {
      res = await fetchFn(url, { ...init, redirect: 'error', signal: ctl.signal });
    } catch {
      const reason: unknown = ctl.signal.reason;
      if (reason instanceof ProviderError) throw reason;
      // connection refused, DNS failure or a refused redirect, before any response: nothing was generated
      throw new ProviderError('unavailable', undefined, true);
    }
    try {
      return await consume(res);
    } catch (e) {
      const reason: unknown = ctl.signal.reason;
      if (ctl.signal.aborted && reason instanceof ProviderError) throw reason;
      if (e instanceof ProviderError) throw e;
      throw new ProviderError('invalid', res.status);
    }
  } finally {
    clearTimeout(timer);
    outer?.removeEventListener('abort', onOuter);
  }
}

export const sleep = (ms: number, signal?: AbortSignal): Promise<void> =>
  new Promise((resolve, reject) => {
    const t = setTimeout(resolve, ms);
    signal?.addEventListener('abort', () => { clearTimeout(t); reject(new ProviderError('aborted')); }, { once: true });
  });
