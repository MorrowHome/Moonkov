

void EvaluateSourceMaterial(SourceContext c,out SourceClosure a,out SourceClosure b,out float factor)
{
float v0=c.attributes0.x;
float3 v1=c.position;
float v2=SourceFBM(v1,0.0,0.340000004,3.0,0.649999976,2.0,false,true);
float v3=lerp(0.620000005,1.0,saturate(SourceDivide((v2-0.25),(0.75-0.25))));
float v4=(v0*v3);
SourceClosure b5=SourceDefaultClosure(c);
float v6=c.attributes0.y;
float4 v7=((v2)<=0.879999995?lerp(float4(0.154799998,0.15264,0.143279999,1.0),float4(0.253699988,0.250160009,0.234819993,1.0),saturate((v2-0.119999997)/0.759999998)):float4(0.253699988,0.250160009,0.234819993,1.0));
float4 v8=lerp(v7,float4(0.245000005,0.241999999,0.226999998,1.0),saturate(v6));
b5.baseColor=(v8).xyz;
b5.metallic=0.0;
b5.roughness=0.939999998;
b5.ior=1.5;
b5.specular=0.5;
b5.coat=0.0;
b5.coatRoughness=0.0299999993;
b5.transmission=0.0;
b5.alpha=1.0;
b5.anisotropy=0.0;
b5.anisotropicRotation=0.0;
float v20=SourceFBM(v1,0.0,58.0,2.0,0.5,2.0,false,true);
float3 v21=SourceBump(c.normal,v20,0.239999995,0.00899999961,c.position,false);
b5.normal=v21;
b5.emission=(float4(1.0,1.0,1.0,1.0)).xyz*0.0;
a=b5; b=b5; factor=0; a.alpha*=(1*v4); b.alpha*=(1*v4);
}
