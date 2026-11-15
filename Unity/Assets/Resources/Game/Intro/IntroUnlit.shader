// Opening cutscene material: texture x colour (x vertex colour when _VColor = 1) plus a view-angle rim.
// Blend, depth write and culling are material properties so one shader covers the opaque hull,
// additive glows (wormhole, accretion disk, stars, particles) and alpha-blended smoke.
// No fog: the space shots happen far above the level, and the burning trail should read through the storm.
Shader "Fnmw/IntroUnlit"
{
    Properties
    {
        _MainTex ("Texture", 2D) = "white" {}
        _Color ("Colour", Color) = (1,1,1,1)
        _RimColor ("Rim colour (a = strength)", Color) = (0,0,0,0)
        _RimPower ("Rim power", Float) = 3
        _VColor ("Use vertex colour", Float) = 0
        [HideInInspector] _SrcBlend ("", Float) = 1
        [HideInInspector] _DstBlend ("", Float) = 0
        [HideInInspector] _ZWrite ("", Float) = 1
        [HideInInspector] _Cull ("", Float) = 2
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" "Queue"="Geometry" "RenderPipeline"="UniversalPipeline" }
        Blend [_SrcBlend] [_DstBlend]
        ZWrite [_ZWrite]
        Cull [_Cull]
        Pass
        {
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            TEXTURE2D(_MainTex); SAMPLER(sampler_MainTex);
            CBUFFER_START(UnityPerMaterial)
                float4 _MainTex_ST; half4 _Color; half4 _RimColor; float _RimPower; float _VColor;
            CBUFFER_END
            struct A { float4 positionOS : POSITION; float3 normalOS : NORMAL; half4 color : COLOR; float2 uv : TEXCOORD0; };
            struct V { float4 positionCS : SV_POSITION; half4 color : COLOR; float2 uv : TEXCOORD0; float3 n : TEXCOORD1; float3 v : TEXCOORD2; };
            V vert(A i)
            {
                V o;
                float3 ws = TransformObjectToWorld(i.positionOS.xyz);
                o.positionCS = TransformWorldToHClip(ws);
                o.color = lerp(half4(1, 1, 1, 1), i.color, (half)_VColor);
                o.uv = TRANSFORM_TEX(i.uv, _MainTex);
                o.n = TransformObjectToWorldNormal(i.normalOS);
                o.v = GetCameraPositionWS() - ws;
                return o;
            }
            half4 frag(V i) : SV_Target
            {
                half4 c = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, i.uv) * _Color * i.color;
                float facing = saturate(abs(dot(normalize(i.n), normalize(i.v))));
                c.rgb += _RimColor.rgb * (_RimColor.a * pow(1 - facing, _RimPower));
                return c;
            }
            ENDHLSL
        }
    }
}
