// Full-screen presenter for the UI. The whole interface is drawn into a low-res texture and
// shown through this pass: a failing instrument. Everything the player feels (curvature,
// phosphor glow, static, tearing, blood wash and tide, cracks, heartbeat vignette, the white
// of the death screen) lives here and is driven by Fnmw.UI.CrtFx.
Shader "Fnmw/UI/CRT"
{
    Properties
    {
        [PerRendererData] _MainTex ("Sprite (unused)", 2D) = "white" {}
        _UiTex ("UI", 2D) = "black" {}
        _CrackTex ("Cracks", 2D) = "black" {}
    }
    SubShader
    {
        Tags { "Queue"="Overlay" "RenderType"="Transparent" "IgnoreProjector"="True" "PreviewType"="Plane" }
        Cull Off ZWrite Off ZTest Always
        Blend One OneMinusSrcAlpha

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.0
            #include "UnityCG.cginc"

            struct appdata { float4 vertex : POSITION; float2 uv : TEXCOORD0; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct v2f { float4 pos : SV_POSITION; float2 uv : TEXCOORD0; UNITY_VERTEX_OUTPUT_STEREO };

            sampler2D _MainTex;
            sampler2D _UiTex;
            sampler2D _CrackTex;
            float4 _UiSize;      // xy = texture size in texels, zw = screen pixels per texel
            float _T;            // unscaled seconds
            float _Curve, _Backplate, _Scan, _ScanLines, _ScanDepth, _Glow;
            float _Threat, _Pulse, _Vignette, _Static, _Burst, _Flicker, _Aberr, _Tear, _Dropout;
            float _Tide, _Wash, _WashA, _WashSeed, _Crack, _RedFlash;
            float _White, _Ink;
            float4 _Shake, _InkColor, _Plate, _Blood, _BloodDeep;

            v2f vert(appdata v)
            {
                v2f o;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                o.pos = UnityObjectToClipPos(v.vertex);
                o.uv = v.uv;
                return o;
            }

            float hash11(float p) { p = frac(p * 0.1031); p *= p + 33.33; p *= p + p; return frac(p); }
            float hash21(float2 p)
            {
                float3 p3 = frac(float3(p.xyx) * 0.1031);
                p3 += dot(p3, p3.yzx + 33.33);
                return frac((p3.x + p3.y) * p3.z);
            }
            float bayer4(float2 p)
            {
                const float m[16] = { 0, 8, 2, 10, 12, 4, 14, 6, 3, 11, 1, 9, 15, 7, 13, 5 };
                int x = (int)fmod(p.x, 4.0);
                int y = (int)fmod(p.y, 4.0);
                return (m[y * 4 + x] + 0.5) / 16.0;
            }
            float max3(float3 v) { return max(v.r, max(v.g, v.b)); }

            // Pixel-art friendly sampling: hard texels with a one-screen-pixel antialiased edge.
            float4 sampleUi(float2 uv)
            {
                float2 px = uv * _UiSize.xy;
                float2 fl = floor(px - 0.5) + 0.5;
                float2 fr = saturate((px - fl - 0.5) * _UiSize.zw + 0.5);
                float2 suv = (fl + fr) / _UiSize.xy;
                float inside = step(0.0, uv.x) * step(uv.x, 1.0) * step(0.0, uv.y) * step(uv.y, 1.0);
                return tex2D(_UiTex, suv) * inside;
            }

            float3 toGamma(float3 c)
            {
                #ifdef UNITY_COLORSPACE_GAMMA
                return c;
                #else
                return LinearToGammaSpace(max(c, 0.0));
                #endif
            }
            float3 toLinear(float3 c)
            {
                #ifdef UNITY_COLORSPACE_GAMMA
                return c;
                #else
                return GammaToLinearSpace(max(c, 0.0));
                #endif
            }

            fixed4 frag(v2f i) : SV_Target
            {
                float2 uv = i.uv;
                float t = _T;
                float2 c = uv - 0.5;
                float r2 = dot(c, c);

                // --- geometry: barrel curve, crack refraction, tearing, shake -------------
                float2 suv = 0.5 + c * (1.0 + _Curve * (r2 - 0.25));

                float2 ct = float2(1.0 / 256.0, 1.0 / 144.0);
                float crk = tex2D(_CrackTex, uv).r;
                float2 cg = float2(tex2D(_CrackTex, uv + float2(ct.x, 0)).r - tex2D(_CrackTex, uv - float2(ct.x, 0)).r,
                                   tex2D(_CrackTex, uv + float2(0, ct.y)).r - tex2D(_CrackTex, uv - float2(0, ct.y)).r);
                suv += cg * 0.005 * _Crack;

                float band = floor(uv.y * 28.0);
                float th = hash11(band * 3.1 + floor(t * 18.0) * 7.3);
                suv.x += (th - 0.5) * 0.14 * _Tear * step(0.55, th);
                // a slow rolling sync bar, always there, stronger with threat
                float roll = frac(uv.y * 0.9 - t * 0.11);
                float rollBar = smoothstep(0.0, 0.02, roll) * smoothstep(0.09, 0.02, roll);
                suv.x += rollBar * 0.004 * (0.3 + _Threat * 2.0) * (1.0 - _White);
                suv += _Shake.xy;

                float inside = step(0.0, suv.x) * step(suv.x, 1.0) * step(0.0, suv.y) * step(suv.y, 1.0);

                // --- the interface itself, with chromatic split -----------------------------
                float2 ab = c * _Aberr;
                float4 s0 = sampleUi(suv);
                float4 sR = sampleUi(suv + ab);
                float4 sB = sampleUi(suv - ab);
                float a = sqrt(saturate(max(s0.a, max(sR.a, sB.a) * 0.9)));
                float3 pm = float3(sR.r, s0.g, sB.b);                // premultiplied-ish, linear
                float3 col = toGamma(pm / max(a, 0.001));
                float rawLum = max3(toGamma(s0.rgb / max(sqrt(s0.a), 0.001))) * sqrt(s0.a);

                // phosphor bloom: four wide taps
                float2 gt = 2.5 / _UiSize.xy;
                float4 g = sampleUi(suv + float2(gt.x, gt.y)) + sampleUi(suv + float2(-gt.x, gt.y))
                         + sampleUi(suv + float2(gt.x, -gt.y)) + sampleUi(suv + float2(-gt.x, -gt.y));
                float3 glow = toGamma(g.rgb * 0.25) * _Glow;

                float3 P = col * a + glow * 0.8;                     // premultiplied, gamma space
                float A = saturate(a + max3(glow) * 0.6);

                // --- backplate: the dark glass of the console -----------------------------
                float flick = hash11(floor(t * 24.0));
                float bp = _Backplate * (1.0 - _Dropout * step(0.45, flick));
                float grain = hash21(floor(suv * _UiSize.xy) + floor(t * 12.0) * 3.7);
                float3 plate = _Plate.rgb * (1.0 - r2 * 1.6) * (0.85 + 0.3 * grain) * inside;
                P += plate * (1.0 - A) * bp;
                A += (1.0 - A) * bp;

                // --- scanlines ----------------------------------------------------------------
                float sl = 0.5 + 0.5 * cos(suv.y * _ScanLines * 6.28318);
                float scan = _Scan * _ScanDepth;
                P *= 1.0 - scan * sl;
                A = saturate(A + scan * 0.12 * sl * (1.0 - bp));

                float lumP = max3(P);

                // --- blood tide rising from the bottom of the glass ----------------------
                float wave = sin(uv.x * 13.0 + t * 1.3) * 0.006 + sin(uv.x * 31.0 - t * 2.3) * 0.003
                           + sin(uv.x * 5.0 + t * 0.6) * 0.008;
                float level = _Tide + wave * step(0.0005, _Tide);
                float under = smoothstep(level + 0.003, level - 0.003, uv.y) * step(0.0005, _Tide);
                float depth = saturate((level - uv.y) / max(level, 0.001));
                float3 blood = lerp(_Blood.rgb, _BloodDeep.rgb, saturate(depth * 1.3));
                P = lerp(P, blood * 0.85 + P * float3(0.95, 0.62, 0.58), under);
                A = lerp(A, 1.0, under * 0.94);
                float edge = smoothstep(0.007, 0.0, abs(uv.y - level)) * step(0.0005, _Tide);
                P += edge * float3(1.0, 0.32, 0.26) * 0.45;
                A = saturate(A + edge * 0.5);

                // --- blood wash dripping from the top (wrong answers) --------------------
                float colId = floor(uv.x * _UiSize.x / 6.0);
                float h1 = hash11(colId * 1.7 + _WashSeed);
                float front = _Wash * (0.18 + 1.05 * h1 * h1);
                float dy = 1.0 - uv.y;
                float drip = step(dy, front) * step(0.0005, _WashA);
                float dripTip = smoothstep(front - 0.03, front, dy) * drip;
                P = lerp(P, _Blood.rgb * (0.55 + 0.7 * lumP) + dripTip * float3(0.3, 0.05, 0.05), drip * _WashA * 0.85);
                A = saturate(A + drip * _WashA * 0.85);
                float redAmt = saturate(_WashA * 0.55 + _RedFlash);
                P = lerp(P, float3(lumP * 1.05 + 0.16 * A, lumP * 0.12, lumP * 0.10), redAmt);

                // --- cracks in the glass --------------------------------------------------
                float crkV = crk * _Crack;
                P += crkV * float3(0.78, 0.74, 0.68) * (0.35 + 0.5 * hash21(floor(uv * 300.0)));
                A = saturate(A + crkV * 0.75);

                // --- vignette that breathes with the heartbeat ---------------------------
                float v = smoothstep(0.42, 1.0, length(c * float2(1.12, 1.0)) * 1.5);
                float vig = saturate(v * (_Vignette + _Pulse * 0.35 * _Threat));
                float3 vcol = _BloodDeep.rgb * _Threat * (0.55 + 0.6 * _Pulse);
                P = P * (1.0 - vig) + vcol * vig;
                A += (1.0 - A) * vig;

                // --- static: at the edges while roaming, everywhere on a burst -----------
                float n = hash21(floor(uv * _UiSize.xy * 0.5) + floor(t * 30.0) * 17.0);
                float sAmt = saturate(_Static * (0.004 + v * v * 2.2) + _Burst);
                float sMask = step(1.0 - sAmt * 0.55, n);
                float n2 = hash21(floor(uv * _UiSize.xy * 0.5) * 1.3 + floor(t * 30.0) * 5.0);
                P = lerp(P, n2 * float3(0.9, 0.84, 0.78), sMask * 0.85);
                A = saturate(A + sMask * 0.85);

                // --- flicker ---------------------------------------------------------------
                P *= 1.0 - _Flicker * (0.35 * flick + 0.65 * step(0.86, flick));

                // --- death: everything goes white, the single line is written in ink -----
                P = lerp(P, float3(1, 1, 1), _White);
                A = lerp(A, 1.0, _White);
                float ink = saturate(rawLum * 1.6) * _Ink;
                P = lerp(P, _InkColor.rgb, ink);
                A = saturate(A + ink);

                // --- PS1 finish: 15-bit colour with ordered dither, dithered transparency --
                float b = bayer4(floor(uv * _UiSize.xy));
                float3 C = P / max(A, 0.001);
                C = floor(saturate(C) * 31.0 + b) / 31.0;
                float Aq = (A > 0.004 && A < 0.996) ? saturate(floor(A * 7.0 + b) / 7.0) : A;

                return fixed4(toLinear(C) * Aq, Aq);
            }
            ENDCG
        }
    }
    Fallback Off
}
