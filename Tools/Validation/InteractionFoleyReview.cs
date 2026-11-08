using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Unity.MP_FPS;
using Unity.MP_FPS.Inventory;
using UnityEngine;

public static class InteractionFoleyReview
{
    public static async Task<string> Run()
    {
        if (!Application.isPlaying || GameManager.Instance) throw new InvalidOperationException("Use CoughVoiceReview.Begin isolated Play scene");
        const BindingFlags hidden = BindingFlags.Instance | BindingFlags.NonPublic;
        var instance = typeof(GameManager).GetProperty("Instance", BindingFlags.Public | BindingFlags.Static);
        var holder = new GameObject("Interaction review manager"); holder.SetActive(false);
        var manager = holder.AddComponent<GameManager>();
        var actor = new GameObject("Interaction review listener"); actor.AddComponent<AudioListener>();
        var sources = new GameObject("Interaction review pool");
        var pool = (SoundGameObjectPool)System.Runtime.Serialization.FormatterServices.GetUninitializedObject(typeof(SoundGameObjectPool));
        var system = new SoundSystem(); var player = new MoonkovPlayerAudio(); int checks = 0;
        bool background = Application.runInBackground; Application.runInBackground = true;
        void Check(bool condition, string reason) { checks++; if (!condition) throw new InvalidOperationException(reason); }
        try
        {
            typeof(SoundGameObjectPool).GetField("m_SourceHolder", hidden).SetValue(pool, sources);
            pool.SoundGameObjectList = new List<SoundGameObject>();
            for (int i = 0; i < 16; i++) pool.SoundGameObjectList.Add(pool.Create());
            for (int i = 0; i < 16; i++) pool.SoundGameObjectList[i].NextAvailable = pool.SoundGameObjectList[(i + 1) % 16];
            pool.NextSoundGameObject = pool.SoundGameObjectList[0]; pool.AvailableSoundObjectCount = 16;
            var lib = MoonkovAudio.Library; system.Init(actor.transform, 16, pool, lib.InventoryOpen.UnityAudioMixer);
            instance.SetValue(null, manager); typeof(GameManager).GetField("m_SoundSystem", hidden).SetValue(manager, system);
            foreach (var entry in InventoryCatalog.Definitions.Values)
                Check(MoonkovInventoryAudio.ForItem(entry.Code, lib) != null, "Silent item type " + entry.Code);
            Check(MoonkovInventoryAudio.ForItem("ration", lib) == lib.InventoryFood, "Ration texture");
            Check(MoonkovInventoryAudio.ForItem("medkit", lib) == lib.InventoryPlastic, "Medical casing texture");
            Check(MoonkovInventoryAudio.ForItem("backpack", lib) == lib.InventoryCloth, "Backpack texture");
            Check(MoonkovInventoryAudio.ForItem("alloy", lib) == lib.InventoryMetal, "Metal texture");
            Check(MoonkovInventoryAudio.ForItem(null, lib) == lib.Equipment, "Unknown item fallback");
            var graph = new InventoryGraph();
            graph.Items.Add(new InventoryItem { Id = "gun", Code = "rifle", Parent = InventoryCatalog.Pockets });
            var command = new InventoryCommand { ItemId = "gun", Parent = InventoryCatalog.Equipment };
            Check(MoonkovInventoryAudio.ForMove(graph, command, lib) == lib.InventoryEquip, "Equip click missing");
            command.Parent = InventoryCatalog.Pockets;
            Check(MoonkovInventoryAudio.ForMove(graph, command, lib) == lib.InventoryMetal, "Move must retain material");
            var receipt = new MoonkovActionAudio(); receipt.Track(1, 7, lib.InventoryMetal);
            Check(receipt.Observe(1, 6, false, false, lib.Error) == null, "Old receipt played pickup");
            Check(receipt.Observe(1, 7, false, true, lib.Error) == lib.Error, "Rejected transaction sounded successful");
            Check(receipt.Observe(1, 7, false, false, lib.Error) == null, "Duplicate receipt replayed pickup");
            foreach (var bank in new[] { lib.InventoryOpen, lib.InventoryClose, lib.InventoryCloth, lib.InventoryMetal,
                lib.InventoryPlastic, lib.InventoryFood, lib.InventoryEquip, lib.InventoryDrop, lib.BatteryInsert,
                lib.Container, lib.ContainerClose, lib.LootSearchWorld })
            {
                AudioClip previous = null;
                for (int n = 0; n < bank.Clips.Count; n++)
                {
                    var info = MoonkovAudio.Play(bank, actor.transform.position);
                    Check(info?.Handle?.IsValid() == true, bank.name + " did not allocate");
                    var source = info.Handle.Emitter.ActiveSoundGameObjects[0].m_AudioSource;
                    Check(source.isPlaying && source.volume > 0 && source.outputAudioMixerGroup.name == "SFX" && !source.loop, bank.name + " native playback");
                    if (previous) Check(source.clip != previous, bank.name + " adjacent variant repeated");
                    previous = source.clip;
                    var timer = System.Diagnostics.Stopwatch.StartNew();
                    while (info.Handle.IsValid() && timer.Elapsed.TotalSeconds < source.clip.length + 1.5)
                    { await Task.Delay(25); system.UpdateSoundSystem(false); }
                    Check(!info.Handle.IsValid() && pool.AvailableSoundObjectCount == 16, bank.name + " did not finish and recycle");
                }
            }
            var ghost = new PredictedPlayerGhost(); RaidHealth.Initialize(ref ghost);
            ghost.ControllerState.CurrentRotation = Unity.Mathematics.quaternion.identity;
            ghost.LootSearching = true; ghost.LootSearchSequence = 1;
            player.Update(ghost, actor.transform, false, .02f);
            Check(!system.m_SoundEmitterActiveList.Any(e => e.Active), "Late join replayed search");
            ghost.LootSearchSequence++;
            player.Update(ghost, actor.transform, false, .02f);
            Check(system.m_SoundEmitterActiveList.Count(e => e.Active && e.Definition == lib.LootSearchWorld) == 1, "Actual search sequence missing");
            int sequence = system.m_SoundEmitterActiveList.First(e => e.Active).SeqId;
            for (int n = 0; n < 50; n++) player.Update(ghost, actor.transform, false, .02f);
            Check(system.m_SoundEmitterActiveList.Single(e => e.Active).SeqId == sequence, "Search snapshots retriggered");
            ghost.LootSearching = false; player.Update(ghost, actor.transform, false, .02f);
            await Task.Delay(160); system.UpdateSoundSystem(false);
            Check(pool.AvailableSoundObjectCount == 16, "Cancelled search retained audio");
            return "PASS " + checks + " item classification, receipts, complete native playback, variants, search cancellation and pooling checks";
        }
        finally
        {
            player.Stop(); system.KillAll(); instance.SetValue(null, null); Application.runInBackground = background;
            UnityEngine.Object.DestroyImmediate(holder); UnityEngine.Object.DestroyImmediate(actor); UnityEngine.Object.DestroyImmediate(sources);
        }
    }
}
