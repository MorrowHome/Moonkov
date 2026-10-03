Shader "Moonkov/Lunar Display"
{
    Properties
    {
        _BaseMap("LRO surface", 2D) = "white" {}
        [Normal] _BumpMap("NASA topology normals", 2D) = "bump" {}
        _SunDirection("Sun direction", Vector) = (0.8, 0.3, -0.6, 0)
        _SunColor("Sunlight", Color) = (1, 0.98, 0.95, 1)
    }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "RenderType"="Opaque" "Queue"="Geometry" }
        Pass
        {
            Tags { "LightMode"="UniversalForward" }
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Packing.hlsl"
            TEXTURE2D(_BaseMap); SAMPLER(sampler_BaseMap);
            TEXTURE2D(_BumpMap); SAMPLER(sampler_BumpMap);
            CBUFFER_START(UnityPerMaterial)
            float4 _SunDirection, _SunColor;
            CBUFFER_END
            struct Attributes { float4 positionOS:POSITION; float3 normalOS:NORMAL; float4 tangentOS:TANGENT; float2 uv:TEXCOORD0; };
            struct Varyings { float4 positionCS:SV_POSITION; float3 positionWS:TEXCOORD0; float3 normalWS:TEXCOORD1; float4 tangentWS:TEXCOORD2; float2 uv:TEXCOORD3; };
            Varyings Vert(Attributes input)
            {
                Varyings o;
                o.positionWS = TransformObjectToWorld(input.positionOS.xyz);
                o.positionCS = TransformWorldToHClip(o.positionWS);
                o.normalWS = TransformObjectToWorldNormal(input.normalOS);
                o.tangentWS = float4(TransformObjectToWorldDir(input.tangentOS.xyz), input.tangentOS.w * GetOddNegativeScale());
                o.uv = input.uv; return o;
            }
            half4 Frag(Varyings input):SV_Target
            {
                float3 macroNormal = normalize(input.normalWS);
                float3 tangent = normalize(input.tangentWS.xyz);
                float3 bitangent = cross(macroNormal, tangent) * input.tangentWS.w;
                float3 detail = UnpackNormal(SAMPLE_TEXTURE2D(_BumpMap, sampler_BumpMap, input.uv));
                detail.xy *= smoothstep(0, .03, min(input.uv.y, 1 - input.uv.y));
                detail.z = sqrt(max(.001, 1 - dot(detail.xy, detail.xy)));
                float3 normal = normalize(tangent * detail.x + bitangent * detail.y + macroNormal * detail.z);
                float3 sun = normalize(_SunDirection.xyz);
                float mu0 = max(0, dot(normal, sun));
                float mu = max(.03, dot(normal, normalize(GetWorldSpaceViewDir(input.positionWS))));
                // Regolith is diffuse. The spherical horizon blocks sun on the night side.
                float horizon = smoothstep(-.015, .015, dot(macroNormal, sun));
                float diffuse = lerp(mu0, 2 * mu0 / max(.05, mu0 + mu), .72) * horizon;
                float3 surface = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, input.uv).rgb;
                float albedo = dot(surface, float3(.2126, .7152, .0722));
                return half4(albedo * (_SunColor.rgb * diffuse * 1.9 + float3(.003, .004, .007)), 1);
            }
            ENDHLSL
        }
    }
}
