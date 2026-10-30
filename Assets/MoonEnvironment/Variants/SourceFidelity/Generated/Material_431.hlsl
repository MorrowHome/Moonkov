

void EvaluateSourceMaterial(SourceContext c,out SourceClosure a,out SourceClosure b,out float factor)
{
float v0=c.attributes0.x;
float3 v1=c.position;
float v2=SourceFBM(v1,0.0,1.79999995,3.0,0.5,2.0,false,true);
float v3=(v0*v2);
SourceClosure b4=SourceDefaultClosure(c);
float4 v5=((v2)<=0.829999983?lerp(float4(0.195050001,0.190899998,0.176789999,1.0),float4(0.256150007,0.250699997,0.232170001,1.0),saturate((v2-0.180000007)/0.649999976)):float4(0.256150007,0.250699997,0.232170001,1.0));
b4.baseColor=(v5).xyz;
b4.metallic=0.0;
b4.roughness=0.920000017;
b4.ior=1.5;
b4.specular=0.5;
b4.coat=0.0;
b4.coatRoughness=0.0299999993;
b4.transmission=0.0;
b4.alpha=1.0;
b4.anisotropy=0.0;
b4.anisotropicRotation=0.0;
float v17=SourceFBM(v1,0.0,75.0,2.0,0.5,2.0,false,true);
float3 v18=SourceBump(c.normal,v17,0.219999999,0.00800000038,c.position,false);
b4.normal=v18;
b4.emission=(float4(1.0,1.0,1.0,1.0)).xyz*0.0;
a=b4; b=b4; factor=0; a.alpha*=(1*v3); b.alpha*=(1*v3);
}
