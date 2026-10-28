

void EvaluateSourceMaterial(SourceContext c,out SourceClosure a,out SourceClosure b,out float factor)
{
SourceClosure b0=SourceDefaultClosure(c);
float3 v1=c.local;
float3 v2=(v1*float3(0.280000001,1.0,7.5));
float v3=SourceFBM(v2,0.0,4.0,2.0,0.5,2.0,false,true);
float v4=lerp(0.0,0.5,saturate(SourceDivide((v3-0.550000012),(0.75-0.550000012))));
float v5=(v1).z;
float v6=lerp(0.239999995,0.0,saturate(SourceDivide((v5-0.100000001),(1.14999998-0.100000001))));
float v7=SourceFBM(v1,0.0,0.360000014,2.0,0.579999983,2.0,false,true);
float v8=(v6*v7);
float4 v9=((v7)<=0.829999983?lerp(float4(0.229599997,0.250099987,0.237800002,1.0),float4(0.307999998,0.335500002,0.319000006,1.0),saturate((v7-0.180000007)/0.649999976)):float4(0.307999998,0.335500002,0.319000006,1.0));
float4 v10=lerp(v9,float4(0.280000001,0.264999986,0.230000004,1.0),saturate(v8));
float4 v11=lerp(v10,float4(0.219999999,0.25,0.239999995,1.0),saturate(v4));
b0.baseColor=(v11).xyz;
b0.metallic=0.579999983;
float v14=lerp(0.574999988,0.685000002,saturate(SourceDivide((v7-0.0),(1.0-0.0))));
b0.roughness=v14;
b0.ior=1.5;
b0.specular=0.5;
b0.coat=0.0;
b0.coatRoughness=0.0299999993;
b0.transmission=0.0;
b0.alpha=1.0;
b0.anisotropy=0.0;
b0.anisotropicRotation=0.0;
float v24=SourceFBM(v1,0.0,125.0,2.0,0.5,2.0,false,true);
float3 v25=SourceBump(c.normal,v24,0.159999996,0.00079999998,c.position,false);
b0.normal=v25;
b0.emission=(float4(1.0,1.0,1.0,1.0)).xyz*0.0;
a=b0; b=b0; factor=0; a.alpha*=1; b.alpha*=1;
}
