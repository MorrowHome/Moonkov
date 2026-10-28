TEXTURE2D(_SourceTex0);
SAMPLER(sampler_SourceTex0);
void EvaluateSourceMaterial(SourceContext c,out SourceClosure a,out SourceClosure b,out float factor)
{
SourceClosure b0=SourceDefaultClosure(c);
b0.baseColor=(float4(0.125,0.159999996,0.170000002,1.0)).xyz;
b0.metallic=0.0;
b0.roughness=0.5;
b0.ior=1.5;
b0.specular=0.5;
b0.coat=0.0;
b0.coatRoughness=0.0299999993;
b0.transmission=0.0;
b0.alpha=1.0;
b0.anisotropy=0.0;
b0.anisotropicRotation=0.0;
float4 v12=SAMPLE_TEXTURE2D(_SourceTex0,sampler_SourceTex0,c.uv0);
float3 v13=SourceTangentNormal(c.normal,(v12).rgb,1.0,c.position,c.uv0,c.tangent);
b0.normal=v13;
b0.emission=(float4(1.0,1.0,1.0,1.0)).xyz*0.0;
a=b0; b=b0; factor=0; a.alpha*=1; b.alpha*=1;
}
