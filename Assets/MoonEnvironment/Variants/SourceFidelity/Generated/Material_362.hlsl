

void EvaluateSourceMaterial(SourceContext c,out SourceClosure a,out SourceClosure b,out float factor)
{
SourceClosure b0=SourceDefaultClosure(c);
float3 v1=c.local;
float3 v2=(v1*float3(0.280000001,1.0,7.5));
float v3=SourceFBM(v2,0.0,4.0,2.0,0.5,2.0,false,true);
float v4=lerp(0.0,0.239999995,saturate(SourceDivide((v3-0.550000012),(0.75-0.550000012))));
float v5=SourceFBM(v1,0.0,0.360000014,2.0,0.579999983,2.0,false,true);
float4 v6=((v5)<=0.829999983?lerp(float4(0.377200007,0.217299998,0.0533000007,1.0),float4(0.505999982,0.291500002,0.0715000033,1.0),saturate((v5-0.180000007)/0.649999976)):float4(0.505999982,0.291500002,0.0715000033,1.0));
float4 v7=lerp(v6,float4(0.219999999,0.25,0.239999995,1.0),saturate(v4));
b0.baseColor=(v7).xyz;
b0.metallic=0.219999999;
float v10=lerp(0.704999983,0.814999998,saturate(SourceDivide((v5-0.0),(1.0-0.0))));
b0.roughness=v10;
b0.ior=1.5;
b0.specular=0.5;
b0.coat=0.0;
b0.coatRoughness=0.0299999993;
b0.transmission=0.0;
b0.alpha=1.0;
b0.anisotropy=0.0;
b0.anisotropicRotation=0.0;
float v20=SourceFBM(v1,0.0,125.0,2.0,0.5,2.0,false,true);
float3 v21=SourceBump(c.normal,v20,0.159999996,0.00079999998,c.position,false);
b0.normal=v21;
b0.emission=(float4(1.0,1.0,1.0,1.0)).xyz*0.0;
a=b0; b=b0; factor=0; a.alpha*=1; b.alpha*=1;
}
