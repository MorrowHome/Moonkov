

void EvaluateSourceMaterial(SourceContext c,out SourceClosure a,out SourceClosure b,out float factor)
{
SourceClosure b0=SourceDefaultClosure(c);
float3 v1=float3(dot(float4(c.position,1),float4(1.0,(-0.0),0.0,(-2183.0))),dot(float4(c.position,1),float4((-0.0),1.0,(-0.0),(-2596.0))),dot(float4(c.position,1),float4(0.0,(-0.0),1.0,(-296.554443))));
float v2=(v1).z;
float v3=SourceFBM(v1,0.0,4.30000019,2.0,0.629999995,2.0,false,true);
float v4=(v3-0.5);
float v5=(v4*0.239999995);
float v6=(v2+v5);
float v7=lerp(1.0,0.0,((saturate(SourceDivide((v6-0.119999997),(1.08000004-0.119999997))))*(saturate(SourceDivide((v6-0.119999997),(1.08000004-0.119999997))))*(3-2*(saturate(SourceDivide((v6-0.119999997),(1.08000004-0.119999997)))))));
float v8=(v7*0.150000006);
float v9=SourceFBM(v1,0.0,8.0,2.0,0.629999995,2.0,false,true);
float v10=lerp(0.379999995,1.0,((saturate(SourceDivide((v9-0.119999997),(0.850000024-0.119999997))))*(saturate(SourceDivide((v9-0.119999997),(0.850000024-0.119999997))))*(3-2*(saturate(SourceDivide((v9-0.119999997),(0.850000024-0.119999997)))))));
float v11=(v8*v10);
float v12=SourceFBM(v1,0.0,0.839999974,2.0,0.629999995,2.0,false,true);
float v13=lerp(0.0,1.0,((saturate(SourceDivide((v12-0.180000007),(0.819999993-0.180000007))))*(saturate(SourceDivide((v12-0.180000007),(0.819999993-0.180000007))))*(3-2*(saturate(SourceDivide((v12-0.180000007),(0.819999993-0.180000007)))))));
float4 v14=lerp(float4(0.219999999,0.239999995,0.236000001,1.0),float4(0.289999992,0.310000002,0.300000012,1.0),saturate(v13));
float4 v15=lerp(v14,float4(0.289999992,0.280000001,0.245000005,1.0),saturate(v11));
b0.baseColor=(v15).xyz;
float v17=(1.0-v11);
float v18=(0.819999993*v17);
b0.metallic=v18;
float v20=SourceFBM(v1,0.0,34.0,2.0,0.629999995,2.0,false,true);
float v21=lerp(0.270000011,0.419999987,((saturate(SourceDivide((v20-0.180000007),(0.819999993-0.180000007))))*(saturate(SourceDivide((v20-0.180000007),(0.819999993-0.180000007))))*(3-2*(saturate(SourceDivide((v20-0.180000007),(0.819999993-0.180000007)))))));
float3 v22=(v1*float3(260.0,260.0,3.5));
float v23=SourceFBM(v22,0.0,1.0,2.0,0.629999995,2.0,false,true);
float v24=(v23-0.5);
float v25=(v24*0.100000001);
float v26=(v21+v25);
float4 v27=lerp(float4(v26,v26,v26,1),float4(0.899999976,0.899999976,0.899999976,1.0),saturate(v11));
b0.roughness=SourceLuminance((v27).xyz);
b0.ior=1.48000002;
b0.specular=0.5;
b0.coat=0.0;
b0.coatRoughness=0.0299999993;
b0.transmission=0.0;
b0.alpha=1.0;
b0.anisotropy=0.0;
b0.anisotropicRotation=0.0;
float v37=SourceFBM(v1,0.0,230.0,1.0,0.629999995,2.0,false,true);
float v38=(v37*0.000549999997);
float v39=(v23*0.000280000007);
float v40=(v38+v39);
float v41=(v11*v20);
float v42=(v41*0.000850000011);
float v43=(v40+v42);
float3 v44=SourceBump(c.normal,v43,0.519999981,1.0,c.position,false);
b0.normal=v44;
b0.emission=(float4(1.0,1.0,1.0,1.0)).xyz*0.0;
a=b0; b=b0; factor=0; a.alpha*=1; b.alpha*=1;
}
