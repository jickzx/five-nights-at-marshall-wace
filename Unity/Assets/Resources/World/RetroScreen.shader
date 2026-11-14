// Presents the low-resolution game render: nearest-neighbour upscale, 4x4 Bayer dither into a limited
// colour depth, a hint of scanline and a dark vignette. Used by a RawImage on a full-screen canvas.
Shader "Fnmw/RetroScreen"
{
    Properties
    {
        [PerRendererData] _MainTex ("Render", 2D) = "black" {}
        _Levels ("Colour levels per channel", Float) = 20
        _Dither ("Dither strength", Range(0,2)) = 0.8
        _Vignette ("Vignette", Range(0,1)) = 0.45
        _Scanline ("Scanline", Range(0,1)) = 0.12
        _Fade ("Fade to colour", Range(0,1)) = 0
        _FadeColor ("Fade colour", Color) = (1,1,1,1)
    }
    SubShader
    {
        Tags { "Queue"="Overlay" "RenderType"="Transparent" "IgnoreProjector"="True" "PreviewType"="Plane" }
        Cull Off ZWrite Off ZTest Always Blend Off
        Pass
        {
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            TEXTURE2D(_MainTex); SAMPLER(sampler_MainTex); float4 _MainTex_TexelSize;
            float _Levels, _Dither, _Vignette, _Scanline, _Fade; half4 _FadeColor;
            struct A { float4 positionOS : POSITION; float2 uv : TEXCOORD0; half4 color : COLOR; };
            struct V { float4 positionCS : SV_POSITION; float2 uv : TEXCOORD0; };
            V vert(A i) { V o; o.positionCS = TransformObjectToHClip(i.positionOS.xyz); o.uv = i.uv; return o; }
            static const float bayer[16] = { 0,8,2,10, 12,4,14,6, 3,11,1,9, 15,7,13,5 };
            half4 frag(V i) : SV_Target
            {
                float2 px = floor(i.uv * _MainTex_TexelSize.zw);
                float2 uv = (px + 0.5) * _MainTex_TexelSize.xy;
                half3 c = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, uv).rgb;
                c = saturate(c);
                int bi = (int)(fmod(px.x, 4) + fmod(px.y, 4) * 4);
                float d = (bayer[bi] / 16.0 - 0.5) * _Dither;
                // Quantise in a perceptual (sqrt) space so the dark storm keeps enough steps.
                c = sqrt(c);
                c = floor(c * _Levels + 0.5 + d) / _Levels;
                c = c * c;
                float2 q = i.uv - 0.5;
                c *= 1 - _Vignette * dot(q, q) * 2.2;
                c *= 1 - _Scanline * (fmod(px.y, 2) < 1 ? 1 : 0);
                c = lerp(c, _FadeColor.rgb, _Fade);
                return half4(saturate(c), 1);
            }
            ENDHLSL
        }
    }
}
