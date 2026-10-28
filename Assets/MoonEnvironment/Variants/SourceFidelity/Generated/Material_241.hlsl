

void EvaluateSourceMaterial(SourceContext c,out SourceClosure a,out SourceClosure b,out float factor)
{
SourceClosure b0=SourceDefaultClosure(c);
float3 v1=c.normal;
float v2=(v1).z;
float v3=lerp(0.0,1.0,saturate(SourceDivide((v2-0.25),(0.899999976-0.25))));
float v4=(v3*0.219999999);
float v5=c.ao.x;
float v6=(1.0-v5);
float v7=(v6*0.579999983);
float v8=(v4+v7);
float3 v9=c.local;
float v10=SourceFBM(v9,0.0,0.850000024,2.0,0.5,2.0,false,true);
float v11=(v10*0.449999988);
float v12=(v11+0.5);
float v13=(v8*v12);
float v14=min(v13,0.600000024);
float4 v15=lerp(float4(0.0549999997,0.0710000023,0.0780000016,1.0),float4(0.104999997,0.103,0.0909999982,1.0),saturate(v14));
b0.baseColor=(v15).xyz;
b0.metallic=0.0;
float v18=(v10*0.0700000003);
float v19=(0.485000014+v18);
float v20=(v14*0.219999999);
float v21=(v19+v20);
b0.roughness=v21;
b0.ior=1.5;
b0.specular=0.5;
b0.coat=0.0;
b0.coatRoughness=0.0299999993;
b0.transmission=0.0;
b0.alpha=1.0;
b0.anisotropy=0.0;
b0.anisotropicRotation=0.0;
float v31=SourceFBM(v9,0.0,28.0,1.60000002,0.5,2.0,false,true);
float3 v32=SourceBump(c.normal,v31,0.075000003,0.00179999997,c.position,false);
b0.normal=v32;
b0.emission=(float4(1.0,1.0,1.0,1.0)).xyz*0.0;
a=b0; b=b0; factor=0; a.alpha*=1; b.alpha*=1;
}
