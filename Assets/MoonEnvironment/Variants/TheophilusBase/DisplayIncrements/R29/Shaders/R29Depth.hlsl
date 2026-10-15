#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Shadows.hlsl"
float3 _LightDirection;
float3 _LightPosition;
struct R29DepthAttributes
{
    float4 positionOS : POSITION;
    float3 normalOS : NORMAL;
    UNITY_VERTEX_INPUT_INSTANCE_ID
};
struct R29DepthVaryings
{
    float4 positionCS : SV_POSITION;
    UNITY_VERTEX_OUTPUT_STEREO
};
R29DepthVaryings R29DepthVertex(R29DepthAttributes input)
{
    R29DepthVaryings output = (R29DepthVaryings)0;
    UNITY_SETUP_INSTANCE_ID(input);
    UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);
    output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
    return output;
}
R29DepthVaryings R29ShadowVertex(R29DepthAttributes input)
{
    R29DepthVaryings output = (R29DepthVaryings)0;
    UNITY_SETUP_INSTANCE_ID(input);
    UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);
    float3 p = TransformObjectToWorld(input.positionOS.xyz);
    float3 n = TransformObjectToWorldNormal(input.normalOS);
    #if _CASTING_PUNCTUAL_LIGHT_SHADOW
        float3 direction = normalize(_LightPosition-p);
    #else
        float3 direction = _LightDirection;
    #endif
    output.positionCS = TransformWorldToHClip(ApplyShadowBias(p,n,direction));
    #if UNITY_REVERSED_Z
        output.positionCS.z = min(output.positionCS.z,UNITY_NEAR_CLIP_VALUE);
    #else
        output.positionCS.z = max(output.positionCS.z,UNITY_NEAR_CLIP_VALUE);
    #endif
    return output;
}
half4 R29DepthFragment(R29DepthVaryings input) : SV_Target { return 0; }
