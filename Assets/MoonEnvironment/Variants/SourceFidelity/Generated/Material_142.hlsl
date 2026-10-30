

void EvaluateSourceMaterial(SourceContext c,out SourceClosure a,out SourceClosure b,out float factor)
{
SourceClosure b0=SourceDefaultClosure(c);
float3 v1=c.position;
float3 v2=(v1-float3(1980.0,2480.0,251.550049));
float v3=(v2).x;
float v4=(v3-(-33.2999992));
float v5=abs(v4);
float v6=(v5*1.29999995);
float v7=(1.0-v6);
float v8=max(v7,0.0);
float v9=(v2).y;
float v10=float(v9>9.80000019);
float v11=(v8*v10);
float3 v12=c.normal;
float v13=(v12).z;
float v14=max(v13,0.0);
float v15=(v11*v14);
float v16=(v15*0.300000012);
float v17=max(v13,0.0);
float v18=(v17*0.0350000001);
float v19=c.ao.x;
float v20=(1.0-v19);
float v21=(v20*0.159999996);
float v22=(v18+v21);
float v23=SourceFBM(v2,0.0,0.25999999,2.0,0.579999983,2.0,false,true);
float v24=(v23*0.600000024);
float v25=(v24+0.550000012);
float v26=(v22*v25);
float4 v27=lerp(float4(0.11468,0.141000003,0.148519993,1.0),float4(0.128710002,0.158250004,0.166690007,1.0),saturate(v23));
float4 v28=lerp(v27,float4(0.310000002,0.289999992,0.25,1.0),saturate(v26));
float4 v29=lerp(v28,float4(0.189999998,0.204999998,0.195999995,1.0),saturate(v16));
b0.baseColor=(v29).xyz;
b0.metallic=0.239999995;
float v32=(v23-0.5);
float v33=(v32*0.119999997);
float v34=(0.75999999+v33);
float v35=(v15*0.0900000036);
float v36=(v34-v35);
b0.roughness=v36;
b0.ior=1.5;
b0.specular=0.319999993;
b0.coat=0.0;
b0.coatRoughness=0.0299999993;
b0.transmission=0.0;
b0.alpha=1.0;
b0.anisotropy=0.0;
b0.anisotropicRotation=0.0;
float v46=SourceFBM(v2,0.0,155.0,2.0,0.699999988,2.0,false,true);
float3 v47=SourceBump(c.normal,v46,0.119999997,0.000349999988,c.position,false);
b0.normal=v47;
b0.emission=(float4(1.0,1.0,1.0,1.0)).xyz*0.0;
a=b0; b=b0; factor=0; a.alpha*=1; b.alpha*=1;
}
