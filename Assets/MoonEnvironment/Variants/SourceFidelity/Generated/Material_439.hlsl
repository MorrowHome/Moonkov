

void EvaluateSourceMaterial(SourceContext c,out SourceClosure a,out SourceClosure b,out float factor)
{
SourceClosure b0=SourceDefaultClosure(c);
float3 v1=c.local;
float3 v2=(v1*float3(32.0,2.0,32.0));
float v3=SourceFBM(v2,0.0,2.0,1.0,0.660000026,2.0,false,true);
float v4=lerp(0.0,1.0,saturate(SourceDivide((v3-0.200000003),(0.800000012-0.200000003))));
float4 v5=lerp(float4(0.300000012,0.324000001,0.326000005,1.0),float4(0.409999996,0.425000012,0.421000004,1.0),saturate(v4));
b0.baseColor=(v5).xyz;
b0.metallic=0.920000017;
float v8=lerp(0.270000011,0.370000005,saturate(SourceDivide((v3-0.200000003),(0.800000012-0.200000003))));
b0.roughness=v8;
b0.ior=1.5;
b0.specular=0.5;
b0.coat=0.0;
b0.coatRoughness=0.0299999993;
b0.transmission=0.0;
b0.alpha=1.0;
b0.anisotropy=0.219999999;
b0.anisotropicRotation=0.0;
float3 v18=SourceBump(c.normal,v3,0.0350000001,0.000250000012,c.position,false);
b0.normal=v18;
b0.emission=(float4(1.0,1.0,1.0,1.0)).xyz*0.0;
a=b0; b=b0; factor=0; a.alpha*=1; b.alpha*=1;
}
