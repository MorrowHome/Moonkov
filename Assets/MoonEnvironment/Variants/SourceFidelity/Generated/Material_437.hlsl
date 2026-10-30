

void EvaluateSourceMaterial(SourceContext c,out SourceClosure a,out SourceClosure b,out float factor)
{
float v0=0.0;
float v1=SourceFresnel(c.normal,c.view,1.45000005);
SourceClosure b2=SourceDefaultClosure(c);
b2.baseColor=(float4(0.699999988,0.779999971,0.800000012,1.0)).xyz;
b2.metallic=1.0;
b2.roughness=0.119999997;
b2.ior=1.5;
b2.specular=0.5;
b2.coat=0.0;
b2.coatRoughness=0.0299999993;
b2.transmission=0.0;
b2.alpha=1.0;
b2.anisotropy=0.0;
b2.anisotropicRotation=0.0;
b2.emission=(float4(1.0,1.0,1.0,1.0)).xyz*0.0;
a=b2; b=b2; factor=0; a.alpha*=((1*v1)*(1-v0)); b.alpha*=((1*v1)*(1-v0));
}
