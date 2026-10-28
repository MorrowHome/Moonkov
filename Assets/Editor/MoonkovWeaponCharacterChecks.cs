using System;
using Unity.Mathematics;
using Unity.MP_FPS;
using Unity.MP_FPS.DollSinger;
using UnityEditor;
using UnityEngine;

public static class MoonkovWeaponCharacterChecks
{
    private static int s_Checks;
    private static void Check(bool value, string message)
    { s_Checks++; if (!value) throw new InvalidOperationException(message); }
    private static bool Near(float a, float b) => Mathf.Abs(a - b) < .00002f;

    [MenuItem("Tools/Moonkov/Check weapon character and red rifle protection")]
    public static string Run()
    {
        s_Checks = 0;
        var library = Resources.Load<RifleFeedbackLibrary>("Moonkov/RifleFeedback");
        var registry = AssetDatabase.LoadAssetAtPath<WeaponRegistry>("Assets/Data/Weapons/WeaponRegistry.asset");
        var rifle = registry.GetWeaponData(DollSingerWeapons.Halo);
        var accepted = library.ForWeapon(DollSingerWeapons.Halo);
        Check(rifle.Automatic && rifle.MagazineSize == 30 && Near(rifle.CooldownInMs, .16f) && rifle.Damage == 45,
            "The accepted red rifle mechanics changed");
        Check(accepted.RecoilPitch == 0 && accepted.GestureReleaseSeconds == 0 && accepted.FireBody.name == "HaloIdentityBody" &&
            accepted.FireTail.name == "HaloIdentityTail", "The accepted red rifle presentation changed");
        foreach (float dt in new[] { 1f / 30, 1f / 60, 1f / 144 })
        {
            var previous = new HaloRecoilResponse(); var current = new HaloRecoilResponse();
            float oldPitch = 0; float2 look = float2.zero;
            for (int frame = 0; frame < 400; frame++)
            {
                if (frame % 19 == 0)
                { previous.Kick(2, 1, .16f); current.Kick(2, 1, .16f, true, accepted); }
                float input = frame % 7 == 0 ? .03f : 0;
                previous.Apply(ref oldPitch, input, dt); current.Apply(ref look, new float2(0, input), dt);
                oldPitch += input; look.y += input;
                Check(Near(oldPitch, look.y) && look.x == 0, "Red rifle aim response changed at " + dt);
            }
        }

        float priorKick = 0;
        foreach (uint id in new uint[] { 3, 4, 5 })
        {
            var profile = library.ForWeapon(id); var weapon = registry.GetWeaponData(id);
            Check(!weapon.Automatic && weapon.MagazineSize == (id == 3 ? 6 : id == 4 ? 4 : 5), "Existing weapon type/capacity changed " + id);
            Check(profile.RecoilPitch > priorKick && profile.RecoilReturnFraction > 0 && profile.RecoilReturnFraction < 1,
                "Revolver, shotgun and precision responses are not distinct"); priorKick = profile.RecoilPitch;
            Check(profile.FireBody && profile.FireTail && profile.FireBody.Clips.Count == 3 && profile.FireTail.Clips.Count == 1,
                "Missing reports/variants " + id);
            foreach (var bank in new[] { profile.FireBody, profile.FireTail })
            foreach (var clip in bank.Clips)
            {
                var importer = (AudioImporter)AssetImporter.GetAtPath(AssetDatabase.GetAssetPath(clip));
                using (var serialized = new SerializedObject(importer))
                    Check(serialized.FindProperty("m_Normalize")?.boolValue == false,
                        "Import normalization would change the authored layer mix " + clip.name);
                var samples = new float[clip.samples * clip.channels];
                Check(clip.GetData(samples, 0), "Cannot read imported PCM " + clip.name);
                float peak = 0; foreach (float sample in samples) peak = Mathf.Max(peak, Mathf.Abs(sample));
                Check(Mathf.Abs(peak - (bank == profile.FireBody ? .86f : .62f)) < .002f,
                    "Imported layer gain differs from the generated recording " + clip.name);
            }
            float headroom = Mathf.Pow(10, profile.FireBody.PitchAndVolumeInfo.VolumeMax / 20) +
                .85f * Mathf.Pow(10, profile.FireTail.PitchAndVolumeInfo.VolumeMax / 20);
            Check(headroom < .8f, "Shot layers exhaust mix headroom " + id);
            foreach (float zoom in new[] { 1f, 6f })
            foreach (float dt in new[] { 1f / 30, 1f / 60, 1f / 144 })
            {
                var response = new HaloRecoilResponse(); float2 aim = float2.zero;
                response.Kick(id, zoom, weapon.CooldownInMs, true, profile); response.Apply(ref aim, float2.zero, dt);
                float peak = -aim.y;
                Check(Near(peak, profile.RecoilPitch / zoom) && Mathf.Abs(aim.x) > 0, "Missing immediate directed shot impulse " + id);
                for (int i = 0; i < Mathf.CeilToInt(2 / dt); i++) response.Apply(ref aim, float2.zero, dt);
                Check(aim.y < -.02f / zoom && -aim.y < peak, "Shot snapped fully back or failed to settle " + id);
                response.Reset(); aim = float2.zero;
                response.Kick(id, zoom, weapon.CooldownInMs, true, profile); response.Apply(ref aim, float2.zero, dt);
                float2 correction = -aim;
                response.Apply(ref aim, correction, dt); aim += correction;
                for (int i = 0; i < Mathf.CeilToInt(2 / dt); i++) response.Apply(ref aim, float2.zero, dt);
                Check(math.length(aim) < .0001f, "Recovery pulled against the player's correction " + id);
                response.Reset(); float2 rest = aim; response.Apply(ref aim, float2.zero, 1);
                Check(math.distance(rest, aim) == 0 && response.Remaining == 0, "Reset left pending movement " + id);
            }
        }
        foreach (var timing in new[] { new Vector2(.42f, .48f), new Vector2(.6f, .52f), new Vector2(.7f, .98f) })
        {
            for (float t = 0; t <= .08f; t += .01f)
                Check(Near(DollSingerBakedGestures.ShotSampleTime(t, timing.x, timing.y), t), "Retiming blurred the firing attack");
            Check(Near(DollSingerBakedGestures.ShotSampleTime(timing.y, timing.x, timing.y), timing.x), "Recovery did not reach the authored rest pose");
            Check(Near(DollSingerBakedGestures.ShotSampleTime(.13f, timing.x, 0), .13f), "Default gesture timing changed");
        }
        return "PASS " + s_Checks + " weapon character, aim correction, gesture timing and red rifle protection checks";
    }
}
