

void EvaluateSourceMaterial(SourceContext c,out SourceClosure a,out SourceClosure b,out float factor)
{
SourceClosure b0=SourceDefaultClosure(c);
float3 v1=c.normal;
float v2=(v1).z;
float v3=lerp(0.0,1.0,saturate(SourceDivide((v2-0.649999976),(0.970000029-0.649999976))));
float3 v4=c.local;
float v5=SourceFBM(v4,0.0,6.0,2.0,0.660000026,2.0,false,true);
float v6=lerp(0.0,0.129999995,saturate(SourceDivide((v5-0.400000006),(0.75-0.400000006))));
float v7=(v3*v6);
float v8=SourceFBM(v4,0.0,3.0,2.0,0.660000026,2.0,false,true);
float v9=lerp(0.0,1.0,saturate(SourceDivide((v8-0.200000003),(0.800000012-0.200000003))));
float4 v10=lerp(float4(0.0430000015,0.0869999975,0.100000001,1.0),float4(0.0610000007,0.115999997,0.127000004,1.0),saturate(v9));
float4 v11=lerp(v10,float4(0.219999999,0.209000006,0.174999997,1.0),saturate(v7));
b0.baseColor=(v11).xyz;
b0.metallic=0.0350000001;
float v14=lerp(0.409999996,0.519999981,saturate(SourceDivide((v8-0.200000003),(0.800000012-0.200000003))));
b0.roughness=v14;
b0.ior=1.5;
b0.specular=0.5;
b0.coat=0.0;
b0.coatRoughness=0.0299999993;
b0.transmission=0.0;
b0.alpha=1.0;
b0.anisotropy=0.0;
b0.anisotropicRotation=0.0;
float v24=SourceFBM(v4,0.0,85.0,1.0,0.660000026,2.0,false,true);
float3 v25=SourceBump(c.normal,v24,0.0500000007,0.000349999988,c.position,false);
b0.normal=v25;
b0.emission=(float4(1.0,1.0,1.0,1.0)).xyz*0.0;
a=b0; b=b0; factor=0; a.alpha*=1; b.alpha*=1;
}
