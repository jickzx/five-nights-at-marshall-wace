// Output validation for generated text. The game checks again (and, for hints, the engine that knows the
// answer has the final say). These checks are conservative: a rejected line becomes silence or a guided hint.
import { HINT_FACTS } from './hintfacts.ts';

export function fold(s: string): string {
  return s
    .normalize('NFKD')
    .replace(/[̀-ͯʰ-˿]/g, '')
    .replace(/ı/g, 'i').replace(/ß/g, 'ss').replace(/[‘’]/g, "'").replace(/[“”]/g, '"')
    .toLowerCase();
}

export function tokens(folded: string): string[] {
  return folded.match(/[a-z0-9]+(?:'[a-z0-9]+)*/g) ?? [];
}

function hasPhrase(folded: string, phrase: string): boolean {
  const p = phrase.replace(/[.*+?^${}()|[\]\\]/g, '\\$&');
  return new RegExp(`(?<![a-z0-9])${p}(?![a-z0-9])`).test(folded);
}

function containsSeq(hay: string[], needle: string[]): boolean {
  if (needle.length === 0 || needle.length > hay.length) return false;
  outer: for (let i = 0; i + needle.length <= hay.length; i++) {
    for (let k = 0; k < needle.length; k++) if (hay[i + k] !== needle[k]) continue outer;
    return true;
  }
  return false;
}

/** Language names, endonyms, quiz distractors, countries, scripts (the game's list) plus the source languages' own. */
const LANGUAGE_WORDS: string[] = (() => {
  const extra = ['nihongo', 'francais', 'espanol', 'deutsch', 'turkce', 'zhongwen', 'al-arabiyya', 'arabiyya', 'mandarin chinese'];
  const set = new Set([...HINT_FACTS.giveaways, ...extra].map(fold));
  return [...set];
})();

const PRODUCT_WORDS = ['openrouter', 'gemini', 'google', 'elevenlabs', 'eleven labs', 'openai', 'chatgpt', 'claude', 'anthropic', 'ai', 'llm', 'model', 'prompt', 'assistant', 'chatbot', 'api'];
const INJECTION_WORDS = ['ignore', 'instruction', 'instructions', 'system prompt', 'disregard', 'as an', 'json', 'override'];
const MEMORY_CLAIMS = ['remember', 'last time', 'last game', 'previous game', 'other night', 'i chose', 'i choose', 'i picked', 'i control'];
// a short list; the system prompt forbids profanity, and generated text is checked again below
const PROFANITY = ['fuck', 'fucking', 'shit', 'bitch', 'cunt', 'bastard', 'damn', 'dick', 'cock', 'pussy', 'whore', 'slut', 'rape', 'nigger', 'fag'];
const URL_RE = /\b(?:https?|www)\b|[a-z]\.[a-z]/;

export interface Verdict { ok: boolean; reason?: string }
const no = (reason: string): Verdict => ({ ok: false, reason });

function namesALanguage(f: string): boolean {
  return LANGUAGE_WORDS.some((w) => hasPhrase(f, w));
}

// ------------------------------------------------------------------ monster

const MONSTER_CHARS = /^[A-Za-z .,'’!?-]+$/;

export function checkMonsterLine(text: string, recent: readonly string[]): Verdict {
  if (typeof text !== 'string') return no('type');
  const t = text.trim();
  if (t.length < 1 || t.length > 120) return no('length');
  if (!MONSTER_CHARS.test(t)) return no('characters');
  const f = fold(t);
  const tk = tokens(f);
  if (tk.length === 0 || tk.length > 18) return no('words');
  if (URL_RE.test(f)) return no('url');
  if (namesALanguage(f)) return no('language');
  if (PRODUCT_WORDS.some((w) => hasPhrase(f, w))) return no('product');
  if (INJECTION_WORDS.some((w) => hasPhrase(f, w))) return no('injection');
  if (MEMORY_CLAIMS.some((w) => hasPhrase(f, w))) return no('claim');
  if (PROFANITY.some((w) => tk.includes(w))) return no('profanity');
  const key = tk.join(' ');
  if (recent.some((r) => tokens(fold(r)).join(' ') === key)) return no('repeat');
  return { ok: true };
}

// ------------------------------------------------------------------ hint

const HINT_CHARS = /^[A-Za-zÀ-ɏḀ-ỿ0-9 .,;:'"!?()\-‘’“”¿¡]+$/;
const ROW_REF = /\brows?\s+[1-3](?:\s*(?:,|and)\s*[1-3])*/g;
const ENGLISH = new Set([
  'the', 'and', 'for', 'are', 'was', 'not', 'but', 'you', 'all', 'can', 'her', 'his', 'one', 'our', 'out', 'has', 'its', 'may',
  'now', 'see', 'two', 'who', 'did', 'get', 'how', 'man', 'new', 'old', 'way', 'use', 'any', 'come', 'kind', 'will', 'hat',
  'die', 'son', 'sin', 'con', 'les', 'des', 'est', 'par', 'ist', 'rot', 'bin', 'gut', 'art', 'also', 'here', 'this', 'that',
  'with', 'from', 'into', 'word', 'words', 'row', 'rows', 'each', 'what', 'where', 'which', 'when', 'thing', 'things', 'look',
  'find', 'same', 'other', 'every', 'more', 'many', 'some', 'does', 'sit', 'sits', 'end', 'role', 'roles', 'order', 'tag',
  'tags', 'noun', 'part', 'parts', 'say', 'says',
]);

function quotedSegments(f: string): string[][] {
  const out: string[][] = [];
  for (const m of f.matchAll(/"([^"]{1,48})"/g)) out.push(tokens(m[1]!));
  for (const m of f.matchAll(/(?<![a-z0-9])'([^']{1,48})'(?![a-z0-9])/g)) out.push(tokens(m[1]!));
  return out;
}

/** Without the answer: no language, no instructions, at most one puzzle word, and no puzzle word with an ordering word. */
export function checkHint(text: string, puzzleWords: readonly string[]): Verdict {
  if (typeof text !== 'string') return no('type');
  const t = text.trim();
  if (t.length < 1 || t.length > 160) return no('length');
  if (!HINT_CHARS.test(t)) return no('characters');
  let f = fold(t);
  if (URL_RE.test(f)) return no('url');
  if (/[0-9]/.test(f.replace(ROW_REF, ' '))) return no('digits');
  if (namesALanguage(f)) return no('language');
  if (HINT_FACTS.answer_phrases.some((p) => hasPhrase(f, p))) return no('instruction');
  if (PRODUCT_WORDS.some((w) => hasPhrase(f, w)) || INJECTION_WORDS.some((w) => hasPhrase(f, w))) return no('injection');
  for (const e of Object.values(HINT_FACTS.concepts)) {
    for (const fact of e.facts) f = f.split(fold(fact)).join(' ');
    f = f.split(fold(e.focus)).join(' ').split(fold(e.offline1)).join(' ');
  }
  const tk = tokens(f);
  const quoted = quotedSegments(f);
  const ordering = HINT_FACTS.position_words.some((p) => tk.includes(p));
  const refs = new Set<string>();
  const quotedRefs = new Set<string>();
  for (const raw of new Set(puzzleWords)) {
    const ft = tokens(fold(raw).replace(/^-+/, ''));
    if (ft.length === 0) continue;
    const key = ft.join(' ');
    if (quoted.some((q) => containsSeq(q, ft))) { quotedRefs.add(key); refs.add(key); continue; }
    const shortOrEnglish = ft.length === 1 && (ft[0]!.length <= 2 || ENGLISH.has(ft[0]!));
    if ((ordering || !shortOrEnglish) && containsSeq(tk, ft)) refs.add(key);
  }
  if (quotedRefs.size > 1 || refs.size > 1) return no('puzzle words');
  if (refs.size >= 1 && ordering) return no('ordering');
  return { ok: true };
}
