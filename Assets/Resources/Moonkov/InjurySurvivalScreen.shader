Shader "Moonkov/Injury and Survival Lens"
{
    Properties
    {
        _VeinMask("Original vessels / blood beads",2D)="black"{}
        _VitalState("Critical / hunger / thirst / impact",Vector)=(0,0,0,0)
        _WoundState("Blood / variation / visibility / strength",Vector)=(0,0,1,1)
        _FeedbackTime("Unscaled presentation time",Float)=0
    }
    SubShader
    {
        Tags {"RenderPipeline"="UniversalPipeline"}
        Pass
        {
            Name "InjurySurvival"
            ZWrite Off ZTest Always Cull Off
            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"
            TEXTURE2D(_VeinMask);SAMPLER(sampler_VeinMask);
            float4 _VitalState,_WoundState;float _FeedbackTime;
            half4 Frag(Varyings input):SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                float2 uv=input.texcoord,q=(uv-.5)*2;
                float edge=smoothstep(.18,1,max(abs(q.x),abs(q.y)));
                float critical=saturate(_VitalState.x),hunger=saturate(_VitalState.y),thirst=saturate(_VitalState.z),impact=saturate(_VitalState.w);
                float t=_FeedbackTime,phase=frac(t*(.92+critical*.45));
                float beat=exp(-phase*phase*180)+.45*exp(-pow(phase-.19,2)*240);
                float2 warped=uv;
                warped.x+=sin(uv.y*48+t*1.3)*.0013*thirst*edge;
                float3 original=SAMPLE_TEXTURE2D_X(_BlitTexture,sampler_LinearClamp,uv).rgb;
                float3 scene=SAMPLE_TEXTURE2D_X(_BlitTexture,sampler_LinearClamp,saturate(warped)).rgb;
                if(thirst>.01)
                {
                    float2 offset=q*.0015*thirst*edge;
                    scene.r=SAMPLE_TEXTURE2D_X(_BlitTexture,sampler_LinearClamp,saturate(warped+offset)).r;
                    scene.b=SAMPLE_TEXTURE2D_X(_BlitTexture,sampler_LinearClamp,saturate(warped-offset)).b;
                }
                float luma=dot(scene,float3(.2126,.7152,.0722));
                scene=lerp(scene,luma*float3(1.04,.9,.72),hunger*.42);
                float faint=.72+.28*sin(t*2.1);
                scene*=1-hunger*(.035+edge*edge*.55*faint);
                scene=lerp(scene,luma*float3(1.07,1.025,.91),thirst*.32);
                scene=scene*(1-thirst*edge*.2)+thirst*edge*.025;
                // Systemic exhaustion gets its own palette instead of repeated gunshot blood flashes.
                float red=critical*(1-max(hunger,thirst)*.9);
                float pulse=.78+.22*beat;
                float tint=red*(.13+edge*edge*.58)*pulse+impact*(.08+edge*.42);
                scene=lerp(scene,scene*float3(.48,.035,.045)+float3(.14,.002,.006),saturate(tint));
                float2 mask=SAMPLE_TEXTURE2D(_VeinMask,sampler_VeinMask,uv).rg;
                float vessel=mask.r*critical*(1-max(hunger,thirst))*smoothstep(.14,.72,edge)*(.65+.35*pulse);
                float2 beadsUV=saturate(uv+float2(sin(_WoundState.y*19),cos(_WoundState.y*23))*.025);
                float beads=SAMPLE_TEXTURE2D(_VeinMask,sampler_VeinMask,beadsUV).g;
                float blood=beads*saturate(_WoundState.x)*smoothstep(.06,.65,edge);
                scene=lerp(scene,float3(.055,.0015,.005),saturate(vessel*.9+blood*.85));
                scene+=float3(.012,.001,.001)*blood*(1-beads);
                float blackening=critical*max(hunger,thirst)*edge*edge*.22*(.6+.4*faint);
                scene*=1-blackening;
                float strength=saturate(_WoundState.z*_WoundState.w);
                return half4(lerp(original,max(scene,0),strength),1);
            }
            ENDHLSL
        }
    }
}
