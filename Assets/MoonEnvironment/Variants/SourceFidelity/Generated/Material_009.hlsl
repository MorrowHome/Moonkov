

void EvaluateSourceMaterial(SourceContext c,out SourceClosure a,out SourceClosure b,out float factor)
{
SourceClosure b0=SourceDefaultClosure(c);
float3 v1=c.normal;
float v2=(v1).z;
float v3=lerp(0.0,0.140000001,saturate(SourceDivide((v2-0.850000024),(1.0-0.850000024))));
float3 v4=c.position;
float3 v5=(v4-float3(1978.05066,2133.54297,206.0));
float v6=SourceFBM(v5,0.0,2.4000001,2.0,0.600000024,2.0,false,true);
float v7=(v3*v6);
float v8=SourceFBM(v5,0.0,0.430000007,2.0,0.600000024,2.0,false,true);
float4 v9=((v8)<=0.800000012?lerp(float4(0.111550003,0.152290002,0.168779999,1.0),float4(0.118450001,0.161709994,0.179220006,1.0),saturate((v8-0.200000003)/0.600000009)):float4(0.118450001,0.161709994,0.179220006,1.0));
float4 v10=lerp(v9,float4(0.25999999,0.25,0.224999994,1.0),saturate(v7));
b0.baseColor=(v10).xyz;
b0.metallic=0.0;
float v13=lerp(0.495000005,0.564999998,saturate(SourceDivide((v8-0.0),(1.0-0.0))));
b0.roughness=v13;
b0.ior=1.5;
b0.specular=0.5;
b0.coat=0.0;
b0.coatRoughness=0.0299999993;
b0.transmission=0.0;
b0.alpha=1.0;
b0.anisotropy=0.0;
b0.anisotropicRotation=0.0;
float v23=SourceFBM(v5,0.0,125.0,2.0,0.600000024,2.0,false,true);
float3 v24=SourceBump(c.normal,v23,0.200000003,0.000280000007,c.position,false);
b0.normal=v24;
b0.emission=(float4(1.0,1.0,1.0,1.0)).xyz*0.0;
a=b0; b=b0; factor=0; a.alpha*=1; b.alpha*=1;
}
