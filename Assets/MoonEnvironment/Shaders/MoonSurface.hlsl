#ifndef MOON_LAB_SURFACE_INCLUDED
#define MOON_LAB_SURFACE_INCLUDED

float2 MoonHash(float2 p)
{
    return frac(sin(float2(dot(p,float2(127.1,311.7)),dot(p,float2(269.5,183.3))))*43758.5453);
}
float MoonNoise(float2 p)
{
    float2 i=floor(p),f=frac(p); f=f*f*(3-2*f);
    return lerp(lerp(MoonHash(i).x,MoonHash(i+float2(1,0)).x,f.x),lerp(MoonHash(i+float2(0,1)).x,MoonHash(i+1).x,f.x),f.y);
}
// Value and analytic derivatives share the same four samples. Filtering each
// octave by its projected footprint prevents distant grit from sparkling.
float3 MoonNoiseGradient(float2 p)
{
    float2 i=floor(p), f=frac(p), u=f*f*(3-2*f), du=6*f*(1-f);
    float a=MoonHash(i).x, b=MoonHash(i+float2(1,0)).x;
    float c=MoonHash(i+float2(0,1)).x, d=MoonHash(i+1).x;
    return float3(lerp(lerp(a,b,u.x),lerp(c,d,u.x),u.y),
        lerp(b-a,d-c,u.y)*du.x, lerp(c-a,d-b,u.x)*du.y);
}
float3 MoonPowderRelief(float2 worldXZ, float distanceToCamera)
{
    float footprint=max(length(ddx(worldXZ)),length(ddy(worldXZ)));
    float3 relief=0;
    const float4 wavelengths=float4(.035,.13,.53,2.1);
    const float4 heights=float4(.0012,.0038,.008,.055);
    [unroll] for(int octave=0;octave<4;octave++)
    {
        float wavelength=wavelengths[octave];
        float resolved=1-smoothstep(wavelength*.2,wavelength*.7,footprint);
        float3 grain=MoonNoiseGradient(worldXZ/wavelength+float2(17.37,41.19)*octave);
        relief+=float3((grain.x-.5)*.055,grain.yz*heights[octave]/wavelength)*resolved;
    }
    return relief*(1-smoothstep(80,180,distanceToCamera));
}
float2 MoonRotate(float2 p,float2 cs) { return float2(cs.x*p.x-cs.y*p.y,cs.y*p.x+cs.x*p.y); }
void MoonLattice(float2 uv,out float2 a,out float2 b,out float2 c,out float3 w)
{
    float2 q=float2(uv.x-uv.y*0.577350269,uv.y*1.154700538),i=floor(q),f=frac(q);
    if(f.x+f.y<1) { a=i;b=i+float2(1,0);c=i+float2(0,1);w=float3(1-f.x-f.y,f.x,f.y); }
    else { a=i+1;b=i+float2(0,1);c=i+float2(1,0);w=float3(f.x+f.y-1,1-f.x,1-f.y); }
    w=pow(max(w,0),2.5);w/=dot(w,float3(1,1,1));
}
float MoonMaterialNearWeight(float3 worldPosition)
{
    // Only synthetic albedo variation recedes with distance. Geometry, cast
    // shadows and the normal-map mip chain keep their own physical depth cues.
    return 1-smoothstep(_MaterialFadeStart,max(_MaterialFadeEnd,_MaterialFadeStart+1),
        distance(worldPosition,_WorldSpaceCameraPos));
}
float MoonMacro(float3 worldPosition)
{
    float2 worldXZ=worldPosition.xz;
    // Preserve near-field variation, but prevent broad noise from reading as
    // painted bands once individual grains and fragments are no longer resolved.
    float2 warp=float2(MoonNoise(worldXZ/113),MoonNoise(worldXZ/113+47.2))*38;
    float broad=MoonNoise((worldXZ+warp)/67);
    float middle=MoonNoise((worldXZ-warp*.4)/17);
    float province=smoothstep(.22,.78,broad*.75+middle*.25);
    float contrast=_MacroContrast*lerp(_DistantVariation,1,MoonMaterialNearWeight(worldPosition));
    return lerp(1,lerp(.62,1.32,province),contrast);
}
float3 MoonImpact(float2 worldXZ)
{
    float2 uv=(worldXZ-_LocalTerrainRect.xy)/max(_LocalTerrainRect.zw,1);
    float edge=min(min(uv.x,uv.y),min(1-uv.x,1-uv.y));
    return tex2D(_ImpactGeology,saturate(uv)).rgb*smoothstep(0,.025,edge)*_ImpactReady;
}
float3 MoonGeology(float2 worldXZ)
{
    float2 uv=(worldXZ-_GeologyRect.xy)/max(_GeologyRect.zw,1);
    float edge=min(min(uv.x,uv.y),min(1-uv.x,1-uv.y));
    float3 authored=tex2D(_GeologyMap,saturate(uv)).rgb * smoothstep(0,0.03,edge);
    return saturate(authored+MoonImpact(worldXZ))*_GeologyStrength;
}
float MoonGeologyTint(float3 mask,float nearWeight)
{
    // Keep a restrained, local ejecta signal at distance, with much weaker
    // dark slope/basin paint. Actual pit shadows are not changed here.
    return 1+mask.r*_EjectaBrightness*lerp(.6,1,nearWeight)
        -(mask.g*.24+mask.b*.18)*lerp(.25,1,nearWeight);
}
float3 MoonFacies(float3 worldPosition,float slope,float3 geology,out float rock)
{
    float2 worldXZ=worldPosition.xz;
    float patch=MoonNoise(worldXZ/8.3+float2(MoonNoise(worldXZ/31),MoonNoise(worldXZ/31+19))*4);
    rock=saturate(smoothstep(.025,.22,slope)*.75+smoothstep(.48,.76,patch)*.6+geology.g*.4);
    rock*=1-geology.r*.65;
    float3 tint=lerp(_DustTint.rgb,_BasaltTint.rgb,rock);
    // At distance, slope affects lighting, not a full dark albedo stripe.
    // Retain small local geology differences around ejecta/rubble instead.
    float distantRock=saturate(.35+geology.g*.15-geology.r*.2);
    float3 distantTint=lerp(_DustTint.rgb,_BasaltTint.rgb,distantRock);
    tint=lerp(distantTint,tint,MoonMaterialNearWeight(worldPosition));
    return lerp(1,tint,_FaciesStrength);
}
float MoonGray(float3 color) { return dot(color,float3(0.2126,0.7152,0.0722)); }
float MoonGrainGray(sampler2D map,float2 uv,float2 dx,float2 dy,float2 cell)
{
    float2 seed=MoonHash(cell),cs=float2(cos(seed.x*6.2831853),sin(seed.x*6.2831853));
    return MoonGray(tex2Dgrad(map,MoonRotate(uv,cs)+seed*31.7,MoonRotate(dx,cs),MoonRotate(dy,cs)).rgb);
}
#endif
