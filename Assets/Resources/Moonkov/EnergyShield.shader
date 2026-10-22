Shader "Moonkov/Energy Shield and Head Seal"
{
    Properties
    {
        [HideInInspector] _ZWrite("Depth writes",Float)=0
        [HideInInspector] _FieldState("Mode / deploy / impact age / break age",Vector)=(0,1.12,100,100)
        [HideInInspector] _FieldFlow("Regenerating / return age / impact strength",Vector)=(0,100,0,0)
        [HideInInspector] _EnergyHitPoint("Authoritative world impact",Vector)=(0,0,0,0)
        [HideInInspector] _OwnerPulse("Owner edge response",Vector)=(0,0,0,0)
        [HideInInspector] _SealTint("Halo color / breathing light",Color)=(1,.28,.52,1)
        [HideInInspector] _SealNeck("Neck seal world center",Vector)=(0,0,0,0)
        [HideInInspector] _SealRight("Neck seal world right radius",Vector)=(0,0,0,0)
        [HideInInspector] _SealForward("Neck seal world forward radius",Vector)=(0,0,0,0)
        [HideInInspector] _SealShape("Spherical neck cut latitude",Vector)=(.55,0,0,0)
    }
    SubShader
    {
        Tags {"RenderPipeline"="UniversalPipeline" "Queue"="Transparent+5" "RenderType"="Transparent"}
        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
        TEXTURE2D_X(_EnergySceneColor);TEXTURE2D_X_FLOAT(_EnergySceneDepth);
        float4 _EnergyColorScaleBias,_EnergyDepthScaleBias;
        CBUFFER_START(UnityPerMaterial)
        float4 _FieldState,_FieldFlow,_EnergyHitPoint,_OwnerPulse;
        float4 _SealTint,_SealNeck,_SealRight,_SealForward,_SealShape;
        CBUFFER_END
        float FieldNoise(float3 p){return frac(sin(dot(floor(p),float3(17.13,97.47,53.71)))*43758.5453);}
        float FieldEye(float raw){return IsPerspectiveProjection()?LinearEyeDepth(raw,_ZBufferParams):LinearDepthToEyeDepth(raw);}
        ENDHLSL
        Pass
        {
            Name "Energy surface"
            Tags {"LightMode"="MoonkovEnergyShield"}
            ZWrite Off ZTest Always Cull Back
            Blend SrcAlpha OneMinusSrcAlpha
            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex FieldVertex
            #pragma fragment FieldFragment
            #pragma multi_compile_instancing
            struct FieldAttributes {float4 positionOS:POSITION;float3 normalOS:NORMAL;float2 uv:TEXCOORD0;UNITY_VERTEX_INPUT_INSTANCE_ID};
            struct FieldVaryings {float4 positionCS:SV_POSITION;float3 positionWS:TEXCOORD0;float3 normalWS:TEXCOORD1;float2 uv:TEXCOORD2;float height:TEXCOORD3;UNITY_VERTEX_OUTPUT_STEREO};
            FieldVaryings FieldVertex(FieldAttributes input)
            {
                FieldVaryings output;UNITY_SETUP_INSTANCE_ID(input);UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);
                output.positionWS=TransformObjectToWorld(input.positionOS.xyz);
                if(_FieldState.x>-.5&&_FieldState.x<.5)
                {
                    float angle=input.uv.x*TWO_PI;
                    float latitude=lerp(-_SealShape.x,1,input.uv.y);
                    float radius=sqrt(saturate(1-latitude*latitude));
                    float3 sphere=float3(sin(angle)*radius,input.uv.y,cos(angle)*radius);
                    output.positionWS=TransformObjectToWorld(sphere);
                    float3 neck=_SealNeck.xyz+sin(angle)*_SealRight.xyz+cos(angle)*_SealForward.xyz;
                    output.positionWS=lerp(neck,output.positionWS,smoothstep(0,.015,input.uv.y));
                    output.normalWS=TransformObjectToWorldNormal(float3(sin(angle)*radius,latitude*(1+_SealShape.x),cos(angle)*radius));
                }
                else output.normalWS=TransformObjectToWorldNormal(input.normalOS);
                output.positionCS=TransformWorldToHClip(output.positionWS);
                output.uv=input.uv;output.height=_FieldState.x<.5?input.uv.y:input.positionOS.y*.5+.5;return output;
            }
            half4 FieldFragment(FieldVaryings input):SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                float2 screen=GetNormalizedScreenSpaceUV(input.positionCS);
                float2 depthUV=screen*_EnergyDepthScaleBias.xy+_EnergyDepthScaleBias.zw;
                float scene=FieldEye(SAMPLE_TEXTURE2D_X(_EnergySceneDepth,sampler_PointClamp,depthUV).r);
                float surface=FieldEye(input.positionCS.z),distanceToScene=scene-surface;
                // Always test real scene depth: these effects must not expose actors through cover.
                clip(distanceToScene+max(.0005,scene*.00001));
                float intersection=saturate(distanceToScene/.035);
                float3 normal=normalize(input.normalWS),view=GetWorldSpaceNormalizeViewDir(input.positionWS);
                float rim=pow(saturate(1-abs(dot(normal,view))),3.6);
                float deploy=1-smoothstep(_FieldState.y-.055,_FieldState.y+.055,input.height);
                float t=_Time.y;
                float weave=pow(saturate(sin(input.uv.x*150.8+sin(input.uv.y*35+t*.5)*.3)),28)*.018;
                float3 cyan=float3(.12,.64,.94);float alpha;float3 color;
                if(_FieldState.x<.5)
                {
                    float border=exp(-input.uv.y*190);
                    float opening=exp(-abs(input.height-_FieldState.y)*110)*step(_FieldState.y,1.02);
                    float coating=pow(saturate(dot(normal,normalize(float3(-.6,.8,.4)))),24)*.055;
                    float2 distortion=float2(dot(normal,UNITY_MATRIX_V[0].xyz),dot(normal,UNITY_MATRIX_V[1].xyz))*.004*(.25+rim*.75);
                    float2 colorUV=(screen+distortion)*_EnergyColorScaleBias.xy+_EnergyColorScaleBias.zw;
                    float3 refracted=SAMPLE_TEXTURE2D_X(_EnergySceneColor,sampler_LinearClamp,colorUV).rgb;
                    float glow=.2+.8*_SealTint.a;
                    float glassRim=rim*rim;
                    float collarLink=1-smoothstep(.002,.02,input.uv.y);
                    alpha=min(.6,.008+collarLink*.045+glassRim*.22+border*.48*glow+opening*.5+coating*.35)*deploy;
                    color=lerp(refracted*.995+_SealTint.rgb*.003,_SealTint.rgb*(1.4+glow)+.12,saturate(collarLink*.2+border*.8+opening*.75+glassRim*.9));
                    color+=_SealTint.rgb*coating*glow;
                    if(_FieldState.x<-.5)
                    {
                        float outline=exp(-input.uv.y*35);
                        float inner=smoothstep(.18,.32,input.uv.y)*(1-smoothstep(.76,.98,input.uv.y));
                        color=lerp(_SealTint.rgb,1,outline*.12);
                        alpha=.008+outline*(.10+.84*_SealTint.a)+inner*(.02+.23*_SealTint.a)+glassRim*.08*glow;
                    }
                }
                else
                {
                    float hit=max(0,1-_FieldState.z/.48),broken=max(0,1-_FieldState.w/.62),returned=max(0,1-_FieldFlow.y/.6);
                    float radius=distance(input.positionWS,_EnergyHitPoint.xyz);
                    float ripple=exp(-abs(radius-_FieldState.z*3.5)*19)*exp(-_FieldState.z*5)*_FieldFlow.z;
                    float recoveryBand=pow(saturate(1-abs(frac(input.height-t*.22)-.5)*2),38)*_FieldFlow.x;
                    float fragments=step(1-broken,FieldNoise(input.positionWS*18+t*.4));
                    alpha=rim*(hit*.34+broken*.35+returned*.12)+ripple*.55+recoveryBand*.075;
                    alpha*=lerp(1,fragments,broken*.8);
                    color=lerp(cyan,float3(.5,.44,.96),broken*.45)+ripple*.18;
                    alpha=min(.48,alpha)*deploy;
                }
                return half4(min(color,_FieldState.x<.5?4:1.15),saturate(alpha)*intersection);
            }
            ENDHLSL
        }
        Pass
        {
            Name "Owner edge impulse"
            Tags {"LightMode"="MoonkovEnergyShieldOwner"}
            ZWrite Off ZTest Always Cull Off
            Blend SrcAlpha OneMinusSrcAlpha
            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex Vert
            #pragma fragment OwnerFragment
            #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"
            half4 OwnerFragment(Varyings input):SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                float2 q=(input.texcoord-.5)*float2(1.25,1);
                // The central sight/face region is exactly clear; HUD is rendered afterwards.
                float edge=smoothstep(.44,.76,length(q));
                return half4(.13,.58,.92,min(.17,_OwnerPulse.x)*edge);
            }
            ENDHLSL
        }
        Pass
        {
            Name "Owner equipped visor"
            Tags {"LightMode"="MoonkovEnergyShieldVisor"}
            ZWrite Off ZTest Always Cull Off
            Blend SrcAlpha OneMinusSrcAlpha
            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex Vert
            #pragma fragment VisorFragment
            #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"
            half4 VisorFragment(Varyings input):SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                float2 uv=input.texcoord;
                float2 q=(uv-.5)*float2(1.25,1);
                float radius=length(q);
                // Stable curved optics, no animated wobble. Center samples the exact
                // original scene; distortion, dispersion and coating grow at the periphery.
                float edge=smoothstep(.30,.72,radius),curve=edge*edge;
                float2 direction=normalize(q+float2(.00001,0));
                float2 warped=uv+direction*.006*curve;
                float2 dispersion=direction*.0009*curve;
                float2 centerUV=saturate(warped)*_EnergyColorScaleBias.xy+_EnergyColorScaleBias.zw;
                float3 scene=SAMPLE_TEXTURE2D_X(_EnergySceneColor,sampler_LinearClamp,centerUV).rgb;
                scene.r=SAMPLE_TEXTURE2D_X(_EnergySceneColor,sampler_LinearClamp,saturate(warped+dispersion)*_EnergyColorScaleBias.xy+_EnergyColorScaleBias.zw).r;
                scene.b=SAMPLE_TEXTURE2D_X(_EnergySceneColor,sampler_LinearClamp,saturate(warped-dispersion)*_EnergyColorScaleBias.xy+_EnergyColorScaleBias.zw).b;
                float seal=exp(-abs(radius-.692)*145)*edge;
                float coating=pow(saturate(dot(direction,normalize(float2(-.7,.9)))),12)*curve;
                float sockets=exp(-length((uv-float2(.075,.08))*float2(1,1.6))*55)+exp(-length((uv-float2(.925,.08))*float2(1,1.6))*55);
                float3 glass=scene*(1-edge*.012)+float3(.002,.004,.008)*edge;
                glass+=_SealTint.rgb*(coating*.018+seal*.035+sockets*.025*_SealTint.a);
                // The full replacement preserves actual refraction amplitude. A small
                // alpha overlay would dilute it into an almost invisible tinted double image.
                return half4(glass,saturate(_OwnerPulse.y));
            }
            ENDHLSL
        }
    }
}
