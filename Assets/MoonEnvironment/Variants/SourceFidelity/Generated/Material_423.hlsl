

void EvaluateSourceMaterial(SourceContext c,out SourceClosure a,out SourceClosure b,out float factor)
{
SourceClosure b0=SourceDefaultClosure(c);
float v1=c.attributes0.x;
float v2=lerp(0.560000002,0.0,saturate(SourceDivide((v1-0.0199999996),(0.74000001-0.0199999996))));
float3 v3=c.position;
float v4=SourceFBM(v3,0.0,1.79999995,3.0,0.5,2.0,false,true);
float v5=(v2*v4);
float4 v6=((v4)<=0.829999983?lerp(float4(0.236550003,0.242359996,0.225759998,1.0),float4(0.310649991,0.318280011,0.29648,1.0),saturate((v4-0.180000007)/0.649999976)):float4(0.310649991,0.318280011,0.29648,1.0));
float4 v7=lerp(v6,float4(0.234999999,0.226999998,0.204999998,1.0),saturate(v5));
b0.baseColor=(v7).xyz;
b0.metallic=0.0;
b0.roughness=0.920000017;
b0.ior=1.5;
b0.specular=0.5;
b0.coat=0.0;
b0.coatRoughness=0.0299999993;
b0.transmission=0.0;
b0.alpha=1.0;
b0.anisotropy=0.0;
b0.anisotropicRotation=0.0;
float v19=SourceFBM(v3,0.0,39.0,2.0,0.5,2.0,false,true);
float3 v20=SourceBump(c.normal,v19,0.219999999,0.0120000001,c.position,false);
b0.normal=v20;
b0.emission=(float4(1.0,1.0,1.0,1.0)).xyz*0.0;
a=b0; b=b0; factor=0; a.alpha*=1; b.alpha*=1;
}
