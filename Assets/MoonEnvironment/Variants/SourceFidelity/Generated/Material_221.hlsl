TEXTURE2D(_SourceTex0);
TEXTURE2D(_SourceTex1);
TEXTURE2D(_SourceTex2);
TEXTURE2D(_SourceTex3);
SAMPLER(sampler_SourceTex0);
void EvaluateSourceMaterial(SourceContext c,out SourceClosure a,out SourceClosure b,out float factor)
{
SourceClosure b0=SourceDefaultClosure(c);
float3 v1=float3(c.uv1,0);
float4 v2=SAMPLE_TEXTURE2D(_SourceTex0,sampler_SourceTex0,(v1).xy);
b0.baseColor=(v2).xyz;
float4 v4=SAMPLE_TEXTURE2D(_SourceTex1,sampler_SourceTex0,(v1).xy);
b0.metallic=SourceLuminance((v4).xyz);
float4 v6=SAMPLE_TEXTURE2D(_SourceTex2,sampler_SourceTex0,(v1).xy);
b0.roughness=SourceLuminance((v6).xyz);
b0.ior=1.5;
b0.specular=0.349999994;
b0.coat=0.0149999997;
b0.coatRoughness=0.5;
b0.transmission=0.0;
b0.alpha=1.0;
b0.anisotropy=0.0;
b0.anisotropicRotation=0.0;
float4 v16=SAMPLE_TEXTURE2D(_SourceTex3,sampler_SourceTex0,(v1).xy);
float3 v17=SourceTangentNormal(c.normal,(v16).rgb,1.0,c.position,c.uv1,c.tangent);
b0.normal=v17;
b0.emission=(float4(1.0,1.0,1.0,1.0)).xyz*0.0;
a=b0; b=b0; factor=0; a.alpha*=1; b.alpha*=1;
}
