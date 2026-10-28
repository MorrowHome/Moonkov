TEXTURE2D(_SourceTex0);
SAMPLER(sampler_SourceTex0);
void EvaluateSourceMaterial(SourceContext c,out SourceClosure a,out SourceClosure b,out float factor)
{
SourceClosure b0=SourceDefaultClosure(c);
b0.baseColor=(float4(0.340000004,0.370000005,0.389999986,1.0)).xyz;
b0.metallic=0.939999998;
b0.roughness=0.239999995;
b0.ior=1.5;
b0.specular=0.5;
b0.coat=0.0;
b0.coatRoughness=0.0299999993;
b0.transmission=0.0;
b0.alpha=1.0;
b0.anisotropy=0.0;
b0.anisotropicRotation=0.0;
float3 v12=float3(c.uv0,0);
float4 v13=SAMPLE_TEXTURE2D(_SourceTex0,sampler_SourceTex0,(v12).xy);
float3 v14=SourceTangentNormal(c.normal,(v13).rgb,0.0250000004,c.position,c.uv0,c.tangent);
b0.normal=v14;
b0.emission=(float4(1.0,1.0,1.0,1.0)).xyz*0.0;
a=b0; b=b0; factor=0; a.alpha*=1; b.alpha*=1;
}
