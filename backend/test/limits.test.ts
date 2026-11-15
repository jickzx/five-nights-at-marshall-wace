import { test } from 'node:test';
import assert from 'node:assert/strict';
import { geminiJson, hang, hintBody, pcm, SECRETS, start } from './helpers.ts';
import { LruCache } from '../src/cache.ts';
import { WindowCounter } from '../src/limits.ts';

test('B6: session quota, IP session limit, IP request limit and the daily budget refuse before any provider call', async () => {
  const h = await start({}, (c) => { c.limits.perSession.pronounce = 1; c.limits.daily.speechChars = 30; });
  try {
    const t = await h.session();
    h.fake.queue.push(() => pcm());
    assert.equal((await h.post('/v1/pronounce', { seq: 1, lang: 'fr', text: 'Le chat noir dort.' }, t)).status, 200);
    const over = await h.post('/v1/pronounce', { seq: 2, lang: 'fr', text: 'Le chien dort.' }, t);
    assert.equal(over.status, 429, 'per-session pronounce quota');
    const t2 = await h.session();
    const budget = await h.post('/v1/pronounce', { seq: 1, lang: 'fr', text: 'Le petit chien mange vite.' }, t2);
    assert.equal(budget.status, 503);
    assert.deepEqual(await budget.json(), { error: { code: 'budget_exhausted' } });
    assert.equal(h.fake.eleven().length, 1);
    // three sessions per IP per window: the fourth is refused
    assert.equal((await h.post('/v1/session', { proto: 1, build: 'x' }, SECRETS.game)).status, 200);
    const fourth = await h.post('/v1/session', { proto: 1, build: 'x' }, SECRETS.game);
    assert.equal(fourth.status, 429);
    assert.ok(((await fourth.json()) as { error: { retry_after_ms: number } }).error.retry_after_ms > 0);
  } finally { await h.close(); }
});

test('B7: a new session does not reset IP or global counters', async () => {
  const h = await start({}, (c) => { c.limits.daily.geminiCalls = 2; c.limits.sessionsPerIpPerWindow = 10; });
  try {
    h.fake.fallback = () => geminiJson({ rows: [1, 3], hint: 'Compare rows 1 and 3: where does the action word wait?' });
    const a = await h.session();
    assert.equal((await h.post('/v1/hint', hintBody({ level: 1 }), a)).status, 200);
    assert.equal((await h.post('/v1/hint', hintBody({ level: 2 }), a)).status, 200);
    const b = await h.session();
    const r = await h.post('/v1/hint', hintBody({ level: 1, target_meaning: 'Please open the window.' }), b);
    assert.equal(r.status, 503, 'the daily Gemini budget is global, not per session');
    assert.equal(h.fake.gemini().length, 2);
  } finally { await h.close(); }
  const h2 = await start({}, (c) => { c.limits.requestsPerIpPerWindow = 3; c.limits.sessionsPerIpPerWindow = 10; });
  try {
    await h2.session(); await h2.session(); await h2.session();
    const t = await h2.post('/v1/session', { proto: 1, build: 'x' }, SECRETS.game);
    assert.equal(t.status, 429, 'requests per IP count across sessions');
  } finally { await h2.close(); }
});

test('B8: three concurrent speech generations with a gate of 2: the third is refused at once, and the gate is released', async () => {
  const h = await start({}, (c) => { c.limits.ttsPerSessionConcurrency = 5; });
  try {
    const [a, b, c] = [await h.session(), await h.session(), await h.session()];
    let release!: () => void;
    const gate = new Promise<void>((r) => { release = r; });
    h.fake.fallback = async () => { await gate; return pcm(); };
    const r1 = await h.post('/v1/pronounce', { seq: 1, lang: 'fr', text: 'Le chat dort.' }, a);
    const r2 = await h.post('/v1/pronounce', { seq: 1, lang: 'es', text: 'El gato duerme.' }, b);
    const t0 = Date.now();
    const r3 = await h.post('/v1/pronounce', { seq: 1, lang: 'de', text: 'Die Katze schläft.' }, c);
    assert.equal(r1.status, 200); assert.equal(r2.status, 200);
    assert.equal(r3.status, 429);
    assert.ok(Date.now() - t0 < 1000, 'refused at once, not queued');
    assert.equal(h.svc.ttsGate.active, 2);
    release();
    const s1 = ((await r1.json()) as { speech: { id: string } }).speech.id;
    assert.equal((await h.get(`/v1/speech/${s1}`, a)).status, 200);
    await new Promise((r) => setTimeout(r, 20));
    assert.equal(h.svc.ttsGate.active, 0, 'finished jobs release their reservation');
  } finally { await h.close(); }
});

test('a failed or timed-out speech job releases the gate; an unbilled failure refunds the characters', async () => {
  const h = await start({}, (c) => { c.limits.ttsDeadlineMs = 150; c.limits.speechWaitMs = 2000; });
  try {
    const t = await h.session();
    h.fake.queue.push(() => new Response('busy', { status: 503 }));
    const r = await h.post('/v1/pronounce', { seq: 1, lang: 'fr', text: 'Le chat dort.' }, t);
    const id = ((await r.json()) as { speech: { id: string } }).speech.id;
    const s = await h.get(`/v1/speech/${id}`, t);
    assert.equal(s.status, 503);
    assert.equal(h.svc.ttsGate.active, 0);
    assert.equal(h.svc.dailySpeech.used, 0, 'HTTP error: nothing billed, characters refunded');
    h.fake.queue.push(hang);
    const r2 = await h.post('/v1/pronounce', { seq: 2, lang: 'fr', text: 'Le chien dort.' }, t);
    const id2 = ((await r2.json()) as { speech: { id: string } }).speech.id;
    const s2 = await h.get(`/v1/speech/${id2}`, t);
    assert.equal(s2.status, 504);
    assert.equal(h.svc.ttsGate.active, 0);
    assert.equal(h.svc.dailySpeech.used, [...'Le chien dort.'].length, 'a timeout may have been billed: kept');
  } finally { await h.close(); }
});

test('the text response completing does not cancel speech: audio is fetched afterwards', async () => {
  const h = await start();
  try {
    const t = await h.session();
    let finish!: () => void;
    const slow = new Promise<void>((r) => { finish = r; });
    h.fake.queue.push(() => geminiJson({ rows: [1, 3], hint: 'Compare rows 1 and 3: where does the action word wait?' }));
    h.fake.queue.push(async () => { await slow; return pcm(1200); });
    const r = await h.post('/v1/hint', hintBody(), t);
    assert.equal(r.status, 200);
    const j = (await r.json()) as { text: string; speech: { id: string } };
    assert.ok(j.text.length > 0);
    setTimeout(finish, 50);
    const audio = await h.get(`/v1/speech/${j.speech.id}`, t);
    assert.equal(audio.status, 200);
    assert.equal((await audio.arrayBuffer()).byteLength, 2400);
  } finally { await h.close(); }
});

test('memory stays bounded: caches evict by bytes, counters by keys, sessions by a cap', async () => {
  let now = 0;
  const c = new LruCache<Buffer>({ maxEntries: 100, maxBytes: 1000, ttlMs: 10_000, now: () => now, size: (b) => b.length });
  for (let i = 0; i < 50; i++) c.set('k' + i, Buffer.alloc(300));
  assert.ok(c.bytes <= 1000); assert.equal(c.size, 3);
  c.set('huge', Buffer.alloc(5000));
  assert.equal(c.get('huge'), undefined, 'an entry bigger than the cap is never stored');
  now = 20_000;
  assert.equal(c.get('k49'), undefined, 'TTL');
  const w = new WindowCounter(1, 1000, () => now, 10);
  for (let i = 0; i < 100; i++) w.take('ip' + i);
  assert.ok(w.take('ip99') === false);
  const h = await start({}, (cfg) => { cfg.limits.maxSessions = 2; cfg.limits.sessionsPerIpPerWindow = 10; });
  try {
    await h.session(); await h.session();
    assert.equal((await h.post('/v1/session', { proto: 1, build: 'x' }, SECRETS.game)).status, 429);
    h.clock.t += 7201 * 1000;
    assert.equal((await h.post('/v1/session', { proto: 1, build: 'x' }, SECRETS.game)).status, 200, 'expired sessions free their slots');
    assert.ok(h.svc.sessionCount <= 2);
  } finally { await h.close(); }
});
