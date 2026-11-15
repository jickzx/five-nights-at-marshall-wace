// Game-token check and signed, server-chosen sessions.
//
// Honest limit: the game token ships inside the game build and can be extracted. It is a speed bump. What
// actually protects the provider accounts is that every endpoint is narrow and every budget is capped
// (limits.ts), plus the spend caps set on the provider accounts themselves.
import { createHash, createHmac, randomBytes, timingSafeEqual } from 'node:crypto';

const digest = (s: string): Buffer => createHash('sha256').update(s).digest();

/** Constant-time comparison against every configured token (hashing equalises lengths). */
export function checkGameToken(presented: string | undefined, tokens: readonly string[]): boolean {
  if (!presented) return false;
  const p = digest(presented);
  let ok = false;
  for (const t of tokens) ok = timingSafeEqual(p, digest(t)) || ok;
  return ok;
}

export function bearer(header: string | string[] | undefined): string | undefined {
  if (typeof header !== 'string') return undefined;
  const m = /^Bearer ([\x21-\x7e]{1,512})$/.exec(header);
  return m ? m[1] : undefined;
}

export interface SessionClaims { sid: string; exp: number }

export class SessionSigner {
  readonly #secret: Buffer;
  constructor(secret: Buffer) {
    this.#secret = secret;
  }

  /** Short salted hash of an IP: bound into the token and used as a limiter key, never logged raw. */
  ipTag(ip: string): string {
    return createHmac('sha256', this.#secret).update('ip:' + ip).digest('base64url').slice(0, 16);
  }

  /** Short salted hash of a session id for logs. */
  logTag(sid: string): string {
    return createHmac('sha256', this.#secret).update('log:' + sid).digest('hex').slice(0, 10);
  }

  newSessionId(): string {
    return randomBytes(16).toString('hex');
  }

  sign(sid: string, exp: number, ip: string): string {
    const body = `v1.${sid}.${exp}`;
    const mac = createHmac('sha256', this.#secret).update(`${body}.${this.ipTag(ip)}`).digest('base64url');
    return `${body}.${mac}`;
  }

  verify(token: string | undefined, ip: string, nowS: number): SessionClaims | null {
    if (!token) return null;
    const m = /^v1\.([0-9a-f]{32})\.(\d{1,12})\.([A-Za-z0-9_-]{43})$/.exec(token);
    if (!m) return null;
    const sid = m[1]!, exp = Number(m[2]);
    const want = createHmac('sha256', this.#secret).update(`v1.${sid}.${exp}.${this.ipTag(ip)}`).digest();
    const got = Buffer.from(m[3]!, 'base64url');
    if (got.length !== want.length || !timingSafeEqual(got, want)) return null;
    if (exp <= nowS) return null;
    return { sid, exp };
  }
}
