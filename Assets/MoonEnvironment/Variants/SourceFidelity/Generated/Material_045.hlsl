

void EvaluateSourceMaterial(SourceContext c,out SourceClosure a,out SourceClosure b,out float factor)
{
SourceClosure b0=SourceDefaultClosure(c);
float3 v1=c.position;
float v2=SourceFBM(v1,0.0,1.39999998,3.0,0.680000007,2.0,false,true);
float4 v3=((v2)<=0.720000029?lerp(float4(0.0,0.0,0.0,1.0),float4(0.170000002,0.170000002,0.170000002,1.0),saturate((v2-0.349999994)/0.370000035)):float4(0.170000002,0.170000002,0.170000002,1.0));
float v4=SourceFBM(v1,0.0,0.649999976,3.0,0.680000007,2.0,false,true);
float4 v5=((v4)<=0.850000024?lerp(float4(0.108000003,0.131999999,0.128000006,1.0),float4(0.162,0.197999999,0.192000002,1.0),saturate((v4-0.150000006)/0.700000018)):float4(0.162,0.197999999,0.192000002,1.0));
float4 v6=lerp(v5,float4(0.129999995,0.0829999968,0.0430000015,1.0),saturate(SourceLuminance((v3).xyz)));
b0.baseColor=(v6).xyz;
b0.metallic=0.949999988;
float3 v9=(v1*float3(1.0,1.0,18.0));
float v10=SourceFBM(v9,0.0,16.0,3.0,0.680000007,2.0,false,true);
float4 v11=((v10)<=0.800000012?lerp(float4(0.25,0.25,0.25,1.0),float4(0.550000012,0.550000012,0.550000012,1.0),saturate((v10-0.200000003)/0.600000009)):float4(0.550000012,0.550000012,0.550000012,1.0));
b0.roughness=SourceLuminance((v11).xyz);
b0.ior=1.5;
b0.specular=0.5;
b0.coat=0.0;
b0.coatRoughness=0.0299999993;
b0.transmission=0.0;
b0.alpha=1.0;
b0.anisotropy=0.0;
b0.anisotropicRotation=0.0;
float v21=SourceFBM(v1,0.0,125.0,2.0,0.680000007,2.0,false,true);
float3 v22=SourceBump(c.normal,v21,0.280000001,0.000300000014,c.position,false);
b0.normal=v22;
b0.emission=(float4(1.0,1.0,1.0,1.0)).xyz*0.0;
a=b0; b=b0; factor=0; a.alpha*=1; b.alpha*=1;
}
