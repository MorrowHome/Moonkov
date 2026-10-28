

void EvaluateSourceMaterial(SourceContext c,out SourceClosure a,out SourceClosure b,out float factor)
{
SourceClosure b0=SourceDefaultClosure(c);
float3 v1=c.normal;
float v2=(v1).z;
float v3=max(v2,0.0);
float v4=(v3*0.0179999992);
float3 v5=c.position;
float3 v6=(v5-float3(1980.0,2480.0,251.550049));
float v7=c.random;
float v8=(v7*17.7000008);
float v9=SourceFBM(v6,v8,0.430000007,2.0,0.550000012,2.0,true,true);
float v10=(v9*0.899999976);
float v11=(0.400000006+v10);
float v12=(v4*v11);
float4 v13=lerp(float4(0.373275012,0.384615004,0.347759992,1.0),float4(0.41672501,0.429385006,0.38824001,1.0),saturate(v9));
float v14=(v7*0.0560000017);
float v15=(0.972000003+v14);
float4 v16=lerp(v13,(v13*float4(v15,v15,v15,1)),saturate(1.0));
float4 v17=lerp(v16,float4(0.25,0.229000002,0.194000006,1.0),saturate(v12));
b0.baseColor=(v17).xyz;
b0.metallic=0.0149999997;
float v20=(v9-0.5);
float v21=(v20*0.100000001);
float v22=(0.699999988+v21);
b0.roughness=v22;
b0.ior=1.5;
b0.specular=0.319999993;
b0.coat=0.0;
b0.coatRoughness=0.0299999993;
b0.transmission=0.0;
b0.alpha=1.0;
b0.anisotropy=0.0;
b0.anisotropicRotation=0.0;
float v32=(v7*9.30000019);
float v33=SourceFBM(v6,v32,180.0,2.0,0.629999995,2.0,true,true);
float3 v34=SourceBump(c.normal,v33,0.109999999,0.000250000012,c.position,false);
b0.normal=v34;
b0.emission=(float4(1.0,1.0,1.0,1.0)).xyz*0.0;
a=b0; b=b0; factor=0; a.alpha*=1; b.alpha*=1;
}
