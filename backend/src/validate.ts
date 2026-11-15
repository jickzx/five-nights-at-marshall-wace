// Request schemas. Every body is a JSON object with exactly the listed keys; anything else is bad_request.
import { CONCEPTS, type Concept } from './hintfacts.ts';
import { LANGS, type Lang } from './config.ts';

export class BadRequest extends Error {}

type Obj = Record<string, unknown>;

function obj(v: unknown, keys: readonly string[], optional: readonly string[] = []): Obj {
  if (typeof v !== 'object' || v === null || Array.isArray(v)) throw new BadRequest('object expected');
  const o = v as Obj;
  for (const k of Object.keys(o)) if (!keys.includes(k) && !optional.includes(k)) throw new BadRequest('unknown field');
  for (const k of keys) if (!(k in o)) throw new BadRequest('missing field');
  return o;
}

function int(v: unknown, min: number, max: number): number {
  if (typeof v !== 'number' || !Number.isInteger(v) || v < min || v > max) throw new BadRequest('integer out of range');
  return v;
}

function oneOf<T extends string>(v: unknown, values: readonly T[]): T {
  if (typeof v !== 'string' || !(values as readonly string[]).includes(v)) throw new BadRequest('unknown value');
  return v as T;
}

function str(v: unknown, re: RegExp, maxChars: number): string {
  if (typeof v !== 'string') throw new BadRequest('string expected');
  if ([...v].length > maxChars || !re.test(v)) throw new BadRequest('string rejected');
  return v;
}

// ------------------------------------------------------------------ session

export interface SessionRequest { proto: 1; build: string }
export function sessionRequest(v: unknown): SessionRequest {
  const o = obj(v, ['proto', 'build']);
  if (o.proto !== 1) throw new BadRequest('proto');
  return { proto: 1, build: str(o.build, /^[A-Za-z0-9._-]{1,32}$/, 32) };
}

// ------------------------------------------------------------------ monster

export const TRIGGERS = ['first_mistake', 'repeat_concept', 'streak', 'stage_up', 'final_chase'] as const;
export type Trigger = (typeof TRIGGERS)[number];
const BUCKET3 = ['low', 'mid', 'high'] as const;

export interface MonsterRequest {
  seq: number; trigger: Trigger; stage: number; concept: Concept | null; concept_misses: number; wrong_streak: number;
  mistakes: number; solved: number; total: number; pace: 'fast' | 'normal' | 'slow'; distance: 'far' | 'mid' | 'near';
  aggression: (typeof BUCKET3)[number];
}

export function monsterRequest(v: unknown): MonsterRequest {
  const o = obj(v, ['seq', 'trigger', 'stage', 'concept', 'concept_misses', 'wrong_streak', 'mistakes', 'solved', 'total', 'pace', 'distance', 'aggression']);
  const r: MonsterRequest = {
    seq: int(o.seq, 0, 1_000_000),
    trigger: oneOf(o.trigger, TRIGGERS),
    stage: int(o.stage, 1, 3),
    concept: o.concept === null ? null : oneOf(o.concept, CONCEPTS),
    concept_misses: int(o.concept_misses, 0, 99),
    wrong_streak: int(o.wrong_streak, 0, 99),
    mistakes: int(o.mistakes, 0, 999),
    solved: int(o.solved, 0, 12),
    total: int(o.total, 0, 12),
    pace: oneOf(o.pace, ['fast', 'normal', 'slow'] as const),
    distance: oneOf(o.distance, ['far', 'mid', 'near'] as const),
    aggression: oneOf(o.aggression, BUCKET3),
  };
  if (r.solved > r.total) throw new BadRequest('solved > total');
  return r;
}

// ------------------------------------------------------------------ hint

// Words and tiles are romanised: Latin letters (with diacritics and the transliteration letters ṭ ḥ ʿ), hyphen,
// apostrophe and the sentence marks the puzzles use. A "word" may be a short phrase with single inner spaces (German
// examples show "Ich weiß, dass" as one unit). No braces, slashes, digits, tabs or newlines.
const WORD_CHAR = "[\\p{Script=Latin}\\p{M}ʿʾ'’\\-¿¡?!.,]";
const WORD_RE = new RegExp(`^${WORD_CHAR}+(?: ${WORD_CHAR}+)*$`, 'u');
const MEANING_RE = /^[\p{Script=Latin}\p{M}0-9 ,.'’?!:;()\-¿¡]{1,100}$/u;

export interface HintExample { meaning: string; words: string[] }
export interface HintRequest {
  seq: number; level: 1 | 2; concept: Concept; attempts: number; target_meaning: string; examples: HintExample[]; tiles: string[];
}

function words(v: unknown, min: number, max: number): string[] {
  if (!Array.isArray(v) || v.length < min || v.length > max) throw new BadRequest('array length');
  return v.map((w) => str(w, WORD_RE, 24));
}

export function hintRequest(v: unknown): HintRequest {
  const o = obj(v, ['seq', 'level', 'concept', 'attempts', 'target_meaning', 'examples', 'tiles']);
  if (!Array.isArray(o.examples) || o.examples.length !== 3) throw new BadRequest('exactly three examples');
  const examples = o.examples.map((e) => {
    const eo = obj(e, ['meaning', 'words']);
    return { meaning: str(eo.meaning, MEANING_RE, 100), words: words(eo.words, 1, 12) };
  });
  return {
    seq: int(o.seq, 0, 1_000_000),
    level: int(o.level, 1, 2) as 1 | 2,
    concept: oneOf(o.concept, CONCEPTS),
    attempts: int(o.attempts, 0, 99),
    target_meaning: str(o.target_meaning, MEANING_RE, 100),
    examples,
    tiles: words(o.tiles, 4, 12),
  };
}

// ------------------------------------------------------------------ pronounce

/** Same bound as Unity's Fnmw.Lang.Speech.MaxLength (measured longest real sentence: 39 characters). */
export const PRONOUNCE_MAX = 80;

const SCRIPT: Record<Lang, RegExp> = {
  ja: /^[\p{Script=Hiragana}\p{Script=Katakana}\p{Script=Han}ー々〆、。！？「」・ 　]+$/u,
  zh: /^[\p{Script=Han}，。！？、：；“” 　]+$/u,
  ar: /^[؀-ۿݐ-ݿ ]+$/u,
  fr: /^[\p{Script=Latin}\p{M} .,!?;:'’«»\-]+$/u,
  es: /^[\p{Script=Latin}\p{M} .,!?¿¡;:'’\-]+$/u,
  de: /^[\p{Script=Latin}\p{M} .,!?;:'’„“\-]+$/u,
  tr: /^[\p{Script=Latin}\p{M} .,!?;:'’\-]+$/u,
};
const NEEDS: Record<Lang, RegExp> = {
  ja: /[\p{Script=Hiragana}\p{Script=Katakana}\p{Script=Han}]/u,
  zh: /\p{Script=Han}/u,
  ar: /[ؠ-ي]/u,
  fr: /\p{Script=Latin}/u, es: /\p{Script=Latin}/u, de: /\p{Script=Latin}/u, tr: /\p{Script=Latin}/u,
};

export interface PronounceRequest { seq: number; lang: Lang; text: string }
export function pronounceRequest(v: unknown): PronounceRequest {
  const o = obj(v, ['seq', 'lang', 'text']);
  const lang = oneOf(o.lang, LANGS);
  if (typeof o.text !== 'string') throw new BadRequest('text');
  const text = o.text.normalize('NFC');
  if (text.trim() !== text || [...text].length < 1 || [...text].length > PRONOUNCE_MAX) throw new BadRequest('text length');
  if (!SCRIPT[lang].test(text) || !NEEDS[lang].test(text)) throw new BadRequest('script');
  return { seq: int(o.seq, 0, 1_000_000), lang, text };
}

/** Opaque ids the server issued: 32 hex characters. */
export const SPEECH_ID_RE = /^[0-9a-f]{32}$/;
