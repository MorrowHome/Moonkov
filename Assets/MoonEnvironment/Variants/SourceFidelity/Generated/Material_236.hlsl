

void EvaluateSourceMaterial(SourceContext c,out SourceClosure a,out SourceClosure b,out float factor)
{
SourceClosure b0=SourceDefaultClosure(c);
float3 v1=c.position;
float v2=SourceFBM(v1,0.0,0.550000012,2.20000005,0.620000005,2.0,false,true);
float4 v3=((v2)<=1.0?lerp(float4(0.075000003,0.0829999968,0.0799999982,1.0),float4(0.0869999975,0.0960000008,0.0920000002,1.0),saturate((v2-0.0)/1.0)):float4(0.0869999975,0.0960000008,0.0920000002,1.0));
b0.baseColor=(v3).xyz;
b0.metallic=0.319999993;
float v6=lerp(0.61500001,0.704999983,saturate(SourceDivide((v2-0.0),(1.0-0.0))));
b0.roughness=v6;
b0.ior=1.5;
b0.specular=0.5;
b0.coat=0.0;
b0.coatRoughness=0.0299999993;
b0.transmission=0.0;
b0.alpha=1.0;
b0.anisotropy=0.0;
b0.anisotropicRotation=0.0;
float v16=SourceFBM(v1,0.0,40.0,2.0,0.5,2.0,false,true);
float3 v17=SourceBump(c.normal,v16,0.109999999,0.00600000005,c.position,false);
b0.normal=v17;
b0.emission=(float4(1.0,1.0,1.0,1.0)).xyz*0.0;
a=b0; b=b0; factor=0; a.alpha*=1; b.alpha*=1;
}
