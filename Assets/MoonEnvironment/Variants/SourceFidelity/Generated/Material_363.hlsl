

void EvaluateSourceMaterial(SourceContext c,out SourceClosure a,out SourceClosure b,out float factor)
{
SourceClosure b0=SourceDefaultClosure(c);
float3 v1=c.local;
float v2=(v1).z;
float v3=lerp(0.219999999,0.0,saturate(SourceDivide((v2-0.100000001),(1.14999998-0.100000001))));
float v4=SourceFBM(v1,0.0,0.360000014,2.0,0.579999983,2.0,false,true);
float v5=(v3*v4);
float4 v6=((v4)<=0.829999983?lerp(float4(0.0615000017,0.0901999995,0.0860999972,1.0),float4(0.0825000033,0.120999999,0.115500003,1.0),saturate((v4-0.180000007)/0.649999976)):float4(0.0825000033,0.120999999,0.115500003,1.0));
float4 v7=lerp(v6,float4(0.280000001,0.264999986,0.230000004,1.0),saturate(v5));
b0.baseColor=(v7).xyz;
b0.metallic=0.569999993;
float v10=lerp(0.625,0.735000014,saturate(SourceDivide((v4-0.0),(1.0-0.0))));
b0.roughness=v10;
b0.ior=1.5;
b0.specular=0.5;
b0.coat=0.0;
b0.coatRoughness=0.0299999993;
b0.transmission=0.0;
b0.alpha=1.0;
b0.anisotropy=0.0;
b0.anisotropicRotation=0.0;
float v20=SourceFBM(v1,0.0,125.0,2.0,0.5,2.0,false,true);
float3 v21=SourceBump(c.normal,v20,0.159999996,0.00079999998,c.position,false);
b0.normal=v21;
b0.emission=(float4(1.0,1.0,1.0,1.0)).xyz*0.0;
a=b0; b=b0; factor=0; a.alpha*=1; b.alpha*=1;
}
