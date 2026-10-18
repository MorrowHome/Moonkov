#ifndef MOON_SILVER_SAND_INCLUDED
#define MOON_SILVER_SAND_INCLUDED

// Art-directed, dielectric mineral glints. No time input or emissive fallback:
// every contribution comes from a real, shadowed URP light.
// _SilverSand: strength, cells/metre, occupied-cell fraction, maximum distance.
// _SilverSheen: broad highlight strength, minimum smoothness, normal spread, unused.
float3 SilverSandHash(float3 p)
{
    p = frac(p * float3(0.1031, 0.1030, 0.0973));
    p += dot(p, p.yxz + 33.33);
    return frac((p.xxy + p.yxx) * p.zyx);
}

half3 SilverSandOneLight(Light light, InputData inputData, half3 grainNormal, half grains)
{
    #if defined(_LIGHT_LAYERS)
    if (!IsMatchingLightLayer(light.layerMask, GetMeshRenderingLayer())) return 0;
    #endif
    half3 n = inputData.normalWS;
    half3 v = inputData.viewDirectionWS;
    half3 h = SafeNormalize(light.direction + v);
    half nl = saturate(dot(n, light.direction));
    half nv = saturate(dot(n, v));
    // Wide rough-surface lobe plus sparse sharper mineral facets. Keep shadows
    // and coarse surface orientation, even when an individual facet faces a lamp.
    half broad = pow(saturate(dot(n, h)), 24.0h) * _SilverSheen.x;
    half fresnel = 0.04h + 0.7h * Pow4(1.0h - nv);
    // The weak, unshadowed poster bounce is diffuse art fill. It must not turn
    // occluded ground into white glitter; direct sunlight and lamps drive facets.
    half directGlint = saturate((Luminance(light.color) - 0.12h) * 1.6h);
    half facets = pow(saturate(dot(grainNormal, h)), 96.0h) * grains * _SilverSand.x * directGlint;
    return light.color * (light.distanceAttenuation * light.shadowAttenuation)
        * nl * (broad * fresnel + facets);
}

half3 SilverSandLighting(InputData inputData)
{
    [branch] if (_SilverSand.x <= 0 && _SilverSheen.x <= 0) return 0;
    float3 p = GetAbsolutePositionWS(inputData.positionWS);
    half3 n = inputData.normalWS;
    float scale = max(_SilverSand.y, 0.01);
    // XZ anchoring avoids UV seams across adjacent terrain tiles and local meshes.
    float2 grid = p.xz * scale;
    float3 seed = SilverSandHash(float3(floor(grid), floor(p.y * 0.5)));
    float2 delta = frac(grid) - lerp(0.2, 0.8, seed.xy);
    float footprint = max(length(ddx(grid)), length(ddy(grid)));
    float3 shape = SilverSandHash(seed + 7.31);
    delta.x *= lerp(0.7, 1.6, shape.x);
    float radius = lerp(0.025, 0.065, shape.y);
    float aa = max(footprint * 0.5, 0.01);
    half grains = (1 - smoothstep(radius - aa, radius + aa, length(delta)))
        * step(seed.z, _SilverSand.z);
    grains *= min(1, (radius * radius) / (aa * aa));
    // Fade before facets become unresolved subpixels (including at grazing angles).
    grains *= 1 - smoothstep(0.1, 0.5, footprint);
    float d = distance(GetCameraPositionWS(), p);
    grains *= 1 - smoothstep(_SilverSand.w * 0.4, max(_SilverSand.w, 0.01), d);
    half3 t = normalize(cross(abs(n.y) < 0.99 ? half3(0,1,0) : half3(0,0,1), n));
    half3 b = cross(n, t);
    half3 grainNormal = normalize(n + (seed.x * 2 - 1) * _SilverSheen.z * t
        + (seed.y * 2 - 1) * _SilverSheen.z * b);
    half3 sum = SilverSandOneLight(GetMainLight(inputData.shadowCoord,
        inputData.positionWS, inputData.shadowMask), inputData, grainNormal, grains);
    #if defined(_ADDITIONAL_LIGHTS)
    #if USE_CLUSTER_LIGHT_LOOP
    UNITY_LOOP for (uint lightIndex = 0; lightIndex < min(URP_FP_DIRECTIONAL_LIGHTS_COUNT, MAX_VISIBLE_LIGHTS); ++lightIndex)
    {
        CLUSTER_LIGHT_LOOP_SUBTRACTIVE_LIGHT_CHECK
        sum += SilverSandOneLight(GetAdditionalLight(lightIndex, inputData.positionWS,
            inputData.shadowMask), inputData, grainNormal, grains);
    }
    #endif
    uint count = GetAdditionalLightsCount();
    LIGHT_LOOP_BEGIN(count)
        sum += SilverSandOneLight(GetAdditionalLight(lightIndex, inputData.positionWS,
            inputData.shadowMask), inputData, grainNormal, grains);
    LIGHT_LOOP_END
    #endif
    return sum;
}
#endif
