// Inside-out dome around the camera: black storm sky by default, dawn gradient with a sun above the clouds.
Shader "Fnmw/SkyDome"
{
    Properties
    {
        _TopColor ("Top", Color) = (0,0,0,1)
        _HorizonColor ("Horizon", Color) = (0.1,0.01,0.01,1)
        _BottomColor ("Bottom", Color) = (0.05,0,0,1)
        _SunColor ("Sun", Color) = (1,0.8,0.6,1)
        _SunDir ("Sun direction", Vector) = (0,0.1,1,0)
        _SunSize ("Sun size", Float) = 0
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" "Queue"="Geometry+500" "RenderPipeline"="UniversalPipeline" }
        Cull Front ZWrite Off
        Pass
        {
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            CBUFFER_START(UnityPerMaterial)
                half4 _TopColor, _HorizonColor, _BottomColor, _SunColor; float4 _SunDir; float _SunSize;
            CBUFFER_END
            struct A { float4 positionOS : POSITION; };
            struct V { float4 positionCS : SV_POSITION; float3 dir : TEXCOORD0; };
            V vert(A i)
            {
                V o;
                float3 ws = TransformObjectToWorld(i.positionOS.xyz);
                o.positionCS = TransformWorldToHClip(ws);
                #if UNITY_REVERSED_Z
                    o.positionCS.z = o.positionCS.w * 1e-6;
                #else
                    o.positionCS.z = o.positionCS.w * (1 - 1e-6);
                #endif
                o.dir = ws - GetCameraPositionWS();
                return o;
            }
            half4 frag(V i) : SV_Target
            {
                float3 d = normalize(i.dir);
                half3 c = d.y > 0 ? lerp(_HorizonColor.rgb, _TopColor.rgb, pow(saturate(d.y), 0.55))
                                  : lerp(_HorizonColor.rgb, _BottomColor.rgb, pow(saturate(-d.y), 0.35));
                float s = dot(d, normalize(_SunDir.xyz));
                if (_SunSize > 0)
                {
                    c += _SunColor.rgb * (smoothstep(1 - _SunSize, 1 - _SunSize * 0.6, s) * 1.5 + pow(saturate(s), 12) * 0.45 + pow(saturate(s), 3) * 0.15);
                }
                return half4(c, 1);
            }
            ENDHLSL
        }
    }
}
