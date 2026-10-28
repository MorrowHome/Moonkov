Shader "MoonEnvironment/Source Static Surface"
{
    Properties
    {
        _BaseMap("Source base colour", 2D) = "white" {}
        _SurfaceMap("Source metallic roughness coat", 2D) = "white" {}
        _WorldNormalMap("Source world normal", 2D) = "bump" {}
        _IrradianceMap("Cycles diffuse irradiance", 2D) = "black" {}
        _RadianceMap("Source specular radiance", 2D) = "black" {}
        _Emission("Source linear emission", Vector) = (0,0,0,0)
        _BakeUVChannel("Bake UV channel", Float) = 0
        _SpecularSamples("Specular samples", Range(1,16)) = 4
        _CoatRoughness("Source coat roughness", Range(0,1)) = .03
        _SpecularIORLevel("Source specular IOR level", Range(0,1)) = .5
        _SourceIOR("Source IOR", Float) = 1.5
        _ProbePosition("Source probe position", Vector) = (0,0,0,0)
        _ProbeBoxMin("Source probe box minimum", Vector) = (0,0,0,0)
        _ProbeBoxMax("Source probe box maximum", Vector) = (0,0,0,0)
    }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "RenderType"="Opaque" "Queue"="Geometry" }
        Pass
        {
            Name "SourceStatic"
            Tags { "LightMode"="UniversalForwardOnly" }
            Cull Back ZWrite On
            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_instancing
            #define _CLEARCOAT 1
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            TEXTURE2D(_BaseMap); SAMPLER(sampler_BaseMap);
            TEXTURE2D(_SurfaceMap); SAMPLER(sampler_SurfaceMap);
            TEXTURE2D(_WorldNormalMap); SAMPLER(sampler_WorldNormalMap);
            TEXTURE2D(_IrradianceMap); SAMPLER(sampler_IrradianceMap);
            TEXTURE2D(_RadianceMap); SAMPLER(sampler_RadianceMap);
            CBUFFER_START(UnityPerMaterial)
            float4 _Emission;
            float4 _ProbePosition, _ProbeBoxMin, _ProbeBoxMax;
            float _BakeUVChannel, _SpecularSamples, _CoatRoughness, _SpecularIORLevel, _SourceIOR;
            CBUFFER_END
            struct Attributes
            {
                float4 positionOS:POSITION;
                float3 normalOS:NORMAL;
                float2 uv0:TEXCOORD0; float2 uv1:TEXCOORD1;
                float2 uv2:TEXCOORD2; float2 uv3:TEXCOORD3;
                float2 uv4:TEXCOORD4; float2 uv5:TEXCOORD5;
                float2 uv6:TEXCOORD6; float2 uv7:TEXCOORD7;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };
            struct Varyings
            {
                float4 positionCS:SV_POSITION;
                float3 positionWS:TEXCOORD0;
                float2 uv:TEXCOORD1;
                float3 normalWS:TEXCOORD2;
                UNITY_VERTEX_OUTPUT_STEREO
            };
            Varyings Vert(Attributes i)
            {
                Varyings o;
                UNITY_SETUP_INSTANCE_ID(i); UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                o.positionWS=TransformObjectToWorld(i.positionOS.xyz);
                o.positionCS=TransformWorldToHClip(o.positionWS);
                o.normalWS=TransformObjectToWorldNormal(i.normalOS);
                o.uv=i.uv0;
                if(_BakeUVChannel>.5) o.uv=i.uv1;
                if(_BakeUVChannel>1.5) o.uv=i.uv2;
                if(_BakeUVChannel>2.5) o.uv=i.uv3;
                if(_BakeUVChannel>3.5) o.uv=i.uv4;
                if(_BakeUVChannel>4.5) o.uv=i.uv5;
                if(_BakeUVChannel>5.5) o.uv=i.uv6;
                if(_BakeUVChannel>6.5) o.uv=i.uv7;
                return o;
            }
            uint SourceHash(uint x)
            {
                x=(x^(x>>16))*0x7feb352du;
                x=(x^(x>>15))*0x846ca68bu;
                return x^(x>>16);
            }
            float SourceRandom(uint2 pixel,uint salt)
            {
                return (SourceHash(pixel.x*1973u+pixel.y*9277u+salt*26699u+911u)&0x00ffffffu)*(1.0/16777216.0);
            }
            float SourceSmithG1(float cosine,float alpha2)
            {
                return 2*cosine/max(cosine+sqrt(alpha2+(1-alpha2)*cosine*cosine),1e-6);
            }
            float3 ProbeDirection(float3 direction,float3 position)
            {
                if(_ProbePosition.w<.5) return direction;
                float3 safeDirection=lerp(-1,1,step(0,direction))*max(abs(direction),1e-5);
                float3 bounds=lerp(_ProbeBoxMin.xyz,_ProbeBoxMax.xyz,step(0,direction));
                float3 distances=(bounds-position)/safeDirection;
                float distance=min(distances.x,min(distances.y,distances.z));
                return position+direction*max(distance,0)-_ProbePosition.xyz;
            }
            float3 SampleSourceRadiance(float3 direction)
            {
                direction=normalize(direction);
                float2 uv=float2(.5+atan2(direction.x,direction.z)/6.28318530718,.5+asin(clamp(direction.y,-1,1))/3.14159265359);
                return SAMPLE_TEXTURE2D_LOD(_RadianceMap,sampler_RadianceMap,uv,0).rgb;
            }
            float3 SpecularRadiance(float3 normal,float3 view,float3 f0,float roughness,uint2 pixel,float3 position,uint seed)
            {
                // GGX half-vector importance sampling with the actual surface normal
                // and view. PDF cancellation gives F*G*VoH/(NoV*NoH). Bright specks
                // come from sampled source HDR radiance, not brightness modulation.
                float alpha=max(roughness*roughness,.001);
                float a2=alpha*alpha;
                if(dot(normal,view)<=0) return 0;
                float noV=max(dot(normal,view),1e-4);
                float3 up=abs(normal.z)<.999 ? float3(0,0,1) : float3(1,0,0);
                float3 tangent=normalize(cross(up,normal));
                float3 bitangent=cross(normal,tangent);
                float3 total=0;
                uint count=(uint)clamp(_SpecularSamples,1,16);
                [loop] for(uint s=0;s<count;s++)
                {
                    float u=SourceRandom(pixel,s*2+seed);
                    float phi=6.28318530718*SourceRandom(pixel,s*2+seed+1);
                    float cosTheta=sqrt((1-u)/(1+(a2-1)*u));
                    float sinTheta=sqrt(saturate(1-cosTheta*cosTheta));
                    float3 h=tangent*(cos(phi)*sinTheta)+bitangent*(sin(phi)*sinTheta)+normal*cosTheta;
                    float voH=dot(view,h);
                    float3 direction=reflect(-view,h);
                    float noL=dot(normal,direction);
                    if(noL>0 && voH>0)
                    {
                        float3 fresnel=f0+(1-f0)*pow(1-saturate(voH),5);
                        float weight=SourceSmithG1(noV,a2)*SourceSmithG1(noL,a2)*voH/max(noV*cosTheta,1e-6);
                        total+=SampleSourceRadiance(ProbeDirection(direction,position))*fresnel*weight;
                    }
                }
                return total/count;
            }
            half4 Frag(Varyings i):SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(i);
                half3 albedo=SAMPLE_TEXTURE2D(_BaseMap,sampler_BaseMap,i.uv).rgb;
                half3 mask=SAMPLE_TEXTURE2D(_SurfaceMap,sampler_SurfaceMap,i.uv).rgb;
                float3 blenderNormal=SAMPLE_TEXTURE2D(_WorldNormalMap,sampler_WorldNormalMap,i.uv).xyz*2-1;
                half3 n=normalize(blenderNormal.xzy);
                half3 view=GetWorldSpaceNormalizeViewDir(i.positionWS);
                half3 irradiance=SAMPLE_TEXTURE2D(_IrradianceMap,sampler_IrradianceMap,i.uv).rgb;
                SurfaceData surface=(SurfaceData)0;
                surface.albedo=albedo; surface.metallic=mask.r;
                surface.smoothness=1-mask.g; surface.occlusion=1; surface.alpha=1;
                surface.clearCoatMask=mask.b; surface.clearCoatSmoothness=1-_CoatRoughness;
                BRDFData brdf;
                InitializeBRDFData(surface,brdf);
                float dielectricF0=pow((_SourceIOR-1)/max(_SourceIOR+1,.001),2)*2*_SpecularIORLevel;
                brdf.specular=lerp(dielectricF0,albedo,mask.r);
                brdf.diffuse=albedo*((1-mask.r)*(1-dielectricF0));
                BRDFData coat=CreateClearCoatBRDFData(surface,brdf);
                half3 color=irradiance*brdf.diffuse+SpecularRadiance(n,view,brdf.specular,mask.g,(uint2)i.positionCS.xy,i.positionWS,17);
                UNITY_BRANCH if(mask.b>0)
                {
                    float3 coatNormal=normalize(i.normalWS);
                    float coatFresnel=.04+.96*pow(1-saturate(dot(coatNormal,view)),5);
                    color=color*(1-coatFresnel*mask.b)+mask.b*SpecularRadiance(coatNormal,view,.04,_CoatRoughness,(uint2)i.positionCS.xy,i.positionWS,101);
                }
                color+=_Emission.rgb;
                return half4(color,1);
            }
            ENDHLSL
        }
    }
    Fallback "Hidden/Universal Render Pipeline/FallbackError"
}
