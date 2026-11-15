// Opaque, depth-writing, stereo-safe flat shading for the VR port's own props (the Touch controller bodies,
// their buttons and the ground guide's base). A fixed key light from above gives the shapes some form in the
// dark without depending on the scene's lights. _Emit lifts the colour towards full brightness (button presses).
Shader "Fnmw/XR/Solid"
{
    Properties
    {
        _Color ("Color", Color) = (1,1,1,1)
        _Emit ("Emit", Range(0,1)) = 0
    }
    SubShader
    {
        Tags { "Queue"="Geometry" "RenderType"="Opaque" "IgnoreProjector"="True" }
        Cull Back ZWrite On ZTest LEqual
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"
            struct appdata { float4 vertex : POSITION; float3 normal : NORMAL; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct v2f { float4 pos : SV_POSITION; float shade : TEXCOORD0; UNITY_VERTEX_OUTPUT_STEREO };
            float4 _Color; float _Emit;
            v2f vert(appdata v)
            {
                v2f o;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                o.pos = UnityObjectToClipPos(v.vertex);
                float3 n = normalize(UnityObjectToWorldNormal(v.normal));
                o.shade = 0.45 + 0.55 * saturate(dot(n, normalize(float3(0.3, 1.0, -0.2))) * 0.5 + 0.5);
                return o;
            }
            fixed4 frag(v2f i) : SV_Target
            {
                float3 c = _Color.rgb * i.shade;
                c = lerp(c, max(_Color.rgb, 0.85), _Emit);
                return fixed4(c, 1);
            }
            ENDCG
        }
    }
}
