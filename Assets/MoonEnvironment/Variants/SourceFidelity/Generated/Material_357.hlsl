

void EvaluateSourceMaterial(SourceContext c,out SourceClosure a,out SourceClosure b,out float factor)
{
SourceClosure b0=SourceDefaultClosure(c);
float3 v1=c.local;
float v2=SourceFBM(v1,0.0,0.360000014,2.0,0.579999983,2.0,false,true);
float4 v3=((v2)<=0.829999983?lerp(float4(0.172199994,0.213200003,0.204999998,1.0),float4(0.231000006,0.286000013,0.275000006,1.0),saturate((v2-0.180000007)/0.649999976)):float4(0.231000006,0.286000013,0.275000006,1.0));
b0.baseColor=(v3).xyz;
b0.metallic=0.620000005;
float v6=lerp(0.555000007,0.665000021,saturate(SourceDivide((v2-0.0),(1.0-0.0))));
b0.roughness=v6;
b0.ior=1.5;
b0.specular=0.5;
b0.coat=0.0;
b0.coatRoughness=0.0299999993;
b0.transmission=0.0;
b0.alpha=1.0;
b0.anisotropy=0.0;
b0.anisotropicRotation=0.0;
float v16=SourceFBM(v1,0.0,125.0,2.0,0.5,2.0,false,true);
float3 v17=SourceBump(c.normal,v16,0.159999996,0.00079999998,c.position,false);
b0.normal=v17;
b0.emission=(float4(1.0,1.0,1.0,1.0)).xyz*0.0;
a=b0; b=b0; factor=0; a.alpha*=1; b.alpha*=1;
}
