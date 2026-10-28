TEXTURE2D(_SourceTex0);
SAMPLER(sampler_SourceTex0);
void EvaluateSourceMaterial(SourceContext c,out SourceClosure a,out SourceClosure b,out float factor)
{
SourceClosure b0=SourceDefaultClosure(c);
b0.baseColor=(float4(0.0649999976,0.0729999989,0.0759999976,1.0)).xyz;
b0.metallic=0.0599999987;
float3 v3=float3(c.uv1,0);
float3 v4=(v3*6.25);
float v5=(SAMPLE_TEXTURE2D(_SourceTex0,sampler_SourceTex0,(v4).xy)).a;
float v6=(1.0-v5);
float v7=lerp(0.564999998,0.595000029,saturate(SourceDivide((v6-0.0),(1.0-0.0))));
b0.roughness=v7;
b0.ior=1.5;
b0.specular=0.5;
b0.coat=0.0;
b0.coatRoughness=0.0299999993;
b0.transmission=0.0;
b0.alpha=1.0;
b0.anisotropy=0.0;
b0.anisotropicRotation=0.0;
b0.emission=(float4(1.0,1.0,1.0,1.0)).xyz*0.0;
a=b0; b=b0; factor=0; a.alpha*=1; b.alpha*=1;
}
