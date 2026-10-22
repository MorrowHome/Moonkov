Shader "Moonkov/Energy Shield and Head Seal"
{
    Properties
    {
        [HideInInspector] _ZWrite("Depth writes",Float)=0
        [HideInInspector] _FieldState("Mode / deploy / impact age / break age",Vector)=(0,1.12,100,100)
        [HideInInspector] _FieldFlow("Regenerating / return age / impact strength",Vector)=(0,100,0,0)
        [HideInInspector] _EnergyHitPoint("Authoritative world impact",Vector)=(0,0,0,0)
        [HideInInspector] _OwnerPulse("Owner edge response",Vector)=(0,0,0,0)
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
                output.positionWS=TransformObjectToWorld(input.positionOS.xyz);output.positionCS=TransformWorldToHClip(output.positionWS);
                output.normalWS=TransformObjectToWorldNormal(input.normalOS);output.uv=input.uv;output.height=input.positionOS.y*.5+.5;return output;
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
                    float scan=pow(saturate(1-abs(frac(input.height*.6-t*.065)-.5)*2),26)*.025;
                    float2 distortion=float2(dot(normal,UNITY_MATRIX_V[0].xyz),dot(normal,UNITY_MATRIX_V[1].xyz))*.0018*(.5+rim*.5);
                    float2 colorUV=(screen+distortion)*_EnergyColorScaleBias.xy+_EnergyColorScaleBias.zw;
                    float3 refracted=SAMPLE_TEXTURE2D_X(_EnergySceneColor,sampler_LinearClamp,colorUV).rgb;
                    alpha=min(.28,.018+rim*.23+weave+scan)*deploy;
                    color=lerp(refracted,cyan,saturate(rim*.65+scan*3));
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
                return half4(min(color,1.15),saturate(alpha)*intersection);
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
    }
}
