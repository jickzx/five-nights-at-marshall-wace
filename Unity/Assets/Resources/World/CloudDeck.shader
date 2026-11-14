// A soft deck of cloud far above the shore. Procedural value noise with holes the red sea shows through.
Shader "Fnmw/CloudDeck"
{
    Properties
    {
        _LitColor ("Lit", Color) = (1,0.85,0.75,1)
        _ShadeColor ("Shade", Color) = (0.6,0.45,0.55,1)
        _Coverage ("Coverage", Range(0,1)) = 0.72
        _Scale ("Scale", Float) = 0.012
        _Opacity ("Opacity", Range(0,1)) = 1
    }
    SubShader
    {
        Tags { "RenderType"="Transparent" "Queue"="Transparent-50" "RenderPipeline"="UniversalPipeline" }
        Blend SrcAlpha OneMinusSrcAlpha ZWrite Off Cull Off
        Pass
        {
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include_with_pragmas "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Fog.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            CBUFFER_START(UnityPerMaterial)
                half4 _LitColor, _ShadeColor; float _Coverage, _Scale, _Opacity;
            CBUFFER_END
            struct A { float4 positionOS : POSITION; };
            struct V { float4 positionCS : SV_POSITION; float3 positionWS : TEXCOORD0; half fog : TEXCOORD1; };
            float H(float2 p) { return frac(sin(dot(p, float2(127.1, 311.7))) * 43758.5453); }
            float N(float2 p) { float2 i = floor(p), f = frac(p); f = f * f * (3 - 2 * f);
                return lerp(lerp(H(i), H(i + float2(1,0)), f.x), lerp(H(i + float2(0,1)), H(i + 1), f.x), f.y); }
            float Fbm(float2 p) { float v = 0, a = 0.5; for (int k = 0; k < 4; k++) { v += a * N(p); p = p * 2.07 + 13.1; a *= 0.5; } return v; }
            V vert(A i)
            {
                V o; o.positionWS = TransformObjectToWorld(i.positionOS.xyz);
                o.positionCS = TransformWorldToHClip(o.positionWS);
                o.fog = ComputeFogFactor(o.positionCS.z);
                return o;
            }
            half4 frag(V i) : SV_Target
            {
                float2 p = i.positionWS.xz * _Scale + float2(_Time.y * 0.004, _Time.y * 0.002);
                float n = Fbm(p);
                float a = smoothstep(1 - _Coverage, 1 - _Coverage + 0.18, n);
                half3 c = lerp(_ShadeColor.rgb, _LitColor.rgb, saturate((n - 0.35) * 2.2));
                float dist = length(i.positionWS.xz - GetCameraPositionWS().xz);
                a *= saturate(1 - dist / 2400) * _Opacity;
                c = MixFog(c, i.fog);
                return half4(c, a);
            }
            ENDHLSL
        }
    }
}
