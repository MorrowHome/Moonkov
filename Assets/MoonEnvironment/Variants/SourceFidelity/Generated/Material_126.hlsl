TEXTURE2D(_SourceTex0);
TEXTURE2D(_SourceTex1);
SAMPLER(sampler_SourceTex0);
void EvaluateSourceMaterial(SourceContext c,out SourceClosure a,out SourceClosure b,out float factor)
{
SourceClosure b0=SourceDefaultClosure(c);
float4 v1=SAMPLE_TEXTURE2D(_SourceTex0,sampler_SourceTex0,c.uv0);
b0.baseColor=(v1).xyz;
float4 v3=SAMPLE_TEXTURE2D(_SourceTex1,sampler_SourceTex0,c.uv0);
float v4=(v3).b;
b0.metallic=v4;
float v6=(v3).g;
b0.roughness=v6;
b0.ior=1.5;
b0.specular=0.5;
b0.coat=0.0;
b0.coatRoughness=0.0299999993;
b0.transmission=0.0;
float v13=(SAMPLE_TEXTURE2D(_SourceTex0,sampler_SourceTex0,c.uv0)).a;
b0.alpha=v13;
b0.anisotropy=0.0;
b0.anisotropicRotation=0.0;
b0.emission=(float4(1.0,1.0,1.0,1.0)).xyz*0.0;
a=b0; b=b0; factor=0; a.alpha*=1; b.alpha*=1;
}
