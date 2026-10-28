

void EvaluateSourceMaterial(SourceContext c,out SourceClosure a,out SourceClosure b,out float factor)
{
SourceClosure b0=SourceDefaultClosure(c);
float3 v1=c.local;
float v2=(v1).x;
float v3=(v2*v2);
float v4=(v1).z;
float v5=(v4*v4);
float v6=(v3+v5);
float v7=sqrt(max(v6,0));
float v8=lerp(0.0,1.0,saturate(SourceDivide((v7-1.88),(2.02999997-1.88))));
float v9=SourceFBM(v1,0.0,1.35000002,2.0,0.600000024,2.0,false,true);
float v10=lerp(0.0,1.0,saturate(SourceDivide((v9-0.529999971),(0.680000007-0.529999971))));
float v11=(v8*v10);
float3 v12=c.normal;
float v13=(v12).z;
float v14=lerp(0.0,1.0,saturate(SourceDivide((v13-0.25),(0.899999976-0.25))));
float v15=(v14*0.200000003);
float v16=c.ao.x;
float v17=(1.0-v16);
float v18=(v17*0.620000005);
float v19=(v15+v18);
float v20=(v4*(-1.0));
float v21=lerp(0.0,1.0,saturate(SourceDivide((v20-1.39999998),(2.0999999-1.39999998))));
float v22=(v21*0.200000003);
float v23=(v19+v22);
float v24=min(v23,0.720000029);
float v25=(v9*0.5);
float v26=(v25+0.449999988);
float v27=(v24*v26);
float4 v28=lerp(float4(0.419999987,0.40200001,0.349999994,1.0),float4(0.104999997,0.101000004,0.0879999995,1.0),saturate(v27));
float4 v29=lerp(v28,float4(0.360000014,0.389999986,0.409999996,1.0),saturate(v11));
b0.baseColor=(v29).xyz;
b0.metallic=v11;
float v32=(1.0-v11);
float v33=(v32*0.419999987);
float v34=(v11*0.300000012);
float v35=(v33+v34);
b0.roughness=v35;
b0.ior=1.5;
b0.specular=0.5;
b0.coat=0.0;
b0.coatRoughness=0.0299999993;
b0.transmission=0.0;
b0.alpha=1.0;
b0.anisotropy=0.0;
b0.anisotropicRotation=0.0;
float v45=SourceFBM(v1,0.0,22.0,1.79999995,0.5,2.0,false,true);
float3 v46=SourceBump(c.normal,v45,0.0799999982,0.00200000009,c.position,false);
b0.normal=v46;
b0.emission=(float4(1.0,1.0,1.0,1.0)).xyz*0.0;
a=b0; b=b0; factor=0; a.alpha*=1; b.alpha*=1;
}
