Shader "MoonEnvironment/SpaceSky"
{
    Properties
    {
        _StarMap("NASA celestial star map (linear HDR)", 2D) = "black" {}
        _StarIntensity("Star map exposure", Range(0, 8)) = 1.2
        _StarsDayVisibility("Day star visibility (0 = photographic exposure)", Range(0, 1)) = 0.65
        _StarRotation("Star map yaw (degrees)", Range(0, 360)) = 0
        _EarthMap("NASA Blue Marble", 2D) = "white" {}
        _EarthDirection("Earth direction in world space", Vector) = (-0.426, 0.878, 0.215, 0)
        _EarthDiameter("Earth angular diameter (degrees)", Range(0.1, 5)) = 1.9
        _EarthRotation("Earth texture longitude (degrees)", Range(0, 360)) = 30
        _EarthBrightness("Earth brightness", Range(0, 4)) = 1.2
        _SunDiameter("Sun angular diameter (degrees)", Range(0.1, 2)) = 0.533
        [HDR] _SunColor("Solar disk radiance (art exposure)", Color) = (80, 78, 76, 1)
        [HideInInspector] _SunDirection("Sun direction in world space", Vector) = (-0.784886, 0.422618, 0.453154, 0)
        [HideInInspector] _SunVisible("Sun enabled", Float) = 1
        [HideInInspector] _Daylight("Day exposure blend", Float) = 1
    }
    SubShader
    {
        Tags { "Queue"="Background" "RenderType"="Background" "PreviewType"="Skybox" "RenderPipeline"="UniversalPipeline" }
        Cull Off ZWrite Off
        Pass
        {
            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            TEXTURE2D(_StarMap); SAMPLER(sampler_StarMap);
            TEXTURE2D(_EarthMap); SAMPLER(sampler_EarthMap);
            CBUFFER_START(UnityPerMaterial)
                float4 _EarthDirection, _SunDirection, _SunColor;
                float _StarIntensity, _StarsDayVisibility, _StarRotation;
                float _EarthDiameter, _EarthRotation, _EarthBrightness;
                float _SunDiameter, _SunVisible, _Daylight;
            CBUFFER_END
            struct Attributes { float4 positionOS : POSITION; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct Varyings { float4 positionCS : SV_POSITION; float3 direction : TEXCOORD0; UNITY_VERTEX_OUTPUT_STEREO };
            Varyings Vert(Attributes input)
            {
                UNITY_SETUP_INSTANCE_ID(input);
                Varyings output;
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                output.direction = TransformObjectToWorldDir(input.positionOS.xyz);
                return output;
            }
            float2 SphericalUV(float3 direction)
            {
                return float2(0.5 + atan2(direction.x, direction.z) / TWO_PI,
                              0.5 + asin(clamp(direction.y, -1, 1)) / PI);
            }
            half4 Frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                float3 ray = normalize(input.direction);
                float3 sun = normalize(_SunDirection.xyz);
                float2 starUV = SphericalUV(ray);
                starUV.x += _StarRotation / 360;
                float3 color = SAMPLE_TEXTURE2D(_StarMap, sampler_StarMap, starUV).rgb
                    * _StarIntensity * lerp(1, _StarsDayVisibility, saturate(_Daylight));

                // A finite disk, without an atmospheric halo or a horizon gradient.
                float sunDistance = length(ray - sun);
                float sunRadius = 2 * sin(radians(_SunDiameter) * 0.25);
                float sunAA = max(fwidth(sunDistance), 0.00001);
                float sunMask = 1 - smoothstep(sunRadius - sunAA, sunRadius + sunAA, sunDistance);
                color = lerp(color, _SunColor.rgb, sunMask * _SunVisible);

                float3 earth = normalize(_EarthDirection.xyz);
                float facing = dot(ray, earth);
                UNITY_BRANCH if (facing > 0.99)
                {
                    float3 referenceUp = abs(earth.y) > 0.999 ? float3(0, 0, 1) : float3(0, 1, 0);
                    float3 right = normalize(cross(referenceUp, earth));
                    float3 up = cross(earth, right);
                    float radius = tan(radians(_EarthDiameter) * 0.5);
                    float2 disk = float2(dot(ray, right), dot(ray, up)) / (facing * radius);
                    float diskRadius = length(disk);
                    float earthAA = max(fwidth(diskRadius), 0.0001);
                    float earthMask = 1 - smoothstep(1 - earthAA, 1 + earthAA, diskRadius);
                    UNITY_BRANCH if (earthMask > 0)
                    {
                        float front = sqrt(saturate(1 - dot(disk, disk)));
                        float3 normal = normalize(disk.x * right + disk.y * up - front * earth);
                        float2 earthUV = SphericalUV(normalize(float3(disk.x, disk.y, front)));
                        earthUV.x += _EarthRotation / 360;
                        float3 albedo = SAMPLE_TEXTURE2D(_EarthMap, sampler_EarthMap, earthUV).rgb;
                        float illumination = saturate(dot(normal, sun));
                        float3 earthColor = albedo * illumination * _EarthBrightness;
                        // A small lit limb belongs to Earth's atmosphere, never to the lunar sky.
                        earthColor += float3(0.08, 0.18, 0.3) * pow(1 - front, 3) * illumination;
                        color = lerp(color, earthColor, earthMask);
                    }
                }
                return half4(color, 1);
            }
            ENDHLSL
        }
    }
    Fallback Off
}
