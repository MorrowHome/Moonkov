

void EvaluateSourceMaterial(SourceContext c,out SourceClosure a,out SourceClosure b,out float factor)
{
SourceClosure b0=SourceDefaultClosure(c);
float3 v1=float3(dot(float4(c.position,1),float4(0.965925872,(-0.258819073),0.0,(-1048.17224))),dot(float4(c.position,1),float4(0.258819073,0.965925872,(-0.0),(-2637.14575))),dot(float4(c.position,1),float4(0.0,(-0.0),1.0,(-216.300018))));
float v2=(v1).z;
float v3=lerp(0.419999987,0.0,saturate(SourceDivide((v2-0.0299999993),(0.720000029-0.0299999993))));
float v4=SourceFBM(v1,0.0,2.4000001,2.0,0.5,2.0,false,true);
float v5=lerp(0.300000012,1.0,saturate(SourceDivide((v4-0.25),(0.800000012-0.25))));
float v6=(v3*v5);
float v7=SourceFBM(v1,0.0,0.180000007,2.0,0.5,2.0,false,true);
float v8=lerp(0.0,1.0,saturate(SourceDivide((v7-0.200000003),(0.800000012-0.200000003))));
float4 v9=lerp(float4(0.314999998,0.312999994,0.282999992,1.0),float4(0.402999997,0.393999994,0.34799999,1.0),saturate(v8));
float4 v10=lerp(v9,float4(0.125,0.131999999,0.123999998,1.0),saturate(v6));
b0.baseColor=(v10).xyz;
b0.metallic=0.0;
b0.roughness=0.839999974;
b0.ior=1.5;
b0.specular=0.5;
b0.coat=0.0;
b0.coatRoughness=0.0299999993;
b0.transmission=0.0;
b0.alpha=1.0;
b0.anisotropy=0.0;
b0.anisotropicRotation=0.0;
float v22=SourceFBM(v1,0.0,140.0,1.0,0.5,2.0,false,true);
float3 v23=SourceBump(c.normal,v22,0.200000003,0.000300000014,c.position,false);
b0.normal=v23;
b0.emission=(float4(1.0,1.0,1.0,1.0)).xyz*0.0;
a=b0; b=b0; factor=0; a.alpha*=1; b.alpha*=1;
}
