Shader "Moonkov/SmokeVolume"
{
    Properties { _Density("Density", Float)=1 }
    SubShader
    {
        Tags {"Queue"="Transparent" "RenderType"="Transparent" "RenderPipeline"="UniversalPipeline"}
        Pass
        {
            Tags {"LightMode"="MoonkovTacticalTransparent"}
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off ZTest Always Cull Front
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma target 3.5
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            TEXTURE2D_X_FLOAT(_MoonkovTacticalSceneDepth);
            float4 _MoonkovTacticalDepthScaleBias;
            CBUFFER_START(UnityPerMaterial)
            float _Density;
            CBUFFER_END
            struct Attributes { float4 positionOS:POSITION; };
            struct Varyings { float4 positionCS:SV_POSITION; float3 positionWS:TEXCOORD0; };
            Varyings Vert(Attributes v)
            {
                Varyings o;o.positionWS=TransformObjectToWorld(v.positionOS.xyz);
                o.positionCS=TransformWorldToHClip(o.positionWS);return o;
            }
            float Hash(float3 p) { p=frac(p*.1031);p+=dot(p,p.yzx+33.33);return frac((p.x+p.y)*p.z); }
            float Noise(float3 p)
            {
                float3 i=floor(p), f=frac(p);f=f*f*(3-2*f);
                return lerp(lerp(lerp(Hash(i),Hash(i+float3(1,0,0)),f.x),lerp(Hash(i+float3(0,1,0)),Hash(i+float3(1,1,0)),f.x),f.y),
                    lerp(lerp(Hash(i+float3(0,0,1)),Hash(i+float3(1,0,1)),f.x),lerp(Hash(i+float3(0,1,1)),Hash(i+1),f.x),f.y),f.z);
            }
            half4 Frag(Varyings input):SV_Target
            {
                float3 ray=IsPerspectiveProjection()?normalize(input.positionWS-_WorldSpaceCameraPos):-UNITY_MATRIX_V[2].xyz;
                float3 origin=IsPerspectiveProjection()?_WorldSpaceCameraPos:input.positionWS-ray*dot(input.positionWS-_WorldSpaceCameraPos,ray);
                float3 cameraOS=TransformWorldToObject(origin)*2;
                float3 directionOS=mul((float3x3)unity_WorldToObject,ray)*2;
                float a=dot(directionOS,directionOS), b=dot(cameraOS,directionOS);
                float d=b*b-a*(dot(cameraOS,cameraOS)-1);
                if(d<=0)discard;
                float start=max(0,(-b-sqrt(d))/a), end=(-b+sqrt(d))/a;
                float2 uv=GetNormalizedScreenSpaceUV(input.positionCS)*_MoonkovTacticalDepthScaleBias.xy+_MoonkovTacticalDepthScaleBias.zw;
                float raw=SAMPLE_TEXTURE2D_X(_MoonkovTacticalSceneDepth,sampler_PointClamp,uv).r;
                float sceneEye=IsPerspectiveProjection()?LinearEyeDepth(raw,_ZBufferParams):LinearDepthToEyeDepth(raw);
                float forward=max(.001,-TransformWorldToViewDir(ray).z);
                end=min(end,sceneEye/forward);
                if(end<=start)discard;
                float stepSize=(end-start)/32, transmittance=1;
                float3 color=0;
                [loop] for(int i=0;i<32;i++)
                {
                    float t=start+(i+.5)*stepSize;
                    float3 p=cameraOS+directionOS*t;
                    float3 ws=origin+ray*t;
                    float3 flow=ws*.65+float3(_Time.y*.07,-_Time.y*.11,_Time.y*.05);
                    float n=Noise(flow);
                    float detail=Noise(ws*2.5-float3(0,_Time.y*.22,0));
                    float edge=saturate((.72+.28*n-length(p))*8);
                    float density=edge*lerp(.65,1.35,n*.75+detail*.25)*_Density*2;
                    float alpha=1-exp(-density*stepSize);
                    float light=saturate(.38+p.y*.18+(n-Noise(flow+float3(-.3,.5,-.2)))*2.5);
                    float3 tint=lerp(float3(.09,.11,.13),float3(.56,.59,.61),light);
                    color+=transmittance*alpha*tint;transmittance*=1-alpha;
                    if(transmittance<.005)break;
                }
                float opacity=1-transmittance;
                return half4(color/max(.0001,opacity),opacity);
            }
            ENDHLSL
        }
    }
}
