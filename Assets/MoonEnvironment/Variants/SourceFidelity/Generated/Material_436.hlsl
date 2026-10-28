

void EvaluateSourceMaterial(SourceContext c,out SourceClosure a,out SourceClosure b,out float factor)
{
SourceClosure b0=SourceDefaultClosure(c);
float3 v1=c.local;
float v2=(v1).z;
float v3=(v1).x;
float v4=float(v3<17.4249992);
float v5=(v4*1.16021729);
float v6=(v5+206.125595);
float v7=(v2-v6);
float v8=lerp(0.419999987,0.0,saturate(SourceDivide((v7-0.0250000004),(0.180000007-0.0250000004))));
float v9=SourceFBM(v1,0.0,0.720000029,2.0,0.660000026,2.0,false,true);
float v10=lerp(0.180000007,1.0,saturate(SourceDivide((v9-0.319999993),(0.720000029-0.319999993))));
float v11=(v8*v10);
float v12=SourceFBM(v1,0.0,9.0,1.0,0.660000026,2.0,false,true);
float v13=lerp((-0.00249999994),0.00249999994,saturate(SourceDivide((v12-0.0),(1.0-0.0))));
float v14=(v7+v13);
float v15=lerp(1.0,0.0,saturate(SourceDivide((v14-1.09500003),(1.10699999-1.09500003))));
float v16=lerp(0.0,1.0,saturate(SourceDivide((v9-0.200000003),(0.800000012-0.200000003))));
float4 v17=lerp(float4(0.294999987,0.303000003,0.279000014,1.0),float4(0.375,0.377999991,0.34799999,1.0),saturate(v16));
float v18=lerp(0.0,1.0,saturate(SourceDivide((v9-0.200000003),(0.800000012-0.200000003))));
float4 v19=lerp(float4(0.0869999975,0.108999997,0.104999997,1.0),float4(0.123000003,0.144999996,0.136000007,1.0),saturate(v18));
float4 v20=lerp(v17,v19,saturate(v15));
float4 v21=lerp(v20,float4(0.194999993,0.180000007,0.150999993,1.0),saturate(v11));
b0.baseColor=(v21).xyz;
b0.metallic=0.0;
float v24=lerp(0.779999971,0.889999986,saturate(SourceDivide((v9-0.200000003),(0.800000012-0.200000003))));
float v25=(v15*(-0.140000001));
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
float v36=SourceFBM(v1,0.0,37.0,2.0,0.660000026,2.0,false,true);
float3 v37=SourceBump(c.normal,v36,0.119999997,0.00120000006,c.position,false);
b0.normal=v37;
b0.emission=(float4(1.0,1.0,1.0,1.0)).xyz*0.0;
a=b0; b=b0; factor=0; a.alpha*=1; b.alpha*=1;
}
