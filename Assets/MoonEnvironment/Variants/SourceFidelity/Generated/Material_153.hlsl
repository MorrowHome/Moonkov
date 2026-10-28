

void EvaluateSourceMaterial(SourceContext c,out SourceClosure a,out SourceClosure b,out float factor)
{
SourceClosure b0=SourceDefaultClosure(c);
float3 v1=c.normal;
float v2=(v1).z;
float v3=max(v2,0.0);
float v4=(v3*0.0179999992);
float v5=c.ao.x;
float v6=(1.0-v5);
float v7=(v6*0.075000003);
float v8=(v4+v7);
float3 v9=c.position;
float3 v10=(v9-float3(1980.0,2480.0,251.550049));
float v11=(v10).z;
float v12=max(v11,0.0);
float v13=(v12*3.4000001);
float v14=(1.0-v13);
float v15=max(v14,0.0);
float v16=(v15*0.0850000009);
float v17=(v8+v16);
float v18=c.random;
float v19=(v18*17.7000008);
float v20=SourceFBM(v10,v19,0.430000007,2.0,0.550000012,2.0,true,true);
float v21=(v20*0.899999976);
float v22=(0.400000006+v21);
float v23=(v17*v22);
float4 v24=lerp(float4(0.151199996,0.173879996,0.178605005,1.0),float4(0.168799996,0.194120005,0.199395001,1.0),saturate(v20));
float v25=(v18*0.0560000017);
float v26=(0.972000003+v25);
float4 v27=lerp(v24,(v24*float4(v26,v26,v26,1)),saturate(1.0));
float4 v28=lerp(v27,float4(0.25,0.229000002,0.194000006,1.0),saturate(v23));
b0.baseColor=(v28).xyz;
b0.metallic=0.74000001;
float v31=(v20-0.5);
float v32=(v31*0.100000001);
float v33=(0.589999974+v32);
b0.roughness=v33;
b0.ior=1.5;
b0.specular=0.319999993;
b0.coat=0.0;
b0.coatRoughness=0.0299999993;
b0.transmission=0.0;
b0.alpha=1.0;
b0.anisotropy=0.0;
b0.anisotropicRotation=0.0;
float v43=(v18*9.30000019);
float v44=SourceFBM(v10,v43,180.0,2.0,0.629999995,2.0,true,true);
float3 v45=SourceBump(c.normal,v44,0.109999999,0.000250000012,c.position,false);
b0.normal=v45;
b0.emission=(float4(1.0,1.0,1.0,1.0)).xyz*0.0;
a=b0; b=b0; factor=0; a.alpha*=1; b.alpha*=1;
}
