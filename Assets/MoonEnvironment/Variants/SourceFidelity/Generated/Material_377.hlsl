

void EvaluateSourceMaterial(SourceContext c,out SourceClosure a,out SourceClosure b,out float factor)
{
SourceClosure b0=SourceDefaultClosure(c);
float3 v1=float3(dot(float4(c.position,1),float4(0.965925872,(-0.258819073),0.0,(-1048.17224))),dot(float4(c.position,1),float4(0.258819073,0.965925872,(-0.0),(-2637.14575))),dot(float4(c.position,1),float4(0.0,(-0.0),1.0,(-216.300018))));
float v2=(v1).x;
float v3=abs(v2);
float v4=(v3-1.35000002);
float v5=abs(v4);
float v6=lerp(0.219999999,0.0,saturate(SourceDivide((v5-0.100000001),(0.400000006-0.100000001))));
float v7=SourceFBM(v1,0.0,0.270000011,2.0,0.5,2.0,false,true);
float v8=(v6*v7);
float4 v9=((v7)<=0.839999974?lerp(float4(0.235200003,0.231000006,0.2016,1.0),float4(0.316399992,0.310750008,0.271200001,1.0),saturate((v7-0.159999996)/0.679999977)):float4(0.316399992,0.310750008,0.271200001,1.0));
float4 v10=lerp(v9,float4(0.115000002,0.119000003,0.108000003,1.0),saturate(v8));
b0.baseColor=(v10).xyz;
b0.metallic=0.0;
float v13=lerp(0.819999993,0.980000019,saturate(SourceDivide((v7-0.0),(1.0-0.0))));
b0.roughness=v13;
b0.ior=1.5;
b0.specular=0.5;
b0.coat=0.0;
b0.coatRoughness=0.0299999993;
b0.transmission=0.0;
b0.alpha=1.0;
b0.anisotropy=0.0;
b0.anisotropicRotation=0.0;
float v23=SourceFBM(v1,0.0,48.0,2.0,0.5,2.0,false,true);
float3 v24=SourceBump(c.normal,v23,0.219999999,0.00120000006,c.position,false);
b0.normal=v24;
b0.emission=(float4(1.0,1.0,1.0,1.0)).xyz*0.0;
a=b0; b=b0; factor=0; a.alpha*=1; b.alpha*=1;
}
