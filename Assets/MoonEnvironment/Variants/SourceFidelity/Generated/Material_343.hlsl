TEXTURE2D(_SourceTex0);
TEXTURE2D(_SourceTex1);
TEXTURE2D(_SourceTex2);
SAMPLER(sampler_SourceTex0);
void EvaluateSourceMaterial(SourceContext c,out SourceClosure a,out SourceClosure b,out float factor)
{
SourceClosure b0=SourceDefaultClosure(c);
float3 v1=float3(c.uv1,0);
float4 v2=SAMPLE_TEXTURE2D(_SourceTex0,sampler_SourceTex0,(v1).xy);
b0.baseColor=(v2).xyz;
float4 v4=SAMPLE_TEXTURE2D(_SourceTex1,sampler_SourceTex0,(v1).xy);
float v5=(v4).r;
b0.metallic=v5;
float v7=(SAMPLE_TEXTURE2D(_SourceTex1,sampler_SourceTex0,(v1).xy)).a;
float v8=(1.0-v7);
b0.roughness=v8;
b0.ior=1.5;
b0.specular=0.5;
b0.coat=0.0;
b0.coatRoughness=0.0299999993;
b0.transmission=0.0;
b0.alpha=1.0;
b0.anisotropy=0.0;
b0.anisotropicRotation=0.0;
float4 v18=SAMPLE_TEXTURE2D(_SourceTex2,sampler_SourceTex0,(v1).xy);
float3 v19=SourceTangentNormal(c.normal,(v18).rgb,1.0,c.position,c.uv1,c.tangent);
b0.normal=v19;
b0.emission=(float4(1.0,1.0,1.0,1.0)).xyz*0.0;
a=b0; b=b0; factor=0; a.alpha*=1; b.alpha*=1;
}
