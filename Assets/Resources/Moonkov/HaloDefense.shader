Shader "Moonkov/Halo Defense"
{
    Properties
    {
        _MainTex ("Formal halo artwork", 2D) = "white" {}
        [HDR] _HaloColor ("Halo color", Color) = (1,0.18,0.42,1)
        _HaloState ("Deployment energy impact owner", Vector) = (1,1,10,0)
        _HaloHit ("Impact point", Vector) = (0,0,0,0)
    }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "Queue"="Transparent+10" "RenderType"="Transparent" }
        Pass
        {
            Tags { "LightMode"="SRPDefaultUnlit" }
            Blend SrcAlpha One
            ZWrite Off
            Cull Off
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            TEXTURE2D(_MainTex); SAMPLER(sampler_MainTex);
            CBUFFER_START(UnityPerMaterial)
                float4 _HaloColor, _HaloState, _HaloHit, _MainTex_TexelSize;
            CBUFFER_END
            struct Attributes { float4 positionOS : POSITION; float2 uv : TEXCOORD0; float4 color : COLOR; };
            struct Varyings { float4 positionCS : SV_POSITION; float2 uv : TEXCOORD0; float3 world : TEXCOORD1; float3 normal : TEXCOORD2; float4 color : COLOR; };
            Varyings Vert(Attributes v)
            {
                Varyings o;
                o.world = TransformObjectToWorld(v.positionOS.xyz);
                o.positionCS = TransformWorldToHClip(o.world);
                o.normal = TransformObjectToWorldNormal(normalize(v.positionOS.xyz));
                o.uv = v.uv; o.color = v.color;
                return o;
            }
            half4 Frag(Varyings i) : SV_Target
            {
                half4 art = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, i.uv);
                float deployment = smoothstep(i.color.r * .35, .65 + i.color.r * .35, _HaloState.x);
                float facing = dot(normalize(i.normal), normalize(GetWorldSpaceViewDir(i.world)));
                float rim = 1 - abs(facing);
                float2 texel = max(_MainTex_TexelSize.xy * 1.5, fwidth(i.uv));
                half nearAlpha = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, i.uv + float2(texel.x,0)).a;
                nearAlpha = min(nearAlpha, SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, i.uv - float2(texel.x,0)).a);
                nearAlpha = min(nearAlpha, SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, i.uv + float2(0,texel.y)).a);
                nearAlpha = min(nearAlpha, SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, i.uv - float2(0,texel.y)).a);
                float edge = saturate((art.a - nearAlpha) * 2.5);
                float distanceToHit = distance(i.world, _HaloHit.xyz);
                float wave = exp(-abs(distanceToHit - _HaloState.z * 3) * 12) * saturate(1 - _HaloState.z * 2);
                float highlight = exp(-distanceToHit * 5) * saturate(1 - _HaloState.z * 3);
                float opacity = lerp(.19 + rim * .18, .015 + pow(rim, 3) * .32, _HaloState.w);
                float backFade = lerp(lerp(.28, 1, step(0, facing)), 1, _HaloState.w);
                half alpha = deployment * backFade * (art.a * .18 + edge * .85) * (opacity + wave * .55 + highlight * .6);
                clip(alpha - .003);
                half3 color = lerp(_HaloColor.rgb, half3(1,.9,.96), saturate(wave + highlight));
                return half4(color * (1.5 + edge * 1.8 + _HaloState.y * .45 + wave * 2), alpha);
            }
            ENDHLSL
        }
    }
}
