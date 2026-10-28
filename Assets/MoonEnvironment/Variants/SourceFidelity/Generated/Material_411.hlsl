

void EvaluateSourceMaterial(SourceContext c,out SourceClosure a,out SourceClosure b,out float factor)
{
SourceClosure b0=SourceDefaultClosure(c);
float3 v1=c.position;
float3 v2=(v1-float3(1731.0,2321.0,219.224365));
float v3=SourceFBM(v2,0.0,0.720000029,3.0,0.699999988,2.0,false,true);
float4 v4=((v3)<=0.800000012?lerp(float4(0.657000005,0.666000009,0.598500013,1.0),float4(0.817600012,0.828800023,0.744799972,1.0),saturate((v3-0.230000004)/0.570000008)):float4(0.817600012,0.828800023,0.744799972,1.0));
b0.baseColor=(v4).xyz;
b0.metallic=0.0399999991;
float v7=lerp(0.730000019,0.930000007,saturate(SourceDivide((v3-0.150000006),(0.850000024-0.150000006))));
b0.roughness=v7;
b0.ior=1.5;
b0.specular=0.5;
b0.coat=0.0;
b0.coatRoughness=0.0299999993;
b0.transmission=0.0;
b0.alpha=1.0;
b0.anisotropy=0.0;
b0.anisotropicRotation=0.0;
float v17=SourceFBM(v2,0.0,65.0,2.0,0.5,2.0,false,true);
float3 v18=SourceBump(c.normal,v17,0.150000006,0.00899999961,c.position,false);
b0.normal=v18;
b0.emission=(float4(1.0,1.0,1.0,1.0)).xyz*0.0;
a=b0; b=b0; factor=0; a.alpha*=1; b.alpha*=1;
}
