

void EvaluateSourceMaterial(SourceContext c,out SourceClosure a,out SourceClosure b,out float factor)
{
SourceClosure b0=SourceDefaultClosure(c);
b0.baseColor=(float4(0.0260000005,0.0370000005,0.0419999994,1.0)).xyz;
b0.metallic=0.0;
b0.roughness=0.280000001;
b0.ior=1.46000004;
b0.specular=0.5;
b0.coat=0.0;
b0.coatRoughness=0.0299999993;
b0.transmission=0.0;
b0.alpha=1.0;
b0.anisotropy=0.0;
b0.anisotropicRotation=0.0;
b0.emission=(float4(1.0,0.460000008,0.189999998,1.0)).xyz*1.5;
a=b0; b=b0; factor=0; a.alpha*=1; b.alpha*=1;
}
