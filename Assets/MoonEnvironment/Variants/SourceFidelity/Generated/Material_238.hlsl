

void EvaluateSourceMaterial(SourceContext c,out SourceClosure a,out SourceClosure b,out float factor)
{
SourceClosure b0=SourceDefaultClosure(c);
float3 v1=c.normal;
float v2=(v1).z;
float v3=lerp(0.0,1.0,saturate(SourceDivide((v2-0.25),(0.899999976-0.25))));
float v4=(v3*0.200000003);
float v5=c.ao.x;
float v6=(1.0-v5);
float v7=(v6*0.620000005);
float v8=(v4+v7);
float3 v9=c.local;
float v10=(v9).z;
float v11=(v10*(-1.0));
float v12=lerp(0.0,1.0,saturate(SourceDivide((v11-3.8499999),(4.8499999-3.8499999))));
float v13=(v12*0.200000003);
float v14=(v8+v13);
float v15=min(v14,0.720000029);
float v16=SourceFBM(v9,0.0,1.35000002,2.0,0.600000024,2.0,false,true);
float v17=(v16*0.5);
float v18=(v17+0.449999988);
float v19=(v15*v18);
float4 v20=lerp(float4(0.400000006,0.25,0.061999999,1.0),float4(0.104999997,0.101000004,0.0879999995,1.0),saturate(v19));
b0.baseColor=(v20).xyz;
b0.metallic=0.0;
float v23=(v16*0.075000003);
float v24=(v23+0.442499995);
float v25=(v19*0.25999999);
float v26=(v24+v25);
b0.roughness=v26;
b0.ior=1.5;
b0.specular=0.5;
b0.coat=0.0;
b0.coatRoughness=0.0299999993;
b0.transmission=0.0;
b0.alpha=1.0;
b0.anisotropy=0.0;
b0.anisotropicRotation=0.0;
float v36=SourceFBM(v9,0.0,22.0,1.79999995,0.5,2.0,false,true);
float3 v37=SourceBump(c.normal,v36,0.0799999982,0.00200000009,c.position,false);
b0.normal=v37;
b0.emission=(float4(1.0,1.0,1.0,1.0)).xyz*0.0;
a=b0; b=b0; factor=0; a.alpha*=1; b.alpha*=1;
}
