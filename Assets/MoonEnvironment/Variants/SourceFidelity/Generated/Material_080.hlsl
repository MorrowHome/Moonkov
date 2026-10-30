

void EvaluateSourceMaterial(SourceContext c,out SourceClosure a,out SourceClosure b,out float factor)
{
SourceClosure b0=SourceDefaultClosure(c);
float3 v1=c.position;
float v2=SourceFBM(v1,0.0,7.0,2.4000001,0.5,2.0,false,true);
float4 v3=((v2)<=0.800000012?lerp(float4(0.499199986,0.507000029,0.413399994,1.0),float4(0.723200023,0.734499991,0.59890002,1.0),saturate((v2-0.200000003)/0.600000009)):float4(0.723200023,0.734499991,0.59890002,1.0));
b0.baseColor=(v3).xyz;
b0.metallic=0.100000001;
b0.roughness=0.790000021;
b0.ior=1.5;
b0.specular=0.5;
b0.coat=0.0;
b0.coatRoughness=0.0299999993;
b0.transmission=0.0;
b0.alpha=1.0;
b0.anisotropy=0.0;
b0.anisotropicRotation=0.0;
float3 v15=SourceBump(c.normal,v2,0.150000006,0.00100000005,c.position,false);
b0.normal=v15;
b0.emission=(float4(1.0,1.0,1.0,1.0)).xyz*0.0;
a=b0; b=b0; factor=0; a.alpha*=1; b.alpha*=1;
}
