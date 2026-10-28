

void EvaluateSourceMaterial(SourceContext c,out SourceClosure a,out SourceClosure b,out float factor)
{
SourceClosure b0=SourceDefaultClosure(c);
float3 v1=c.local;
float v2=SourceFBM(v1,0.0,4.0,2.0,0.660000026,2.0,false,true);
float v3=lerp(0.0,1.0,saturate(SourceDivide((v2-0.200000003),(0.800000012-0.200000003))));
float4 v4=lerp(float4(0.264999986,0.286000013,0.284999996,1.0),float4(0.337000012,0.354000002,0.34799999,1.0),saturate(v3));
b0.baseColor=(v4).xyz;
b0.metallic=0.899999976;
float3 v7=(v1*float3(46.0,2.0,3.0));
float v8=SourceFBM(v7,0.0,1.20000005,2.0,0.660000026,2.0,false,true);
float v9=lerp(0.310000002,0.409999996,saturate(SourceDivide((v8-0.200000003),(0.800000012-0.200000003))));
float v10=lerp(0.0,0.100000001,saturate(SourceDivide((v2-0.400000006),(0.75-0.400000006))));
float v11=(v9+v10);
b0.roughness=v11;
b0.ior=1.5;
b0.specular=0.5;
b0.coat=0.0;
b0.coatRoughness=0.0299999993;
b0.transmission=0.0;
b0.alpha=1.0;
b0.anisotropy=0.300000012;
b0.anisotropicRotation=0.0;
float3 v21=SourceBump(c.normal,v8,0.0350000001,0.000199999995,c.position,false);
b0.normal=v21;
b0.emission=(float4(1.0,1.0,1.0,1.0)).xyz*0.0;
a=b0; b=b0; factor=0; a.alpha*=1; b.alpha*=1;
}
