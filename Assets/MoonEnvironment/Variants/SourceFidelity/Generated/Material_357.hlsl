

void EvaluateSourceMaterial(SourceContext c,out SourceClosure a,out SourceClosure b,out float factor)
{
SourceClosure b0=SourceDefaultClosure(c);
float3 v1=c.position;
float v2=SourceFBM(v1,0.0,1.60000002,3.0,0.5,2.0,false,true);
float4 v3=((v2)<=0.800000012?lerp(float4(0.248999998,0.265599996,0.248999998,1.0),float4(0.324000001,0.345600009,0.324000001,1.0),saturate((v2-0.219999999)/0.580000013)):float4(0.324000001,0.345600009,0.324000001,1.0));
b0.baseColor=(v3).xyz;
b0.metallic=0.699999988;
b0.roughness=0.680000007;
b0.ior=1.5;
b0.specular=0.5;
b0.coat=0.0;
b0.coatRoughness=0.0299999993;
b0.transmission=0.0;
b0.alpha=1.0;
b0.anisotropy=0.0;
b0.anisotropicRotation=0.0;
float v15=SourceFBM(v1,0.0,45.0,2.0,0.5,2.0,false,true);
float3 v16=SourceBump(c.normal,v15,0.100000001,0.00899999961,c.position,false);
b0.normal=v16;
b0.emission=(float4(1.0,1.0,1.0,1.0)).xyz*0.0;
a=b0; b=b0; factor=0; a.alpha*=1; b.alpha*=1;
}
