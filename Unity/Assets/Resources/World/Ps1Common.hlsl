// Shared PS1 helpers for the World shaders (Forward+ aware light loop, vertex snap, flat normals).
#ifndef FNMW_PS1_COMMON
#define FNMW_PS1_COMMON

#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

float4 _FnmwSnapRes;      // xy = virtual resolution vertices snap to (0 = off)
half4  _FnmwAmbient;      // flat ambient set by Atmosphere
half   _FnmwLightSteps;   // posterise diffuse light into N bands (0 = smooth)

float4 Ps1Snap(float4 positionCS)
{
    if (_FnmwSnapRes.x < 1) return positionCS;
    float2 grid = _FnmwSnapRes.xy * 0.5;
    float w = max(positionCS.w, 1e-4);
    positionCS.xy = floor(positionCS.xy / w * grid + 0.5) / grid * w;
    return positionCS;
}

half3 FlatNormal(float3 positionWS, half3 hint)
{
    half3 n = normalize(cross(ddy(positionWS), ddx(positionWS)));
    return dot(n, hint) < 0 ? -n : n;
}

half Band(half x)
{
    if (_FnmwLightSteps < 1) return x;
    return floor(x * _FnmwLightSteps + 0.5) / _FnmwLightSteps;
}

// Lambert from the main light plus every additional light (flashlight, console glows, fires).
half3 Ps1Lighting(float3 positionWS, half3 normalWS, float4 svPosition)
{
    InputData inputData = (InputData)0;
    inputData.positionWS = positionWS;
    inputData.normalWS = normalWS;
    inputData.normalizedScreenSpaceUV = GetNormalizedScreenSpaceUV(svPosition);

    Light mainLight = GetMainLight();
    half3 c = _FnmwAmbient.rgb + mainLight.color * Band(saturate(dot(normalWS, mainLight.direction)));

#if defined(_ADDITIONAL_LIGHTS)
    uint pixelLightCount = GetAdditionalLightsCount();
    #if USE_CLUSTER_LIGHT_LOOP
    [loop] for (uint lightIndex = 0; lightIndex < min(URP_FP_DIRECTIONAL_LIGHTS_COUNT, MAX_VISIBLE_LIGHTS); lightIndex++)
    {
        Light l = GetAdditionalLight(lightIndex, positionWS);
        c += l.color * l.distanceAttenuation * Band(saturate(dot(normalWS, l.direction)));
    }
    #endif
    LIGHT_LOOP_BEGIN(pixelLightCount)
        Light light = GetAdditionalLight(lightIndex, positionWS);
        c += light.color * light.distanceAttenuation * Band(saturate(dot(normalWS, light.direction)));
    LIGHT_LOOP_END
#endif
    return c;
}

#endif
