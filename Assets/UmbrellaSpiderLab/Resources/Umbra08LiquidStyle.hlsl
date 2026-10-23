#include "Assets/Shaders/AlienVoidStyle.hlsl"
// Both reconstructed liquid and tensile threads evaluate the same wet material
// in fluid-local coordinates, so roots do not change colour or roughness.
void UmbraLiquidStyle(float3 local, out half3 albedo, out half smoothness,
                      out half coat, out half coatSmoothness)
{
    float film = sin(dot(local, float3(17, 3, 11)) + sin(local.y * 9) * .7) * _FilmVariation;
    half3 tissueAlbedo; half wetness;
    AlienTissueSurface(local, _SkinColor.rgb, _CarapaceColor.rgb, _TissueColor.rgb, tissueAlbedo, wetness);
    albedo = lerp(_BaseColor.rgb, tissueAlbedo, _VoidAmount);
    smoothness = saturate(lerp(_Smoothness, wetness, _VoidAmount) + film);
    coat = _CoatMask * lerp(1, wetness, _VoidAmount);
    coatSmoothness = _CoatSmoothness;
}

half3 UmbraVoidEmission(float3 local, half3 normal, half3 view)
{
    if (_VoidAmount < .001) return 0;
    return AlienVoidRadiance(local, normal, view, _VoidCharge, half3(.32,.34,.40)) * _VoidAmount;
}
