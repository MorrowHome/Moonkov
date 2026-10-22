using System;
using System.Collections;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using Unity.MP_FPS;
using Unity.MP_FPS.DollSinger;

/// <summary>Temporary state injection into the lab's actual production player. No damage or new scene/prefab.</summary>
public static class MeleePresentationPreview
{
    private static MeleePreviewRunner runner;
    [MenuItem("Tools/Moonkov/Melee/Begin In Playing Gesture Lab")]
    public static void Begin()
    {
        if (!EditorApplication.isPlaying) throw new InvalidOperationException("Play WeaponGestureLab before starting the melee preview.");
        var lab = UnityEngine.Object.FindAnyObjectByType<WeaponGestureLab>();
        if (!lab || !lab.Player) throw new InvalidOperationException("The actual production player in WeaponGestureLab is not ready.");
        if (runner) return;
        runner = lab.gameObject.AddComponent<MeleePreviewRunner>(); runner.Initialize(lab);
    }
    public static void Show(MeleeWeaponKind weapon, MeleeAttackKind attack = MeleeAttackKind.None, int combo = 0, float elapsed = 0)
    { if (!runner) Begin(); runner.Show(weapon, attack, combo, elapsed); }
    public static void PlayAttack(MeleeWeaponKind weapon, MeleeAttackKind attack, int combo = 0)
    { if (!runner) Begin(); runner.Show(weapon, attack, combo, 0, false); }
    public static void Capture(string name)
    { if (!runner) throw new InvalidOperationException("Begin the preview first."); runner.StartCoroutine(runner.Capture(name)); }
    [MenuItem("Tools/Moonkov/Melee/End Preview")]
    public static void End() { if (runner) UnityEngine.Object.Destroy(runner); runner = null; }
}

[DefaultExecutionOrder(-90)]
public sealed class MeleePreviewRunner : MonoBehaviour
{
    private WeaponGestureLab lab;
    private DollSingerMeleePresentation presentation;
    private MeleeViewState state;
    private float started, sampled;
    private bool frozen;
    public void Initialize(WeaponGestureLab value)
    {
        lab = value;
        presentation = lab.Halo.GetComponent<DollSingerMeleePresentation>();
        if (!presentation) presentation = lab.Halo.gameObject.AddComponent<DollSingerMeleePresentation>();
        presentation.Configure(lab.Halo.GetComponent<Animator>(), lab.PlayerView, lab.Halo);
        Show(MeleeWeaponKind.Fists, MeleeAttackKind.None, 0, 0);
    }
    public void Show(MeleeWeaponKind weapon, MeleeAttackKind attack, int combo, float elapsed, bool freeze = true)
    {
        state.Weapon = state.AttackWeapon = weapon; state.Attack = attack; state.ComboIndex = Mathf.Clamp(combo, 0, 2);
        state.AttackSequence++; state.Alive = state.IsOwner = true;
        sampled = Mathf.Max(0, elapsed); started = Time.time; frozen = freeze;
    }
    private void Update()
    {
        if (!presentation || !lab) return;
        state.FirstPerson = lab.PlayerView.IsFirstPerson; state.AttackDirection = lab.PlayerView.transform.forward;
        state.Elapsed = sampled + (frozen ? 0 : Time.time - started);
        state.Phase = MeleePhase.Idle;
        if (state.Attack != MeleeAttackKind.None && MeleeRules.TryDefinition(state.Weapon, out var definition))
        {
            var timing = definition.Timing(state.Attack, state.ComboIndex);
            state.Phase = state.Elapsed < timing.Windup ? MeleePhase.Windup :
                state.Elapsed < timing.Windup + timing.Active ? MeleePhase.Active : state.Elapsed < timing.Total ? MeleePhase.Recovery : MeleePhase.Idle;
        }
        presentation.SetPresentation(state);
    }
    public IEnumerator Capture(string name)
    {
        // Let the current source pose settle, then read the same four real camera targets.
        yield return new WaitForSeconds(.32f); yield return new WaitForEndOfFrame();
        string destination = "Library/MeleePreview"; Directory.CreateDirectory(destination);
        string safeName = string.Concat(name.Where(c => char.IsLetterOrDigit(c) || c == '-' || c == '_'));
        var cameras = new[] { lab.PlayerView.camera }.Concat(lab.Observers).ToArray();
        for (int i = 0; i < cameras.Length; i++)
        {
            var target = cameras[i].targetTexture; if (!target) continue;
            var previous = RenderTexture.active; RenderTexture.active = target;
            var texture = new Texture2D(target.width, target.height, TextureFormat.RGB24, false);
            texture.ReadPixels(new Rect(0, 0, texture.width, texture.height), 0, 0); texture.Apply();
            File.WriteAllBytes(destination + "/" + safeName + "-" + new[] { "owner", "front", "side", "back" }[i] + ".png", texture.EncodeToPNG());
            UnityEngine.Object.Destroy(texture); RenderTexture.active = previous;
        }
        File.WriteAllText(destination + "/" + safeName + ".txt", "Production player, local cosmetic state injection. No network/damage validation.\nBake " + presentation.BakeRevision + "\n");
        Debug.Log("Melee four-camera preview captured: " + destination + "/" + safeName);
    }
    private void OnDisable() { if (presentation) presentation.SetPresentation(default); }
}
