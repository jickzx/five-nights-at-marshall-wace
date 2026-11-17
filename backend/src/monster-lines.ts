// Monster lines written on the server when OpenRouter is unavailable (no key, key disabled, out of credits, or daily
// budget spent). The line is put together on the spot from the trigger and the words of the sentence the player is
// decoding, so the beast still talks about what the player is doing. Every line still passes checkMonsterLine.
import { randomInt } from 'node:crypto';
import { checkMonsterLine } from './textcheck.ts';
import type { MonsterRequest } from './validate.ts';

// Words that carry no picture. Anything left is a word the beast can grab hold of.
const STOP = new Set([
  'a', 'an', 'the', 'and', 'or', 'but', 'of', 'to', 'in', 'on', 'at', 'for', 'with', 'from', 'by', 'is', 'are', 'was',
  'were', 'be', 'been', 'am', 'do', 'does', 'did', 'not', 'no', 'i', 'you', 'he', 'she', 'it', 'we', 'they', 'me', 'him',
  'her', 'us', 'them', 'my', 'your', 'his', 'its', 'our', 'their', 'this', 'that', 'these', 'those', 'please', 'very',
  'will', 'can', 'has', 'have', 'had', 'there', 'here', 'what', 'who', 'which', 'when', 'where', 'how', 'if', 'so',
  'as', 'than', 'then', 'too', 'also', 'into', 'onto', 'up', 'down', 'out', 'over', 'under', 'about', 'one', 'some',
]);

const DETERMINERS = new Set(['the', 'a', 'an', 'my', 'your', 'his', 'her', 'our', 'their', 'this', 'that', 'some']);

/**
 * Content words from the meaning, lower-case, letters only. Nouns come first: the word right after "the"/"a"
 * (or the one after that, for "the old man"), because a noun reads well in any line; verbs and the rest follow.
 */
export function keywords(meaning: string | null | undefined): string[] {
  if (!meaning) return [];
  const toks = meaning.toLowerCase().split(/[^a-z'’]+/).map((w) => w.replace(/^['’]+|['’]+$/g, '').replace(/['’]s$/, '')).filter(Boolean);
  const ok = (w: string | undefined): w is string => !!w && w.length >= 3 && !STOP.has(w);
  const nouns: string[] = [];
  for (let i = 0; i < toks.length; i++) {
    if (!DETERMINERS.has(toks[i]!)) continue;
    const a = toks[i + 1], b = toks[i + 2];
    const n = ok(a) && ok(b) && !b.endsWith('s') ? b : ok(a) ? a : undefined;
    if (n && !nouns.includes(n)) nouns.push(n);
  }
  const rest = toks.filter((w, i) => ok(w) && !nouns.includes(w) && toks.indexOf(w) === i);
  return [...nouns, ...rest].slice(0, 6);
}

const cap = (s: string) => s.charAt(0).toUpperCase() + s.slice(1);

// {w} = a word from the sentence (a noun when there is one), {v} = a second, different word; {W} = {w} capitalised.
// Lines say the word as a word ("the word for cat"), so they still read when the only word left is a verb.
const WORD_LINES: Record<string, string[]> = {
  puzzle_start: [
    '{W}. Say it for me. I am listening.',
    'Ah, {w}. A small word to die over.',
    'Go on. Find the word for {w}. I will wait right here.',
    '{W}, then {v}. You will not have time for both.',
    'I can smell the word {w} on your breath already.',
    'Spell out {w}, little analyst. I count every second.',
    'A sentence about {w}. How sweet. Read it slowly.',
  ],
  solved: [
    '{W}. Correct. That only makes you louder.',
    'So you found the word for {w}. I found your footprints.',
    'Clever. The word {w} will not save you from me.',
    'One sentence closer to your ship. One step closer to me.',
    'You said {w} so sweetly. Say it again when I am behind you.',
  ],
  first_mistake: [
    'Wrong. That is not how you say {w}.',
    'You stumbled on {w}. I heard it from the water.',
    'Not {w}. Not like that. I am coming.',
  ],
  mistake: [
    'Wrong again. The word {w} is slipping through your fingers.',
    'You fumbled {w}. I took a step.',
    '{W}? No. Try again, while you still can.',
    'Every time you miss {w}, I come closer.',
  ],
  repeat_concept: [
    'The same mistake. The word {w} keeps beating you.',
    'You keep tripping over {w}. I keep getting closer.',
    'Again? {W} was right there.',
  ],
  streak: [
    'Wrong, and wrong, and wrong. Even {w} is laughing at you.',
    'Keep guessing at {w}. I love the sound of it.',
    'Your hands shake over {w}. I can hear them.',
  ],
  stalk: [
    'Still stuck on {w}? I am thinking about you.',
    'Take your time with {w}. I have all night.',
    'The tide is rising, and {w} is still unsolved.',
    'Quiet now. I can hear you whisper {w}.',
  ],
  stage_up: [
    'I grew while you puzzled over {w}.',
    'Something in me just woke. It wants {w}. It wants you.',
  ],
  final_chase: [
    'Run. Take your precious {w} with you.',
    'All those words, and none of them say stop.',
  ],
};

const PLAIN_LINES: Record<string, string[]> = {
  puzzle_start: [
    'Another console. Another chance to fail.',
    'Read it carefully. I am reading you.',
    'Go on, decode it. I will be right here.',
  ],
  solved: [
    'Clever. It will not matter.',
    'Correct. And still so far from your ship.',
    'One more solved. One less place to hide.',
  ],
  first_mistake: ['One wrong word. I heard it.', 'You slipped. I am closer now.'],
  mistake: ['Wrong. I took a step.', 'Another mistake. The shore is getting shorter.', 'No. Try again, while you can.'],
  repeat_concept: ['The same mistake again. I can wait.', 'You keep stumbling on the same thing.'],
  streak: ['Wrong, and wrong again. Keep going.', 'Every mistake brings me nearer.'],
  stalk: [
    'I can hear you breathing.',
    'The water is warm tonight. Come closer.',
    'Do not look behind you. Not yet.',
    'You are taking so long. I like that.',
  ],
  stage_up: ['I am bigger now. Can you hear it?', 'Something in me just grew.'],
  final_chase: ['Run. I am right behind you.', 'The ship is far. I am not.'],
};

function pick<T>(xs: readonly T[], rand: (n: number) => number): T { return xs[rand(xs.length)]!; }

/**
 * One line for this trigger, using the sentence's words when there are any. Tries word lines first, then plain
 * ones, and returns the first that passes the same check as a OpenRouter line (so it is never a recent repeat).
 */
export function writeMonsterLine(req: Pick<MonsterRequest, 'trigger' | 'meaning' | 'distance'>, recent: readonly string[],
  rand: (n: number) => number = randomInt): string | null {
  const words = keywords(req.meaning);
  const tries: string[] = [];
  if (words.length > 0) {
    const pool = [...(WORD_LINES[req.trigger] ?? WORD_LINES.stalk!)];
    for (let i = 0; i < 12 && pool.length > 0; i++) {
      const t = pool.splice(rand(pool.length), 1)[0]!;
      const w = rand(3) < 2 ? words[0]! : pick(words, rand);   // usually the main noun
      const others = words.filter((x) => x !== w);
      if (t.includes('{v}') && others.length === 0) continue;
      const v = others.length > 0 ? pick(others, rand) : w;
      tries.push(t.replaceAll('{W}', cap(w)).replaceAll('{w}', w).replaceAll('{v}', v));
    }
  }
  const plain = [...(PLAIN_LINES[req.trigger] ?? PLAIN_LINES.stalk!)];
  while (plain.length > 0) tries.push(plain.splice(rand(plain.length), 1)[0]!);
  for (const line of tries) if (checkMonsterLine(line, recent).ok) return line;
  return null;
}
