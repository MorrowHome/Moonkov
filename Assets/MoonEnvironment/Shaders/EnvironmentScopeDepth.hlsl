#ifndef MOON_ENVIRONMENT_SCOPE_DEPTH_INCLUDED
#define MOON_ENVIRONMENT_SCOPE_DEPTH_INCLUDED
#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
TEXTURE2D_X_FLOAT(_MoonEnvironmentSceneDepth);
float4 _MoonEnvironmentDepthScaleBias;
void MoonEnvironmentDepthTest(float4 positionCS)
{
    float2 uv = GetNormalizedScreenSpaceUV(positionCS) * _MoonEnvironmentDepthScaleBias.xy + _MoonEnvironmentDepthScaleBias.zw;
    float raw = SAMPLE_TEXTURE2D_X(_MoonEnvironmentSceneDepth, sampler_PointClamp, uv).r;
    float sceneEye = IsPerspectiveProjection() ? LinearEyeDepth(raw, _ZBufferParams) : LinearDepthToEyeDepth(raw);
    float surfaceEye = IsPerspectiveProjection() ? LinearEyeDepth(positionCS.z, _ZBufferParams) : LinearDepthToEyeDepth(positionCS.z);
    // Positive coverage must coincide with the real opaque surface, including actor occlusion.
    clip(max(.0005, sceneEye * .00001) - abs(sceneEye - surfaceEye));
}
#endif
