

void EvaluateSourceMaterial(SourceContext c,out SourceClosure a,out SourceClosure b,out float factor)
{
SourceClosure b0=SourceDefaultClosure(c);
float3 v1=c.position;
float3 v2=(v1*float3(2.0,2.0,0.219999999));
float v3=SourceFBM(v2,0.0,3.29999995,3.0,0.680000007,2.0,false,true);
float4 v4=((v3)<=0.75?lerp(float4(0.0,0.0,0.0,1.0),float4(0.200000003,0.200000003,0.200000003,1.0),saturate((v3-0.449999988)/0.300000012)):float4(0.200000003,0.200000003,0.200000003,1.0));
float v5=(v1).z;
float v6=lerp(0.0,1.0,saturate(SourceDivide((v5-6.5999999),(14.0-6.5999999))));
float4 v7=((v6)<=0.189189196?lerp(float4(0.779999971,0.779999971,0.779999971,1.0),float4(0.400000006,0.400000006,0.400000006,1.0),saturate((v6-0.0)/0.189189196)):((v6)<=0.391891897?lerp(float4(0.400000006,0.400000006,0.400000006,1.0),float4(0.180000007,0.180000007,0.180000007,1.0),saturate((v6-0.189189196)/0.202702701)):((v6)<=0.864864886?lerp(float4(0.180000007,0.180000007,0.180000007,1.0),float4(0.25,0.25,0.25,1.0),saturate((v6-0.391891897)/0.472972989)):((v6)<=1.0?lerp(float4(0.25,0.25,0.25,1.0),float4(0.100000001,0.100000001,0.100000001,1.0),saturate((v6-0.864864886)/0.135135114)):float4(0.100000001,0.100000001,0.100000001,1.0)))));
float v8=(SourceLuminance((v4).xyz)*SourceLuminance((v7).xyz));
float3 v9=(v1*float3(1.0,1.0,1.39999998));
float v10=SourceFBM(v9,0.0,24.0,3.0,0.680000007,2.0,false,true);
float4 v11=((v10)<=0.720000029?lerp(float4(0.0,0.0,0.0,1.0),float4(1.0,1.0,1.0,1.0),saturate((v10-0.620000005)/0.100000024)):float4(1.0,1.0,1.0,1.0));
float v12=SourceFBM(v1,0.0,2.70000005,3.0,0.680000007,2.0,false,true);
float4 v13=((v12)<=0.620000005?lerp(float4(0.0,0.0,0.0,1.0),float4(0.949999988,0.949999988,0.949999988,1.0),saturate((v12-0.430000007)/0.189999998)):float4(0.949999988,0.949999988,0.949999988,1.0));
float v14=(SourceLuminance((v11).xyz)*SourceLuminance((v13).xyz));
float v15=(v14*SourceLuminance((v7).xyz));
float v16=SourceFBM(v1,0.0,0.649999976,3.0,0.680000007,2.0,false,true);
float4 v17=((v16)<=0.850000024?lerp(float4(0.274049997,0.263610005,0.214890003,1.0),float4(0.355949998,0.342390001,0.279110014,1.0),saturate((v16-0.150000006)/0.700000018)):float4(0.355949998,0.342390001,0.279110014,1.0));
float4 v18=lerp(v17,float4(0.0769999996,0.0450000018,0.0250000004,1.0),saturate(v15));
float4 v19=lerp(v18,float4(0.109999999,0.0979999974,0.075000003,1.0),saturate(v8));
b0.baseColor=(v19).xyz;
b0.metallic=0.0450000018;
float3 v22=(v1*float3(1.0,1.0,0.400000006));
float v23=SourceFBM(v22,0.0,7.0,3.0,0.680000007,2.0,false,true);
float4 v24=((v23)<=0.800000012?lerp(float4(0.360000014,0.360000014,0.360000014,1.0),float4(0.680000007,0.680000007,0.680000007,1.0),saturate((v23-0.200000003)/0.600000009)):float4(0.680000007,0.680000007,0.680000007,1.0));
b0.roughness=SourceLuminance((v24).xyz);
b0.ior=1.5;
b0.specular=0.5;
b0.coat=0.0;
b0.coatRoughness=0.0299999993;
b0.transmission=0.0;
b0.alpha=1.0;
b0.anisotropy=0.0;
b0.anisotropicRotation=0.0;
float v34=SourceFBM(v1,0.0,125.0,2.0,0.680000007,2.0,false,true);
float3 v35=SourceBump(c.normal,v34,0.280000001,0.000699999975,c.position,false);
b0.normal=v35;
b0.emission=(float4(1.0,1.0,1.0,1.0)).xyz*0.0;
a=b0; b=b0; factor=0; a.alpha*=1; b.alpha*=1;
}
