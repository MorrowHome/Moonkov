

void EvaluateSourceMaterial(SourceContext c,out SourceClosure a,out SourceClosure b,out float factor)
{
SourceClosure b0=SourceDefaultClosure(c);
float3 v1=c.position;
float v2=(v1).z;
float v3=lerp(0.200000003,0.0,saturate(SourceDivide((v2-199.431458),(200.881454-199.431458))));
float3 v4=(v1-float3(1724.0,2122.5,199.431458));
float v5=SourceFBM(v4,0.0,0.720000029,3.0,0.699999988,2.0,false,true);
float v6=SourceFBM(v4,0.0,8.5,2.0,0.5,2.0,false,true);
float v7=(v5*v6);
float v8=lerp(0.0,0.0700000003,saturate(SourceDivide((v7-0.360000014),(0.479999989-0.360000014))));
float4 v9=((v5)<=0.800000012?lerp(float4(0.273600012,0.291599989,0.262800008,1.0),float4(0.425599992,0.453599989,0.408800006,1.0),saturate((v5-0.230000004)/0.570000008)):float4(0.425599992,0.453599989,0.408800006,1.0));
float4 v10=lerp(v9,float4(0.104999997,0.125,0.119000003,1.0),saturate(v8));
float4 v11=lerp(v10,float4(0.239999995,0.228,0.197999999,1.0),saturate(v3));
b0.baseColor=(v11).xyz;
b0.metallic=0.319999993;
float v14=lerp(0.569999993,0.769999981,saturate(SourceDivide((v5-0.150000006),(0.850000024-0.150000006))));
b0.roughness=v14;
b0.ior=1.5;
b0.specular=0.5;
b0.coat=0.0;
b0.coatRoughness=0.0299999993;
b0.transmission=0.0;
b0.alpha=1.0;
b0.anisotropy=0.0;
b0.anisotropicRotation=0.0;
float v24=SourceFBM(v4,0.0,65.0,2.0,0.5,2.0,false,true);
float3 v25=SourceBump(c.normal,v24,0.150000006,0.00899999961,c.position,false);
b0.normal=v25;
b0.emission=(float4(1.0,1.0,1.0,1.0)).xyz*0.0;
a=b0; b=b0; factor=0; a.alpha*=1; b.alpha*=1;
}
