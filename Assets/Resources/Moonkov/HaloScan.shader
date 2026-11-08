Shader "Moonkov/HaloScan"
{
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" }
        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
        #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"
        TEXTURE2D_X_FLOAT(_ScanDepth);
        float4 _ScanDepthScaleBias, _ScanOrigin, _ScanState, _ScanColor, _ScanTexel;
        float4 _ScanContacts[24], _ScanRight, _ScanUp;
        float4x4 _ScanInverseVP, _ScanVP;
        float Grid(float2 uv)
        {
            float2 edge = abs(frac(uv - .5) - .5) / max(fwidth(uv), .0001);
            return 1 - saturate(min(edge.x, edge.y) - .2);
        }
        ENDHLSL
        Pass
        {
            Name "Terrain pulse"
            ZWrite Off ZTest Always Cull Off
            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex Vert
            #pragma fragment Terrain
            #pragma multi_compile_instancing
            half4 Terrain(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                half4 original = SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, input.texcoord);
                if (_ScanState.w < .5 || _ScanState.x > _ScanState.y + 4) return original;
                float2 screen = (input.texcoord - _BlitScaleBias.zw) / _BlitScaleBias.xy;
                float raw = SAMPLE_TEXTURE2D_X(_ScanDepth, sampler_PointClamp, screen * _ScanDepthScaleBias.xy + _ScanDepthScaleBias.zw).r;
                #if UNITY_REVERSED_Z
                if (raw < .00001) return original;
                #else
                if (raw > .99999) return original;
                raw = lerp(UNITY_NEAR_CLIP_VALUE, 1, raw);
                #endif
                float3 world = ComputeWorldSpacePosition(screen, raw, _ScanInverseVP);
                float distance = length(world - _ScanOrigin.xyz);
                float behind = _ScanState.x - distance;
                float front = exp(-abs(behind) * 5);
                float echo = exp(-abs(behind - .65) * 9) * .28;
                float trail = saturate(behind * 4) * exp(-max(behind, 0) * .42);
                float3 normal = abs(normalize(cross(ddx(world), ddy(world))));
                normal = pow(normal, 6); normal /= max(dot(normal, float3(1, 1, 1)), .001);
                float3 grid = float3(Grid(world.zy * 1.5), Grid(world.xz * 1.5), Grid(world.xy * 1.5));
                float contours = 1 - saturate(abs(frac(world.y * 2 - .5) - .5) / max(fwidth(world.y * 2), .004) - .15);
                float2 depthUV = screen * _ScanDepthScaleBias.xy + _ScanDepthScaleBias.zw;
                float centerDepth = LinearEyeDepth(SAMPLE_TEXTURE2D_X(_ScanDepth, sampler_PointClamp, depthUV).r, _ZBufferParams);
                float dx = LinearEyeDepth(SAMPLE_TEXTURE2D_X(_ScanDepth, sampler_PointClamp, depthUV + float2(_ScanTexel.x, 0)).r, _ZBufferParams);
                float dy = LinearEyeDepth(SAMPLE_TEXTURE2D_X(_ScanDepth, sampler_PointClamp, depthUV + float2(0, _ScanTexel.y)).r, _ZBufferParams);
                float silhouette = saturate((abs(centerDepth - dx) + abs(centerDepth - dy)) / max(.1, centerDepth) * 8);
                float crease = saturate(length(fwidth(normal)) * 2);
                float3 dotGrid = abs(frac(world * 1.5 - .5) - .5);
                float dots = max(max(step(length(dotGrid.xy), .045) * normal.z,
                    step(length(dotGrid.xz), .045) * normal.y), step(length(dotGrid.zy), .045) * normal.x);
                float detail = dot(grid, normal) * .38 + contours * .18 + dots * .65 + max(silhouette, crease) * .8;
                float limit = saturate((_ScanState.y - distance) * 2) * saturate((_ScanState.y + 3 - _ScanState.x) / 3);
                float3 tint = lerp(_ScanColor.rgb, float3(.72, .92, 1), .3);
                float intensity = (front * 1.05 + echo + trail * detail) * limit;
                float wash = trail * limit * .13;
                float luminance = dot(original.rgb, float3(.2126, .7152, .0722));
                float3 surface = lerp(original.rgb, luminance * .7 + tint * .25, wash);
                return half4(surface + tint * intensity, original.a);
            }
            ENDHLSL
        }
        Pass
        {
            Name "Authorized contacts"
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off ZTest Always Cull Off
            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex ContactVertex
            #pragma fragment ContactFragment
            struct ContactVaryings { float4 positionCS:SV_POSITION; float2 uv:TEXCOORD0; float fade:TEXCOORD1; };
            ContactVaryings ContactVertex(uint vertexID:SV_VertexID, uint instanceID:SV_InstanceID)
            {
                static const float2 corners[6] = { float2(-1,-1), float2(-1,1), float2(1,1), float2(-1,-1), float2(1,1), float2(1,-1) };
                float2 corner = corners[vertexID];
                float4 contact = _ScanContacts[instanceID];
                float3 world = contact.xyz + _ScanRight.xyz * corner.x * .65 + _ScanUp.xyz * corner.y;
                ContactVaryings output; output.positionCS = mul(_ScanVP, float4(world, 1)); output.uv = corner; output.fade = contact.w;
                return output;
            }
            half4 ContactFragment(ContactVaryings input):SV_Target
            {
                float2 a = abs(input.uv);
                float edgeX = 1 - smoothstep(.014, .036, abs(a.x - .83));
                float edgeY = 1 - smoothstep(.012, .027, abs(a.y - .88));
                float bracket = max(edgeX * step(.65, a.y) * step(a.y, .9), edgeY * step(.55, a.x) * step(a.x, .85));
                float dot = (1 - smoothstep(.045, .07, length(input.uv - float2(0, .88)))) * .8;
                return half4(1, .36, .10, max(bracket, dot) * input.fade * .85);
            }
            ENDHLSL
        }
        Pass
        {
            Name "Authorized silhouettes"
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off ZTest Always Cull Back
            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex SilhouetteVertex
            #pragma fragment SilhouetteFragment
            #pragma multi_compile_instancing
            struct ShapeAttributes { float4 positionOS:POSITION; float3 normalOS:NORMAL; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct ShapeVaryings { float4 positionCS:SV_POSITION; float3 normalWS:TEXCOORD0; float3 positionWS:TEXCOORD1; };
            ShapeVaryings SilhouetteVertex(ShapeAttributes input)
            {
                UNITY_SETUP_INSTANCE_ID(input);
                ShapeVaryings output;
                output.positionWS = TransformObjectToWorld(input.positionOS.xyz);
                output.positionCS = TransformWorldToHClip(output.positionWS);
                output.normalWS = TransformObjectToWorldNormal(input.normalOS);
                return output;
            }
            half4 SilhouetteFragment(ShapeVaryings input):SV_Target
            {
                float rim = pow(saturate(1 - abs(dot(normalize(input.normalWS), normalize(GetWorldSpaceViewDir(input.positionWS))))), 2.5);
                float lines = .8 + .2 * sin(input.positionWS.y * 85);
                return half4(1, .24 + rim * .25, .055, (.08 + rim * .55) * lines);
            }
            ENDHLSL
        }
    }
}
