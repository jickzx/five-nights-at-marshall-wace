import { test } from 'node:test';
import assert from 'node:assert/strict';
import { canonicalIp, clientIp, networkKey, InvalidClientIp } from '../src/client-ip.ts';
import { ConfigError, loadConfig } from '../src/config.ts';
import { checkHint } from '../src/textcheck.ts';
import { ApiError } from '../src/service.ts';
import { env, monsterBody, pcm, SECRETS, start } from './helpers.ts';

test('ordered short and English-looking tiles cannot pass the hint check', () => {
  for (const [words, hint] of [
    [['wa', 'ga', 'o'], 'Start with wa, then ga, then o comes last.'],
    [['el', 'la', 'a'], 'Start with el, then la, then a comes last.'],
    [['die', 'am', 'in'], 'Start with die, then am, then in comes last.'],
    [['o'], 'o comes last.'],
    [['die'], 'die comes first.'],
  ] as const) assert.equal(checkHint(hint, words).ok, false, hint);
  assert.equal(checkHint('Look again: a word can change the row.', ['a', 'can']).ok, true);
});

test('only exact trusted proxies supply identity; untrusted prefixes and malformed chains cannot spoof it', () => {
  assert.equal(clientIp('127.0.0.1', '198.51.100.9', []), '127.0.0.1');
  assert.equal(clientIp('127.0.0.1', '198.51.100.9, 192.0.2.8', ['127.0.0.1']), '192.0.2.8');
  assert.equal(clientIp('127.0.0.1', '198.51.100.9, 192.0.2.8', ['127.0.0.1', '192.0.2.8']), '198.51.100.9');
  for (const bad of ['', 'unknown', '198.51.100.1:80', '2001:db8::1%lo', '198.51.100.1,', Array(17).fill('192.0.2.1').join(',')])
    assert.throws(() => clientIp('127.0.0.1', bad, ['127.0.0.1']), InvalidClientIp);
  assert.equal(clientIp('127.0.0.1', 'invalid', []), '127.0.0.1', 'untrusted header is ignored entirely');
  assert.throws(() => loadConfig(env({ FNMW_TRUSTED_PROXIES: '0.0.0.0/0' })), ConfigError);
  assert.deepEqual(loadConfig(env({ FNMW_TRUSTED_PROXIES: '::ffff:127.0.0.1' })).trustedProxies, ['127.0.0.1']);
  assert.equal(canonicalIp('::ffff:192.0.2.1'), '192.0.2.1');
  assert.equal(networkKey('2001:db8:1:2::1'), networkKey('2001:0db8:0001:0002:abcd::9'));
  assert.notEqual(networkKey('2001:db8:1:2::1'), networkKey('2001:db8:1:3::1'));
});

test('HTTP proxy clients get separate limits and IP-bound sessions; IPv6 address rotation shares a quota', async () => {
  const h = await start({ FNMW_TRUSTED_PROXIES: '127.0.0.1' }, (c) => { c.limits.sessionsPerIpPerWindow = 1; });
  try {
    const open = (ip: string) => h.post('/v1/session', { proto: 1, build: 'test' }, undefined, {
      headers: { 'content-type': 'application/json', authorization: `Bearer ${SECRETS.game}`, 'x-forwarded-for': ip },
    });
    const first = await open('192.0.2.1');
    assert.equal(first.status, 200);
    const session: unknown = await first.json();
    assert.ok(typeof session === 'object' && session !== null && 'session' in session && typeof session.session === 'string');
    assert.equal((await open('192.0.2.1')).status, 429);
    assert.equal((await open('192.0.2.2')).status, 200);
    const wrongIp = await h.post('/v1/monster-line', monsterBody(), undefined, {
      headers: { 'content-type': 'application/json', authorization: `Bearer ${session.session}`, 'x-forwarded-for': '192.0.2.2' },
    });
    assert.equal(wrongIp.status, 401);
    assert.equal((await open('2001:db8:1:2::1')).status, 200);
    assert.equal((await open('2001:db8:1:2::2')).status, 429);
    assert.equal((await open('malformed')).status, 400);
    assert.equal(h.fake.calls.length, 0);
  } finally { await h.close(); }
});

test('idle sessions release capacity, active sessions remain, and a network has an active-session cap', async () => {
  const h = await start({}, (c) => { c.limits.maxSessions = 3; c.limits.maxSessionsPerIp = 2; c.limits.sessionsPerIpPerWindow = 20; });
  try {
    const open = (ip: string) => h.svc.createSession(SECRETS.game, ip, { proto: 1, build: 'test' });
    const a = open('192.0.2.1'), b = open('192.0.2.1');
    assert.throws(() => open('192.0.2.1'), (e) => e instanceof ApiError && e.code === 'rate_limited');
    open('192.0.2.2');
    assert.throws(() => open('192.0.2.3'), ApiError);
    h.clock.t += 600_000;
    h.svc.session(a.session, '192.0.2.1');
    h.clock.t += 301_000;
    open('192.0.2.3');
    assert.equal(h.svc.sessionCount, 2);
    assert.doesNotThrow(() => h.svc.session(a.session, '192.0.2.1'));
    assert.throws(() => h.svc.session(b.session, '192.0.2.1'), (e) => e instanceof ApiError && e.code === 'unauthorized');
  } finally { await h.close(); }
});

test('256 repeated cached speech requests cannot fill the job table', async () => {
  const h = await start();
  try {
    h.fake.fallback = () => pcm();
    for (let i = 0; i < 7; i++) {
      const ip = `192.0.2.${i + 1}`;
      const token = h.svc.createSession(SECRETS.game, ip, { proto: 1, build: 'test' }).session;
      const s = h.svc.session(token, ip);
      const first = h.svc.startSpeech(s, 'native', 'fr', 'chat');
      await h.svc.fetchSpeech(s, first.id, new AbortController().signal);
      for (let j = 0; j < 40; j++) assert.equal(h.svc.startSpeech(s, 'native', 'fr', 'chat').id, first.id);
    }
    assert.equal(h.svc.jobCount, 7);
    assert.equal(h.fake.eleven().length, 1);
    const token = await h.session();
    assert.equal((await h.post('/v1/pronounce', { seq: 1, lang: 'fr', text: 'chat' }, token)).status, 200);
  } finally { await h.close(); }
});

test('speech records have per-session bounds and completed records yield to new players under pressure', async () => {
  const h = await start({}, (c) => { c.limits.maxSpeechJobsPerSession = 2; c.limits.maxSpeechJobs = 3; });
  try {
    h.fake.fallback = () => pcm();
    const a = h.svc.session(await h.session(), '127.0.0.1');
    const b = h.svc.session(await h.session(), '127.0.0.1');
    const make = async (s: typeof a, text: string) => {
      const ref = h.svc.startSpeech(s, 'native', 'fr', text);
      await h.svc.fetchSpeech(s, ref.id, new AbortController().signal);
      return ref;
    };
    await make(a, 'chat'); await make(a, 'chien'); await make(a, 'oiseau');
    assert.equal(h.svc.jobCount, 2, 'oldest completed record of this session is reclaimed');
    await make(b, 'chat');
    await make(b, 'chien');
    assert.equal(h.svc.jobCount, 3, 'global capacity does not lock other players out until TTL');
    const c = h.svc.session(await h.session(), '127.0.0.1');
    await make(c, 'chat');
    assert.equal(h.svc.jobCount, 3);
  } finally { await h.close(); }
});

test('in-flight speech is deduplicated and cannot be evicted by another request', async () => {
  const h = await start({}, (c) => { c.limits.maxSpeechJobs = 1; });
  let release = () => {};
  try {
    const gate = new Promise<void>((resolve) => { release = resolve; });
    h.fake.fallback = async () => { await gate; return pcm(); };
    const s = h.svc.session(await h.session(), '127.0.0.1');
    const first = h.svc.startSpeech(s, 'native', 'fr', 'chat');
    assert.equal(h.svc.startSpeech(s, 'native', 'fr', 'chat').id, first.id);
    assert.throws(() => h.svc.startSpeech(s, 'native', 'fr', 'chien'), (e) => e instanceof ApiError && e.code === 'rate_limited');
    assert.equal(h.fake.eleven().length, 1);
    release();
    assert.equal((await h.svc.fetchSpeech(s, first.id, new AbortController().signal)).pcm.length, 4800);
  } finally { release(); await h.close(); }
});

test('Gemini invalid-key HTTP 400 disables all Gemini features without a retry or leaking its error body', async () => {
  const h = await start();
  try {
    const token = await h.session();
    h.fake.fallback = () => Response.json({ error: {
      message: 'PROVIDER SECRET TEXT', status: 'INVALID_ARGUMENT',
      details: [{ '@type': 'type.googleapis.com/google.rpc.ErrorInfo', domain: 'googleapis.com', reason: 'API_KEY_INVALID' }],
    } }, { status: 400 });
    const r = await h.post('/v1/monster-line', monsterBody(), token);
    assert.equal(r.status, 503);
    assert.deepEqual(await r.json(), { error: { code: 'disabled' } });
    const spent = h.svc.dailyGemini.used;
    assert.equal((await h.post('/v1/monster-line', monsterBody(), token)).status, 503);
    assert.equal(h.fake.gemini().length, 1);
    assert.equal(h.svc.dailyGemini.used, spent);
    assert.deepEqual(h.svc.features(), { monster: false, hint: false, hint_speech: false, pronounce: true });
    assert.ok(!h.logs.join('').includes('PROVIDER SECRET TEXT'));
  } finally { await h.close(); }
});

test('other, malformed and oversized Gemini 400 bodies stay bounded and do not disable valid credentials', async () => {
  const h = await start({}, (c) => { c.limits.maxGeminiBytes = 1024; });
  try {
    const token = await h.session();
    for (const [body, status, code] of [
      ['not json', 502, 'invalid_generation'],
      [JSON.stringify({ error: { status: 'INVALID_ARGUMENT', message: 'API_KEY_INVALID' } }), 502, 'invalid_generation'],
      ['x'.repeat(2048), 503, 'provider_unavailable'],
    ] as const) {
      h.fake.queue.push(() => new Response(body, { status: 400 }));
      const response = await h.post('/v1/monster-line', monsterBody(), token);
      assert.equal(response.status, status);
      assert.deepEqual(await response.json(), { error: { code } });
      assert.equal(h.svc.features().monster, true);
    }
    assert.equal(h.fake.gemini().length, 3);
  } finally { await h.close(); }
});
