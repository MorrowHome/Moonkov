

void EvaluateSourceMaterial(SourceContext c,out SourceClosure a,out SourceClosure b,out float factor)
{
SourceClosure b0=SourceDefaultClosure(c);
float v1=c.attributes0.x;
float3 v2=c.position;
float v3=SourceFBM(v2,0.0,0.550000012,3.0,0.649999976,2.0,false,true);
float4 v4=((v3)<=0.879999995?lerp(float4(0.126000002,0.128879994,0.126719996,1.0),float4(0.206499994,0.211219996,0.207680002,1.0),saturate((v3-0.119999997)/0.759999998)):float4(0.206499994,0.211219996,0.207680002,1.0));
float4 v5=lerp(v4,float4(0.245000005,0.241999999,0.226999998,1.0),saturate(v1));
b0.baseColor=(v5).xyz;
b0.metallic=0.0;
b0.roughness=0.939999998;
b0.ior=1.5;
b0.specular=0.5;
b0.coat=0.0;
b0.coatRoughness=0.0299999993;
b0.transmission=0.0;
b0.alpha=1.0;
b0.anisotropy=0.0;
b0.anisotropicRotation=0.0;
float v17=SourceFBM(v2,0.0,21.0,2.0,0.5,2.0,false,true);
float3 v18=SourceBump(c.normal,v17,0.300000012,0.0219999999,c.position,false);
b0.normal=v18;
b0.emission=(float4(1.0,1.0,1.0,1.0)).xyz*0.0;
a=b0; b=b0; factor=0; a.alpha*=1; b.alpha*=1;
}
