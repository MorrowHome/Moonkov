using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Unity.MP_FPS;
using Unity.MP_FPS.DollSinger;
using UnityEditor;
using UnityEngine;

// Run between CoughVoiceReview.Begin and Restore in the existing Editor.
public static class HandlingFoleyReview
{
    public static async Task<string> Run()
    {
        if (!Application.isPlaying || GameManager.Instance) throw new InvalidOperationException("Use isolated empty Play scene");
        const BindingFlags hidden = BindingFlags.Instance | BindingFlags.NonPublic;
        int checks = 0;
        void Check(bool value, string message) { checks++; if (!value) throw new InvalidOperationException(message); }
        var instance = typeof(GameManager).GetProperty("Instance", BindingFlags.Public | BindingFlags.Static);
        var holder = new GameObject("Handling review manager"); holder.SetActive(false);
        var manager = holder.AddComponent<GameManager>();
        var listener = new GameObject("Handling review listener"); listener.AddComponent<AudioListener>();
        var sources = new GameObject("Handling review sources");
        var pool = (SoundGameObjectPool)System.Runtime.Serialization.FormatterServices.GetUninitializedObject(typeof(SoundGameObjectPool));
        var system = new SoundSystem(); var player = new MoonkovPlayerAudio(); var foley = new RaidActionFoley();
        var melee = new MoonkovMeleeAudio(); GameObject rigHolder = null; GrenadePresentation grenade = null;
        bool background = Application.runInBackground; Application.runInBackground = true;
        try
        {
            typeof(SoundGameObjectPool).GetField("m_SourceHolder", hidden).SetValue(pool, sources);
            pool.SoundGameObjectList = new List<SoundGameObject>();
            for (int i = 0; i < 16; i++) pool.SoundGameObjectList.Add(pool.Create());
            for (int i = 0; i < 16; i++) pool.SoundGameObjectList[i].NextAvailable = pool.SoundGameObjectList[(i + 1) % 16];
            pool.NextSoundGameObject = pool.SoundGameObjectList[0]; pool.AvailableSoundObjectCount = 16;
            var lib = MoonkovAudio.Library; system.Init(listener.transform, 16, pool, lib.WeaponDraw.UnityAudioMixer);
            instance.SetValue(null, manager); typeof(GameManager).GetField("m_SoundSystem", hidden).SetValue(manager, system);
            int Count(SoundDef bank) => system.m_SoundEmitterActiveList.Count(e => e.Active && e.Definition == bank);
            int Last() => system.m_SoundEmitterActiveList.Select(e => e.SeqId).DefaultIfEmpty(-1).Max();
            SoundSystem.SoundInfo Weapon() => (SoundSystem.SoundInfo)typeof(MoonkovPlayerAudio).GetField("m_WeaponSound", hidden).GetValue(player);
            foreach (var bank in new[] { lib.WeaponDraw, lib.WeaponDrawPistol, lib.WeaponStow, lib.WeaponAimIn, lib.WeaponAimOut,
                lib.MeleeDraw, lib.GrenadeDraw, lib.GrenadePin, lib.GrenadeThrow, lib.OxygenConnect, lib.OxygenDisconnect,
                lib.Light, lib.RoomSwitch, lib.DryFire, lib.TerminalStart, lib.TerminalComplete })
            {
                var info = MoonkovAudio.Play(bank, listener.transform.position);
                Check(info?.Handle?.IsValid() == true, bank.name + " allocation");
                var source = info.Handle.Emitter.ActiveSoundGameObjects[0].m_AudioSource;
                Check(source.isPlaying && source.volume > 0 && source.outputAudioMixerGroup.name == "SFX" && !source.loop,
                    bank.name + " native source/routing");
                var timer = System.Diagnostics.Stopwatch.StartNew();
                while (info.Handle.IsValid() && timer.Elapsed.TotalSeconds < source.clip.length + 1.5)
                { await Task.Delay(25); system.UpdateSoundSystem(false); }
                Check(!info.Handle.IsValid() && pool.AvailableSoundObjectCount == 16, bank.name + " natural end/recycle");
            }
            foreach (bool owner in new[] { true, false })
            {
                player.Stop(); system.KillAll(); player = new MoonkovPlayerAudio();
                var ghost = new PredictedPlayerGhost(); RaidHealth.Initialize(ref ghost); ghost.BreathableAir = true;
                ghost.ControllerState.CurrentRotation = Unity.Mathematics.quaternion.identity;
                ghost.EquippedWeaponID = DollSingerWeapons.Halo;
                ghost.EquippedWeaponSlot = 1; ghost.EquippedWeaponItem = "rifle-a";
                player.Update(ghost, listener.transform, owner, .02f);
                Check(Weapon() == null, "First snapshot replayed draw");
                foreach (var id in new[] { DollSingerWeapons.Revolver, DollSingerWeapons.Halo, DollSingerWeapons.None })
                {
                    ghost.EquippedWeaponID = id; player.Update(ghost, listener.transform, owner, .02f);
                    var expected = id == DollSingerWeapons.None ? lib.WeaponStow : id == DollSingerWeapons.Revolver ? lib.WeaponDrawPistol : lib.WeaponDraw;
                    var info = Weapon();
                    Check(info?.Handle?.IsValid() == true && info.SoundDefinition == expected, "Actual weapon transition " + id);
                    var source = info.Handle.Emitter.ActiveSoundGameObjects[0].m_AudioSource;
                    Check(source.isPlaying && source.spatialBlend == (owner ? 0 : 1) && source.transform.IsChildOf(listener.transform),
                        "Weapon owner/remote and follow");
                    for (int n = 0; n < 40; n++) player.Update(ghost, listener.transform, owner, .02f);
                    Check(Weapon() == info, "Weapon repeated snapshot retriggered");
                }
                ghost.EquippedWeaponID = DollSingerWeapons.Halo; player.Update(ghost, listener.transform, owner, .02f);
                var prior = Weapon(); ghost.EquippedWeaponSlot = 2; ghost.EquippedWeaponItem = "rifle-b";
                player.Update(ghost, listener.transform, owner, .02f);
                Check(Weapon() != prior && Weapon()?.Handle?.IsValid() == true, "Same-model slot swap was silent");
                Check(Weapon().SoundDefinition == lib.WeaponDraw, "Same-model slot used wrong cue");
                prior = Weapon(); for (int n = 0; n < 40; n++) player.Update(ghost, listener.transform, owner, .02f);
                Check(Weapon() == prior, "Same-model snapshots repeated draw");
                ghost.EquippedWeaponItem = "rifle-c"; player.Update(ghost, listener.transform, owner, .02f);
                Check(Weapon() != prior && Weapon()?.Handle?.IsValid() == true, "Same-slot item replacement was silent");
                ghost.CurrentHealth = 0; player.Update(ghost, listener.transform, owner, .02f);
                await Task.Delay(400); system.UpdateSoundSystem(false);
                Check(Weapon() == null && pool.AvailableSoundObjectCount == 16, "Death retained weapon/voice sources");

                foley.Stop(); system.KillAll(); foley = new RaidActionFoley();
                void Oxygen(float phase) => foley.Observe(HandAction.RefillOxygen, 1, phase, .5f, "oxygen_canister",
                    listener.transform.position, owner, true, .08f, .64f, listener.transform);
                Oxygen(0); Check(Count(lib.OxygenConnect) == 0, "Early oxygen connector");
                Oxygen(.1f); Check(Count(lib.OxygenConnect) == 1, "Oxygen connector missing");
                Oxygen(.51f); Check(Count(lib.OxygenCanisterFlow) == 1, "Oxygen flow missing");
                Oxygen(.83f); Check(Count(lib.OxygenDisconnect) == 1, "Oxygen disconnect missing");
                int before = Last(); for (int n = 0; n < 40; n++) Oxygen(.83f);
                Check(before == Last(), "Oxygen markers duplicated");
                var disconnect = system.m_SoundEmitterActiveList.Single(e => e.Active && e.Definition == lib.OxygenDisconnect).ActiveSoundGameObjects[0].m_AudioSource;
                Check(disconnect.isPlaying && disconnect.spatialBlend == (owner ? 0 : 1) && disconnect.transform.IsChildOf(listener.transform),
                    "Oxygen disconnect owner/remote and follow");
                foley.Stop(); await Task.Delay(180); system.UpdateSoundSystem(false);
                Check(pool.AvailableSoundObjectCount == 16, "Interrupted oxygen retained sounds");

                melee = new MoonkovMeleeAudio(); ghost.CurrentHealth = ghost.MaxHealth;
                ghost.MeleeWeapon = (byte)MeleeWeaponKind.None;
                melee.Update(ghost, Vector3.zero, owner); Check(Count(lib.MeleeDraw) == 0, "Late melee snapshot replayed draw");
                ghost.MeleeWeapon = (byte)MeleeWeaponKind.Dagger; melee.Update(ghost, Vector3.zero, owner);
                Check(Count(lib.MeleeDraw) == 1, "Dagger draw missing");
                before = Last(); for (int n = 0; n < 40; n++) melee.Update(ghost, Vector3.zero, owner);
                Check(before == Last(), "Melee equip repeated");
                ghost.MeleeWeapon = (byte)MeleeWeaponKind.None; melee.Update(ghost, Vector3.zero, owner);
                Check(before == Last(), "Melee un-equip duplicated weapon draw");
                ghost.MeleeWeapon = (byte)MeleeWeaponKind.Fists; melee.Update(ghost, Vector3.zero, owner);
                Check(Count(lib.InventoryCloth) == 1, "Fists should use clothing movement");
                melee.Stop(); system.KillAll();
            }

            rigHolder = new GameObject("Grenade audio rig review"); rigHolder.SetActive(false);
            var root = UnityEngine.Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/DollSinger/Prefabs/DollSingerNetworkPlayer.prefab"), rigHolder.transform);
            foreach (var b in root.GetComponentsInChildren<MonoBehaviour>(true)) b.enabled = false;
            foreach (var a in root.GetComponentsInChildren<AudioListener>(true)) a.enabled = false;
            foreach (var c in root.GetComponentsInChildren<Camera>(true)) c.enabled = false;
            var animator = root.GetComponentInChildren<Animator>(true);
            for (var node = animator.transform; node; node = node.parent) node.gameObject.SetActive(true);
            animator.Rebind(); animator.Update(0);
            grenade = animator.gameObject.AddComponent<GrenadePresentation>(); grenade.enabled = false;
            grenade.Configure(animator, null, "DollSinger");
            var hand = animator.GetBoneTransform(HumanBodyBones.RightHand);
            foreach (bool owner in new[] { true, false })
            {
                grenade.ResetPresentation(); system.KillAll();
                var ghost = new PredictedPlayerGhost { CurrentHealth = 100, GrenadeItem = EnergyGrenadeRules.Code, GrenadeHoldId = 1 };
                void Present(GrenadePhase phase, float progress)
                {
                    ghost.GrenadePhase = (byte)phase; ghost.GrenadePhaseRemaining = GrenadeHandling.Duration(phase) * (1 - progress);
                    grenade.Present(ghost, owner); grenade.SamplePreview(progress);
                }
                Present(GrenadePhase.Drawing, 0); Check(Count(lib.GrenadeDraw) == 1, "Actual grenade draw missing");
                int before = Last(); for (int n = 0; n < 40; n++) Present(GrenadePhase.Drawing, .2f);
                Check(before == Last(), "Grenade draw repeated");
                Present(GrenadePhase.Priming, .79f); Check(Count(lib.GrenadePin) == 0, "Pin sounded before animation marker");
                Present(GrenadePhase.Priming, .81f); Check(Count(lib.GrenadePin) == 1, "Actual pin pull missing");
                var pin = (SoundSystem.SoundInfo)typeof(GrenadePresentation).GetField("pinSound", hidden).GetValue(grenade);
                var pinSource = pin.Handle.Emitter.ActiveSoundGameObjects[0].m_AudioSource;
                Check(pinSource.isPlaying && pinSource.spatialBlend == (owner ? 0 : 1) && pinSource.transform.IsChildOf(hand), "Pin routing/follow");
                before = Last(); for (int n = 0; n < 40; n++) Present(GrenadePhase.Priming, .85f);
                Check(before == Last(), "Pin repeated");
                Present(GrenadePhase.Throwing, 0); Check(Count(lib.GrenadeThrow) == 1, "Grenade throw missing");
                await Task.Delay(70); system.UpdateSoundSystem(false);
                Check(pin.Handle.IsValid() && pinSource.isPlaying, "Quick throw cut off pin tail");
                Present(GrenadePhase.Stowing, 0); Check(Count(lib.WeaponStow) == 1, "Grenade stow missing");
                ghost.CurrentHealth = 0; grenade.Present(ghost, owner);
                await Task.Delay(180); system.UpdateSoundSystem(false);
                Check(pool.AvailableSoundObjectCount == 16, "Grenade reset retained handles");
            }
            return "PASS " + checks + " complete native playback, weapon snapshots, melee, oxygen markers and production grenade presentation checks";
        }
        finally
        {
            player.Stop(); foley.Stop(); melee.Stop(); if (grenade) grenade.ResetPresentation(); system.KillAll();
            instance.SetValue(null, null); Application.runInBackground = background;
            if (rigHolder) UnityEngine.Object.DestroyImmediate(rigHolder);
            UnityEngine.Object.DestroyImmediate(holder); UnityEngine.Object.DestroyImmediate(listener); UnityEngine.Object.DestroyImmediate(sources);
        }
    }
}
