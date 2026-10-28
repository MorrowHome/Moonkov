

void EvaluateSourceMaterial(SourceContext c,out SourceClosure a,out SourceClosure b,out float factor)
{
SourceClosure b0=SourceDefaultClosure(c);
float3 v1=c.position;
float v2=distance(v1,float3(1976.95374,2131.79053,210.849945));
float v3=lerp(1.0,0.0,saturate(SourceDivide((v2-0.189999998),(0.569999993-0.189999998))));
float v4=distance(v1,float3(1978.51294,2121.68799,207.486557));
float v5=lerp(1.0,0.0,saturate(SourceDivide((v4-0.189999998),(0.569999993-0.189999998))));
float v6=max(v3,v5);
float3 v7=(v1-float3(1978.05066,2133.54297,206.0));
float v8=SourceFBM(v7,0.0,19.0,1.0,0.600000024,2.0,false,true);
float v9=lerp(0.0,0.649999976,saturate(SourceDivide((v8-0.469999999),(0.670000017-0.469999999))));
float v10=(v6*v9);
float v11=SourceFBM(v7,0.0,0.430000007,2.0,0.600000024,2.0,false,true);
float4 v12=((v11)<=0.800000012?lerp(float4(0.0824500024,0.101850003,0.111550003,1.0),float4(0.0875499994,0.108149998,0.118450001,1.0),saturate((v11-0.200000003)/0.600000009)):float4(0.0875499994,0.108149998,0.118450001,1.0));
float4 v13=lerp(v12,float4(0.300000012,0.319999993,0.310000002,1.0),saturate(v10));
b0.baseColor=(v13).xyz;
b0.metallic=0.720000029;
float v16=lerp(0.485000014,0.555000007,saturate(SourceDivide((v11-0.0),(1.0-0.0))));
float4 v17=lerp(float4(v16,v16,v16,1),float4(0.280000001,0.280000001,0.280000001,1.0),saturate(v10));
b0.roughness=SourceLuminance((v17).xyz);
b0.ior=1.5;
b0.specular=0.5;
b0.coat=0.0;
b0.coatRoughness=0.0299999993;
b0.transmission=0.0;
b0.alpha=1.0;
b0.anisotropy=0.0;
b0.anisotropicRotation=0.0;
float v27=SourceFBM(v7,0.0,105.0,2.0,0.600000024,2.0,false,true);
float3 v28=SourceBump(c.normal,v27,0.200000003,0.000380000012,c.position,false);
b0.normal=v28;
b0.emission=(float4(1.0,1.0,1.0,1.0)).xyz*0.0;
a=b0; b=b0; factor=0; a.alpha*=1; b.alpha*=1;
}
