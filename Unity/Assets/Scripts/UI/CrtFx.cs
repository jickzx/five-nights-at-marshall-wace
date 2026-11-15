using UnityEngine;

namespace Fnmw.UI
{
    /// <summary>What kind of glass the player is looking through right now.</summary>
    internal enum GlassMode { Off, Title, Visor, Console, Decode, Death, Escape }

    /// <summary>
    /// Drives the CRT material. Screens pick a mode and fire impulses (a wrong answer, a stage change);
    /// threat and the heartbeat do the rest. All values ease, so mode changes read as the tube warming up.
    /// </summary>
    internal sealed class CrtFx
    {
        readonly Material _mat;
        Texture2D _crackTex;
        byte[] _crackPx;
        const int CrackW = 256, CrackH = 144;

        public GlassMode Mode { get; private set; } = GlassMode.Off;
        public float Threat;            // 0..1 target, from GameState
        public int Stage = 1;
        public int Cracks { get; private set; }
        public float Curve { get; private set; }
        public float Pulse { get; private set; }
        public float Dread { get; private set; }     // smoothed threat after the current mode has damped it
        public float WhiteTarget, InkTarget;
        public float Clock { get; private set; }

        float _backplate, _scan, _glow, _vignette, _tide, _white, _ink, _calm, _threat;
        float _shake, _burst, _tear, _washFront, _washA, _redFlash, _dropout, _splash;
        float _beat, _glitchIn = 3f, _glitchFor;
        uint _rng = 0x1234567u;

        public CrtFx(Material mat)
        {
            _mat = mat;
            _crackPx = new byte[CrackW * CrackH];
            _crackTex = new Texture2D(CrackW, CrackH, TextureFormat.R8, false)
            { filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp, hideFlags = HideFlags.HideAndDontSave };
            _crackTex.LoadRawTextureData(_crackPx); _crackTex.Apply(false);
            if (_mat)
            {
                _mat.SetTexture("_CrackTex", _crackTex);
                _mat.SetColor("_Blood", Pal.Blood);
                _mat.SetColor("_BloodDeep", Pal.BloodDeep);
                _mat.SetColor("_InkColor", Pal.Ink);
            }
        }

        public void Dispose() => Kit.Kill(_crackTex);

        float Rand() { _rng ^= _rng << 13; _rng ^= _rng >> 17; _rng ^= _rng << 5; return (_rng & 0xFFFFFF) / (float)0x1000000; }

        public void SetMode(GlassMode mode, bool snap = false)
        {
            Mode = mode;
            if (mode != GlassMode.Death) { WhiteTarget = 0; InkTarget = 0; }
            if (snap) { Target(out _backplate, out float curve, out _scan, out _glow, out _vignette, out _, out _calm); Curve = curve; }
        }

        void Target(out float backplate, out float curve, out float scan, out float glow, out float vignette, out float tide, out float calm)
        {
            switch (Mode)
            {
                case GlassMode.Title: backplate = 1; curve = 0.14f; scan = 0.30f; glow = 0.55f; vignette = 0.55f; tide = 0.05f; calm = 0.4f; break;
                case GlassMode.Visor: backplate = 0; curve = 0.05f; scan = 0.14f; glow = 0.45f; vignette = 0.22f; tide = 0; calm = 0; break;
                case GlassMode.Console: backplate = 1; curve = 0.14f; scan = 0.30f; glow = 0.55f; vignette = 0.45f; tide = 0.02f + 0.17f * _threat; calm = 0; break;
                case GlassMode.Decode: backplate = 1; curve = 0.14f; scan = 0.16f; glow = 0.7f; vignette = 0.35f; tide = 0; calm = 0.8f; break;
                case GlassMode.Death: backplate = 0; curve = 0.05f; scan = 0.05f; glow = 0.2f; vignette = 0.2f; tide = 0; calm = 0.6f; break;
                case GlassMode.Escape: backplate = 1; curve = 0.10f; scan = 0.12f; glow = 0.6f; vignette = 0.3f; tide = 0; calm = 1f; break;
                default: backplate = 0; curve = 0.05f; scan = 0; glow = 0; vignette = 0; tide = 0; calm = 1; break;
            }
        }

        /// <summary>A jolt through the instrument. Any argument can be zero.</summary>
        public void Kick(float shake = 0, float burst = 0, float tear = 0, float red = 0)
        {
            _shake = Mathf.Max(_shake, shake);
            _burst = Mathf.Max(_burst, burst);
            _tear = Mathf.Max(_tear, tear);
            _redFlash = Mathf.Max(_redFlash, red);
        }

        /// <summary>Blood runs down the glass and the tide jumps: a wrong answer.</summary>
        public void Bleed()
        {
            _washFront = 0.02f; _washA = 1f; _splash = 0.1f;
            if (_mat) _mat.SetFloat("_WashSeed", Rand() * 97f);
        }

        /// <summary>Adds a permanent crack radiating from a point on the glass (uv space).</summary>
        public void Crack(Vector2 at)
        {
            Cracks++;
            int arms = 5 + (int)(Rand() * 4);
            for (int i = 0; i < arms; i++)
            {
                float ang = (i + Rand() * 0.7f) / arms * Mathf.PI * 2f;
                CrackArm(at, ang, 0.10f + Rand() * 0.22f, 2);
            }
            _crackTex.LoadRawTextureData(_crackPx); _crackTex.Apply(false);
        }

        void CrackArm(Vector2 p, float ang, float len, int depth)
        {
            int segs = 5 + (int)(Rand() * 4);
            for (int s = 0; s < segs; s++)
            {
                ang += (Rand() - 0.5f) * 0.7f;
                var q = p + new Vector2(Mathf.Cos(ang) * 0.5625f, Mathf.Sin(ang)) * (len / segs);
                CrackLine(p, q, (byte)(255 - s * 90 / segs));
                if (depth > 0 && Rand() < 0.22f) CrackArm(q, ang + (Rand() < 0.5f ? 0.8f : -0.8f), len * 0.45f, depth - 1);
                p = q;
            }
        }

        void CrackLine(Vector2 a, Vector2 b, byte v)
        {
            int x0 = (int)(a.x * CrackW), y0 = (int)(a.y * CrackH), x1 = (int)(b.x * CrackW), y1 = (int)(b.y * CrackH);
            int n = Mathf.Max(Mathf.Abs(x1 - x0), Mathf.Abs(y1 - y0), 1);
            for (int i = 0; i <= n; i++)
            {
                int x = x0 + (x1 - x0) * i / n, y = y0 + (y1 - y0) * i / n;
                if (x < 0 || y < 0 || x >= CrackW || y >= CrackH) continue;
                int idx = y * CrackW + x;
                if (_crackPx[idx] < v) _crackPx[idx] = v;
            }
        }

        public void Tick(float dt)
        {
            dt = Mathf.Clamp(dt, 0f, 0.1f);
            Clock += dt;
            Target(out float backplate, out float curve, out float scan, out float glow, out float vignette, out float tide, out float calm);
            float k = 1f - Mathf.Exp(-dt * 9f);
            _backplate = Mathf.Lerp(_backplate, backplate, k);
            Curve = Mathf.Lerp(Curve, curve, k);
            _scan = Mathf.Lerp(_scan, scan, k);
            _glow = Mathf.Lerp(_glow, glow, k);
            _vignette = Mathf.Lerp(_vignette, vignette, k);
            _calm = Mathf.Lerp(_calm, calm, 1f - Mathf.Exp(-dt * 3f));
            _threat = Mathf.Lerp(_threat, Mathf.Clamp01(Threat), 1f - Mathf.Exp(-dt * 4f));
            _tide = Mathf.Lerp(_tide, tide, 1f - Mathf.Exp(-dt * 1.6f));
            _white = Mathf.MoveTowards(_white, WhiteTarget, dt / 4.5f);
            _ink = Mathf.MoveTowards(_ink, InkTarget, dt / 2.5f);

            Dread = _threat * (1f - 0.8f * _calm);

            // heartbeat: lub-dub, 54 to 150 bpm
            _beat = Mathf.Repeat(_beat + dt * Mathf.Lerp(54f, 150f, _threat) / 60f, 1f);
            Pulse = Mathf.Max(Bump(_beat, 0.06f), 0.65f * Bump(_beat, 0.30f));

            // impulses decay
            _shake = Mathf.MoveTowards(_shake, 0, dt * 2.2f);
            _burst = Mathf.MoveTowards(_burst, 0, dt * 1.8f);
            _tear = Mathf.MoveTowards(_tear, 0, dt * 2.5f);
            _redFlash = Mathf.MoveTowards(_redFlash, 0, dt * 1.4f);
            _splash = Mathf.MoveTowards(_splash, 0, dt * 0.05f);
            if (_washA > 0)
            {
                _washFront = Mathf.MoveTowards(_washFront, 1f, dt * 0.55f);
                _washA = Mathf.MoveTowards(_washA, 0, dt / 3.2f);
            }

            // the instrument fails in fits when the beast is close
            _glitchIn -= dt * (0.3f + Dread * 2.5f);
            if (_glitchIn <= 0) { _glitchIn = 0.8f + Rand() * 2.4f; _glitchFor = Dread > 0.3f ? 0.06f + Rand() * 0.12f : 0f; }
            _glitchFor = Mathf.Max(0, _glitchFor - dt);
            float fit = _glitchFor > 0 ? 1f : 0f;
            _dropout = Mode == GlassMode.Console && Dread > 0.5f ? fit * 0.5f : 0f;

            if (!_mat) return;
            var shake = new Vector2(Rand() - 0.5f, Rand() - 0.5f) * (_shake * 0.03f + Dread * Dread * 0.0025f);
            _mat.SetFloat("_T", Clock);
            _mat.SetFloat("_Curve", Curve);
            _mat.SetFloat("_Backplate", _backplate);
            _mat.SetFloat("_Scan", _scan);
            _mat.SetFloat("_Glow", _glow);
            _mat.SetFloat("_Threat", Dread);
            _mat.SetFloat("_Pulse", Pulse);
            _mat.SetFloat("_Vignette", _vignette + 0.8f * Dread);
            _mat.SetFloat("_Static", (0.002f + 0.3f * Dread * Dread) * (1f - 0.9f * _calm));
            _mat.SetFloat("_Burst", _burst * 0.8f);
            _mat.SetFloat("_Flicker", (0.03f + 0.4f * Dread) * (1f - 0.7f * _calm));
            _mat.SetFloat("_Aberr", 0.002f + 0.012f * Dread + _shake * 0.02f + fit * 0.01f);
            _mat.SetFloat("_Tear", Mathf.Clamp01(_tear + fit * 0.6f));
            _mat.SetFloat("_Dropout", _dropout);
            _mat.SetFloat("_Tide", Mathf.Max(0, _tide + (_tide > 0.01f ? _splash + Pulse * 0.006f * Dread : 0)));
            _mat.SetFloat("_Wash", _washFront);
            _mat.SetFloat("_WashA", Kit.Ease(_washA));
            _mat.SetFloat("_Crack", Cracks > 0 ? 1f - 0.88f * _calm : 0f);   // the warm screens stay legible
            _mat.SetFloat("_RedFlash", _redFlash);
            _mat.SetFloat("_White", Kit.Ease(_white));
            _mat.SetFloat("_Ink", _ink);
            _mat.SetVector("_Shake", shake);
            _mat.SetColor("_Plate", Mode == GlassMode.Decode ? Pal.WarmDark : Mode == GlassMode.Escape ? Pal.Indigo : Mode == GlassMode.Title ? Pal.Abyss : Pal.Soot);
        }

        public float White => _white;

        static float Bump(float p, float centre)
        {
            float d = (p - centre) / 0.035f;
            return Mathf.Exp(-d * d);
        }
    }
}
