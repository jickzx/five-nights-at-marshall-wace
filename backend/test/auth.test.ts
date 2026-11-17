import { test } from 'node:test';
import assert from 'node:assert/strict';
import { ConfigError, loadConfig } from '../src/config.ts';
import { SessionSigner } from '../src/auth.ts';
import { env, openRouterJson, hintBody, monsterBody, pcm, SECRETS, start } from './helpers.ts';

test('B1: no auth, wrong game token, tampered/expired/other-secret session -> 401 and no provider call', async () => {
  const h = await start();
  try {
    assert.equal((await h.post('/v1/session', { proto: 1, build: 'x' })).status, 401);
    assert.equal((await h.post('/v1/session', { proto: 1, build: 'x' }, 'wrong-token-0123456789abcdef')).status, 401);
    assert.equal((await h.post('/v1/monster-line', monsterBody())).status, 401);
    const token = await h.session();
    const tampered = token.slice(0, -2) + (token.endsWith('AA') ? 'BB' : 'AA');
    assert.equal((await h.post('/v1/monster-line', monsterBody(), tampered)).status, 401);
    const other = new SessionSigner(Buffer.from('another-secret-another-secret-another-secret')).sign(token.split('.')[1]!, 9_999_999_999, '127.0.0.1');
    assert.equal((await h.post('/v1/hint', hintBody(), other)).status, 401);
    // expiry: move the clock past the session's two hours
    h.clock.t += 7201 * 1000;
    assert.equal((await h.post('/v1/monster-line', monsterBody(), token)).status, 401);
    assert.equal(h.fake.calls.length, 0);
    const body = await (await h.post('/v1/monster-line', monsterBody(), token)).json();
    assert.deepEqual(body, { error: { code: 'unauthorized' } });
  } finally { await h.close(); }
});

test('session ids are server-chosen, and an id the server never issued is refused even with a valid MAC', async () => {
  const h = await start();
  try {
    const a = await h.session(), b = await h.session();
    assert.notEqual(a.split('.')[1], b.split('.')[1]);
    assert.match(a.split('.')[1]!, /^[0-9a-f]{32}$/);
    const forged = h.svc.signer.sign('0'.repeat(32), Math.floor(h.clock.t / 1000) + 3600, '127.0.0.1');
    assert.equal((await h.post('/v1/pronounce', { seq: 1, lang: 'fr', text: 'Le chat dort.' }, forged)).status, 401);
    assert.equal((await h.post('/v1/session', { proto: 1, build: 'x', extra: 1 }, SECRETS.game)).status, 400);
  } finally { await h.close(); }
});

test('B2: speech id owned by another session, unknown or expired -> 404', async () => {
  const h = await start();
  try {
    const a = await h.session(), b = await h.session();
    h.fake.queue.push(() => pcm());
    const r = await h.post('/v1/pronounce', { seq: 1, lang: 'fr', text: 'Le chat noir dort.' }, a);
    assert.equal(r.status, 200);
    const { speech } = (await r.json()) as { speech: { id: string } };
    assert.equal((await h.get(`/v1/speech/${speech.id}`, b)).status, 404, 'another session');
    assert.equal((await h.get(`/v1/speech/${'f'.repeat(32)}`, a)).status, 404, 'unknown');
    assert.equal((await h.get(`/v1/speech/../../etc`, a)).status, 404, 'not an id');
    const ok = await h.get(`/v1/speech/${speech.id}`, a);
    assert.equal(ok.status, 200);
    assert.equal(ok.headers.get('x-fnmw-audio'), 'pcm_s16le;rate=24000;channels=1');
    assert.equal((await ok.arrayBuffer()).byteLength, 4800);
    h.clock.t += 601 * 1000;
    assert.equal((await h.get(`/v1/speech/${speech.id}`, a)).status, 404, 'expired after ten minutes');
  } finally { await h.close(); }
});

test('B21: a missing key or voice id turns that feature off; the server still starts', async () => {
  const h = await start({ ELEVENLABS_API_KEY: '', FNMW_VOICE_SHIP: '' });
  try {
    const r = await h.post('/v1/session', { proto: 1, build: 'x' }, SECRETS.game);
    const s = (await r.json()) as { session: string; mode: string; features: Record<string, boolean> };
    assert.equal(s.mode, 'live');
    assert.deepEqual(s.features, { monster: false, hint: true, hint_speech: false, pronounce: false });
    const m = await h.post('/v1/monster-line', monsterBody(), s.session);
    assert.equal(m.status, 503);
    assert.deepEqual(await m.json(), { error: { code: 'disabled' } });
    assert.equal(h.fake.calls.length, 0);
  } finally { await h.close(); }
  const none = loadConfig(env({ OPENROUTER_API_KEY: '', ELEVENLABS_API_KEY: undefined }));
  assert.equal(none.mode, 'live', 'live is the default; without keys every feature is off');
});

test('B22: live mode refuses a short session secret, a bad model id, and a missing game token', () => {
  assert.throws(() => loadConfig(env({ FNMW_SESSION_SECRET: 'short' })), ConfigError);
  assert.throws(() => loadConfig(env({ FNMW_OPENROUTER_MODEL: 'models/../x' })), ConfigError);
  assert.throws(() => loadConfig(env({ FNMW_VOICE_SHIP: 'a/b' })), ConfigError);
  assert.throws(() => loadConfig(env({ FNMW_GAME_TOKENS: '' })), ConfigError);
  assert.throws(() => loadConfig(env({ FNMW_PROVIDER_MODE: 'yolo' })), ConfigError);
  assert.throws(() => loadConfig(env({ FNMW_MOCK_FAULT: '429' })), ConfigError, 'faults are for mock mode only');
  // error messages name variables, never values
  try { loadConfig(env({ FNMW_SESSION_SECRET: 'short' })); } catch (e) { assert.doesNotMatch(String(e), /short/); }
});

test('B20: no log line carries a secret, token, request text or generated text', async () => {
  const h = await start();
  try {
    const token = await h.session();
    h.fake.queue.push(() => openRouterJson({ line: 'You keep stumbling on the same thing.' }));
    h.fake.queue.push(() => pcm());
    const m = await h.post('/v1/monster-line', monsterBody(), token);
    const mj = (await m.json()) as { speech: { id: string } };
    await (await h.get(`/v1/speech/${mj.speech.id}`, token)).arrayBuffer();
    h.fake.queue.push(() => openRouterJson({ rows: [1, 3], hint: 'Compare rows 1 and 3: where does the action word wait?' }));
    h.fake.queue.push(() => pcm());
    await (await h.post('/v1/hint', hintBody(), token)).json();
    h.fake.queue.push(() => new Response('{"detail":{"message":"secret provider detail"}}', { status: 401 }));
    await (await h.post('/v1/pronounce', { seq: 1, lang: 'de', text: 'Der Hund schläft.' }, token)).json();
    const all = h.logs.join('\n');
    assert.ok(h.logs.length > 5);
    for (const needle of [SECRETS.game, SECRETS.session, SECRETS.openRouter, SECRETS.eleven, token, token.split('.')[1]!, 'stumbling',
      'action word', 'Der Hund', 'tobira', 'Please open', 'secret provider detail', '127.0.0.1', 'voiceMonster01'])
      assert.ok(!all.includes(needle), `log leaked: ${needle}`);
    for (const line of h.logs) assert.doesNotThrow(() => JSON.parse(line));
  } finally { await h.close(); }
});

test('healthz says ok and nothing else; unknown routes are 404 with no CORS headers', async () => {
  const h = await start();
  try {
    const r = await h.get('/healthz');
    assert.deepEqual(await r.json(), { ok: true });
    const n = await h.get('/v1/nothing');
    assert.equal(n.status, 404);
    assert.equal(n.headers.get('access-control-allow-origin'), null);
  } finally { await h.close(); }
});


test('OpenRouter configuration allows only free model ids and requires a local key', () => {
  const cfg = loadConfig(env({ OPENROUTER_API_KEY: undefined }));
  assert.equal(cfg.openRouterKey, undefined);
  assert.equal(cfg.openRouterModel, 'openrouter/free');
  assert.equal(loadConfig(env({ FNMW_OPENROUTER_MODEL: 'test/model:free' })).openRouterModel, 'test/model:free');
  for (const model of ['test/paid', 'openrouter/auto', 'test/model:free/other'])
    assert.throws(() => loadConfig(env({ FNMW_OPENROUTER_MODEL: model })), ConfigError);
});
