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
float2 MoonRotate(float2 p,float2 cs) { return float2(cs.x*p.x-cs.y*p.y,cs.y*p.x+cs.x*p.y); }
void MoonLattice(float2 uv,out float2 a,out float2 b,out float2 c,out float3 w)
{
    float2 q=float2(uv.x-uv.y*0.577350269,uv.y*1.154700538),i=floor(q),f=frac(q);
    if(f.x+f.y<1) { a=i;b=i+float2(1,0);c=i+float2(0,1);w=float3(1-f.x-f.y,f.x,f.y); }
    else { a=i+1;b=i+float2(0,1);c=i+float2(1,0);w=float3(f.x+f.y-1,1-f.x,1-f.y); }
    w=pow(max(w,0),2.5);w/=dot(w,float3(1,1,1));
}
float MoonMacro(float2 worldXZ) { return 0.92+0.1*MoonNoise(worldXZ/71)+0.06*MoonNoise(worldXZ/18); }
float3 MoonGeology(float2 worldXZ)
{
    float2 uv=(worldXZ-_GeologyRect.xy)/max(_GeologyRect.zw,1);
    float edge=min(min(uv.x,uv.y),min(1-uv.x,1-uv.y));
    return tex2D(_GeologyMap,saturate(uv)).rgb * smoothstep(0,0.03,edge) * _GeologyStrength;
}
float MoonGeologyTint(float3 mask) { return 1 + mask.r*_EjectaBrightness - mask.g*0.055 - mask.b*0.035; }
float MoonGray(float3 color) { return dot(color,float3(0.2126,0.7152,0.0722)); }
float MoonGrainGray(sampler2D map,float2 uv,float2 dx,float2 dy,float2 cell)
{
    float2 seed=MoonHash(cell),cs=float2(cos(seed.x*6.2831853),sin(seed.x*6.2831853));
    return MoonGray(tex2Dgrad(map,MoonRotate(uv,cs)+seed*31.7,MoonRotate(dx,cs),MoonRotate(dy,cs)).rgb);
}
#endif
