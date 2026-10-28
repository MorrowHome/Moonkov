

void EvaluateSourceMaterial(SourceContext c,out SourceClosure a,out SourceClosure b,out float factor)
{
SourceClosure b0=SourceDefaultClosure(c);
b0.baseColor=(float4(0.0120000001,0.0149999997,0.0160000008,1.0)).xyz;
b0.metallic=0.0;
float3 v3=c.local;
float v4=SourceFBM(v3,0.0,1.35000002,2.0,0.600000024,2.0,false,true);
float v5=(v4*0.075000003);
float v6=(v5+0.80250001);
b0.roughness=v6;
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
