// Flat-shaded, vertex-snapped, fogged lit surface for every solid thing on the shore.
Shader "Fnmw/PS1Lit"
{
    Properties
    {
        _BaseColor ("Color", Color) = (0.5,0.5,0.5,1)
        _BaseMap ("Texture", 2D) = "white" {}
        _EmissionColor ("Emission", Color) = (0,0,0,1)
        _Cull ("Cull", Float) = 2
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" "RenderPipeline"="UniversalPipeline" "Queue"="Geometry" }
        Cull [_Cull]

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode"="UniversalForward" }
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile _ _ADDITIONAL_LIGHTS_VERTEX _ADDITIONAL_LIGHTS
            #pragma multi_compile _ _CLUSTER_LIGHT_LOOP
            #pragma multi_compile_fragment _ _LIGHT_COOKIES
            #pragma multi_compile_instancing
            #include_with_pragmas "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Fog.hlsl"
            #include "Ps1Common.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _BaseColor; float4 _BaseMap_ST; half4 _EmissionColor; float _Cull;
            CBUFFER_END
            TEXTURE2D(_BaseMap); SAMPLER(sampler_BaseMap);

            struct A { float4 positionOS : POSITION; float3 normalOS : NORMAL; float2 uv : TEXCOORD0; half4 color : COLOR; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct V { float4 positionCS : SV_POSITION; float3 positionWS : TEXCOORD0; half3 normalWS : TEXCOORD1; float2 uv : TEXCOORD2; half fog : TEXCOORD3; };

            V vert(A i)
            {
                UNITY_SETUP_INSTANCE_ID(i);
                V o;
                o.positionWS = TransformObjectToWorld(i.positionOS.xyz);
                o.positionCS = Ps1Snap(TransformWorldToHClip(o.positionWS));
                o.normalWS = TransformObjectToWorldNormal(i.normalOS);
                o.uv = TRANSFORM_TEX(i.uv, _BaseMap);
                o.fog = ComputeFogFactor(o.positionCS.z);
                return o;
            }

            half4 frag(V i) : SV_Target
            {
                half3 n = FlatNormal(i.positionWS, normalize(i.normalWS));
                half3 albedo = _BaseColor.rgb * SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, i.uv).rgb;
                half3 c = albedo * Ps1Lighting(i.positionWS, n, i.positionCS) + _EmissionColor.rgb;
                c = MixFog(c, i.fog);
                return half4(c, 1);
            }
            ENDHLSL
        }

        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode"="DepthOnly" }
            ZWrite On ColorMask R
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            float4 _FnmwSnapRes;
            struct A { float4 positionOS : POSITION; };
            float4 vert(A i) : SV_POSITION
            {
                float4 cs = TransformObjectToHClip(i.positionOS.xyz);
                if (_FnmwSnapRes.x >= 1) { float2 g = _FnmwSnapRes.xy * 0.5; float w = max(cs.w, 1e-4); cs.xy = floor(cs.xy / w * g + 0.5) / g * w; }
                return cs;
            }
            half4 frag() : SV_Target { return 0; }
            ENDHLSL
        }
    }
}
