using System;
using System.Linq;
using Unity.MP_FPS;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

/// <summary>Checks chapter template selection without loading a player profile or raid.</summary>
public static class CampaignSiteChecks
{
    [MenuItem("Moonkov/Campaign/Check Facility Selection")]
    public static void Run()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Run in Edit Mode.");
        var root = new GameObject("Temporary campaign facility") { hideFlags = HideFlags.HideAndDontSave };
        root.SetActive(false);
        try
        {
            var site = root.AddComponent<RaidMissionSite>();
            site.Author(new[] { new RaidMissionAnchor { Id = "entry", Label = "entry" } },
                Array.Empty<RaidMissionDevice>(), null, null);
            var original = site.Definitions.Single(d => d.Id == "mc00_archive");
            float oldOffer = original.OfferSeconds, oldActive = original.ActiveSeconds;
            site.SetCampaignChapter(1);
            Require(site.FirstChapter && site.Definitions.Single(d => d.Id == "mc00_mooncore").Objectives.Any(n => n.Id == "report"),
                "first chapter keeps registration");
            site.SetCampaignChapter(2);
            var archive = site.Definitions.Single(d => d.Id == "mc00_archive");
            Require(!site.FirstChapter && archive.OfferSeconds == 600 && archive.ActiveSeconds == 600 &&
                archive.Objectives.Single(n => n.Id == "tag_a").RecordBeforePrerequisites &&
                archive.Objectives.Single(n => n.Id == "tag_b").RecordBeforePrerequisites,
                "archive chapter is guaranteed and retains early scanning");
            Require(original.OfferSeconds == oldOffer && original.ActiveSeconds == oldActive,
                "chapter setup does not mutate the ordinary random facility");
            site.SetCampaignChapter(0);
            Require(ReferenceEquals(site.Definitions.Single(d => d.Id == "mc00_archive"), original),
                "ordinary missions recover their authored templates");
            bool rejected = false;
            try { site.SetCampaignChapter(3); } catch (ArgumentOutOfRangeException) { rejected = true; }
            Require(rejected && ReferenceEquals(site.Definitions.Single(d => d.Id == "mc00_archive"), original),
                "unsupported chapter cannot replace the active template");
        }
        finally { Object.DestroyImmediate(root); }
        Debug.Log("Campaign facility selection checks passed. Scene and gameplay acceptance remain separate.");
    }

    private static void Require(bool value, string name)
    { if (!value) throw new InvalidOperationException("Campaign facility check: " + name); }
}
