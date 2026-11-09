using System;
using System.IO;
using System.Linq;
using Unity.MP_FPS;
using Unity.MP_FPS.Inventory;
using UnityEditor;
using UnityEngine;

public static class LootRevealAudioChecks
{
    public static string Install()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Use Edit mode");
        var lib = Resources.Load<MoonkovAudioLibrary>("Moonkov/AudioLibrary");
        foreach (var bank in new[] { lib.Container, lib.LootSearchWorld })
        {
            bank.PitchAndVolumeInfo.VolumeMax = bank == lib.Container ? -20 : -18;
            bank.PitchAndVolumeInfo.VolumeMin = bank.PitchAndVolumeInfo.VolumeMax - 1;
            EditorUtility.SetDirty(bank); AssetDatabase.SaveAssetIfDirty(bank);
        }
        return "Installed quieter per-item box and world rummage gains";
    }

    public static string Run()
    {
        int checks = 0;
        void Check(bool value, string message) { checks++; if (!value) throw new InvalidOperationException(message); }
        var carried = InventoryGraph.Create(false, false);
        var cache = LootInventoryExchange.CreateCache(1);
        int count = cache.Children(LootInventoryExchange.Root).Count();
        var audio = new MoonkovLootRevealAudio();
        RaidInventoryClientState State(int visible, bool done = false, uint request = 10, int raid = 1) =>
            new RaidInventoryClientState { RaidId = raid, RequestId = request, LootId = 7,
                DissectingLootId = done ? -1 : 7, ActionFeedback = done ? "搜索完成。" : null,
                Graph = LootInventoryExchange.Snapshot(carried, LootInventoryExchange.SearchPreview(cache, visible)) };

        audio.Observe(State(count, true));
        Check(!audio.TryPlay(0), "Late join replayed historical discoveries");
        audio.Begin(1, 10, 7, carried);
        audio.Observe(State(count, true, 9)); audio.Observe(State(count, true, 10, 2));
        Check(!audio.TryPlay(0), "Stale request or foreign raid revealed items");
        audio.Observe(State(0)); Check(!audio.TryPlay(0), "Empty search start played a success");
        for (int i = 1; i <= count; i++)
        {
            var snapshot = State(i, i == count);
            audio.Observe(snapshot);
            Check(audio.TryPlay(i * 2), "Missing per-item cue, including final item: " + i);
            audio.Observe(snapshot); snapshot.Graph.Items.Reverse(); audio.Observe(snapshot);
            Check(!audio.TryPlay(i * 2 + 1.5f), "Snapshot refresh/reordering repeated an item");
        }
        audio.Begin(1, 11, 7, carried);
        var reopened = State(count, true, 11); reopened.ActionFeedback = null;
        audio.Observe(reopened);
        Check(!audio.TryPlay(50), "Reopening a searched box replayed discoveries");

        audio.Reset(); audio.Begin(1, 10, 7, carried);
        audio.Observe(State(count, true)); // All progress packets coalesced into the completion.
        for (int i = 0; i < count; i++)
        {
            Check(audio.TryPlay(i * 2), "Coalesced snapshot lost an item");
            Check(!audio.TryPlay(i * 2 + .1f), "Coalesced cues overlapped");
        }
        Check(!audio.TryPlay(100), "Whole-search completion added an extra sound");

        audio.Reset(); audio.Begin(1, 10, 7, carried);
        audio.Observe(State(2)); audio.Cancel();
        Check(!audio.TryPlay(100), "Closing/cancelling retained queued cues");
        audio.Begin(1, 11, 7, carried); audio.Observe(State(2, false, 11));
        Check(!audio.TryPlay(100), "Resuming repeated previously discovered items");
        audio.Observe(State(3, false, 11));
        Check(audio.TryPlay(100), "Resuming missed a new item");

        audio.Reset(); audio.Begin(1, 10, 7, carried);
        audio.Observe(State(2));
        var denied = State(count, true); denied.Error = InventoryError.Inaccessible;
        audio.Observe(denied);
        Check(!audio.TryPlay(100), "Rejected/closed search played success");
        audio.Reset(); audio.Begin(1, 10, 7, carried); audio.Observe(State(count, true));
        var closed = State(count, true); closed.LootId = -1; audio.Observe(closed);
        Check(!audio.TryPlay(100), "Server closure retained completed-search queue");

        var baseline = State(1).Graph;
        baseline.Children(LootInventoryExchange.Root).First().Parent = InventoryCatalog.Pockets;
        audio.Reset(); audio.Begin(1, 10, 7, baseline);
        var moved = State(1); moved.Graph.Children(LootInventoryExchange.Root).First().Quantity++;
        audio.Observe(moved);
        Check(!audio.TryPlay(0), "Quantity change or owned item moved into loot counted as a discovery");
        cache = LootInventoryExchange.CreateCache(0);
        var bag = cache.Children(LootInventoryExchange.Root).Single(i => i.Code == "small_pack");
        cache.AddSupply("water", 1, bag.Id, true);
        var nested = State(0);
        nested.Graph = LootInventoryExchange.Snapshot(carried, LootInventoryExchange.SearchPreview(cache, new[] { bag.Id }));
        audio.Reset(); audio.Begin(1, 10, 7, carried); audio.Observe(nested);
        Check(audio.TryPlay(0) && !audio.TryPlay(2), "Bag reveal counted nested contents as extra discoveries");
        audio.Begin(2, 1, 7, carried); audio.Observe(State(1, false, 1, 2));
        Check(audio.TryPlay(0), "New raid retained old discovery history");

        var lib = Resources.Load<MoonkovAudioLibrary>("Moonkov/AudioLibrary");
        Check(lib.Container.Clips.Count == 1 && AssetDatabase.GetAssetPath(lib.Container.Clips[0]) ==
            "Assets/Audio/Moonkov/Clips/Interaction/Container.wav", "Original box sound replaced");
        Check(lib.Container.PitchAndVolumeInfo.VolumeMin == -21 && lib.Container.PitchAndVolumeInfo.VolumeMax == -20,
            "Reveal gain does not match installer");
        Check(lib.LootSearchWorld.PitchAndVolumeInfo.VolumeMin == -19 && lib.LootSearchWorld.PitchAndVolumeInfo.VolumeMax == -18,
            "Rummage gain does not match installer");
        Check(lib.Container.Clips[0].length < 1.3f && lib.Container.DistanceInfo.SpatialBlend == 0,
            "Box sound overlaps queue cadence or is incorrectly spatialized");
        MoonkovInteractionAudioChecks.Run();
        return $"PASS {checks} loot discovery, deduplication, catch-up, cancellation, raid and bank checks; interaction regressions passed";
    }

    public static string Preview()
    {
        var lib = Resources.Load<MoonkovAudioLibrary>("Moonkov/AudioLibrary");
        var clip = lib.Container.Clips[0]; clip.LoadAudioData();
        var pcm = new float[clip.samples * clip.channels];
        if (!clip.GetData(pcm, 0)) throw new InvalidOperationException("Cannot read imported PCM");
        int stride = (int)(clip.frequency * 1.7f);
        var output = new short[stride * 2 + clip.samples];
        float gain = Mathf.Pow(10, (lib.Container.PitchAndVolumeInfo.VolumeMin + lib.Container.PitchAndVolumeInfo.VolumeMax) / 40) * .8f * .85f;
        for (int n = 0; n < 3; n++)
            for (int i = 0; i < clip.samples; i++)
            {
                float sample = 0;
                for (int c = 0; c < clip.channels; c++) sample += pcm[i * clip.channels + c] / clip.channels;
                output[n * stride + i] = (short)(Mathf.Clamp(sample * gain, -1, 1) * short.MaxValue);
            }
        string path = Path.GetFullPath("LocalData/GameAudioReview/Previews/LootPerItemQuiet.wav");
        Directory.CreateDirectory(Path.GetDirectoryName(path));
        using (var writer = new BinaryWriter(File.Create(path)))
        {
            writer.Write(System.Text.Encoding.ASCII.GetBytes("RIFF")); writer.Write(36 + output.Length * 2);
            writer.Write(System.Text.Encoding.ASCII.GetBytes("WAVEfmt ")); writer.Write(16); writer.Write((short)1); writer.Write((short)1);
            writer.Write(clip.frequency); writer.Write(clip.frequency * 2); writer.Write((short)2); writer.Write((short)16);
            writer.Write(System.Text.Encoding.ASCII.GetBytes("data")); writer.Write(output.Length * 2);
            foreach (short sample in output) writer.Write(sample);
        }
        return path;
    }
}
