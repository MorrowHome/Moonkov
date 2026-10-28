

void EvaluateSourceMaterial(SourceContext c,out SourceClosure a,out SourceClosure b,out float factor)
{
SourceClosure b0=SourceDefaultClosure(c);
float3 v1=c.position;
float3 v2=(v1*float3(4.0,0.180000007,1.0));
float v3=SourceFBM(v2,0.0,22.0,3.0,0.680000007,2.0,false,true);
float4 v4=((v3)<=0.74000001?lerp(float4(0.0,0.0,0.0,1.0),float4(0.189999998,0.189999998,0.189999998,1.0),saturate((v3-0.579999983)/0.160000026)):float4(0.189999998,0.189999998,0.189999998,1.0));
float v5=(v1).x;
float v6=(v5-74.4499969);
float v7=abs(v6);
float4 v8=((v7)<=0.680000007?lerp(float4(0.0666000023,0.0782999992,0.0737999976,1.0),float4(0.0813999996,0.0957000032,0.0901999995,1.0),saturate((v7-0.0)/0.680000007)):((v7)<=1.0?lerp(float4(0.0813999996,0.0957000032,0.0901999995,1.0),float4(0.0,0.0,0.0,1.0),saturate((v7-0.680000007)/0.319999993)):float4(0.0,0.0,0.0,1.0)));
float v9=(v5-79.5500031);
float v10=abs(v9);
float4 v11=((v10)<=0.600000024?lerp(float4(1.0,1.0,1.0,1.0),float4(0.699999988,0.699999988,0.699999988,1.0),saturate((v10-0.0)/0.600000024)):((v10)<=1.0?lerp(float4(0.699999988,0.699999988,0.699999988,1.0),float4(0.0,0.0,0.0,1.0),saturate((v10-0.600000024)/0.399999976)):float4(0.0,0.0,0.0,1.0)));
float v12=max(SourceLuminance((v8).xyz),SourceLuminance((v11).xyz));
float v13=(v1).y;
float v14=(v13-(-82.1999969));
float v15=abs(v14);
float4 v16=((v15)<=0.5?lerp(float4(1.0,1.0,1.0,1.0),float4(0.699999988,0.699999988,0.699999988,1.0),saturate((v15-0.0)/0.5)):((v15)<=1.0?lerp(float4(0.699999988,0.699999988,0.699999988,1.0),float4(0.0,0.0,0.0,1.0),saturate((v15-0.5)/0.5)):float4(0.0,0.0,0.0,1.0)));
float v17=max(v12,SourceLuminance((v16).xyz));
float3 v18=(v1*float3(2.0,0.5,1.0));
float v19=SourceFBM(v18,0.0,1.70000005,3.0,0.680000007,2.0,false,true);
float4 v20=((v19)<=0.699999988?lerp(float4(0.649999976,0.649999976,0.649999976,1.0),float4(1.0,1.0,1.0,1.0),saturate((v19-0.300000012)/0.399999976)):float4(1.0,1.0,1.0,1.0));
float v21=(v17*SourceLuminance((v20).xyz));
float v22=(SourceLuminance((v4).xyz)*v21);
float v23=(v21*0.349999994);
float v24=SourceFBM(v1,0.0,0.649999976,3.0,0.680000007,2.0,false,true);
float4 v25=((v24)<=0.850000024?lerp(float4(0.0562399998,0.0661199987,0.0623200014,1.0),float4(0.0917600021,0.107879996,0.101680003,1.0),saturate((v24-0.150000006)/0.700000018)):float4(0.0917600021,0.107879996,0.101680003,1.0));
float4 v26=lerp(v25,float4(0.0450000018,0.0520000011,0.0469999984,1.0),saturate(v23));
float4 v27=lerp(v26,float4(0.150000006,0.144999996,0.119999997,1.0),saturate(v22));
b0.baseColor=(v27).xyz;
b0.metallic=0.0599999987;
float4 v30=lerp(float4(0.839999974,0.839999974,0.839999974,1.0),float4(0.670000017,0.670000017,0.670000017,1.0),saturate(v21));
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
float3 v41=SourceBump(c.normal,v40,0.280000001,0.00124999997,c.position,false);
b0.normal=v41;
b0.emission=(float4(1.0,1.0,1.0,1.0)).xyz*0.0;
a=b0; b=b0; factor=0; a.alpha*=1; b.alpha*=1;
}
