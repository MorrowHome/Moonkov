

void EvaluateSourceMaterial(SourceContext c,out SourceClosure a,out SourceClosure b,out float factor)
{
SourceClosure b0=SourceDefaultClosure(c);
float3 v1=c.position;
float3 v2=(v1*float3(1.0,7.0,40.0));
float v3=SourceFBM(v2,0.0,9.0,3.0,0.680000007,2.0,false,true);
float4 v4=((v3)<=0.620000005?lerp(float4(0.0,0.0,0.0,1.0),float4(0.180000007,0.180000007,0.180000007,1.0),saturate((v3-0.379999995)/0.24000001)):float4(0.180000007,0.180000007,0.180000007,1.0));
float v5=SourceFBM(v1,0.0,0.649999976,3.0,0.680000007,2.0,false,true);
float4 v6=((v5)<=0.850000024?lerp(float4(0.0266399998,0.0333000012,0.0325599983,1.0),float4(0.0453599989,0.0566999987,0.0554400012,1.0),saturate((v5-0.150000006)/0.700000018)):float4(0.0453599989,0.0566999987,0.0554400012,1.0));
float4 v7=lerp(v6,float4(0.119999997,0.135000005,0.129999995,1.0),saturate(SourceLuminance((v4).xyz)));
b0.baseColor=(v7).xyz;
b0.metallic=0.860000014;
float3 v10=(v1*float3(1.0,1.0,20.0));
float v11=SourceFBM(v10,0.0,10.0,3.0,0.680000007,2.0,false,true);
float4 v12=((v11)<=0.75?lerp(float4(0.400000006,0.400000006,0.400000006,1.0),float4(0.699999988,0.699999988,0.699999988,1.0),saturate((v11-0.25)/0.5)):float4(0.699999988,0.699999988,0.699999988,1.0));
b0.roughness=SourceLuminance((v12).xyz);
b0.ior=1.5;
b0.specular=0.5;
b0.coat=0.0;
b0.coatRoughness=0.0299999993;
b0.transmission=0.0;
b0.alpha=1.0;
b0.anisotropy=0.0;
b0.anisotropicRotation=0.0;
float v22=SourceFBM(v1,0.0,125.0,2.0,0.680000007,2.0,false,true);
float3 v23=SourceBump(c.normal,v22,0.280000001,0.000449999992,c.position,false);
b0.normal=v23;
b0.emission=(float4(1.0,1.0,1.0,1.0)).xyz*0.0;
a=b0; b=b0; factor=0; a.alpha*=1; b.alpha*=1;
}
