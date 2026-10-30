

void EvaluateSourceMaterial(SourceContext c,out SourceClosure a,out SourceClosure b,out float factor)
{
SourceClosure b0=SourceDefaultClosure(c);
float3 v1=c.normal;
float v2=(v1).z;
float v3=max(v2,0.0);
float v4=(v3*0.0549999997);
float3 v5=c.position;
float3 v6=(v5-float3(1980.0,2480.0,251.550049));
float v7=SourceFBM(v6,0.0,0.25999999,2.0,0.579999983,2.0,false,true);
float v8=(v7*0.600000024);
float v9=(v8+0.550000012);
float v10=(v4*v9);
float4 v11=lerp(float4(0.274479985,0.312079996,0.309260011,1.0),float4(0.30805999,0.35025999,0.347095013,1.0),saturate(v7));
float4 v12=lerp(v11,float4(0.310000002,0.289999992,0.25,1.0),saturate(v10));
b0.baseColor=(v12).xyz;
b0.metallic=0.200000003;
float v15=(v7-0.5);
float v16=(v15*0.119999997);
float v17=(0.660000026+v16);
b0.roughness=v17;
b0.ior=1.5;
b0.specular=0.319999993;
b0.coat=0.0;
b0.coatRoughness=0.0299999993;
b0.transmission=0.0;
b0.alpha=1.0;
b0.anisotropy=0.0;
b0.anisotropicRotation=0.0;
float v27=SourceFBM(v6,0.0,155.0,2.0,0.699999988,2.0,false,true);
float3 v28=SourceBump(c.normal,v27,0.119999997,0.000349999988,c.position,false);
b0.normal=v28;
b0.emission=(float4(1.0,1.0,1.0,1.0)).xyz*0.0;
a=b0; b=b0; factor=0; a.alpha*=1; b.alpha*=1;
}
