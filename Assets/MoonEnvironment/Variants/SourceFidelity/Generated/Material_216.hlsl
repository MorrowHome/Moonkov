TEXTURE2D(_SourceTex0);
TEXTURE2D(_SourceTex1);
TEXTURE2D(_SourceTex2);
TEXTURE2D(_SourceTex3);
SAMPLER(sampler_SourceTex0);
SAMPLER(sampler_SourceTex3);
void EvaluateSourceMaterial(SourceContext c,out SourceClosure a,out SourceClosure b,out float factor)
{
SourceClosure b0=SourceDefaultClosure(c);
float3 v1=float3(c.uv1,0);
float4 v2=SAMPLE_TEXTURE2D(_SourceTex0,sampler_SourceTex0,(v1).xy);
b0.baseColor=(v2).xyz;
float4 v4=SAMPLE_TEXTURE2D(_SourceTex1,sampler_SourceTex0,(v1).xy);
float v5=(v4).r;
b0.metallic=v5;
float v7=(v4).g;
b0.roughness=v7;
b0.ior=1.5;
b0.specular=0.5;
b0.coat=0.0;
b0.coatRoughness=0.0299999993;
b0.transmission=0.0;
b0.alpha=1.0;
b0.anisotropy=0.0;
b0.anisotropicRotation=0.0;
float4 v17=SAMPLE_TEXTURE2D(_SourceTex2,sampler_SourceTex0,(v1).xy);
float3 v18=float3(c.uv2,0);
float4 v19=SAMPLE_TEXTURE2D(_SourceTex3,sampler_SourceTex3,(v18).xy);
float4 v20=lerp(float4(0.5,0.5,1.0,1.0),v19,saturate(0.699999988));
float3 v21=((v17).xyz+(v20).xyz);
float3 v22=(v21-float3(0.5,0.5,1.0));
float3 v23=SourceTangentNormal(c.normal,(float4(v22,1)).rgb,1.0,c.position,c.uv1,c.tangent);
b0.normal=v23;
b0.emission=(float4(1.0,1.0,1.0,1.0)).xyz*0.0;
a=b0; b=b0; factor=0; a.alpha*=1; b.alpha*=1;
}
