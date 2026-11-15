// The approved hint grounding, shared with the game (Unity's Fnmw.Lang.HintFacts; an EditMode test keeps them equal).
import { readFileSync } from 'node:fs';

export const CONCEPTS = [
  'word-order', 'verb-final', 'particles', 'compound-meaning', 'adjective-order', 'gender-agreement', 'plural-agreement',
  'verb-ending', 'punctuation', 'suffix-order', 'vowel-harmony', 'measure-word', 'consonant-root', 'vowel-pattern', 'vocabulary',
] as const;
export type Concept = (typeof CONCEPTS)[number];

export interface ConceptFacts { focus: string; facts: string[]; offline1: string }
export interface HintFactsFile {
  version: string;
  concepts: Record<Concept, ConceptFacts>;
  giveaways: string[];
  answer_phrases: string[];
  position_words: string[];
  guided: { level1: string; level2: string };
}

function load(): HintFactsFile {
  const raw = JSON.parse(readFileSync(new URL('../data/hint-facts.json', import.meta.url), 'utf8')) as HintFactsFile;
  for (const c of CONCEPTS) {
    const e = raw.concepts?.[c];
    if (!e || typeof e.focus !== 'string' || !Array.isArray(e.facts) || e.facts.length === 0) throw new Error(`hint-facts.json: bad entry for ${c}`);
  }
  return raw;
}

export const HINT_FACTS: HintFactsFile = load();
export const FACTS_VERSION: string = HINT_FACTS.version;

/** A hint built from approved text and the rows the model chose: no generated wording at all. */
export function guidedHint(level: 1 | 2, concept: Concept, a: number, b: number): string {
  const e = HINT_FACTS.concepts[concept];
  const t = level === 1 ? HINT_FACTS.guided.level1 : HINT_FACTS.guided.level2;
  return t.replace('{a}', String(a)).replace('{b}', String(b)).replace('{focus}', e.focus).replace('{fact}', e.facts[0]!);
}
