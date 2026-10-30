

void EvaluateSourceMaterial(SourceContext c,out SourceClosure a,out SourceClosure b,out float factor)
{
SourceClosure b0=SourceDefaultClosure(c);
float3 v1=c.local;
float v2=SourceFBM(v1,0.0,3.4000001,2.0,0.660000026,2.0,false,true);
float v3=lerp(0.0,1.0,saturate(SourceDivide((v2-0.200000003),(0.800000012-0.200000003))));
float4 v4=lerp(float4(0.137999997,0.155000001,0.112000003,1.0),float4(0.178000003,0.194000006,0.144999996,1.0),saturate(v3));
b0.baseColor=(v4).xyz;
b0.metallic=0.0450000018;
float v7=lerp(0.5,0.639999986,saturate(SourceDivide((v2-0.200000003),(0.800000012-0.200000003))));
b0.roughness=v7;
b0.ior=1.5;
b0.specular=0.5;
b0.coat=0.0;
b0.coatRoughness=0.0299999993;
b0.transmission=0.0;
b0.alpha=1.0;
b0.anisotropy=0.0;
b0.anisotropicRotation=0.0;
float v17=SourceFBM(v1,0.0,72.0,1.0,0.660000026,2.0,false,true);
float3 v18=SourceBump(c.normal,v17,0.0350000001,0.000250000012,c.position,false);
b0.normal=v18;
b0.emission=(float4(1.0,1.0,1.0,1.0)).xyz*0.0;
a=b0; b=b0; factor=0; a.alpha*=1; b.alpha*=1;
}
