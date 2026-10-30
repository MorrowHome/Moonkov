

void EvaluateSourceMaterial(SourceContext c,out SourceClosure a,out SourceClosure b,out float factor)
{
SourceClosure b0=SourceDefaultClosure(c);
float3 v1=c.position;
float3 v2=(v1-float3(2315.8811,2077.59253,240.773361));
float v3=dot(v2,float3(0.42261827,0.906307817,0.0));
float v4=(v3-(-11.9999504));
float v5=abs(v4);
float v6=(v5-6.25011301);
float v7=dot(v2,float3((-0.906307817),0.42261827,0.0));
float v8=(v7-5.60007048);
float v9=abs(v8);
float v10=(v9-3.2500186);
float v11=max(v6,v10);
float v12=lerp(0.180000007,0.0,saturate(SourceDivide((v11-0.0149999997),(0.230000004-0.0149999997))));
float v13=max(0.0,v12);
float3 v14=c.normal;
float v15=dot(v14,float3(0.0,0.0,1.0));
float v16=lerp(0.0,1.0,saturate(SourceDivide((v15-0.5),(0.980000019-0.5))));
float v17=(v13*v16);
float4 v18=lerp(float4(0.0900000036,0.0900000036,0.0900000036,1.0),float4(0.159999996,0.158999994,0.155000001,1.0),saturate(v17));
b0.baseColor=(v18).xyz;
b0.metallic=0.0;
float v21=SourceFBM(v2,0.0,1.89999998,2.0,0.5,2.0,false,true);
float v22=lerp(0.699999988,0.819999993,saturate(SourceDivide((v21-0.200000003),(0.800000012-0.200000003))));
float v23=(v17*0.550000012);
float v24=(v22+v23);
b0.roughness=v24;
b0.ior=1.48000002;
b0.specular=0.449999988;
b0.coat=0.0;
b0.coatRoughness=0.0299999993;
b0.transmission=0.0;
b0.alpha=1.0;
b0.anisotropy=0.0;
b0.anisotropicRotation=0.0;
float v34=SourceFBM(v2,0.0,18.0,2.0,0.5,2.0,false,true);
float3 v35=SourceBump(c.normal,v34,0.200000003,0.000600000028,c.position,false);
b0.normal=v35;
b0.emission=(float4(1.0,1.0,1.0,1.0)).xyz*0.0;
a=b0; b=b0; factor=0; a.alpha*=1; b.alpha*=1;
}
