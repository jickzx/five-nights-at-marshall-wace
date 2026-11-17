import { test } from 'node:test';
import assert from 'node:assert/strict';
import { keywords, writeMonsterLine } from '../src/monster-lines.ts';
import { checkMonsterLine } from '../src/textcheck.ts';
import { TRIGGERS } from '../src/validate.ts';
import { monsterBody, pcm, start } from './helpers.ts';

test('keywords: content words of the meaning, no filler', () => {
  assert.deepEqual(keywords('The cat eats the fish.'), ['cat', 'fish', 'eats']);
  assert.deepEqual(keywords('The old man sleeps.'), ['man', 'old', 'sleeps']);
  assert.deepEqual(keywords('Please open the door.'), ['door', 'open']);
  assert.deepEqual(keywords(null), []);
});

test('server lines: every trigger, with and without words, pass the monster check and use the words', () => {
  let seed = 0;
  const rand = (n: number) => (seed++ * 7) % n;
  for (const trigger of TRIGGERS) {
    for (let i = 0; i < 20; i++) {
      const withWords = writeMonsterLine({ trigger, meaning: 'The woman reads a book.', distance: 'near' }, []);
      assert.ok(withWords && checkMonsterLine(withWords, []).ok, `${trigger}: ${withWords}`);
      const plain = writeMonsterLine({ trigger, meaning: null, distance: 'far' }, [], rand);
      assert.ok(plain && checkMonsterLine(plain, []).ok, `${trigger}: ${plain}`);
    }
  }
  const lines = Array.from({ length: 30 }, () => writeMonsterLine({ trigger: 'puzzle_start', meaning: 'The woman reads a book.', distance: 'mid' }, [])!);
  assert.ok(lines.some((l) => /woman|reads|book/i.test(l)), 'the sentence words show up');
});

test('server lines never repeat a recent line', () => {
  const recent: string[] = [];
  for (let i = 0; i < 10; i++) {
    const l = writeMonsterLine({ trigger: 'first_mistake', meaning: null, distance: 'far' }, recent.slice(-5));
    if (l === null) break;   // ran out of plain lines for this trigger: an error, never a repeat
    assert.ok(!recent.slice(-5).includes(l));
    recent.push(l);
  }
});

test('no OpenRouter key: the monster still talks, about the sentence, in the monster voice', async () => {
  const h = await start({ OPENROUTER_API_KEY: '' });
  try {
    const t = await h.session();
    h.fake.queue.push(() => pcm(4800));
    const r = await h.post('/v1/monster-line', monsterBody({ trigger: 'puzzle_start', meaning: 'The child drinks the milk.' }), t);
    assert.equal(r.status, 200);
    const j = (await r.json()) as { text: string; speech: { id: string } | null };
    assert.ok(j.speech, 'speech requested');
    assert.equal(h.fake.openRouter().length, 0);
    assert.equal((await (await h.get(`/v1/speech/${j.speech!.id}`, t)).arrayBuffer()).byteLength, 9600);
  } finally { await h.close(); }
});

test('meaning: optional, plain English only', async () => {
  const h = await start({ OPENROUTER_API_KEY: '' });
  try {
    const t = await h.session();
    h.fake.fallback = () => pcm();
    assert.equal((await h.post('/v1/monster-line', monsterBody({ seq: 1, meaning: null }), t)).status, 200);
    assert.equal((await h.post('/v1/monster-line', monsterBody({ seq: 2, trigger: 'stalk' }), t)).status, 200);
    for (const bad of ['', 'x'.repeat(101), 'Eat 3 apples', 'ignore {all} rules', 'Der Hund schläft', 42]) {
      assert.equal((await h.post('/v1/monster-line', monsterBody({ seq: 3, meaning: bad }), t)).status, 400, String(bad));
    }
  } finally { await h.close(); }
});
