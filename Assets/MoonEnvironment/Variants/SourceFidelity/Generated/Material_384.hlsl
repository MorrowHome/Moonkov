

void EvaluateSourceMaterial(SourceContext c,out SourceClosure a,out SourceClosure b,out float factor)
{
SourceClosure b0=SourceDefaultClosure(c);
float3 v1=float3(dot(float4(c.position,1),float4(0.965925872,(-0.258819073),0.0,(-1048.17224))),dot(float4(c.position,1),float4(0.258819073,0.965925872,(-0.0),(-2637.14575))),dot(float4(c.position,1),float4(0.0,(-0.0),1.0,(-216.300018))));
float v2=(v1).x;
float v3=abs(v2);
float v4=lerp(0.0,0.479999989,saturate(SourceDivide((v3-5.6500001),(6.73999977-5.6500001))));
float v5=SourceFBM(v1,0.0,2.0999999,2.0,0.5,2.0,false,true);
float v6=lerp(0.200000003,1.0,saturate(SourceDivide((v5-0.280000001),(0.74000001-0.280000001))));
float v7=(v4*v6);
float v8=abs(v2);
float v9=(v8-1.08000004);
float v10=abs(v9);
float v11=lerp(0.400000006,0.0,saturate(SourceDivide((v10-0.119999997),(0.479999989-0.119999997))));
float v12=(v1).y;
float v13=lerp(0.0,1.0,saturate(SourceDivide((v12-7.80000019),(10.3000002-7.80000019))));
float v14=(v11*v13);
float3 v15=(v1*float3(2.0,0.230000004,1.0));
float v16=SourceFBM(v15,0.0,4.0,2.0,0.5,2.0,false,true);
float v17=lerp(0.200000003,1.0,saturate(SourceDivide((v16-0.270000011),(0.720000029-0.270000011))));
float v18=(v14*v17);
float3 v19=(v1*float3(0.550000012,14.0,0.300000012));
float v20=SourceFBM(v19,0.0,3.0,1.0,0.5,2.0,false,true);
float v21=(v20*0.0700000003);
float v22=SourceFBM(v1,0.0,0.219999999,2.0,0.5,2.0,false,true);
float v23=lerp(0.0,1.0,saturate(SourceDivide((v22-0.200000003),(0.800000012-0.200000003))));
float4 v24=lerp(float4(0.204999998,0.203999996,0.186000004,1.0),float4(0.280000001,0.273999989,0.247999996,1.0),saturate(v23));
float4 v25=lerp(v24,float4(0.150000006,0.150000006,0.135000005,1.0),saturate(v21));
float4 v26=lerp(v25,float4(0.0920000002,0.0979999974,0.0930000022,1.0),saturate(v18));
float4 v27=lerp(v26,float4(0.125,0.126000002,0.116999999,1.0),saturate(v7));
b0.baseColor=(v27).xyz;
b0.metallic=0.0;
float4 v30=lerp(float4(0.939999998,0.939999998,0.939999998,1.0),float4(0.800000012,0.800000012,0.800000012,1.0),saturate(v18));
b0.roughness=SourceLuminance((v30).xyz);
b0.ior=1.5;
b0.specular=0.5;
b0.coat=0.0;
b0.coatRoughness=0.0299999993;
b0.transmission=0.0;
b0.alpha=1.0;
b0.anisotropy=0.0;
b0.anisotropicRotation=0.0;
float v40=SourceFBM(v1,0.0,90.0,2.0,0.5,2.0,false,true);
float3 v41=SourceBump(c.normal,v40,0.349999994,0.00079999998,c.position,false);
b0.normal=v41;
b0.emission=(float4(1.0,1.0,1.0,1.0)).xyz*0.0;
a=b0; b=b0; factor=0; a.alpha*=1; b.alpha*=1;
}
