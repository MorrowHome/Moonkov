#ifndef MOON_LAB_PHASE_INCLUDED
#define MOON_LAB_PHASE_INCLUDED


// SHOE and CBOE angular shapes from Hapke (2002/2012), not an
// inverse-cosine Akimov approximation. Width controls are angle scales in
// degrees: h=tan(scale/2), NOT both peaks' FWHM. See doi:2011JE003916.
float MoonOppositionPeaks(float cosPhase)
{
    float t=sqrt(max(0,(1-cosPhase)/max(1+cosPhase,1e-6)));
    float hs=max(tan(radians(_ShadowHidingWidth)*0.5),1e-5);
    float hc=max(tan(radians(_CoherentWidth)*0.5),1e-5);
    float shadow=1+_ShadowHidingAmplitude/(1+t/hs);
    float x=t/hc;
    // The analytic x->0 limit avoids cancellation and division by zero.
    float ratio=x<0.001?1-x*0.5+x*x/6:(1-exp(-x))/max(x,1e-6);
    float coherent=1+_CoherentAmplitude*(1+ratio)/(2*(1+x)*(1+x));
    return shadow*coherent;
}

float MoonSolarAveragedPeaks(float cosPhase)
{
    // Four unique evaluations approximate a uniform solar disk of radius
    // 0.266 degrees. This regularises the phase peak only, not cast shadows.
    const float rho=0.0036703; // radius*sqrt(5/8); quadrature has correct second moment
    float s=sqrt(max(0,1-cosPhase*cosPhase));
    float c=cosPhase*cos(rho), offset=s*sin(rho);
    return 0.2*MoonOppositionPeaks(cosPhase)+0.2*MoonOppositionPeaks(clamp(c+offset,-1,1))
        +0.2*MoonOppositionPeaks(clamp(c-offset,-1,1))+0.4*MoonOppositionPeaks(c);
}

float MoonPhaseReflectance(float3 lightDirection,float3 viewDirection)
{
    if(_PhaseEnabled<0.001) return 1;
    float c=clamp(dot(normalize(lightDirection),normalize(viewDirection)),-1,1);
    float alpha=acos(c);
    // Keep the existing brightness at a 30-degree reference phase. Broad
    // phase falloff and these initial presets are not a local Hapke fit.
    float reference=MoonSolarAveragedPeaks(0.8660254);
    float factor=exp(-_PhaseSlope*(alpha-0.5235988))*MoonSolarAveragedPeaks(c)/max(reference,1e-4);
    return lerp(1,factor,saturate(_PhaseEnabled));
}
#endif
