Shader "Moonkov/Halo Light Glare"
{
    Properties { _Glare("Source XY, aspect, strength",Vector)=(.5,.5,1.5,0) }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" }
        Pass
        {
            Name "HaloLightGlare"
            ZWrite Off ZTest Always Cull Off
            Blend SrcAlpha OneMinusSrcAlpha
            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"
            float4 _Glare;
            half4 Frag(Varyings input):SV_Target
            {
                float2 delta=input.texcoord-_Glare.xy;
                delta.x*=max(.25,_Glare.z);
                float radius=dot(delta,delta);
                float bloom=exp(-radius*9);
                float core=exp(-radius*180);
                float streak=exp(-abs(delta.y)*150)*exp(-abs(delta.x)*3);
                float alpha=saturate(_Glare.w)*(.18+.48*bloom+.24*core+.12*streak);
                return half4(1,.97,.9,saturate(alpha));
            }
            ENDHLSL
        }
    }
}
