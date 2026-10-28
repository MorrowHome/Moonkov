

void EvaluateSourceMaterial(SourceContext c,out SourceClosure a,out SourceClosure b,out float factor)
{
SourceClosure b0=SourceDefaultClosure(c);
b0.baseColor=(float4(0.419999987,0.455000013,0.469999999,1.0)).xyz;
b0.metallic=0.959999979;
float3 v3=c.position;
float3 v4=(v3-float3(2015.0,2130.0,250.0));
float v5=SourceFBM(v4,0.0,180.0,1.0,0.5,2.0,false,true);
float v6=lerp(0.222000003,0.257999986,saturate(SourceDivide((v5-0.0),(1.0-0.0))));
b0.roughness=v6;
b0.ior=1.5;
b0.specular=0.5;
b0.coat=0.0;
b0.coatRoughness=0.0299999993;
b0.transmission=0.0;
b0.alpha=1.0;
b0.anisotropy=0.0;
b0.anisotropicRotation=0.0;
float3 v16=SourceBump(c.normal,v5,0.129999995,5.99999985e-05,c.position,false);
b0.normal=v16;
b0.emission=(float4(1.0,1.0,1.0,1.0)).xyz*0.0;
a=b0; b=b0; factor=0; a.alpha*=1; b.alpha*=1;
}
