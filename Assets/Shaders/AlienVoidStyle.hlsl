#ifndef MOONKOV_ALIEN_VOID_STYLE
#define MOONKOV_ALIEN_VOID_STYLE
// A dark wet mass needs reflected light and a restrained ash edge; black is not additive light.
half3 AlienVoidRadiance(float3 local, half3 normal, half3 view, float charge, half3 tint)
{
    float folds = sin(dot(local, float3(19, 7, 23)) + sin(local.z * 11) * 1.7) *
                  sin(dot(local, float3(-13, 29, 5)) - _Time.y * (.45 + charge));
    float vein = pow(saturate(1 - abs(folds) * 22), 7);
    float tide = .6 + .4 * sin(local.y * 8 - _Time.y * (1.5 + charge * 3));
    float rim = pow(1 - saturate(abs(dot(normalize(normal), normalize(view)))), 3.5);
    return tint * (rim * (.10 + charge * .42) + vein * tide * (.045 + charge * .75));
}
#endif
