

void EvaluateSourceMaterial(SourceContext c,out SourceClosure a,out SourceClosure b,out float factor)
{
SourceClosure b0=SourceDefaultClosure(c);
float3 v1=c.position;
float v2=SourceFBM(v1,0.0,1.60000002,3.0,0.5,2.0,false,true);
float4 v3=((v2)<=0.800000012?lerp(float4(0.0149400001,0.0224099997,0.0240700003,1.0),float4(0.0194400009,0.0291600004,0.0313199982,1.0),saturate((v2-0.219999999)/0.580000013)):float4(0.0194400009,0.0291600004,0.0313199982,1.0));
b0.baseColor=(v3).xyz;
b0.metallic=0.100000001;
b0.roughness=0.870000005;
b0.ior=1.5;
b0.specular=0.5;
b0.coat=0.0;
b0.coatRoughness=0.0299999993;
b0.transmission=0.0;
b0.alpha=1.0;
b0.anisotropy=0.0;
b0.anisotropicRotation=0.0;
float v15=SourceFBM(v1,0.0,45.0,2.0,0.5,2.0,false,true);
float3 v16=SourceBump(c.normal,v15,0.100000001,0.0250000004,c.position,false);
b0.normal=v16;
b0.emission=(float4(1.0,1.0,1.0,1.0)).xyz*0.0;
a=b0; b=b0; factor=0; a.alpha*=1; b.alpha*=1;
}
