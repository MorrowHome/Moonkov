

void EvaluateSourceMaterial(SourceContext c,out SourceClosure a,out SourceClosure b,out float factor)
{
SourceClosure b0=SourceDefaultClosure(c);
float3 v1=float3(dot(float4(c.position,1),float4(0.965925872,(-0.258819073),0.0,(-1048.17224))),dot(float4(c.position,1),float4(0.258819073,0.965925872,(-0.0),(-2637.14575))),dot(float4(c.position,1),float4(0.0,(-0.0),1.0,(-216.300018))));
float v2=(v1).z;
float v3=lerp(0.419999987,0.0,saturate(SourceDivide((v2-0.0599999987),(0.949999988-0.0599999987))));
float v4=SourceFBM(v1,0.0,0.649999976,2.0,0.5,2.0,false,true);
float v5=(v3*v4);
float4 v6=((v4)<=0.839999974?lerp(float4(0.0966000035,0.167999998,0.172199994,1.0),float4(0.129950002,0.225999996,0.231649995,1.0),saturate((v4-0.159999996)/0.679999977)):float4(0.129950002,0.225999996,0.231649995,1.0));
float4 v7=lerp(v6,float4(0.189999998,0.180000007,0.150000006,1.0),saturate(v5));
b0.baseColor=(v7).xyz;
b0.metallic=0.200000003;
float v10=lerp(0.460000008,0.620000005,saturate(SourceDivide((v4-0.0),(1.0-0.0))));
b0.roughness=v10;
b0.ior=1.5;
b0.specular=0.5;
b0.coat=0.0;
b0.coatRoughness=0.0299999993;
b0.transmission=0.0;
b0.alpha=1.0;
b0.anisotropy=0.0;
b0.anisotropicRotation=0.0;
float v20=SourceFBM(v1,0.0,110.0,2.0,0.5,2.0,false,true);
float3 v21=SourceBump(c.normal,v20,0.219999999,0.000549999997,c.position,false);
b0.normal=v21;
b0.emission=(float4(1.0,1.0,1.0,1.0)).xyz*0.0;
a=b0; b=b0; factor=0; a.alpha*=1; b.alpha*=1;
}
