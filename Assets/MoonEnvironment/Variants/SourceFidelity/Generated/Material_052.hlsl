

void EvaluateSourceMaterial(SourceContext c,out SourceClosure a,out SourceClosure b,out float factor)
{
SourceClosure b0=SourceDefaultClosure(c);
float3 v1=c.position;
float v2=SourceFBM(v1,0.0,0.649999976,3.0,0.680000007,2.0,false,true);
float4 v3=((v2)<=0.850000024?lerp(float4(0.207000002,0.229499996,0.224999994,1.0),float4(0.252999991,0.280499995,0.275000006,1.0),saturate((v2-0.150000006)/0.700000018)):float4(0.252999991,0.280499995,0.275000006,1.0));
b0.baseColor=(v3).xyz;
b0.metallic=0.959999979;
float3 v6=(v1*float3(1.0,1.0,24.0));
float v7=SourceFBM(v6,0.0,12.0,3.0,0.680000007,2.0,false,true);
float4 v8=((v7)<=0.800000012?lerp(float4(0.230000004,0.230000004,0.230000004,1.0),float4(0.50999999,0.50999999,0.50999999,1.0),saturate((v7-0.200000003)/0.600000009)):float4(0.50999999,0.50999999,0.50999999,1.0));
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
float3 v19=SourceBump(c.normal,v18,0.280000001,0.000220000002,c.position,false);
b0.normal=v19;
b0.emission=(float4(1.0,1.0,1.0,1.0)).xyz*0.0;
a=b0; b=b0; factor=0; a.alpha*=1; b.alpha*=1;
}
