// node:http adapter: body limit, JSON parsing, routing, error shape, safe headers. No CORS headers are sent.
import { createServer, type IncomingMessage, type Server, type ServerResponse } from 'node:http';
import { bearer } from './auth.ts';
import { clientIp, InvalidClientIp } from './client-ip.ts';
import { ApiError, VoiceService } from './service.ts';
import { BadRequest, hintRequest, monsterRequest, pronounceRequest, sessionRequest, SPEECH_ID_RE } from './validate.ts';

const JSON_HEADERS = {
  'content-type': 'application/json; charset=utf-8',
  'cache-control': 'no-store',
  'x-content-type-options': 'nosniff',
};

class TooLarge extends Error {}

async function readBody(req: IncomingMessage, max: number): Promise<unknown> {
  const len = Number(req.headers['content-length'] ?? NaN);
  if (Number.isFinite(len) && len > max) throw new TooLarge();
  const ct = String(req.headers['content-type'] ?? '');
  if (!/^application\/json(\s*;\s*charset=utf-8)?$/i.test(ct)) throw new BadRequest('content-type');
  const chunks: Buffer[] = [];
  let total = 0;
  for await (const chunk of req) {
    const b = chunk as Buffer;
    total += b.length;
    if (total > max) throw new TooLarge();
    chunks.push(b);
  }
  let text: string;
  try { text = new TextDecoder('utf-8', { fatal: true }).decode(Buffer.concat(chunks, total)); }
  catch { throw new BadRequest('utf-8'); }
  try { return JSON.parse(text); } catch { throw new BadRequest('json'); }
}

function send(res: ServerResponse, status: number, body: unknown, extra: Record<string, string> = {}): void {
  if (res.headersSent) return;
  const data = JSON.stringify(body);
  res.writeHead(status, { ...JSON_HEADERS, 'content-length': String(Buffer.byteLength(data)), ...extra });
  res.end(data);
}

function sendError(res: ServerResponse, e: ApiError): void {
  const body: { error: { code: string; retry_after_ms?: number } } = { error: { code: e.code } };
  if (e.retryAfterMs !== undefined) body.error.retry_after_ms = Math.round(e.retryAfterMs);
  send(res, e.status, body);
}

export function createApp(svc: VoiceService): Server {
  const server = createServer((req, res) => {
    void handle(svc, req, res);
  });
  server.requestTimeout = 15_000;
  server.headersTimeout = 10_000;
  server.keepAliveTimeout = 5_000;
  server.maxHeadersCount = 50;
  return server;
}

async function handle(svc: VoiceService, req: IncomingMessage, res: ServerResponse): Promise<void> {
  const t0 = svc.now();
  const url = (req.url ?? '/').split('?')[0]!;
  const method = req.method ?? 'GET';
  // a client that disconnects aborts the upstream generation it was waiting for
  const ctl = new AbortController();
  res.on('close', () => { if (!res.writableFinished) ctl.abort(); });
  let route = 'unknown';
  try {
    if (method === 'GET' && url === '/healthz') { route = 'healthz'; send(res, 200, { ok: true }); return; }
    const ip = clientIp(req.socket.remoteAddress, req.headers['x-forwarded-for'], svc.cfg.trustedProxies);
    svc.admit(ip);
    const max = svc.cfg.limits.bodyBytes;
    if (method === 'POST' && url === '/v1/session') {
      route = 'session';
      const body = sessionRequest(await readBody(req, max));
      send(res, 200, svc.createSession(bearer(req.headers.authorization), ip, body));
      return;
    }
    if (method === 'POST' && url === '/v1/monster-line') {
      route = 'monster';
      const s = svc.session(bearer(req.headers.authorization), ip);
      const body = monsterRequest(await readBody(req, max));
      send(res, 200, await svc.monsterLine(s, body, ctl.signal));
      return;
    }
    if (method === 'POST' && url === '/v1/hint') {
      route = 'hint';
      const s = svc.session(bearer(req.headers.authorization), ip);
      const body = hintRequest(await readBody(req, max));
      send(res, 200, await svc.hint(s, body, ctl.signal));
      return;
    }
    if (method === 'POST' && url === '/v1/pronounce') {
      route = 'pronounce';
      const s = svc.session(bearer(req.headers.authorization), ip);
      const body = pronounceRequest(await readBody(req, max));
      send(res, 200, svc.pronounce(s, body));
      return;
    }
    const m = /^\/v1\/speech\/([^/]+)$/.exec(url);
    if (method === 'GET' && m) {
      route = 'speech';
      const s = svc.session(bearer(req.headers.authorization), ip);
      if (!SPEECH_ID_RE.test(m[1]!)) throw new ApiError('not_found');
      const { pcm, source } = await svc.fetchSpeech(s, m[1]!, ctl.signal);
      if (res.headersSent || ctl.signal.aborted) return;
      res.writeHead(200, {
        'content-type': 'application/octet-stream',
        'content-length': String(pcm.length),
        'cache-control': 'no-store',
        'x-content-type-options': 'nosniff',
        'x-fnmw-audio': 'pcm_s16le;rate=24000;channels=1',
        'x-fnmw-source': source,
      });
      res.end(pcm);
      return;
    }
    throw new ApiError('not_found');
  } catch (e) {
    const err = e instanceof ApiError ? e
      : e instanceof TooLarge ? new ApiError('too_large')
      : e instanceof BadRequest || e instanceof InvalidClientIp ? new ApiError('bad_request')
      : new ApiError('provider_unavailable');
    if (!(e instanceof ApiError) && !(e instanceof TooLarge) && !(e instanceof BadRequest) && !(e instanceof InvalidClientIp))
      svc.log.info({ event: 'internal_error', route }); // never the message: it could carry content
    sendError(res, err);
    if (err.code === 'too_large') req.resume();
  } finally {
    svc.log.info({ event: 'request', route, status: res.statusCode, ms: svc.now() - t0 });
  }
}
