

void EvaluateSourceMaterial(SourceContext c,out SourceClosure a,out SourceClosure b,out float factor)
{
SourceClosure b0=SourceDefaultClosure(c);
float v1=0.0;
float3 v2=c.position;
float3 v3=(v2-float3(2140.396,1880.3302,234.642624));
float v4=dot(v3,float3((-0.766044438),0.642787635,0.0));
float v5=dot(v3,float3((-0.642787635),(-0.766044438),0.0));
float v6=(v3).z;
float3 v7=float3(v4,v5,v6);
float v8=SourceFBM(v7,0.0,0.075000003,2.4000001,0.579999983,2.0,false,true);
float v9=lerp(0.0,1.0,saturate(SourceDivide((v8-0.239999995),(0.75999999-0.239999995))));
float v10=SourceDivide(v4,13.0);
float v11=floor(v10);
float v12=SourceDivide(v5,4.5);
float v13=floor(v12);
float v14=SourceDivide(v6,3.79999995);
float v15=floor(v14);
float3 v16=float3(v11,v13,v15);
float v17=SourceWhiteNoise(v16,0.0,false);
float v18=(v17-0.5);
float v19=(v18*0.0250000004);
float v20=(v9+v19);
float4 v21=lerp(float4(0.0578200035,0.0578200035,0.0578200035,1.0),float4(0.060180001,0.060180001,0.060180001,1.0),saturate(v20));
float4 v22=lerp(v21,float4(0.239999995,0.237000003,0.230000004,1.0),saturate(v1));
b0.baseColor=(v22).xyz;
float v24=(1.0-v1);
float v25=(0.0*v24);
b0.metallic=v25;
float v27=SourceFBM(v7,0.0,1.70000005,2.0,0.579999983,2.0,false,true);
float v28=lerp(0.579999983,0.75999999,saturate(SourceDivide((v27-0.219999999),(0.779999971-0.219999999))));
float v29=(1.0-v1);
float v30=(v28*v29);
float v31=(v1*0.939999998);
float v32=(v30+v31);
b0.roughness=v32;
b0.ior=1.48000002;
b0.specular=0.449999988;
b0.coat=0.0;
b0.coatRoughness=0.0299999993;
b0.transmission=0.0;
b0.alpha=1.0;
b0.anisotropy=0.0;
b0.anisotropicRotation=0.0;
float v42=SourceFBM(v7,0.0,18.0,2.0,0.579999983,2.0,false,true);
float3 v43=SourceBump(c.normal,v42,0.300000012,0.00079999998,c.position,false);
b0.normal=v43;
b0.emission=(float4(1.0,1.0,1.0,1.0)).xyz*0.0;
a=b0; b=b0; factor=0; a.alpha*=1; b.alpha*=1;
}
