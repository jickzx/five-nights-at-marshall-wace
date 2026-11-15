// Mock providers for keyless local runs. They never touch the network. The "speech" is a beeping tone
// pattern, plainly not a voice; every response that uses it says source "mock", and the game labels it MOCK.
import { createHash } from 'node:crypto';
import type { MockFault } from '../config.ts';
import { ProviderError, sleep } from './common.ts';
import { SAMPLE_RATE } from './elevenlabs.ts';

const MONSTER_LINES: Record<string, string[]> = {
  first_mistake: ['One wrong word. I heard it.', 'You slipped. I am closer now.'],
  repeat_concept: ['The same mistake again. I can wait.', 'You keep stumbling on the same thing.'],
  streak: ['Wrong, and wrong again. Keep going.', 'Every mistake brings me nearer.'],
  stage_up: ['I am bigger now. Can you hear it?', 'Something in me just grew.'],
  final_chase: ['Run. I am right behind you.', 'The ship is far. I am not.'],
};

export async function mockGemini(kind: 'monster' | 'hint', user: Record<string, unknown>, fault: MockFault, signal?: AbortSignal): Promise<unknown> {
  await applyFault(fault, signal);
  if (fault === 'garbage') return 'not the json you asked for';
  if (kind === 'monster') {
    const lines = MONSTER_LINES[String(user.trigger)] ?? MONSTER_LINES.streak!;
    return { line: lines[Number(user.seq ?? 0) % lines.length] };
  }
  const level = user.level === 2 ? 2 : 1;
  return level === 1
    ? { rows: [1, 3], hint: 'Compare rows 1 and 3 closely before you build the sentence.' }
    : { rows: [2], hint: 'Row 2 shows the pattern most clearly. Study it again.' };
}

/** A syllable-like beep pattern whose length follows the text, capped at six seconds. */
export async function mockTts(text: string, fault: MockFault, signal?: AbortSignal): Promise<Buffer> {
  await applyFault(fault, signal);
  if (fault === 'garbage') return Buffer.from([1, 2, 3]); // odd length: invalid PCM
  const seed = createHash('sha256').update(text).digest();
  const syllables = Math.max(2, Math.min(24, Math.round([...text].length / 3)));
  const beep = Math.round(SAMPLE_RATE * 0.14), gap = Math.round(SAMPLE_RATE * 0.06);
  const total = Math.min(SAMPLE_RATE * 6, syllables * (beep + gap));
  const pcm = Buffer.alloc(total * 2);
  for (let s = 0, i = 0; s < syllables && i < total; s++) {
    const hz = 300 + (seed[s % seed.length]! % 8) * 60;
    for (let k = 0; k < beep && i < total; k++, i++) {
      const env = Math.min(1, k / 240, (beep - k) / 240);
      pcm.writeInt16LE(Math.round(Math.sin((2 * Math.PI * hz * k) / SAMPLE_RATE) * env * 9000), i * 2);
    }
    i += gap;
  }
  return pcm;
}

async function applyFault(fault: MockFault, signal?: AbortSignal): Promise<void> {
  if (fault === 'timeout') { await sleep(60_000, signal); throw new ProviderError('timeout'); }
  if (fault === '429') throw new ProviderError('rate_limited', 429, true);
  if (fault === '500') throw new ProviderError('unavailable', 500, true);
}
