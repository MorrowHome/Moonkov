using System;
using Unity.MP_FPS;
using UnityEditor;
using UnityEngine;

public static class MoonkovGunplaySetup
{
    [MenuItem("Tools/Moonkov/Weapons/Apply deliberate gunplay baseline")]
    public static void Apply()
    {
        if (EditorApplication.isPlaying) throw new InvalidOperationException("Use Edit mode.");
        var registry = AssetDatabase.LoadAssetAtPath<WeaponRegistry>("Assets/Data/Weapons/WeaponRegistry.asset");
        for (uint id = DollSingerWeapons.Halo; id <= DollSingerWeapons.Nocturne; id++)
        {
            var weapon = registry.GetWeaponData(id);
            ConfigureRange(weapon, id);
            if (id == DollSingerWeapons.Halo)
            { weapon.Damage = 45; weapon.CooldownInMs = .16f; weapon.ReloadTime = 2.6f; }
            if (id == DollSingerWeapons.Revolver)
            { weapon.Damage = 70; weapon.CooldownInMs = .85f; weapon.ReloadTime = 3.6f; }
            if (id == DollSingerWeapons.Shotgun)
            { weapon.SpreadDegrees = 3f; weapon.ReloadTime = 3.4f; }
            if (id == DollSingerWeapons.Sniper)
            { weapon.CooldownInMs = 1.6f; weapon.ReloadTime = 3.8f; }
            EditorUtility.SetDirty(weapon);
            AssetDatabase.SaveAssetIfDirty(weapon);
        }
        Debug.Log("Gunplay baseline applied: deliberate rifle/revolver cadence, four charge times, thirteen distance profiles.");
    }

    // Also called by the existing installers, so cloning a rifle cannot give
    // a shotgun rifle range or make a newly rebuilt launcher lose blast damage.
    public static void ConfigureRange(WeaponData weapon, uint id)
    {
        (float start, float end, float minimum) = id switch
        {
            DollSingerWeapons.Halo => (40, 160, .55f),
            DollSingerWeapons.Revolver => (20, 100, .4f),
            DollSingerWeapons.Shotgun => (7, 24, .06f),
            DollSingerWeapons.Sniper => (100, 320, .75f),
            DollSingerWeapons.Cicada => (15, 75, .3f),
            DollSingerWeapons.Trine => (40, 160, .55f),
            DollSingerWeapons.Aegis => (45, 180, .6f),
            DollSingerWeapons.Vesper => (60, 220, .65f),
            DollSingerWeapons.Thorn => (12, 38, .15f),
            DollSingerWeapons.Rondo => (30, 130, .45f),
            DollSingerWeapons.Nocturne => (80, 260, .65f),
            _ => (0, 0, 1)
        };
        weapon.DamageFalloffStart = start;
        weapon.DamageFalloffEnd = end;
        weapon.MinimumDamageFraction = minimum;
    }
}
