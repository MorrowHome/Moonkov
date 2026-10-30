

void EvaluateSourceMaterial(SourceContext c,out SourceClosure a,out SourceClosure b,out float factor)
{
SourceClosure b0=SourceDefaultClosure(c);
float3 v1=c.local;
float v2=SourceFBM(v1,0.0,3.5,4.0,0.720000029,2.0,false,true);
float4 v3=((v2)<=0.850000024?lerp(float4(0.0105600003,0.0140800001,0.0140800001,1.0),float4(0.0134399999,0.0179200005,0.0179200005,1.0),saturate((v2-0.150000006)/0.700000018)):float4(0.0134399999,0.0179200005,0.0179200005,1.0));
b0.baseColor=(v3).xyz;
b0.metallic=0.0500000007;
float v6=lerp(0.75999999,0.980000019,saturate(SourceDivide((v2-0.0),(1.0-0.0))));
b0.roughness=v6;
b0.ior=1.5;
b0.specular=0.5;
b0.coat=0.0;
b0.coatRoughness=0.0299999993;
b0.transmission=0.0;
b0.alpha=1.0;
b0.anisotropy=0.0;
b0.anisotropicRotation=0.0;
float v16=SourceFBM(v1,0.0,95.0,2.0,0.5,2.0,false,true);
float3 v17=SourceBump(c.normal,v16,0.119999997,0.0120000001,c.position,false);
b0.normal=v17;
b0.emission=(float4(1.0,1.0,1.0,1.0)).xyz*0.0;
a=b0; b=b0; factor=0; a.alpha*=1; b.alpha*=1;
}
