

void EvaluateSourceMaterial(SourceContext c,out SourceClosure a,out SourceClosure b,out float factor)
{
SourceClosure b0=SourceDefaultClosure(c);
float3 v1=c.normal;
float v2=dot(v1,float3(0.0,0.0,1.0));
float v3=lerp(0.0,1.0,saturate(SourceDivide((v2-0.5),(0.980000019-0.5))));
float v4=(0.0*v3);
float4 v5=lerp(float4(0.0900000036,0.0900000036,0.0900000036,1.0),float4(0.159999996,0.158999994,0.155000001,1.0),saturate(v4));
b0.baseColor=(v5).xyz;
b0.metallic=0.0;
float3 v8=c.position;
float3 v9=(v8-float3(2282.42847,2024.78235,233.575241));
float v10=SourceFBM(v9,0.0,1.89999998,2.0,0.5,2.0,false,true);
float v11=lerp(0.699999988,0.819999993,saturate(SourceDivide((v10-0.200000003),(0.800000012-0.200000003))));
float v12=(v4*0.550000012);
float v13=(v11+v12);
b0.roughness=v13;
b0.ior=1.48000002;
b0.specular=0.449999988;
b0.coat=0.0;
b0.coatRoughness=0.0299999993;
b0.transmission=0.0;
b0.alpha=1.0;
b0.anisotropy=0.0;
b0.anisotropicRotation=0.0;
float v23=SourceFBM(v9,0.0,18.0,2.0,0.5,2.0,false,true);
float3 v24=SourceBump(c.normal,v23,0.200000003,0.000600000028,c.position,false);
b0.normal=v24;
b0.emission=(float4(1.0,1.0,1.0,1.0)).xyz*0.0;
a=b0; b=b0; factor=0; a.alpha*=1; b.alpha*=1;
}
