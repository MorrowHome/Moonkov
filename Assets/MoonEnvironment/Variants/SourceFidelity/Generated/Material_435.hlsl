

void EvaluateSourceMaterial(SourceContext c,out SourceClosure a,out SourceClosure b,out float factor)
{
SourceClosure b0=SourceDefaultClosure(c);
float3 v1=c.local;
float v2=SourceFBM(v1,0.0,1.20000005,2.0,0.660000026,2.0,false,true);
float4 v3=lerp(float4(0.119999997,0.135000005,0.123000003,1.0),float4(0.175999999,0.180999994,0.157000005,1.0),saturate(v2));
b0.baseColor=(v3).xyz;
b0.metallic=0.0;
float v6=lerp(0.730000019,0.870000005,saturate(SourceDivide((v2-0.200000003),(0.800000012-0.200000003))));
b0.roughness=v6;
b0.ior=1.5;
b0.specular=0.5;
b0.coat=0.0;
b0.coatRoughness=0.0299999993;
b0.transmission=0.0;
b0.alpha=1.0;
b0.anisotropy=0.0;
b0.anisotropicRotation=0.0;
float v16=SourceFBM(v1,0.0,31.0,2.0,0.660000026,2.0,false,true);
float3 v17=SourceBump(c.normal,v16,0.159999996,0.00179999997,c.position,false);
b0.normal=v17;
b0.emission=(float4(1.0,1.0,1.0,1.0)).xyz*0.0;
a=b0; b=b0; factor=0; a.alpha*=1; b.alpha*=1;
}
