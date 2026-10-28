

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
float v11=SourceFBM(v10,0.0,0.25999999,2.0,0.579999983,2.0,false,true);
float v12=(v11*0.600000024);
float v13=(v12+0.550000012);
float v14=(v8*v13);
float4 v15=lerp(float4(0.0601599999,0.078019999,0.0855399966,1.0),float4(0.06752,0.0875649974,0.0960050002,1.0),saturate(v11));
float4 v16=lerp(v15,float4(0.310000002,0.289999992,0.25,1.0),saturate(v14));
b0.baseColor=(v16).xyz;
b0.metallic=0.430000007;
float v19=(v11-0.5);
float v20=(v19*0.119999997);
float v21=(0.560000002+v20);
b0.roughness=v21;
b0.ior=1.5;
b0.specular=0.319999993;
b0.coat=0.0;
b0.coatRoughness=0.0299999993;
b0.transmission=0.0;
b0.alpha=1.0;
b0.anisotropy=0.0;
b0.anisotropicRotation=0.0;
float v31=SourceFBM(v10,0.0,155.0,2.0,0.699999988,2.0,false,true);
float3 v32=SourceBump(c.normal,v31,0.119999997,0.000349999988,c.position,false);
b0.normal=v32;
b0.emission=(float4(1.0,1.0,1.0,1.0)).xyz*0.0;
a=b0; b=b0; factor=0; a.alpha*=1; b.alpha*=1;
}
