

void EvaluateSourceMaterial(SourceContext c,out SourceClosure a,out SourceClosure b,out float factor)
{
SourceClosure b0=SourceDefaultClosure(c);
b0.baseColor=(float4(0.620000005,0.670000017,0.649999976,1.0)).xyz;
b0.metallic=0.0;
b0.roughness=0.479999989;
b0.ior=1.5;
b0.specular=0.5;
b0.coat=0.0;
b0.coatRoughness=0.0299999993;
b0.transmission=0.0;
b0.alpha=1.0;
b0.anisotropy=0.0;
b0.anisotropicRotation=0.0;
b0.emission=(float4(1.0,0.680000007,0.379999995,1.0)).xyz*2.0;
a=b0; b=b0; factor=0; a.alpha*=1; b.alpha*=1;
}
