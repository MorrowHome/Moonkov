

void EvaluateSourceMaterial(SourceContext c,out SourceClosure a,out SourceClosure b,out float factor)
{
SourceClosure b0=SourceDefaultClosure(c);
float3 v1=c.local;
float v2=SourceFBM(v1,0.0,17.0,2.0,0.660000026,2.0,false,true);
float v3=lerp(0.0,1.0,saturate(SourceDivide((v2-0.200000003),(0.800000012-0.200000003))));
float4 v4=lerp(float4(0.234999999,0.257999986,0.246999994,1.0),float4(0.289999992,0.307000011,0.286000013,1.0),saturate(v3));
b0.baseColor=(v4).xyz;
b0.metallic=0.75999999;
float v7=lerp(0.430000007,0.569999993,saturate(SourceDivide((v2-0.200000003),(0.800000012-0.200000003))));
b0.roughness=v7;
b0.ior=1.5;
b0.specular=0.5;
b0.coat=0.0;
b0.coatRoughness=0.0299999993;
b0.transmission=0.0;
b0.alpha=1.0;
b0.anisotropy=0.0;
b0.anisotropicRotation=0.0;
float3 v17=SourceBump(c.normal,v2,0.0350000001,0.000220000002,c.position,false);
b0.normal=v17;
b0.emission=(float4(1.0,1.0,1.0,1.0)).xyz*0.0;
a=b0; b=b0; factor=0; a.alpha*=1; b.alpha*=1;
}
