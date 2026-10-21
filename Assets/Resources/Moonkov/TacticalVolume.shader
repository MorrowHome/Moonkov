Shader "Moonkov/TacticalVolume"
{
    Properties
    {
        _BaseColor("Tint",Color)=(1,1,1,.4)
        _Flame("Flame",Float)=0
    }
    SubShader
    {
        Tags {"Queue"="Transparent" "RenderType"="Transparent" "RenderPipeline"="UniversalPipeline"}
        Pass
        {
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            Cull Off
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            CBUFFER_START(UnityPerMaterial)
            half4 _BaseColor;
            float _Flame;
            CBUFFER_END
            struct Attributes {float4 positionOS:POSITION;float2 uv:TEXCOORD0;half4 color:COLOR;};
            struct Varyings {float4 positionCS:SV_POSITION;float2 uv:TEXCOORD0;half4 color:COLOR;};
            Varyings Vert(Attributes input)
            {Varyings output;output.positionCS=TransformObjectToHClip(input.positionOS.xyz);output.uv=input.uv;output.color=input.color;return output;}
            half4 Frag(Varyings input):SV_Target
            {
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
        }
    }
}
