// In-memory LRU with an entry cap, a byte cap and a TTL. Nothing is written to disk.
import type { Clock } from './limits.ts';

export class LruCache<V> {
  readonly #map = new Map<string, { v: V; bytes: number; at: number }>();
  readonly #maxEntries: number;
  readonly #maxBytes: number;
  readonly #ttlMs: number;
  readonly #now: Clock;
  readonly #size: (v: V) => number;
  #bytes = 0;

  constructor(opts: { maxEntries: number; maxBytes: number; ttlMs: number; now: Clock; size: (v: V) => number }) {
    this.#maxEntries = opts.maxEntries; this.#maxBytes = opts.maxBytes; this.#ttlMs = opts.ttlMs; this.#now = opts.now; this.#size = opts.size;
  }

  get bytes(): number { return this.#bytes; }
  get size(): number { return this.#map.size; }

  get(key: string): V | undefined {
    const e = this.#map.get(key);
    if (!e) return undefined;
    if (this.#now() - e.at >= this.#ttlMs) { this.delete(key); return undefined; }
    this.#map.delete(key); this.#map.set(key, e); // most recently used last
    return e.v;
  }

  set(key: string, v: V): void {
    const bytes = this.#size(v);
    if (bytes > this.#maxBytes) return; // never store something that alone breaks the cap
    this.delete(key);
    this.#map.set(key, { v, bytes, at: this.#now() });
    this.#bytes += bytes;
    while (this.#map.size > this.#maxEntries || this.#bytes > this.#maxBytes) {
      const oldest = this.#map.keys().next().value!;
      this.delete(oldest);
    }
  }

  delete(key: string): void {
    const e = this.#map.get(key);
    if (!e) return;
    this.#bytes -= e.bytes;
    this.#map.delete(key);
  }
}
