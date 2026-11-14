// The rising red sea: faceted sine swell, slick specular glints off the main light and the flashlight, fog.
Shader "Fnmw/BloodSea"
{
    Properties
    {
        _DeepColor ("Deep", Color) = (0.07,0.0,0.004,1)
        _CrestColor ("Crest", Color) = (0.6,0.02,0.02,1)
        _GlowColor ("Glow", Color) = (0.07,0.0,0.0,1)
        _WaveHeight ("Wave height", Float) = 0.3
        _WaveScale ("Wave scale", Float) = 0.3
        _WaveSpeed ("Wave speed", Float) = 0.6
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" "RenderPipeline"="UniversalPipeline" "Queue"="Geometry+10" }
        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode"="UniversalForward" }
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile _ _ADDITIONAL_LIGHTS_VERTEX _ADDITIONAL_LIGHTS
            #pragma multi_compile _ _CLUSTER_LIGHT_LOOP
            #include_with_pragmas "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Fog.hlsl"
            #include "Ps1Common.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _DeepColor; half4 _CrestColor; half4 _GlowColor; float _WaveHeight; float _WaveScale; float _WaveSpeed;
            CBUFFER_END

            struct A { float4 positionOS : POSITION; };
            struct V { float4 positionCS : SV_POSITION; float3 positionWS : TEXCOORD0; half h : TEXCOORD1; half fog : TEXCOORD2; };

            float Swell(float2 p, float t)
            {
                float s = sin(p.x * _WaveScale + t) * 0.5
                        + sin((p.x * 0.6 + p.y) * _WaveScale * 1.7 - t * 1.3) * 0.3
                        + sin((p.y * 1.3 - p.x * 0.4) * _WaveScale * 2.9 + t * 2.1) * 0.2
                        + sin((p.x * 0.8 - p.y * 1.1) * _WaveScale * 5.3 - t * 2.9) * 0.22
                        + sin((p.x * 1.7 + p.y * 0.5) * _WaveScale * 7.1 + t * 3.7) * 0.12;
                return s;
            }

            V vert(A i)
            {
                V o;
                float3 ws = TransformObjectToWorld(i.positionOS.xyz);
                float t = _Time.y * _WaveSpeed;
                float s = Swell(ws.xz, t);
                ws.y += s * _WaveHeight;
                o.positionWS = ws;
                o.h = s * 0.5 + 0.5;
                o.positionCS = Ps1Snap(TransformWorldToHClip(ws));
                o.fog = ComputeFogFactor(o.positionCS.z);
                return o;
            }

            half4 frag(V i) : SV_Target
            {
                half3 n = FlatNormal(i.positionWS, half3(0,1,0));
                half3 albedo = lerp(_DeepColor.rgb, _CrestColor.rgb, saturate(i.h * i.h));
                half3 lit = Ps1Lighting(i.positionWS, n, i.positionCS);
                // Wet slick: hard specular from the main light so the swell catches the light in flat facets.
                Light ml = GetMainLight();
                half3 v = normalize(GetCameraPositionWS() - i.positionWS);
                half spec = pow(saturate(dot(n, normalize(ml.direction + v))), 40) * 2.2;
                // Grazing facets pick up the red murk; facing ones stay black and wet.
                half fres = pow(1 - saturate(dot(n, v)), 4);
                half3 c = albedo * lit + _GlowColor.rgb * (0.5 + i.h) + _CrestColor.rgb * fres * 0.3 + ml.color * spec * half3(0.95, 0.22, 0.16);
                // From far away (and from the air) the whole sea glows a dull arterial red.
                float dist = length(GetCameraPositionWS() - i.positionWS);
                c += _CrestColor.rgb * saturate((dist - 25) / 220) * 0.55;
                c = MixFog(c, i.fog);
                return half4(c, 1);
            }
            ENDHLSL
        }
    }
}
