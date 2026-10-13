Shader "MoonEnvironment/SpaceSky"
{
    Properties
    {
        _StarMap("NASA celestial star map (linear HDR)", 2D) = "black" {}
        _StarIntensity("Star map exposure", Range(0, 8)) = 1.2
        _StarsDayVisibility("Day star visibility (0 = photographic exposure)", Range(0, 1)) = 0
        _StarPointBoost("Bright star definition", Range(1, 6)) = 1
        _MilkyWayIntensity("Diffuse star field strength", Range(0, 1)) = 1
        _StarPeakRadiance("Star highlight ceiling (HDR)", Range(0.5, 8)) = 4
        [HideInInspector] _AstronomicalSky("Mean orbit sky", Float) = 0
        [HideInInspector] _SkyEast("Equatorial east", Vector) = (1,0,0,0)
        [HideInInspector] _SkyUp("Equatorial up", Vector) = (0,0,1,0)
        [HideInInspector] _SkyNorth("Equatorial north", Vector) = (0,1,0,0)
        _StarRotation("Star map yaw (degrees)", Range(0, 360)) = 0
        _EarthMap("NASA Blue Marble", 2D) = "white" {}
        _EarthDirection("Earth direction in world space", Vector) = (-0.426, 0.878, 0.215, 0)
        _EarthDiameter("Earth angular diameter (degrees)", Range(0.1, 5)) = 1.9
        _EarthAngleScale("Earth art direction scale (1 = physical size)", Range(1, 8)) = 1
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
                float _StarPointBoost, _MilkyWayIntensity, _StarPeakRadiance;
                float _EarthDiameter, _EarthAngleScale, _EarthRotation, _EarthBrightness;
                float _SunDiameter, _SunVisible, _Daylight;
                float _AstronomicalSky;
                float4 _SkyEast, _SkyUp, _SkyNorth;
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
            float3 ReadStableStars(float2 uv)
            {
                // Equirectangular longitude wraps at the meridian. Correct its derivatives
                // before sampling so the seam does not select an almost-empty coarse mip.
                float2 dx = ddx(uv), dy = ddy(uv);
                dx.x -= round(dx.x); dy.x -= round(dy.x);
                float2 a = (dx + dy) * .25;
                float2 b = (dx - dy) * .25;
                // Fixed subpixel coverage, with ordinary mip filtering: no frame jitter,
                // negative mip bias or animated noise to make tiny stars scintillate.
                float3 stars = SAMPLE_TEXTURE2D_GRAD(_StarMap, sampler_StarMap, uv + a, dx, dy).rgb;
                stars += SAMPLE_TEXTURE2D_GRAD(_StarMap, sampler_StarMap, uv - a, dx, dy).rgb;
                stars += SAMPLE_TEXTURE2D_GRAD(_StarMap, sampler_StarMap, uv + b, dx, dy).rgb;
                stars += SAMPLE_TEXTURE2D_GRAD(_StarMap, sampler_StarMap, uv - b, dx, dy).rgb;
                stars *= .25 * _StarIntensity;
                float luminance = dot(stars, float3(.2126, .7152, .0722));
                // Separate bright catalog stars from the diffuse field without a hard
                // threshold that would pop stars in and out during camera movement.
                float definition = luminance / (luminance + .06);
                stars *= lerp(_MilkyWayIntensity, _StarPointBoost, definition);
                float peak = max(stars.r, max(stars.g, stars.b));
                stars /= 1 + peak / max(.5, _StarPeakRadiance);
                return stars;
            }
            half4 Frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                float3 ray = normalize(input.direction);
                float3 sun = normalize(_SunDirection.xyz);
                float3 equatorial = ray.x*_SkyEast.xyz + ray.y*_SkyUp.xyz + ray.z*_SkyNorth.xyz;
                // NASA's equirectangular map uses RA horizontally and declination vertically.
                float2 starUV = _AstronomicalSky > .5
                    ? float2(.5 + atan2(equatorial.y,equatorial.x)/TWO_PI, .5 + asin(clamp(equatorial.z,-1,1))/PI)
                    : SphericalUV(ray);
                starUV.x += _StarRotation / 360;
                float3 color = ReadStableStars(starUV)
                    * lerp(1, _StarsDayVisibility, saturate(_Daylight));

                // A finite disk, without an atmospheric halo or a horizon gradient.
                float sunDistance = length(ray - sun);
                float sunRadius = 2 * sin(radians(_SunDiameter) * 0.25);
                float sunAA = max(fwidth(sunDistance), 0.00001);
                float sunMask = 1 - smoothstep(sunRadius - sunAA, sunRadius + sunAA, sunDistance);
                color = lerp(color, _SunColor.rgb, sunMask * _SunVisible);

                float3 earth = normalize(_EarthDirection.xyz);
                float facing = dot(ray, earth);
                float earthDiameter = clamp(_EarthDiameter * _EarthAngleScale, .1, 40);
                UNITY_BRANCH if (facing > cos(radians(earthDiameter * .5 + .25)))
                {
                    float3 referenceUp = abs(earth.y) > 0.999 ? float3(0, 0, 1) : float3(0, 1, 0);
                    float3 right = normalize(cross(referenceUp, earth));
                    float3 up = cross(earth, right);
                    float radius = tan(radians(earthDiameter) * 0.5);
                    float2 disk = float2(dot(ray, right), dot(ray, up)) / (facing * radius);
                    float diskRadius = length(disk);
                    float earthAA = max(fwidth(diskRadius), 0.0001);
                    float earthMask = 1 - smoothstep(1 - earthAA, 1 + earthAA, diskRadius);
                    UNITY_BRANCH if (earthMask > 0)
                    {
                        float front = sqrt(saturate(1 - dot(disk, disk)));
                        float3 normal = normalize(disk.x * right + disk.y * up - front * earth);
                        float2 earthUV = SphericalUV(normalize(float3(disk.x, disk.y, front)));
                        if (_AstronomicalSky > .5)
                        {
                            float3 eqNormal = normal.x*_SkyEast.xyz + normal.y*_SkyUp.xyz + normal.z*_SkyNorth.xyz;
                            earthUV = float2(.5 + atan2(eqNormal.y,eqNormal.x)/TWO_PI - _EarthRotation/360,
                                .5 + asin(clamp(eqNormal.z,-1,1))/PI);
                        }
                        else earthUV.x += _EarthRotation / 360;
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
