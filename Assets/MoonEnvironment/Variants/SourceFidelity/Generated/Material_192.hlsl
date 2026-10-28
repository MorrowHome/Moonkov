TEXTURE2D(_SourceTex0);
TEXTURE2D(_SourceTex1);
TEXTURE2D(_SourceTex2);
SAMPLER(sampler_SourceTex0);
void EvaluateSourceMaterial(SourceContext c,out SourceClosure a,out SourceClosure b,out float factor)
{
SourceClosure b0=SourceDefaultClosure(c);
float3 v1=float3(c.uv0,0);
float3 v2=(v1*0.5);
float4 v3=SAMPLE_TEXTURE2D(_SourceTex0,sampler_SourceTex0,(v2).xy);
float4 v4=lerp(v3,float4(0.310000002,0.314999998,0.294999987,1.0),saturate(0.879999995));
b0.baseColor=(v4).xyz;
b0.metallic=0.0;
float v7=(SAMPLE_TEXTURE2D(_SourceTex1,sampler_SourceTex0,(v2).xy)).a;
float v8=(1.0-v7);
float v9=lerp(0.829999983,0.875,saturate(SourceDivide((v8-0.0),(1.0-0.0))));
b0.roughness=v9;
b0.ior=1.5;
b0.specular=0.5;
b0.coat=0.0;
b0.coatRoughness=0.0299999993;
b0.transmission=0.0;
b0.alpha=1.0;
b0.anisotropy=0.0;
b0.anisotropicRotation=0.0;
float4 v19=SAMPLE_TEXTURE2D(_SourceTex2,sampler_SourceTex0,(v2).xy);
float3 v20=SourceTangentNormal(c.normal,(v19).rgb,0.119999997,c.position,c.uv0,c.tangent);
b0.normal=v20;
b0.emission=(float4(1.0,1.0,1.0,1.0)).xyz*0.0;
a=b0; b=b0; factor=0; a.alpha*=1; b.alpha*=1;
}
