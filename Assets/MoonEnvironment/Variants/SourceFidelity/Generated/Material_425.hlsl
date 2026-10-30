

void EvaluateSourceMaterial(SourceContext c,out SourceClosure a,out SourceClosure b,out float factor)
{
SourceClosure b0=SourceDefaultClosure(c);
float3 v1=c.position;
float3 v2=(v1-float3(1731.0,2321.0,219.224365));
float v3=SourceFBM(v2,0.0,0.720000029,3.0,0.699999988,2.0,false,true);
float v4=SourceFBM(v2,0.0,8.5,2.0,0.5,2.0,false,true);
float v5=(v3*v4);
float v6=lerp(0.0,0.319999993,saturate(SourceDivide((v5-0.360000014),(0.479999989-0.360000014))));
float4 v7=((v3)<=0.800000012?lerp(float4(0.136800006,0.165600002,0.154799998,1.0),float4(0.212799996,0.257600009,0.240799993,1.0),saturate((v3-0.230000004)/0.570000008)):float4(0.212799996,0.257600009,0.240799993,1.0));
float4 v8=lerp(v7,float4(0.104999997,0.125,0.119000003,1.0),saturate(v6));
b0.baseColor=(v8).xyz;
b0.metallic=0.400000006;
float v11=lerp(0.629999995,0.829999983,saturate(SourceDivide((v3-0.150000006),(0.850000024-0.150000006))));
b0.roughness=v11;
b0.ior=1.5;
b0.specular=0.5;
b0.coat=0.0;
b0.coatRoughness=0.0299999993;
b0.transmission=0.0;
b0.alpha=1.0;
b0.anisotropy=0.0;
b0.anisotropicRotation=0.0;
float v21=SourceFBM(v2,0.0,65.0,2.0,0.5,2.0,false,true);
float3 v22=SourceBump(c.normal,v21,0.150000006,0.00899999961,c.position,false);
b0.normal=v22;
b0.emission=(float4(1.0,1.0,1.0,1.0)).xyz*0.0;
a=b0; b=b0; factor=0; a.alpha*=1; b.alpha*=1;
}
