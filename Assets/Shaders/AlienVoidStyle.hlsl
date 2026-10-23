#ifndef MOONKOV_ALIEN_VOID_STYLE
#define MOONKOV_ALIEN_VOID_STYLE
// Static pigmentation in rig-local space: broad carapace islands, vascular seams
// and small worn speckles. It follows the creature rather than swimming with time.
void AlienTissueSurface(float3 p, half3 skin, half3 plate, half3 tissue,
                       out half3 albedo, out half wetness)
{
    float broad = sin(p.x * 5.3 + sin(p.z * 3.1)) * sin(p.z * 6.1 - p.y * 4.7);
    float plates = smoothstep(-.12, .46, broad);
    float fold = abs(sin(p.y * 19 + p.z * 11 + sin(p.x * 9) * 1.4));
    float seam = (1 - smoothstep(.08, .23, fold)) * (1 - plates * .7);
    float speckle = pow(saturate(sin(dot(p, float3(61, 47, 73))) *
        sin(dot(p, float3(-37, 83, 29)))), 8);
    albedo = lerp(skin, plate, plates * .82);
    albedo = lerp(albedo, tissue, seam * .78);
    albedo *= .78 + .22 * fold;
    albedo = lerp(albedo, plate * 1.12, speckle * .35);
    wetness = saturate(.86 - plates * .48 + seam * .1);
}
// A dark wet mass needs reflected light and a restrained ash edge; black is not additive light.
half3 AlienVoidRadiance(float3 local, half3 normal, half3 view, float charge, half3 tint)
{
    float folds = sin(dot(local, float3(19, 7, 23)) + sin(local.z * 11) * 1.7) *
                  sin(dot(local, float3(-13, 29, 5)) - _Time.y * (.45 + charge));
    float vein = pow(saturate(1 - abs(folds) * 22), 7);
    float tide = .6 + .4 * sin(local.y * 8 - _Time.y * (1.5 + charge * 3));
    float rim = pow(1 - saturate(abs(dot(normalize(normal), normalize(view)))), 3.5);
    return tint * (charge * (rim * .16 + vein * tide * .28));
}
#endif
