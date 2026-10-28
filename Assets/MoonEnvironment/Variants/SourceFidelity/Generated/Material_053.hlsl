

void EvaluateSourceMaterial(SourceContext c,out SourceClosure a,out SourceClosure b,out float factor)
{
SourceClosure b0=SourceDefaultClosure(c);
float v1=SourceFBM(c.generated,0.0,2.20000005,1.0,0.5,2.0,false,true);
float4 v2=((v1)<=1.0?lerp(float4(0.173999995,0.195749998,0.191400006,1.0),float4(0.216000006,0.243000001,0.237599999,1.0),saturate((v1-0.0)/1.0)):float4(0.216000006,0.243000001,0.237599999,1.0));
b0.baseColor=(v2).xyz;
b0.metallic=0.720000029;
b0.roughness=0.569999993;
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
