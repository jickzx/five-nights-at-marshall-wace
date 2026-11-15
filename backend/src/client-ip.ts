import { isIP } from 'node:net';
export class InvalidClientIp extends Error {}

/** Canonical addresses keep mapped IPv4 and equivalent IPv6 spellings in the same quota. */
export function canonicalIp(value: string): string {
  if (value.includes('%') || !isIP(value)) throw new InvalidClientIp();
  if (isIP(value) === 4) return value;
  const compressed = new URL(`http://[${value}]/`).hostname.slice(1, -1);
  const [left = '', right] = compressed.split('::');
  const head = left ? left.split(':') : [];
  const tail = right ? right.split(':') : [];
  const parts = right === undefined ? head : [...head, ...Array<string>(8 - head.length - tail.length).fill('0'), ...tail];
  const words = parts.map((p) => Number.parseInt(p, 16));
  if (words.slice(0, 5).every((w) => w === 0) && words[5] === 0xffff) {
    const high = words[6]!, low = words[7]!;
    return `${high >>> 8}.${high & 255}.${low >>> 8}.${low & 255}`;
  }
  return words.map((w) => w.toString(16).padStart(4, '0')).join(':');
}

export function networkKey(ip: string): string {
  const canonical = canonicalIp(ip);
  return canonical.includes(':') ? canonical.split(':').slice(0, 4).join(':') + '::/64' : canonical;
}

/** Only an explicitly trusted socket peer may supply a chain, read from the nearest hop backwards. */
export function clientIp(peer: string | undefined, forwarded: string | string[] | undefined, trusted: readonly string[]): string {
  let current = canonicalIp(peer ?? '');
  if (!trusted.includes(current) || forwarded === undefined) return current;
  if (typeof forwarded !== 'string' || forwarded.length > 2048) throw new InvalidClientIp();
  const hops = forwarded.split(',');
  if (hops.length > 16) throw new InvalidClientIp();
  const addresses = hops.map((hop) => canonicalIp(hop.trim()));
  for (let i = addresses.length - 1; i >= 0 && trusted.includes(current); i--) current = addresses[i]!;
  return current;
}
