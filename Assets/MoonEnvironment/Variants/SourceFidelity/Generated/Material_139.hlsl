

void EvaluateSourceMaterial(SourceContext c,out SourceClosure a,out SourceClosure b,out float factor)
{
SourceClosure b0=SourceDefaultClosure(c);
float3 v1=c.normal;
float v2=(v1).z;
float v3=max(v2,0.0);
float v4=(v3*0.0549999997);
float v5=c.ao.x;
float v6=(1.0-v5);
float v7=(v6*0.159999996);
float v8=(v4+v7);
float3 v9=c.position;
float3 v10=(v9-float3(1980.0,2480.0,251.550049));
float v11=(v10).z;
float v12=(v11-0.0);
float v13=max(v12,0.0);
float v14=(v13*1.60000002);
float v15=(1.0-v14);
float v16=max(v15,0.0);
float v17=(v16*0.129999995);
float v18=(v8+v17);
float v19=SourceFBM(v10,0.0,0.25999999,2.0,0.579999983,2.0,false,true);
float v20=(v19*0.600000024);
float v21=(v20+0.550000012);
float v22=(v18*v21);
float v23=float(v11<0.819999993);
float4 v24=lerp(float4(0.239700004,0.226539999,0.199279994,1.0),float4(0.269024998,0.254254997,0.223660007,1.0),saturate(v19));
float4 v25=lerp(v24,float4(0.115000002,0.136999995,0.138999999,1.0),saturate(v23));
float4 v26=lerp(v25,float4(0.310000002,0.289999992,0.25,1.0),saturate(v22));
b0.baseColor=(v26).xyz;
b0.metallic=0.0;
float v29=(v19-0.5);
float v30=(v29*0.119999997);
float v31=(0.899999976+v30);
b0.roughness=v31;
b0.ior=1.5;
b0.specular=0.319999993;
b0.coat=0.0;
b0.coatRoughness=0.0299999993;
b0.transmission=0.0;
b0.alpha=1.0;
b0.anisotropy=0.0;
b0.anisotropicRotation=0.0;
float v41=SourceFBM(v10,0.0,72.0,2.0,0.699999988,2.0,false,true);
float3 v42=SourceBump(c.normal,v41,0.180000007,0.000899999985,c.position,false);
b0.normal=v42;
b0.emission=(float4(1.0,1.0,1.0,1.0)).xyz*0.0;
a=b0; b=b0; factor=0; a.alpha*=1; b.alpha*=1;
}
