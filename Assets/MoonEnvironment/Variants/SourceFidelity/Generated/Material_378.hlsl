

void EvaluateSourceMaterial(SourceContext c,out SourceClosure a,out SourceClosure b,out float factor)
{
SourceClosure b0=SourceDefaultClosure(c);
float3 v1=float3(dot(float4(c.position,1),float4(0.965925872,(-0.258819073),0.0,(-1048.17224))),dot(float4(c.position,1),float4(0.258819073,0.965925872,(-0.0),(-2637.14575))),dot(float4(c.position,1),float4(0.0,(-0.0),1.0,(-216.300018))));
float v2=SourceFBM(v1,0.0,0.649999976,2.0,0.5,2.0,false,true);
float4 v3=((v2)<=0.839999974?lerp(float4(0.0126,0.109200001,0.109200001,1.0),float4(0.0169500001,0.146899998,0.146899998,1.0),saturate((v2-0.159999996)/0.679999977)):float4(0.0169500001,0.146899998,0.146899998,1.0));
b0.baseColor=(v3).xyz;
b0.metallic=0.300000012;
float v6=lerp(0.170000002,0.330000013,saturate(SourceDivide((v2-0.0),(1.0-0.0))));
b0.roughness=v6;
b0.ior=1.5;
b0.specular=0.5;
b0.coat=0.0;
b0.coatRoughness=0.0299999993;
b0.transmission=0.0;
b0.alpha=1.0;
b0.anisotropy=0.0;
b0.anisotropicRotation=0.0;
b0.emission=(float4(1.0,1.0,1.0,1.0)).xyz*0.0;
a=b0; b=b0; factor=0; a.alpha*=1; b.alpha*=1;
}
