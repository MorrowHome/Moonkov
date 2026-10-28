

void EvaluateSourceMaterial(SourceContext c,out SourceClosure a,out SourceClosure b,out float factor)
{
SourceClosure b0=SourceDefaultClosure(c);
float v1=SourceFBM(c.generated,0.0,2.20000005,1.0,0.5,2.0,false,true);
float4 v2=((v1)<=1.0?lerp(float4(0.234899998,0.252299994,0.239250004,1.0),float4(0.291599989,0.313199997,0.296999991,1.0),saturate((v1-0.0)/1.0)):float4(0.291599989,0.313199997,0.296999991,1.0));
b0.baseColor=(v2).xyz;
b0.metallic=0.779999971;
b0.roughness=0.430000007;
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
