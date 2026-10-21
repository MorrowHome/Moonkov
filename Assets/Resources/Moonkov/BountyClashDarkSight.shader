Shader "Moonkov/BountyClashDarkSight"
{
    SubShader
    {
        Tags {"RenderPipeline"="UniversalPipeline"}
        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
        #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"
        TEXTURE2D_X_FLOAT(_DarkDepth);
        float4 _DarkDepthScaleBias,_DarkState,_DarkTexel;
        float4 _DarkContacts[11];
        float4 _DarkCameraRight,_DarkCameraUp;
        float4x4 _DarkViewProjection;
        float Hash(float2 p) {return frac(sin(dot(p,float2(127.1,311.7)))*43758.5453);}
        float Noise(float2 p)
        {
            float2 i=floor(p),f=frac(p);f=f*f*(3-2*f);
            return lerp(lerp(Hash(i),Hash(i+float2(1,0)),f.x),lerp(Hash(i+float2(0,1)),Hash(i+1),f.x),f.y);
        }
        float Fbm(float2 p){return Noise(p)*.57+Noise(p*2.03+13.1)*.28+Noise(p*4.07-7.4)*.15;}
        float Eye(float raw){return IsPerspectiveProjection()?LinearEyeDepth(raw,_ZBufferParams):LinearDepthToEyeDepth(raw);}
        ENDHLSL
        Pass
        {
            Name "Cold world"
            ZWrite Off ZTest Always Cull Off
            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex Vert
            #pragma fragment Environment
            #pragma multi_compile_instancing
            half4 Environment(Varyings input):SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                float2 uv=input.texcoord;
                half4 original=SAMPLE_TEXTURE2D_X(_BlitTexture,sampler_LinearClamp,uv);
                float3 c=max(original.rgb,0);
                #ifndef UNITY_COLORSPACE_GAMMA
                c=LinearToSRGB(c);
                #endif
                float luma=dot(c,float3(.2126,.7152,.0722));
                float edge=0,near=1;
                if(_DarkState.w>.5)
                {
                    float2 screen=(uv-_BlitScaleBias.zw)/_BlitScaleBias.xy;
                    float2 depthUV=screen*_DarkDepthScaleBias.xy+_DarkDepthScaleBias.zw;
                    float center=Eye(SAMPLE_TEXTURE2D_X(_DarkDepth,sampler_PointClamp,depthUV).r);
                    float nx=Eye(SAMPLE_TEXTURE2D_X(_DarkDepth,sampler_PointClamp,depthUV+float2(_DarkTexel.x,0)).r);
                    float ny=Eye(SAMPLE_TEXTURE2D_X(_DarkDepth,sampler_PointClamp,depthUV+float2(0,_DarkTexel.y)).r);
                    edge=saturate((abs(center-nx)+abs(center-ny))/max(.12,center)*2.5);
                    near=exp(-center*.035);
                }
                float mapped=pow(smoothstep(.15,.82,luma),1.9)*.94+.015;
                mapped=max(mapped,luma*.24+.015); // Preserve usable space even in dim interiors.
                mapped+=edge*.14*near;
                float2 screenUV=(uv-_BlitScaleBias.zw)/_BlitScaleBias.xy;
                float2 q=(screenUV-.5)*float2(1.3,1);
                float radius=length(q);
                float vignette=smoothstep(.27,.78,radius);
                float drift=Noise(screenUV*float2(120,75)+float2(_DarkState.z*.65,-_DarkState.z*.4))-.5;
                float3 styled=mapped*float3(.94,.975,1.0)*(1-vignette*.28)+drift*.009;
                // Instinct is a directionless soft elliptical edge breath, never a rectangular radar.
                float breath=.78+.22*sin(_DarkState.z*2.25);
                float instinct=smoothstep(.36,.78,radius)*_DarkState.y*breath;
                styled=lerp(styled,float3(.95,.27,.035),instinct*.20);
                #ifndef UNITY_COLORSPACE_GAMMA
                styled=SRGBToLinear(max(styled,0));
                #endif
                return half4(lerp(original.rgb,styled,saturate(_DarkState.x)),original.a);
            }
            ENDHLSL
        }
        Pass
        {
            Name "Authorized uncertain smoke"
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off ZTest Always Cull Off
            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex CloudVertex
            #pragma fragment CloudFragment
            struct CloudVaryings {float4 positionCS:SV_POSITION;float2 uv:TEXCOORD0;nointerpolation float seed:TEXCOORD1;};
            CloudVaryings CloudVertex(uint vertexID:SV_VertexID,uint instanceID:SV_InstanceID)
            {
                static const float2 corners[6]={float2(-1,-1),float2(-1,1),float2(1,1),float2(-1,-1),float2(1,1),float2(1,-1)};
                float2 corner=corners[vertexID];float4 contact=_DarkContacts[instanceID];
                float3 world=contact.xyz+_DarkCameraRight.xyz*corner.x*contact.w+_DarkCameraUp.xyz*corner.y*contact.w*.9;
                CloudVaryings output;output.positionCS=mul(_DarkViewProjection,float4(world,1));output.uv=corner;output.seed=instanceID*7.13+3.7;return output;
            }
            half4 CloudFragment(CloudVaryings input):SV_Target
            {
                float2 q=input.uv;float t=_DarkState.z;
                float flow=Fbm(q*2.9+float2(t*.11,-t*.21)+input.seed);
                float2 warp=float2(Noise(q*3+float2(t*.14,input.seed)),Noise(q*3+float2(input.seed,-t*.17)))-.5;
                float2 body=q+warp*.28;
                // Soft overlapping lobes suggest an uncertain presence, never a skeleton/hitbox.
                float torso=exp(-dot(body*float2(1.65,1.8),body*float2(1.65,1.8))*2.0);
                float2 head=(body-float2(.05,.30))*float2(3.3,3.8);
                float upper=exp(-dot(head,head)*1.7)*.3;
                float veil=exp(-dot(body*float2(1.3,1.25),body*float2(1.3,1.25))*2)*.25;
                float edge=smoothstep(1,.53,max(abs(q.x),abs(q.y)));
                float density=saturate((torso+upper+veil)*(.4+flow*.9))*edge;
                float alpha=density*.62;
                float3 orange=lerp(float3(.7,.075,.008),float3(1.65,.43,.035),flow);
                return half4(orange,alpha);
            }
            ENDHLSL
        }
    }
}
