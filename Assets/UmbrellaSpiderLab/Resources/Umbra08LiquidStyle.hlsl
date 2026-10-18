#include "Assets/Shaders/AlienVoidStyle.hlsl"
// Both reconstructed liquid and tensile threads evaluate the same wet material
// in fluid-local coordinates, so roots do not change colour or roughness.
void UmbraLiquidStyle(float3 local, out half3 albedo, out half smoothness,
                      out half coat, out half coatSmoothness)
{
    float film = sin(dot(local, float3(17, 3, 11)) + sin(local.y * 9) * .7) * _FilmVariation;
    albedo = lerp(_BaseColor.rgb, half3(.0012, .0015, .0021), _VoidAmount);
    smoothness = saturate(_Smoothness + film);
    coat = _CoatMask;
    coatSmoothness = _CoatSmoothness;
}

half3 UmbraVoidEmission(float3 local, half3 normal, half3 view)
{
    if (_VoidAmount < .001) return 0;
    return AlienVoidRadiance(local, normal, view, _VoidCharge, half3(.32,.34,.40)) * _VoidAmount;
}
