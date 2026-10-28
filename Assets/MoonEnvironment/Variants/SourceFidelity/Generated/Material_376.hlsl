

void EvaluateSourceMaterial(SourceContext c,out SourceClosure a,out SourceClosure b,out float factor)
{
SourceClosure b0=SourceDefaultClosure(c);
float3 v1=float3(dot(float4(c.position,1),float4(0.965925872,(-0.258819073),0.0,(-1048.17224))),dot(float4(c.position,1),float4(0.258819073,0.965925872,(-0.0),(-2637.14575))),dot(float4(c.position,1),float4(0.0,(-0.0),1.0,(-216.300018))));
float v2=SourceFBM(v1,0.0,0.649999976,2.0,0.5,2.0,false,true);
float4 v3=((v2)<=0.839999974?lerp(float4(0.546000004,0.537599981,0.470400006,1.0),float4(0.734499991,0.723200023,0.632799983,1.0),saturate((v2-0.159999996)/0.679999977)):float4(0.734499991,0.723200023,0.632799983,1.0));
b0.baseColor=(v3).xyz;
b0.metallic=0.0;
float v6=lerp(0.629999995,0.790000021,saturate(SourceDivide((v2-0.0),(1.0-0.0))));
b0.roughness=v6;
b0.ior=1.5;
b0.specular=0.5;
b0.coat=0.0;
b0.coatRoughness=0.0299999993;
b0.transmission=0.0;
b0.alpha=1.0;
b0.anisotropy=0.0;
b0.anisotropicRotation=0.0;
float v16=SourceFBM(v1,0.0,110.0,2.0,0.5,2.0,false,true);
float3 v17=SourceBump(c.normal,v16,0.219999999,0.000300000014,c.position,false);
b0.normal=v17;
b0.emission=(float4(1.0,1.0,1.0,1.0)).xyz*0.0;
a=b0; b=b0; factor=0; a.alpha*=1; b.alpha*=1;
}
