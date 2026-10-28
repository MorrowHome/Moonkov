

void EvaluateSourceMaterial(SourceContext c,out SourceClosure a,out SourceClosure b,out float factor)
{
SourceClosure b0=SourceDefaultClosure(c);
float3 v1=c.normal;
float v2=(v1).z;
float v3=max(v2,0.0);
float3 v4=c.position;
float3 v5=(v4-float3(1980.0,2480.0,251.550049));
float v6=SourceFBM(v5,0.0,155.0,2.0,0.699999988,2.0,false,true);
float v7=float(v6>0.680000007);
float v8=(v3*v7);
float v9=(v8*0.280000001);
float v10=(v3*0.0549999997);
float v11=SourceFBM(v5,0.0,0.25999999,2.0,0.579999983,2.0,false,true);
float v12=(v11*0.600000024);
float v13=(v12+0.550000012);
float v14=(v10*v13);
float4 v15=lerp(float4(0.282000005,0.124080002,0.0234999992,1.0),float4(0.316500008,0.139259994,0.0263749994,1.0),saturate(v11));
float4 v16=lerp(v15,float4(0.310000002,0.289999992,0.25,1.0),saturate(v14));
float4 v17=lerp(v16,float4(0.209999993,0.200000003,0.150000006,1.0),saturate(v9));
b0.baseColor=(v17).xyz;
b0.metallic=0.0599999987;
float v20=(v11-0.5);
float v21=(v20*0.119999997);
float v22=(0.670000017+v21);
b0.roughness=v22;
b0.ior=1.5;
b0.specular=0.319999993;
b0.coat=0.0;
b0.coatRoughness=0.0299999993;
b0.transmission=0.0;
b0.alpha=1.0;
b0.anisotropy=0.0;
b0.anisotropicRotation=0.0;
float3 v32=SourceBump(c.normal,v6,0.119999997,0.000349999988,c.position,false);
b0.normal=v32;
b0.emission=(float4(1.0,1.0,1.0,1.0)).xyz*0.0;
a=b0; b=b0; factor=0; a.alpha*=1; b.alpha*=1;
}
