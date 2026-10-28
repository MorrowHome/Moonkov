

void EvaluateSourceMaterial(SourceContext c,out SourceClosure a,out SourceClosure b,out float factor)
{
SourceClosure b0=SourceDefaultClosure(c);
float3 v1=c.position;
float3 v2=(v1-float3(1978.05066,2133.54297,206.0));
float v3=SourceFBM(v2,0.0,0.430000007,2.0,0.600000024,2.0,false,true);
float4 v4=((v3)<=0.800000012?lerp(float4(0.227950007,0.271600008,0.281300008,1.0),float4(0.242050007,0.288399994,0.298700005,1.0),saturate((v3-0.200000003)/0.600000009)):float4(0.242050007,0.288399994,0.298700005,1.0));
b0.baseColor=(v4).xyz;
b0.metallic=0.0;
float v7=lerp(0.455000013,0.524999976,saturate(SourceDivide((v3-0.0),(1.0-0.0))));
b0.roughness=v7;
b0.ior=1.5;
b0.specular=0.5;
b0.coat=0.0;
b0.coatRoughness=0.0299999993;
b0.transmission=0.0;
b0.alpha=1.0;
b0.anisotropy=0.0;
b0.anisotropicRotation=0.0;
float v17=SourceFBM(v2,0.0,155.0,2.0,0.600000024,2.0,false,true);
float3 v18=SourceBump(c.normal,v17,0.200000003,0.000220000002,c.position,false);
b0.normal=v18;
b0.emission=(float4(1.0,1.0,1.0,1.0)).xyz*0.0;
a=b0; b=b0; factor=0; a.alpha*=1; b.alpha*=1;
}
