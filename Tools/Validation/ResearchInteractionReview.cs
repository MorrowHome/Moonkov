using System;
using System.Reflection;
using Unity.Entities;
using Unity.MP_FPS;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

public static class ResearchInteractionReview
{
    public static string Run()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode || BuildPipeline.isBuildingPlayer)
            throw new InvalidOperationException("Use an idle Editor in Edit mode");
        using var world = new World("Research HUD interaction review");
        var preview = EditorSceneManager.NewPreviewScene();
        var query = world.EntityManager.CreateEntityQuery(ComponentType.ReadOnly<PredictedPlayerGhost>());
        try
        {
            var root = new GameObject("Research HUD review");
            root.SetActive(false);
            SceneManager.MoveGameObjectToScene(root, preview);
            var hud = root.AddComponent<RaidHUD>();
            const BindingFlags hidden = BindingFlags.Instance | BindingFlags.NonPublic;
            typeof(RaidHUD).GetField("m_PlayerQuery", hidden).SetValue(hud, query);
            var gate = typeof(RaidHUD).GetMethod("ResearchInteractionAllowed", hidden);
            if (gate == null) throw new InvalidOperationException("Missing research HUD interaction gate");
            int checks = 0;
            void Check(bool expected, string label, bool live = true, bool clash = false)
            {
                bool actual = (bool)gate.Invoke(hud, new object[] { live, clash });
                if (actual != expected) throw new InvalidOperationException(label + ": expected " + expected + ", got " + actual);
                checks++;
            }
            Check(false, "Inactive HUD does not read a missing player", live: false);
            Check(false, "Clash does not read a missing player", clash: true);
            var player = world.EntityManager.CreateEntity(typeof(PredictedPlayerGhost));
            var state = new PredictedPlayerGhost { CurrentHealth = 100, HaloEquipped = true };
            void Apply() => world.EntityManager.SetComponentData(player, state);
            Apply(); Check(true, "Idle hands can operate");
            foreach (var mode in new[] { HaloDeploymentMode.Light, HaloDeploymentMode.Shield, HaloDeploymentMode.Sentry })
            {
                state.HaloDeploymentMode = mode;
                state.HaloTransferPhase = HaloTransferPhase.Extending; Apply(); Check(false, mode + " is still extending");
                state.HaloTransferPhase = HaloTransferPhase.Sending; Apply(); Check(true, mode + " is anchored");
                state.HaloTransferPhase = HaloTransferPhase.Returning; Apply(); Check(false, mode + " is returning");
            }
            state.HaloTransferPhase = HaloTransferPhase.Sending;
            foreach (var mode in new[] { HaloDeploymentMode.Transfer, HaloDeploymentMode.AllyGuard })
            {
                state.HaloDeploymentMode = mode; Apply(); Check(false, mode + " keeps the halo occupied");
            }
            state.HaloDeploymentMode = HaloDeploymentMode.Sentry; Apply();
            Check(false, "Menus or lost focus block interaction", live: false);
            Check(false, "Clash blocks research interaction", clash: true);
            state.GrenadePhase = (byte)GrenadePhase.Live; Apply(); Check(false, "Held grenade blocks interaction");
            state.GrenadePhase = 0;
            state.HaloDefenseHeld = true; Apply(); Check(false, "Handheld defense blocks interaction");
            state.HaloDefenseHeld = false;
            state.ControllerState.IsReloadingState = true; Apply(); Check(false, "Reload blocks interaction");
            state.ControllerState.IsReloadingState = false;
            state.WeaponEquipRemaining = 1; Apply(); Check(false, "Weapon equip blocks interaction");
            state.WeaponEquipRemaining = 0;
            state.LastShotTick = state.LastSentryShotTick = 42; Apply(); Check(true, "Sentry fire keeps the HUD gate open");
            return checks + " actual research HUD gate checks passed; no Play mode or Transport.";
        }
        finally
        {
            query.Dispose();
            EditorSceneManager.ClosePreviewScene(preview);
        }
    }
}
