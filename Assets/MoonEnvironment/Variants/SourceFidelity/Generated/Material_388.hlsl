

void EvaluateSourceMaterial(SourceContext c,out SourceClosure a,out SourceClosure b,out float factor)
{
float v0=c.attributes0.x;
float3 v1=float3(dot(float4(c.position,1),float4(0.965925872,(-0.258819073),0.0,(-1048.17224))),dot(float4(c.position,1),float4(0.258819073,0.965925872,(-0.0),(-2637.14575))),dot(float4(c.position,1),float4(0.0,(-0.0),1.0,(-216.300018))));
float v2=SourceFBM(v1,0.0,14.0,2.0,0.5,2.0,false,true);
float v3=lerp(0.400000006,1.0,saturate(SourceDivide((v2-0.239999995),(0.720000029-0.239999995))));
float v4=(v0*v3);
SourceClosure b5=SourceDefaultClosure(c);
float v6=SourceFBM(v1,0.0,5.0,3.0,0.5,2.0,false,true);
float v7=lerp(0.0,1.0,saturate(SourceDivide((v6-0.200000003),(0.800000012-0.200000003))));
float4 v8=lerp(float4(0.100000001,0.103,0.0960000008,1.0),float4(0.170000002,0.171000004,0.157000005,1.0),saturate(v7));
b5.baseColor=(v8).xyz;
b5.metallic=0.0;
b5.roughness=1.0;
b5.ior=1.5;
b5.specular=0.5;
b5.coat=0.0;
b5.coatRoughness=0.0299999993;
b5.transmission=0.0;
b5.alpha=1.0;
b5.anisotropy=0.0;
b5.anisotropicRotation=0.0;
float v20=SourceFBM(v1,0.0,85.0,2.0,0.5,2.0,false,true);
float3 v21=SourceBump(c.normal,v20,0.5,0.00300000003,c.position,false);
b5.normal=v21;
b5.emission=(float4(1.0,1.0,1.0,1.0)).xyz*0.0;
a=b5; b=b5; factor=0; a.alpha*=(1*v4); b.alpha*=(1*v4);
}
