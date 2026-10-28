

void EvaluateSourceMaterial(SourceContext c,out SourceClosure a,out SourceClosure b,out float factor)
{
SourceClosure b0=SourceDefaultClosure(c);
b0.baseColor=(float4(0.189999998,0.215000004,0.224999994,1.0)).xyz;
b0.metallic=0.0900000036;
b0.roughness=0.430000007;
b0.ior=1.5;
b0.specular=0.5;
b0.coat=0.159999996;
b0.coatRoughness=0.319999993;
b0.transmission=0.0;
b0.alpha=1.0;
b0.anisotropy=0.0;
b0.anisotropicRotation=0.0;
b0.emission=(float4(1.0,1.0,1.0,1.0)).xyz*0.0;
a=b0; b=b0; factor=0; a.alpha*=1; b.alpha*=1;
}
