#ifndef MOON_SILVER_SUBJECT_DEPTH_INCLUDED
#define MOON_SILVER_SUBJECT_DEPTH_INCLUDED
#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

TEXTURE2D_X_FLOAT(_MoonSubjectSceneDepth);
float4 _MoonSubjectDepthScaleBias;

void MoonSubjectDepthTest(float4 positionCS, float subjectDepth, bool writesDepth)
{
    float2 uv = GetNormalizedScreenSpaceUV(positionCS);
    uv = uv * _MoonSubjectDepthScaleBias.xy + _MoonSubjectDepthScaleBias.zw;
    float raw = SAMPLE_TEXTURE2D_X(_MoonSubjectSceneDepth, sampler_PointClamp, uv).r;
    float sceneEye = LinearEyeDepth(raw, _ZBufferParams);
    float subjectEye = LinearEyeDepth(subjectDepth, _ZBufferParams);
    float tolerance = max(.0005, sceneEye * .00001);
    // Opaque coverage must coincide with the surface that actually wrote depth.
    // Being in front of the background is insufficient for proxy volumes/cutouts.
    clip(writesDepth ? tolerance - abs(sceneEye - subjectEye) : sceneEye + tolerance - subjectEye);
}
#endif
