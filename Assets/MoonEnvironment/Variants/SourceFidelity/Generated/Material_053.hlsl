

void EvaluateSourceMaterial(SourceContext c,out SourceClosure a,out SourceClosure b,out float factor)
{
SourceClosure b0=SourceDefaultClosure(c);
float3 v1=c.position;
float v2=SourceFBM(v1,0.0,0.649999976,3.0,0.680000007,2.0,false,true);
float4 v3=((v2)<=0.850000024?lerp(float4(0.0493000001,0.0586499982,0.0535500012,1.0),float4(0.0666999966,0.0793500021,0.0724499971,1.0),saturate((v2-0.150000006)/0.700000018)):float4(0.0666999966,0.0793500021,0.0724499971,1.0));
b0.baseColor=(v3).xyz;
b0.metallic=0.720000029;
float3 v6=(v1*float3(14.0,1.0,1.0));
float v7=SourceFBM(v6,0.0,10.0,3.0,0.680000007,2.0,false,true);
float4 v8=((v7)<=0.819999993?lerp(float4(0.50999999,0.50999999,0.50999999,1.0),float4(0.850000024,0.850000024,0.850000024,1.0),saturate((v7-0.180000007)/0.639999986)):float4(0.850000024,0.850000024,0.850000024,1.0));
b0.roughness=SourceLuminance((v8).xyz);
b0.ior=1.5;
b0.specular=0.5;
b0.coat=0.0;
b0.coatRoughness=0.0299999993;
b0.transmission=0.0;
b0.alpha=1.0;
b0.anisotropy=0.0;
b0.anisotropicRotation=0.0;
float v18=SourceFBM(v1,0.0,125.0,2.0,0.680000007,2.0,false,true);
float3 v19=SourceBump(c.normal,v18,0.280000001,0.00120000006,c.position,false);
b0.normal=v19;
b0.emission=(float4(1.0,1.0,1.0,1.0)).xyz*0.0;
a=b0; b=b0; factor=0; a.alpha*=1; b.alpha*=1;
}
