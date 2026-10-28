

void EvaluateSourceMaterial(SourceContext c,out SourceClosure a,out SourceClosure b,out float factor)
{
SourceClosure b0=SourceDefaultClosure(c);
float3 v1=c.position;
float3 v2=(v1*float3(1.0,1.0,1.39999998));
float v3=SourceFBM(v2,0.0,24.0,3.0,0.680000007,2.0,false,true);
float4 v4=((v3)<=0.720000029?lerp(float4(0.0,0.0,0.0,1.0),float4(1.0,1.0,1.0,1.0),saturate((v3-0.620000005)/0.100000024)):float4(1.0,1.0,1.0,1.0));
float v5=SourceFBM(v1,0.0,2.70000005,3.0,0.680000007,2.0,false,true);
float4 v6=((v5)<=0.620000005?lerp(float4(0.0,0.0,0.0,1.0),float4(0.600000024,0.600000024,0.600000024,1.0),saturate((v5-0.430000007)/0.189999998)):float4(0.600000024,0.600000024,0.600000024,1.0));
float v7=(SourceLuminance((v4).xyz)*SourceLuminance((v6).xyz));
float v8=(v1).z;
float v9=lerp(0.0,1.0,saturate(SourceDivide((v8-6.5999999),(9.39999962-6.5999999))));
float4 v10=((v9)<=0.75?lerp(float4(0.600000024,0.600000024,0.600000024,1.0),float4(0.25,0.25,0.25,1.0),saturate((v9-0.0)/0.75)):((v9)<=1.0?lerp(float4(0.25,0.25,0.25,1.0),float4(0.0399999991,0.0399999991,0.0399999991,1.0),saturate((v9-0.75)/0.25)):float4(0.0399999991,0.0399999991,0.0399999991,1.0)));
float v11=(v7*SourceLuminance((v10).xyz));
float4 v12=((v9)<=0.285714298?lerp(float4(1.0,1.0,1.0,1.0),float4(0.0,0.0,0.0,1.0),saturate((v9-0.0178571437)/0.267857155)):float4(0.0,0.0,0.0,1.0));
float3 v13=(v1*float3(2.0,2.0,0.200000003));
float v14=SourceFBM(v13,0.0,3.0,3.0,0.680000007,2.0,false,true);
float v15=(SourceLuminance((v12).xyz)*v14);
float v16=float(v8>8.13000011);
float v17=float(v8<8.23999977);
float v18=(v16*v17);
float v19=(v18*0.620000005);
float4 v20=((v9)<=0.45714286?lerp(float4(1.0,1.0,1.0,1.0),float4(0.0,0.0,0.0,1.0),saturate((v9-0.439285725)/0.0178571343)):float4(0.0,0.0,0.0,1.0));
float v21=SourceFBM(v1,0.0,0.649999976,3.0,0.680000007,2.0,false,true);
float4 v22=((v21)<=0.850000024?lerp(float4(0.151800007,0.170200005,0.160999998,1.0),float4(0.178200006,0.1998,0.188999996,1.0),saturate((v21-0.150000006)/0.700000018)):float4(0.178200006,0.1998,0.188999996,1.0));
float4 v23=lerp(v22,float4(0.0359999985,0.050999999,0.0480000004,1.0),saturate(SourceLuminance((v20).xyz)));
float4 v24=lerp(v23,float4(0.189999998,0.104999997,0.0350000001,1.0),saturate(v19));
float4 v25=lerp(v24,float4(0.0649999976,0.057,0.0419999994,1.0),saturate(v15));
float4 v26=lerp(v25,float4(0.061999999,0.0670000017,0.0610000007,1.0),saturate(v11));
b0.baseColor=(v26).xyz;
b0.metallic=0.0299999993;
float v29=SourceFBM(v1,0.0,5.0,3.0,0.680000007,2.0,false,true);
float4 v30=((v29)<=0.800000012?lerp(float4(0.670000017,0.670000017,0.670000017,1.0),float4(0.879999995,0.879999995,0.879999995,1.0),saturate((v29-0.200000003)/0.600000009)):float4(0.879999995,0.879999995,0.879999995,1.0));
b0.roughness=SourceLuminance((v30).xyz);
b0.ior=1.5;
b0.specular=0.5;
b0.coat=0.0;
b0.coatRoughness=0.0299999993;
b0.transmission=0.0;
b0.alpha=1.0;
b0.anisotropy=0.0;
b0.anisotropicRotation=0.0;
float v40=SourceFBM(v1,0.0,125.0,2.0,0.680000007,2.0,false,true);
float3 v41=SourceBump(c.normal,v40,0.280000001,0.00100000005,c.position,false);
b0.normal=v41;
b0.emission=(float4(1.0,1.0,1.0,1.0)).xyz*0.0;
a=b0; b=b0; factor=0; a.alpha*=1; b.alpha*=1;
}
