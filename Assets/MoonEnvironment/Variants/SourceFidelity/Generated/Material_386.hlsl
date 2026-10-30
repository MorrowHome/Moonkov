

void EvaluateSourceMaterial(SourceContext c,out SourceClosure a,out SourceClosure b,out float factor)
{
SourceClosure b0=SourceDefaultClosure(c);
float3 v1=float3(dot(float4(c.position,1),float4(0.965925872,(-0.258819073),0.0,(-1048.17224))),dot(float4(c.position,1),float4(0.258819073,0.965925872,(-0.0),(-2637.14575))),dot(float4(c.position,1),float4(0.0,(-0.0),1.0,(-216.300018))));
float v2=(v1).y;
float v3=(v2+7.9000001);
float v4=SourcePingPong(v3,0.474999994);
float v5=lerp(0.330000013,0.0,saturate(SourceDivide((v4-0.0350000001),(0.109999999-0.0350000001))));
float v6=SourceFBM(v1,0.0,0.899999976,2.0,0.5,2.0,false,true);
float v7=lerp(0.25,1.0,saturate(SourceDivide((v6-0.300000012),(0.699999988-0.300000012))));
float v8=(v5*v7);
float v9=(v2+7.9000001);
float v10=SourceDivide(v9,0.949999988);
float v11=floor(v10);
float v12=SourceWhiteNoise1(v11);
float v13=(v12*0.119999997);
float v14=SourceFBM(v1,0.0,0.140000001,2.0,0.5,2.0,false,true);
float v15=lerp(0.0,1.0,saturate(SourceDivide((v14-0.200000003),(0.800000012-0.200000003))));
float4 v16=lerp(float4(0.063000001,0.0820000023,0.0869999975,1.0),float4(0.0930000022,0.111000001,0.114,1.0),saturate(v15));
float4 v17=lerp(v16,float4(0.129999995,0.141000003,0.136000007,1.0),saturate(v13));
float4 v18=lerp(v17,float4(0.180000007,0.180000007,0.158999994,1.0),saturate(v8));
b0.baseColor=(v18).xyz;
b0.metallic=0.159999996;
float v21=SourceFBM(v1,0.0,0.800000012,2.0,0.5,2.0,false,true);
float v22=lerp(0.579999983,0.720000029,saturate(SourceDivide((v21-0.200000003),(0.800000012-0.200000003))));
b0.roughness=v22;
b0.ior=1.5;
b0.specular=0.5;
b0.coat=0.0;
b0.coatRoughness=0.0299999993;
b0.transmission=0.0;
b0.alpha=1.0;
b0.anisotropy=0.0;
b0.anisotropicRotation=0.0;
float v32=SourceFBM(v1,0.0,180.0,1.0,0.5,2.0,false,true);
float3 v33=SourceBump(c.normal,v32,0.200000003,0.000180000003,c.position,false);
b0.normal=v33;
b0.emission=(float4(1.0,1.0,1.0,1.0)).xyz*0.0;
a=b0; b=b0; factor=0; a.alpha*=1; b.alpha*=1;
}
