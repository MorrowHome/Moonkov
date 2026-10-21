Shader "Moonkov/TacticalVolume"
{
    Properties
    {
        _BaseColor("Tint",Color)=(1,1,1,.4)
        _BaseMap("Existing Particle Pack texture",2D)="white"{}
        _UseTexture("Use existing texture",Float)=0
        _Flame("Procedural fallback flame",Float)=0
        _SoftDistance("Ground intersection fade",Float)=0
        [HideInInspector] _ZWrite("Depth writes",Float)=0
    }
    SubShader
    {
        Tags {"Queue"="Transparent" "RenderType"="Transparent" "RenderPipeline"="UniversalPipeline"}
        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
        TEXTURE2D_X_FLOAT(_MoonkovTacticalSceneDepth);
        float4 _MoonkovTacticalDepthScaleBias;
        TEXTURE2D(_BaseMap);SAMPLER(sampler_BaseMap);
        CBUFFER_START(UnityPerMaterial)
        half4 _BaseColor;
        float _Flame,_UseTexture,_SoftDistance;
        CBUFFER_END
        struct Attributes {float4 positionOS:POSITION;float2 uv:TEXCOORD0;half4 color:COLOR;UNITY_VERTEX_INPUT_INSTANCE_ID};
        struct Varyings {float4 positionCS:SV_POSITION;float2 uv:TEXCOORD0;half4 color:COLOR;UNITY_VERTEX_OUTPUT_STEREO};
        Varyings Vert(Attributes input)
        {
            Varyings output;UNITY_SETUP_INSTANCE_ID(input);UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);
            output.positionCS=TransformObjectToHClip(input.positionOS.xyz);output.uv=input.uv;output.color=input.color;return output;
        }
        half4 ParticleColor(Varyings input)
        {
            if(_UseTexture>.5)return SAMPLE_TEXTURE2D(_BaseMap,sampler_BaseMap,input.uv)*_BaseColor*input.color;
            float2 q=input.uv*2-1;
            float noise=sin(q.x*11+q.y*7+_Time.y*2)*sin(q.y*9-q.x*5-_Time.y*1.6);
            float radial=dot(q,q);
            float soft=smoothstep(1,.1,radial+.12*noise);
            half3 color=_BaseColor.rgb*input.color.rgb;
            if(_Flame>.5)
            {
                float width=max(.08,1-input.uv.y*.85);
                q.x+=(sin(input.uv.y*9+_Time.y*7)*.1)*input.uv.y;
                soft=smoothstep(1,.2,abs(q.x)/width)*smoothstep(1,.15,input.uv.y)*smoothstep(0,.15,input.uv.y);
                soft*=saturate(1-radial*.3+noise*.35);
                color=lerp(half3(2.1,.95,.18),half3(.95,.12,.015),input.uv.y);
            }
            return half4(color,soft*_BaseColor.a*input.color.a);
        }
        ENDHLSL
        Pass
        {
            Tags {"LightMode"="MoonkovTacticalTransparent"}
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off ZTest Always Cull Off
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_instancing
            half4 Frag(Varyings input):SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);half4 color=ParticleColor(input);
                // Always depth-test, including materials with no optional soft fade.
                float2 uv=GetNormalizedScreenSpaceUV(input.positionCS)*_MoonkovTacticalDepthScaleBias.xy+_MoonkovTacticalDepthScaleBias.zw;
                float raw=SAMPLE_TEXTURE2D_X(_MoonkovTacticalSceneDepth,sampler_PointClamp,uv).r;
                float sceneEye=IsPerspectiveProjection()?LinearEyeDepth(raw,_ZBufferParams):LinearDepthToEyeDepth(raw);
                float particleEye=IsPerspectiveProjection()?LinearEyeDepth(input.positionCS.z,_ZBufferParams):LinearDepthToEyeDepth(input.positionCS.z);
                float distanceToScene=sceneEye-particleEye;
                clip(distanceToScene+max(.0005,sceneEye*.00001));
                if(_SoftDistance>.001)color.a*=saturate(distanceToScene/_SoftDistance);
                return color;
            }
            ENDHLSL
        }
    }
}
