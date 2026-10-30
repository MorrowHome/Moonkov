

void EvaluateSourceMaterial(SourceContext c,out SourceClosure a,out SourceClosure b,out float factor)
{
SourceClosure b0=SourceDefaultClosure(c);
float3 v1=c.position;
float3 v2=(v1*float3(1.0,1.0,1.39999998));
float v3=SourceFBM(v2,0.0,24.0,3.0,0.680000007,2.0,false,true);
float4 v4=((v3)<=0.720000029?lerp(float4(0.0,0.0,0.0,1.0),float4(1.0,1.0,1.0,1.0),saturate((v3-0.620000005)/0.100000024)):float4(1.0,1.0,1.0,1.0));
float v5=SourceFBM(v1,0.0,2.70000005,3.0,0.680000007,2.0,false,true);
float4 v6=((v5)<=0.620000005?lerp(float4(0.0,0.0,0.0,1.0),float4(0.699999988,0.699999988,0.699999988,1.0),saturate((v5-0.430000007)/0.189999998)):float4(0.699999988,0.699999988,0.699999988,1.0));
float v7=(SourceLuminance((v4).xyz)*SourceLuminance((v6).xyz));
float v8=(v7*1.0);
float v9=SourceFBM(v1,0.0,0.649999976,3.0,0.680000007,2.0,false,true);
float4 v10=((v9)<=0.850000024?lerp(float4(0.292400002,0.172000006,0.0326800011,1.0),float4(0.387600005,0.228,0.0433200002,1.0),saturate((v9-0.150000006)/0.700000018)):float4(0.387600005,0.228,0.0433200002,1.0));
float4 v11=lerp(v10,float4(0.0549999997,0.064000003,0.0610000007,1.0),saturate(v8));
b0.baseColor=(v11).xyz;
float v13=(v8*0.800000012);
float v14=(0.0399999991+v13);
b0.metallic=v14;
float v16=SourceFBM(v1,0.0,5.0,3.0,0.680000007,2.0,false,true);
float4 v17=((v16)<=0.850000024?lerp(float4(0.409999996,0.409999996,0.409999996,1.0),float4(0.730000019,0.730000019,0.730000019,1.0),saturate((v16-0.150000006)/0.700000018)):float4(0.730000019,0.730000019,0.730000019,1.0));
b0.roughness=SourceLuminance((v17).xyz);
b0.ior=1.5;
b0.specular=0.5;
b0.coat=0.0;
b0.coatRoughness=0.0299999993;
b0.transmission=0.0;
b0.alpha=1.0;
b0.anisotropy=0.0;
b0.anisotropicRotation=0.0;
float v27=SourceFBM(v1,0.0,125.0,2.0,0.680000007,2.0,false,true);
float3 v28=SourceBump(c.normal,v27,0.280000001,0.000600000028,c.position,false);
b0.normal=v28;
b0.emission=(float4(1.0,1.0,1.0,1.0)).xyz*0.0;
a=b0; b=b0; factor=0; a.alpha*=1; b.alpha*=1;
}
