

void EvaluateSourceMaterial(SourceContext c,out SourceClosure a,out SourceClosure b,out float factor)
{
SourceClosure b0=SourceDefaultClosure(c);
float3 v1=c.position;
float3 v2=(v1-float3(1978.05066,2133.54297,206.0));
float v3=SourceFBM(v2,0.0,0.430000007,2.0,0.600000024,2.0,false,true);
float4 v4=((v3)<=0.800000012?lerp(float4(0.111550003,0.126100004,0.130950004,1.0),float4(0.118450001,0.133900002,0.139050007,1.0),saturate((v3-0.200000003)/0.600000009)):float4(0.118450001,0.133900002,0.139050007,1.0));
b0.baseColor=(v4).xyz;
b0.metallic=0.819999993;
float3 v7=(v2*float3(55.0,55.0,0.649999976));
float v8=SourceFBM(v7,0.0,1.0,1.0,0.600000024,2.0,false,true);
float v9=lerp(0.405000001,0.514999986,saturate(SourceDivide((v8-0.0),(1.0-0.0))));
b0.roughness=v9;
b0.ior=1.5;
b0.specular=0.5;
b0.coat=0.0;
b0.coatRoughness=0.0299999993;
b0.transmission=0.0;
b0.alpha=1.0;
b0.anisotropy=0.280000001;
b0.anisotropicRotation=0.0;
float v19=SourceFBM(v2,0.0,140.0,2.0,0.600000024,2.0,false,true);
float3 v20=SourceBump(c.normal,v19,0.200000003,0.000199999995,c.position,false);
b0.normal=v20;
b0.emission=(float4(1.0,1.0,1.0,1.0)).xyz*0.0;
a=b0; b=b0; factor=0; a.alpha*=1; b.alpha*=1;
}
