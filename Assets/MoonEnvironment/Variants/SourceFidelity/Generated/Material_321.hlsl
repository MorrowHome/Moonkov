

void EvaluateSourceMaterial(SourceContext c,out SourceClosure a,out SourceClosure b,out float factor)
{
SourceClosure b0=SourceDefaultClosure(c);
float3 v1=c.position;
float v2=SourceFBM(v1,0.0,0.649999976,3.0,0.699999988,2.0,false,true);
float4 v3=((v2)<=0.850000024?lerp(float4(0.0273599997,0.0309599992,0.0324000008,1.0),float4(0.04256,0.0481600016,0.0504000001,1.0),saturate((v2-0.150000006)/0.700000018)):float4(0.04256,0.0481600016,0.0504000001,1.0));
b0.baseColor=(v3).xyz;
b0.metallic=0.0500000007;
float v6=lerp(0.629999995,0.899999976,saturate(SourceDivide((v2-0.100000001),(0.899999976-0.100000001))));
b0.roughness=v6;
b0.ior=1.5;
b0.specular=0.5;
b0.coat=0.0;
b0.coatRoughness=0.0299999993;
b0.transmission=0.0;
b0.alpha=1.0;
b0.anisotropy=0.0;
b0.anisotropicRotation=0.0;
float v16=SourceFBM(v1,0.0,35.0,2.0,0.5,2.0,false,true);
float3 v17=SourceBump(c.normal,v16,0.280000001,0.00300000003,c.position,false);
b0.normal=v17;
b0.emission=(float4(1.0,1.0,1.0,1.0)).xyz*0.0;
a=b0; b=b0; factor=0; a.alpha*=1; b.alpha*=1;
}
