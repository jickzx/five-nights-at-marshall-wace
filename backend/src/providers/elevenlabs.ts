// ElevenLabs text to speech, raw 24 kHz 16-bit mono PCM. Host and path are constants; only the validated voice id
// is interpolated. Never retried automatically: a paid generation is not repeated behind the player's back.
// Unverified live behaviour (no key was available): language_code with the configured model, voice access on the
// account's plan. See backend/README.md.
import { createHash } from 'node:crypto';
import { callProvider, errorForStatus, ProviderError, readLimited, type Fetch } from './common.ts';

export const ELEVEN_ORIGIN = 'https://api.elevenlabs.io';
export const SAMPLE_RATE = 24000;

export type Channel = 'monster' | 'ship' | 'native';

/** Fixed per channel. The monster's character comes from the game's own DSP, not from these. */
export const VOICE_SETTINGS: Record<Channel, object> = {
  monster: { stability: 0.35, similarity_boost: 0.75, style: 0.3, speed: 0.92, use_speaker_boost: true },
  ship: { stability: 0.7, similarity_boost: 0.8, style: 0, speed: 1.0, use_speaker_boost: true },
  native: { stability: 0.65, similarity_boost: 0.8, style: 0, speed: 0.9, use_speaker_boost: true },
};

export interface ElevenDeps { fetch: Fetch; key: string; model: string; maxBytes: number }
export interface TtsCall { voiceId: string; text: string; lang: string; channel: Channel; deadlineMs: number }

export function ttsRequest(deps: ElevenDeps, call: TtsCall): { url: string; init: RequestInit } {
  const url = `${ELEVEN_ORIGIN}/v1/text-to-speech/${encodeURIComponent(call.voiceId)}?output_format=pcm_24000`;
  const seed = createHash('sha256').update(call.text).digest().readUInt32BE(0) % 4294967295;
  const body = {
    text: call.text,
    model_id: deps.model,
    language_code: call.lang,
    voice_settings: VOICE_SETTINGS[call.channel],
    seed,
    apply_text_normalization: 'auto',
  };
  return {
    url,
    init: { method: 'POST', headers: { 'content-type': 'application/json', accept: 'audio/pcm', 'xi-api-key': deps.key }, body: JSON.stringify(body) },
  };
}

export async function synthesize(deps: ElevenDeps, call: TtsCall, signal?: AbortSignal): Promise<Buffer> {
  const { url, init } = ttsRequest(deps, call);
  return await callProvider(deps.fetch, url, init, call.deadlineMs, signal, async (res) => {
    if (res.status !== 200) { await res.body?.cancel().catch(() => {}); throw errorForStatus(res.status); }
    const pcm = await readLimited(res, deps.maxBytes);
    if (pcm.length === 0 || pcm.length % 2 !== 0) throw new ProviderError('invalid', 200);
    return pcm;
  });
}
