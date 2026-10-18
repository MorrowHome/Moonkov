Shader "Moonkov/Alien Mucus Lens"
{
    Properties { _Remaining("Remaining seconds",Float)=0 _Elapsed("Elapsed seconds",Float)=0 }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" }
        Pass
        {
            Name "MucusLens"
            ZWrite Off ZTest Always Cull Off
            Blend SrcAlpha OneMinusSrcAlpha
            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"
            float _Remaining,_Elapsed;
            float Blob(float2 uv,float2 centre,float2 radius,float phase)
            {
                centre.y-=_Elapsed*.014;
                float2 p=(uv-centre)/radius;
                float a=atan2(p.y,p.x);
                float edge=length(p)+.09*sin(a*5+phase)+.035*sin(a*11-phase);
                return 1-smoothstep(.75,1.02,edge);
            }
            half4 Frag(Varyings input):SV_Target
            {
                float2 uv=input.texcoord;
                float mask=Blob(uv,float2(.24,.59),float2(.25,.35),.7);
                mask=max(mask,Blob(uv,float2(.77,.26),float2(.25,.24),2.4));
                mask=max(mask,Blob(uv,float2(.55,.83),float2(.28,.15),1.6));
                mask=max(mask,Blob(uv,float2(.48,.47),float2(.13,.12),3.1));
                float drip=1-smoothstep(.012,.025,abs(uv.x-(.22+.012*sin(uv.y*18))));
                drip*=smoothstep(.05,.16,uv.y)*(1-smoothstep(.48,.58,uv.y));
                mask=max(mask,drip*.85);
                float rim=smoothstep(.06,.25,mask)*(1-smoothstep(.35,.7,mask));
                // Thin wet ridges catch light even against a dark interior.
                float ridge=pow(saturate(1-abs(mask-.42)*8),3);
                float streak=pow(saturate(sin(uv.x*125+sin(uv.y*24)*2)),24);
                float glint=ridge*saturate(sin(uv.x*32+uv.y*17)+.15);
                float fade=smoothstep(0,.75,max(0,_Remaining));
                half3 colour=half3(.006,.012,.011)+rim*half3(.075,.12,.10);
                colour+=glint*half3(.27,.38,.31)+streak*mask*.018;
                return half4(colour,saturate(mask)*.9*fade);
            }
            ENDHLSL
        }
    }
}
