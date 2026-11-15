// In-process limits. They hold for ONE long-lived process: a restart resets them, and several instances
// (serverless, autoscaling) would each count separately. Provider-side spend caps and key quotas are the only
// hard stops on money; these are best-effort brakes that keep a leaked game token from running up a bill fast.

export type Clock = () => number; // milliseconds

/** Fixed-window counter per key, with a bounded number of keys (oldest windows are dropped first). */
export class WindowCounter {
  readonly #limit: number;
  readonly #windowMs: number;
  readonly #maxKeys: number;
  readonly #now: Clock;
  readonly #map = new Map<string, { start: number; n: number }>();

  constructor(limit: number, windowMs: number, now: Clock, maxKeys = 10_000) {
    this.#limit = limit; this.#windowMs = windowMs; this.#now = now; this.#maxKeys = maxKeys;
  }

  /** Counts one hit; false (and no count) when the key is over its limit. */
  take(key: string): boolean {
    const now = this.#now();
    let e = this.#map.get(key);
    if (!e || now - e.start >= this.#windowMs) {
      this.#map.delete(key);
      e = { start: now, n: 0 };
      if (this.#map.size >= this.#maxKeys) {
        // drop expired windows, then the oldest, so memory stays bounded
        for (const [k, v] of this.#map) if (now - v.start >= this.#windowMs) this.#map.delete(k);
        while (this.#map.size >= this.#maxKeys) this.#map.delete(this.#map.keys().next().value!);
      }
      this.#map.set(key, e);
    }
    if (e.n >= this.#limit) return false;
    e.n++;
    return true;
  }

  retryAfterMs(key: string): number {
    const e = this.#map.get(key);
    return e ? Math.max(0, this.#windowMs - (this.#now() - e.start)) : 0;
  }
}

/** Concurrency gate that refuses at once instead of queueing. */
export class Gate {
  readonly #max: number;
  #active = 0;
  constructor(max: number) { this.#max = max; }
  get active(): number { return this.#active; }
  tryEnter(): boolean {
    if (this.#active >= this.#max) return false;
    this.#active++;
    return true;
  }
  leave(): void { if (this.#active > 0) this.#active--; }
}

/** A per-UTC-day counter with reserve/refund, for provider calls and speech characters. */
export class DailyBudget {
  readonly #limit: number;
  readonly #now: Clock;
  #day = '';
  #used = 0;
  constructor(limit: number, now: Clock) { this.#limit = limit; this.#now = now; }
  #roll(): void {
    const d = new Date(this.#now()).toISOString().slice(0, 10);
    if (d !== this.#day) { this.#day = d; this.#used = 0; }
  }
  get used(): number { this.#roll(); return this.#used; }
  reserve(n: number): boolean {
    this.#roll();
    if (this.#used + n > this.#limit) return false;
    this.#used += n;
    return true;
  }
  refund(n: number): void { this.#roll(); this.#used = Math.max(0, this.#used - n); }
}

/** After a provider says the key is bad or out of credit, the feature stays off for a while (no retry storm). */
export class ProviderSwitch {
  readonly #now: Clock;
  readonly #ms: number;
  #until = 0;
  constructor(disableMs: number, now: Clock) { this.#ms = disableMs; this.#now = now; }
  trip(): void { this.#until = this.#now() + this.#ms; }
  get off(): boolean { return this.#now() < this.#until; }
}
