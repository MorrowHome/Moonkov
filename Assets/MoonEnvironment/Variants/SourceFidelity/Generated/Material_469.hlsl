

void EvaluateSourceMaterial(SourceContext c,out SourceClosure a,out SourceClosure b,out float factor)
{
float v0=c.attributes0.x;
SourceClosure b1=SourceDefaultClosure(c);
float v2=c.attributes0.y;
float4 v3=lerp(float4(0.135000005,0.131999999,0.125,1.0),float4(0.121500008,0.118799999,0.112499997,1.0),saturate(v2));
b1.baseColor=(v3).xyz;
b1.metallic=0.0;
b1.roughness=0.939999998;
b1.ior=1.5;
b1.specular=0.5;
b1.coat=0.0;
b1.coatRoughness=0.0299999993;
b1.transmission=0.0;
b1.alpha=1.0;
b1.anisotropy=0.0;
b1.anisotropicRotation=0.0;
float3 v15=c.position;
float v16=SourceFBM(v15,0.0,53.0,2.0,0.5,2.0,false,true);
float3 v17=SourceBump(c.normal,v16,v2,0.00600000005,c.position,false);
b1.normal=v17;
b1.emission=(float4(1.0,1.0,1.0,1.0)).xyz*0.0;
a=b1; b=b1; factor=0; a.alpha*=(1*v0); b.alpha*=(1*v0);
}
