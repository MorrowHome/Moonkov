TEXTURE2D(_SourceTex0);
TEXTURE2D(_SourceTex1);
SAMPLER(sampler_SourceTex0);
void EvaluateSourceMaterial(SourceContext c,out SourceClosure a,out SourceClosure b,out float factor)
{
SourceClosure b0=SourceDefaultClosure(c);
b0.baseColor=(float4(0.180000007,0.209999993,0.224999994,1.0)).xyz;
b0.metallic=0.0;
float3 v3=float3(c.uv0,0);
float4 v4=SAMPLE_TEXTURE2D(_SourceTex0,sampler_SourceTex0,(v3).xy);
b0.roughness=SourceLuminance((v4).xyz);
b0.ior=1.46000004;
b0.specular=0.5;
b0.coat=0.0;
b0.coatRoughness=0.0299999993;
b0.transmission=0.0;
b0.alpha=1.0;
b0.anisotropy=0.0;
b0.anisotropicRotation=0.0;
float4 v14=SAMPLE_TEXTURE2D(_SourceTex1,sampler_SourceTex0,(v3).xy);
float3 v15=SourceTangentNormal(c.normal,(v14).rgb,1.0,c.position,c.uv0,c.tangent);
b0.normal=v15;
b0.emission=(float4(1.0,1.0,1.0,1.0)).xyz*0.0;
a=b0; b=b0; factor=0; a.alpha*=1; b.alpha*=1;
}
