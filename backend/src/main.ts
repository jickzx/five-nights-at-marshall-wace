// Entry point: one long-lived process (the limits and caches are in memory; see limits.ts).
import { ConfigError, features, loadConfig } from './config.ts';
import { Logger } from './log.ts';
import { createApp } from './server.ts';
import { VoiceService } from './service.ts';

const log = new Logger();
let cfg;
try {
  cfg = loadConfig(process.env);
} catch (e) {
  // ConfigError messages name variables, never values
  process.stderr.write(`config error: ${e instanceof ConfigError ? e.message : 'unexpected'}\n`);
  process.exit(1);
}
const svc = new VoiceService({ config: cfg, fetch: (url, init) => fetch(url, init), logger: log });
const server = createApp(svc);
server.listen(cfg.port, cfg.host, () => {
  const f = features(cfg);
  process.stdout.write(
    `fnmw voice backend on http://${cfg.host}:${cfg.port} mode=${cfg.mode}` +
      ` monster=${f.monster} hint=${f.hint} hint_speech=${f.hint_speech} pronounce=${f.pronounce}\n`,
  );
});
for (const sig of ['SIGINT', 'SIGTERM'] as const) process.on(sig, () => server.close(() => process.exit(0)));
