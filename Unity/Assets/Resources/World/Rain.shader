// Blood rain streaks for the particle system: vertex colour times tint, soft-edged, alpha blended.
Shader "Fnmw/Rain"
{
    Properties { _Tint ("Tint", Color) = (0.6,0.02,0.02,0.7) }
    SubShader
    {
        Tags { "RenderType"="Transparent" "Queue"="Transparent" "RenderPipeline"="UniversalPipeline" }
        Blend SrcAlpha OneMinusSrcAlpha ZWrite Off Cull Off
        Pass
        {
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include_with_pragmas "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Fog.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            CBUFFER_START(UnityPerMaterial) half4 _Tint; CBUFFER_END
            struct A { float4 positionOS : POSITION; half4 color : COLOR; float2 uv : TEXCOORD0; };
            struct V { float4 positionCS : SV_POSITION; half4 color : COLOR; float2 uv : TEXCOORD0; half fog : TEXCOORD1; };
            V vert(A i) { V o; o.positionCS = TransformObjectToHClip(i.positionOS.xyz); o.color = i.color; o.uv = i.uv; o.fog = ComputeFogFactor(o.positionCS.z); return o; }
            half4 frag(V i) : SV_Target
            {
                half edge = 1 - abs(i.uv.x * 2 - 1);
                half4 c = i.color * _Tint;
                c.a *= saturate(edge * 2) * saturate(i.uv.y * 3);
                c.rgb = MixFog(c.rgb, i.fog * 0.5 + 0.5 * saturate(i.fog * 2));
                return c;
            }
            ENDHLSL
        }
    }
}
