TEXTURE2D(_SourceTex0);
TEXTURE2D(_SourceTex1);
TEXTURE2D(_SourceTex2);
TEXTURE2D(_SourceTex3);
SAMPLER(sampler_SourceTex0);
SAMPLER(sampler_SourceTex2);
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
float3 v8=float3(c.uv0,0);
float3 v9=(v8*2.0);
float4 v10=SAMPLE_TEXTURE2D(_SourceTex2,sampler_SourceTex2,(v9).xy);
float v11=(SourceLuminance((v10).xyz)-0.5);
float v12=(v11*0.0700000003);
float v13=saturate((v7+v12));
b0.roughness=v13;
b0.ior=1.5;
b0.specular=0.5;
b0.coat=0.0;
b0.coatRoughness=0.0299999993;
b0.transmission=0.0;
b0.alpha=1.0;
b0.anisotropy=0.0;
b0.anisotropicRotation=0.0;
float4 v23=SAMPLE_TEXTURE2D(_SourceTex3,sampler_SourceTex2,(v9).xy);
float3 v24=SourceTangentNormal(c.normal,(v23).rgb,0.239999995,c.position,c.uv0,c.tangent);
b0.normal=v24;
b0.emission=(float4(1.0,1.0,1.0,1.0)).xyz*0.0;
a=b0; b=b0; factor=0; a.alpha*=1; b.alpha*=1;
}
