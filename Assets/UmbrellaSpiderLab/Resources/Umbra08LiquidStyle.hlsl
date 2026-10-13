// Both reconstructed liquid and tensile threads evaluate the same wet material
// in fluid-local coordinates, so roots do not change colour or roughness.
void UmbraLiquidStyle(float3 local, out half3 albedo, out half smoothness,
                      out half coat, out half coatSmoothness)
{
    float film = sin(dot(local, float3(17, 3, 11)) + sin(local.y * 9) * .7) * _FilmVariation;
    albedo = _BaseColor.rgb;
    smoothness = saturate(_Smoothness + film);
    coat = _CoatMask;
    coatSmoothness = _CoatSmoothness;
}
