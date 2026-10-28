

void EvaluateSourceMaterial(SourceContext c,out SourceClosure a,out SourceClosure b,out float factor)
{
SourceClosure b0=SourceDefaultClosure(c);
float3 v1=float3(dot(float4(c.position,1),float4(0.965925872,(-0.258819073),0.0,(-1048.17224))),dot(float4(c.position,1),float4(0.258819073,0.965925872,(-0.0),(-2637.14575))),dot(float4(c.position,1),float4(0.0,(-0.0),1.0,(-216.300018))));
float v2=SourceFBM(v1,0.0,13.0,2.0,0.5,2.0,false,true);
float v3=lerp(0.0,1.0,saturate(SourceDivide((v2-0.200000003),(0.800000012-0.200000003))));
float4 v4=lerp(float4(0.0850000009,0.0909999982,0.0850000009,1.0),float4(0.180000007,0.184,0.165000007,1.0),saturate(v3));
b0.baseColor=(v4).xyz;
b0.metallic=0.0;
b0.roughness=0.980000019;
b0.ior=1.5;
b0.specular=0.5;
b0.coat=0.0;
b0.coatRoughness=0.0299999993;
b0.transmission=0.0;
b0.alpha=1.0;
b0.anisotropy=0.0;
b0.anisotropicRotation=0.0;
float v16=SourceFBM(v1,0.0,150.0,2.0,0.5,2.0,false,true);
float3 v17=SourceBump(c.normal,v16,0.400000006,0.00100000005,c.position,false);
b0.normal=v17;
b0.emission=(float4(1.0,1.0,1.0,1.0)).xyz*0.0;
a=b0; b=b0; factor=0; a.alpha*=1; b.alpha*=1;
}
