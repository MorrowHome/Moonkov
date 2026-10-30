

void EvaluateSourceMaterial(SourceContext c,out SourceClosure a,out SourceClosure b,out float factor)
{
SourceClosure b0=SourceDefaultClosure(c);
float3 v1=c.normal;
float v2=(v1).z;
float v3=lerp(0.0,1.0,saturate(SourceDivide((v2-0.140000001),(0.800000012-0.140000001))));
float3 v4=c.position;
float v5=SourceFBM(v4,0.0,0.649999976,3.0,0.699999988,2.0,false,true);
float v6=(v3*v5);
float4 v7=((v5)<=0.850000024?lerp(float4(0.165600002,0.169200003,0.154799998,1.0),float4(0.257600009,0.263200015,0.240799993,1.0),saturate((v5-0.150000006)/0.700000018)):float4(0.257600009,0.263200015,0.240799993,1.0));
float4 v8=lerp(v7,float4(0.230000004,0.215000004,0.185000002,1.0),saturate(v6));
b0.baseColor=(v8).xyz;
b0.metallic=0.419999987;
float v11=lerp(0.419999987,0.689999998,saturate(SourceDivide((v5-0.100000001),(0.899999976-0.100000001))));
float4 v12=lerp(float4(v11,v11,v11,1),float4(0.910000026,0.910000026,0.910000026,1.0),saturate(v6));
b0.roughness=SourceLuminance((v12).xyz);
b0.ior=1.5;
b0.specular=0.5;
b0.coat=0.0;
b0.coatRoughness=0.0299999993;
b0.transmission=0.0;
b0.alpha=1.0;
b0.anisotropy=0.0;
b0.anisotropicRotation=0.0;
float v22=SourceFBM(v4,0.0,35.0,2.0,0.5,2.0,false,true);
float3 v23=SourceBump(c.normal,v22,0.280000001,0.00800000038,c.position,false);
b0.normal=v23;
b0.emission=(float4(1.0,1.0,1.0,1.0)).xyz*0.0;
a=b0; b=b0; factor=0; a.alpha*=1; b.alpha*=1;
}
