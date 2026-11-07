Shader "Hidden/Moonkov/SquadGlass"
{
    Properties { _MainTex ("Squad frame", 2D) = "black" {} }
    SubShader
    {
        Cull Off
        ZWrite Off
        ZTest Always

        Pass
        {
            HLSLPROGRAM
            #pragma vertex vert_img
            #pragma fragment Frost
            #include "UnityCG.cginc"

            sampler2D _MainTex;
            float2 _BlurStep;
            half _ApplyTint;

            half4 Frost(v2f_img input) : SV_Target
            {
                float2 uv = input.uv;
                float2 nearTap = _BlurStep * 1.384615;
                float2 farTap = _BlurStep * 3.230769;
                half3 color = tex2D(_MainTex, uv).rgb * 0.227027;
                color += (tex2D(_MainTex, uv - nearTap).rgb + tex2D(_MainTex, uv + nearTap).rgb) * 0.316216;
                color += (tex2D(_MainTex, uv - farTap).rgb + tex2D(_MainTex, uv + farTap).rgb) * 0.070270;
                half3 tinted = color * half3(.37, .42, .49) / (1 + 2.5 * color) + half3(.008, .011, .017);
                return half4(lerp(color, tinted, _ApplyTint), 1);
            }
            ENDHLSL
        }
    }
}
