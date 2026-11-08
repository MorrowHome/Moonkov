using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Unity.Entities;
using Unity.MP_FPS;
using Unity.MP_FPS.DollSinger;
using Unity.MP_FPS.Inventory;
using Unity.NetCode;
using UnityEditor;
using UnityEditor.Localization;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Localization.Tables;
using UnityEngine.Rendering.Universal;
using Object = UnityEngine.Object;

public static class TeamTrackerReview
{
    private const string Output = "LocalData/TeamTracker";
    private static readonly BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    public static object Run()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Use the existing idle Editor.");
        Directory.CreateDirectory(Output);
        int checks = 0;
        void Check(bool pass, string reason) { checks++; if (!pass) throw new InvalidOperationException(reason); }
        var graph = InventoryGraph.Create(false, false);
        var item = new InventoryItem { Id = Guid.NewGuid().ToString("D"), Code = TeamOutlineRules.ItemCode, Parent = "equipment", Region = "Special1" };
        graph.Items.Add(item);
        foreach (string slot in new[] { "Special1", "Special2", "Special3" })
        { item.Region = slot; Check(graph.Validate() == InventoryError.None && TeamOutlineRules.Equipped(graph), "All three special slots activate the tracker"); }
        item.Parent = "pockets"; item.Region = "1";
        Check(!TeamOutlineRules.Equipped(graph), "Carrying in pockets does not activate it");
        Check(LootInventoryExchange.DropOnDeath(graph).Count(TeamOutlineRules.ItemCode) == 1, "Unequipped tracker drops on death");
        graph.Items.Add(item); item.Parent = "equipment"; item.Region = "Special1";
        Check(LootInventoryExchange.DropOnDeath(graph).Count(TeamOutlineRules.ItemCode) == 0 && TeamOutlineRules.Equipped(graph), "Equipped tracker is retained on death");
        Check(SecureInventory.FailureReturn(graph).Count(TeamOutlineRules.ItemCode) == 1, "Equipped tracker is retained on timeout");
        var account = InventoryGraph.Create(true, false); account.AddSupply("dust", 100, "stash");
        var buy = new ShopCommand { RequestId = Guid.NewGuid().ToString("D"), Code = TeamOutlineRules.ItemCode, Operation = ShopOperation.Buy, Quantity = 1, ExpectedVersion = account.Version };
        Check(ShopRules.Apply(account, buy, out var purchased) == null && purchased.Count(TeamOutlineRules.ItemCode) == 1 && purchased.Count("dust") == 64, "Real shop transaction buys the tracker for 36 dust");
        var localState = new PredictedPlayerGhost { CurrentHealth = 100, MaxHealth = 100, OnlineHuman = true, OnlineTeam = 1 };
        var otherState = localState;
        Check(TeamOutlineRules.Teammate(localState, otherState, true, true), "Online teammates are accepted");
        otherState.OnlineTeam = 2;
        Check(!TeamOutlineRules.Teammate(localState, otherState, true, true), "Other online teams are excluded");
        otherState = localState; otherState.OnlineTeam = -1;
        Check(!TeamOutlineRules.Teammate(localState, otherState, true, true), "Unknown team is excluded");
        otherState = localState; otherState.CurrentHealth = 0;
        Check(!TeamOutlineRules.Teammate(localState, otherState, true, true), "Corpses are excluded");
        var coop = new PredictedPlayerGhost { CurrentHealth = 100 };
        Check(TeamOutlineRules.Teammate(coop, coop, true, true), "Cooperative human teammates are accepted");
        Check(!TeamOutlineRules.Teammate(coop, coop, true, false), "Cooperative NPC enemies are excluded");
        var clash = new PredictedPlayerGhost { CurrentHealth = 100, ClashEnabled = true, ClashTeam = 0, ClashLife = ClashLife.Alive, ClashCanAct = true };
        var downed = clash; downed.CurrentHealth = 0; downed.ClashLife = ClashLife.Downed; downed.ClashCanAct = false;
        Check(TeamOutlineRules.Teammate(clash, downed, true, false), "Downed allied Clash bots remain rescue targets");
        downed.ClashTeam = 1;
        Check(!TeamOutlineRules.Teammate(clash, downed, true, false), "Downed enemy remains excluded");
        Check(!TeamOutlineRules.Teammate(clash, coop, true, true), "Different modes are excluded");
        Check(!TeamOutlineRules.CanView(downed), "Downed viewer cannot use tracker");
        var collection = LocalizationEditorSettings.GetStringTableCollection("MoonkovUI");
        foreach (var reference in collection.Tables)
        {
            var table = (StringTable)reference.asset;
            Check(!string.IsNullOrWhiteSpace(table.GetEntry("T-01 Squad tracker")?.LocalizedValue), "Item name exists in " + table.LocaleIdentifier.Code);
        }
        Check(Unity.MP_FPS.Client.ContainerInventoryView.Art(TeamOutlineRules.ItemCode) == Unity.MP_FPS.Client.StashArtKind.TeamTracker, "Dedicated item art mapping");
        string physical = PhysicalItemAssetSetup.Validate(); checks++;

        var scene = EditorSceneManager.NewPreviewScene();
        var world = new World("Squad tracker client review, no transport");
        var em = world.EntityManager;
        var system = world.GetOrCreateSystemManaged<TeamOutlineClientSystem>();
        RenderTexture target = null; Material surface = null;
        var root = new GameObject("Squad tracker review"); UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(root, scene);
        try
        {
            var cameraNode = new GameObject("Local owner camera"); cameraNode.transform.SetParent(root.transform);
            var camera = cameraNode.AddComponent<Camera>(); camera.enabled = false; camera.scene = scene; camera.cullingMask = 1 << 5;
            camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = new Color(.025f, .035f, .05f);
            camera.nearClipPlane = .05f; camera.farClipPlane = 30; camera.fieldOfView = 48;
            camera.transform.position = new Vector3(0, 1.4f, -4); camera.transform.LookAt(new Vector3(0, .95f, 3));
            var data = camera.GetUniversalAdditionalCameraData(); data.renderPostProcessing = false; data.volumeLayerMask = 0;
            var lampNode = new GameObject("Preview light"); lampNode.transform.SetParent(root.transform);
            var lamp = lampNode.AddComponent<Light>(); lamp.type = LightType.Directional; lamp.intensity = 1.6f; lamp.cullingMask = 1 << 5;
            lampNode.transform.rotation = Quaternion.Euler(40, -30, 0);
            surface = new Material(Shader.Find("Universal Render Pipeline/Lit")); surface.SetColor("_BaseColor", new Color(.12f, .15f, .18f));
            GameObject Box(string name, Vector3 pos, Vector3 scale)
            {
                var go = GameObject.CreatePrimitive(PrimitiveType.Cube); go.name = name; go.transform.SetParent(root.transform);
                go.transform.position = pos; go.transform.localScale = scale; go.layer = 5; go.GetComponent<Renderer>().sharedMaterial = surface; return go;
            }
            Box("Floor", new Vector3(0, -.15f, 3), new Vector3(14, .3f, 14));
            Box("Opaque cover", new Vector3(0, 1.1f, 2.3f), new Vector3(1.4f, 2.2f, .2f));
            var connection = em.CreateEntity(typeof(NetworkStreamInGame));
            var raid = em.CreateEntity(typeof(RaidClientState));
            em.SetComponentData(raid, new RaidClientState { Snapshot = new RaidSnapshotV2Rpc { Sequence = 1, RaidId = 7, Phase = RaidPhase.Active } });
            var inventory = new RaidInventoryClientState(); em.AddComponentObject(raid, inventory);
            uint sequence = 0;
            void Snapshot(InventoryGraph contents, int raidId = 7)
            {
                string json = RaidInventoryTransport.Encode(contents); sequence++;
                int size = RaidInventoryTransport.ChunkCharacters, chunks = (json.Length + size - 1) / size;
                bool accepted = false;
                for (int i = 0; i < chunks; i++) accepted = inventory.Receive(new RaidInventoryChunkV2Rpc { RaidId = raidId, Sequence = sequence, Index = i, Count = chunks,
                    Json = json.Substring(i * size, Math.Min(size, json.Length - i * size)) }, RaidInventoryTransport.Decode);
                Check(accepted, "Real complete inventory snapshot accepted");
            }
            Snapshot(graph);
            var ownerNode = new GameObject("Viewer"); ownerNode.transform.SetParent(root.transform);
            var ownerLink = ownerNode.AddComponent<GhostGameObject>();
            var view = ownerNode.AddComponent<DollSingerView>(); view.enabled = false; view.camera = camera;
            var presentation = ownerNode.AddComponent<DollSingerNetworkPresentation>(); presentation.enabled = false;
            presentation.SetGhostGameObject(ownerLink);
            typeof(DollSingerNetworkPresentation).GetField("m_Linked", Private).SetValue(presentation, true);
            typeof(DollSingerNetworkPresentation).GetField("m_ViewActivated", Private).SetValue(presentation, true);
            typeof(DollSingerNetworkPresentation).GetField("m_View", Private).SetValue(presentation, view);
            Entity Actor(GameObject go, int id, PredictedPlayerGhost health)
            {
                var entity = em.CreateEntity(typeof(PredictedPlayerGhost), typeof(GhostOwner), typeof(GhostOwnerIsLocal));
                em.SetComponentData(entity, health); em.SetComponentData(entity, new GhostOwner { NetworkId = id });
                em.SetComponentEnabled<GhostOwnerIsLocal>(entity, id == 1);
                em.AddComponentObject(entity, new GhostGameObjectLink { LinkedInstance = go.GetComponent<GhostGameObject>() }); return entity;
            }
            var owner = Actor(ownerNode, 1, localState);
            var actors = new List<Entity>(); var skins = new List<SkinnedMeshRenderer[]>();
            string[] prefabs = { "Assets/DollSinger/Prefabs/DollSingerNetworkPlayer.prefab", "Assets/HatsuneMiku/Prefabs/HatsuneMikuNetworkPlayer.prefab", "Assets/Shian/Prefabs/ShianNetworkPlayer.prefab" };
            for (int i = 0; i < prefabs.Length; i++)
            {
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabs[i]);
                if (!prefab) throw new InvalidOperationException("Missing " + prefabs[i]);
                var model = (GameObject)PrefabUtility.InstantiatePrefab(prefab, scene); model.transform.SetParent(root.transform);
                model.transform.position = new Vector3((i - 1) * 1.9f, 0, 3.3f); model.transform.rotation = Quaternion.Euler(0, 180, 0);
                foreach (var t in model.GetComponentsInChildren<Transform>(true)) t.gameObject.layer = 5;
                foreach (var behaviour in model.GetComponentsInChildren<MonoBehaviour>(true)) behaviour.enabled = false;
                foreach (var c in model.GetComponentsInChildren<Camera>(true)) c.enabled = false;
                foreach (var l in model.GetComponentsInChildren<Light>(true)) l.enabled = false;
                model.GetComponentInChildren<DollSingerMovement>(true).gameObject.SetActive(true);
                var animator = model.GetComponentInChildren<Animator>(); animator.Rebind(); animator.Update(0); animator.cullingMode = AnimatorCullingMode.CullUpdateTransforms;
                var renderers = model.GetComponentsInChildren<SkinnedMeshRenderer>(true);
                foreach (var skin in renderers) skin.updateWhenOffscreen = false;
                var state = localState; if (i == 2) state.OnlineTeam = 2;
                actors.Add(Actor(model, i + 2, state)); skins.Add(renderers);
            }
            target = new RenderTexture(1200, 760, 24, RenderTextureFormat.ARGB32); target.Create(); camera.aspect = 1200f / 760;
            int Pixels(string name)
            {
                var previous = RenderTexture.active; var image = new Texture2D(target.width, target.height, TextureFormat.RGB24, false);
                try
                {
                    camera.targetTexture = target; system.Update(); camera.Render(); RenderTexture.active = target;
                    image.ReadPixels(new Rect(0, 0, target.width, target.height), 0, 0); image.Apply();
                    if (name != null) File.WriteAllBytes(Output + "/" + name + ".png", image.EncodeToPNG());
                    return image.GetPixels32().Count(p => p.g > 120 && p.g > p.r * 1.7f && p.b > p.r * 1.5f);
                }
                finally { camera.targetTexture = null; RenderTexture.active = previous; Object.DestroyImmediate(image); }
            }
            var empty = InventoryGraph.Create(false, false); Snapshot(empty);
            int baseline = Pixels("unequipped"); Snapshot(graph);
            int outlined = Pixels("equipped");
            Check(outlined > baseline + 200, "Real client system renders the teammate contours through opaque cover");
            Check(skins[0].All(s => s.updateWhenOffscreen) && skins[1].All(s => s.updateWhenOffscreen) && skins[2].All(s => !s.updateWhenOffscreen), "Only teammates receive occluded animation support");
            Check(!ShaderUtil.ShaderHasError(Resources.Load<Shader>("Moonkov/TeamOutline")), "Native shader compiled");
            foreach (var actor in actors) em.SetComponentData(actor, localState);
            Check(Pixels("three-characters") > outlined, "All three actual character models render complete contours");
            Snapshot(empty); Check(Math.Abs(Pixels(null) - baseline) < 20, "Unequipping removes every outline");
            Check(skins.All(group => group.All(s => !s.updateWhenOffscreen)), "Unequipping restores offscreen skinning");
            Snapshot(graph, 8); Check(Math.Abs(Pixels(null) - baseline) < 20, "Stale raid inventory does not authorize the effect");
            Snapshot(graph); var dead = localState; dead.CurrentHealth = 0; em.SetComponentData(owner, dead);
            Check(Math.Abs(Pixels(null) - baseline) < 20, "Viewer death removes all outlines"); em.SetComponentData(owner, localState);
            em.SetComponentEnabled<GhostOwnerIsLocal>(owner, false);
            Check(Math.Abs(Pixels(null) - baseline) < 20, "Losing local ownership clears effect"); em.SetComponentEnabled<GhostOwnerIsLocal>(owner, true);
            em.SetComponentEnabled<GhostOwnerIsLocal>(actors[0], true);
            Check(Math.Abs(Pixels(null) - baseline) < 20, "Ambiguous local owners do not select a viewer"); em.SetComponentEnabled<GhostOwnerIsLocal>(actors[0], false);
            Check(Pixels(null) > baseline + 200, "Restored ownership resumes the effect");
            em.SetComponentData(actors[1], dead);
            int afterDeath = Pixels(null); Check(afterDeath < PixelsWithLivingTarget(), "Target death removes its contour");
            int PixelsWithLivingTarget() { em.SetComponentData(actors[1], localState); return Pixels(null); }
            var settled = em.GetComponentData<RaidClientState>(raid); settled.Snapshot.Phase = RaidPhase.Extracted; em.SetComponentData(raid, settled);
            Check(Math.Abs(Pixels(null) - baseline) < 20, "Extraction clears effect");
            settled.Snapshot.Phase = RaidPhase.Active; em.SetComponentData(raid, settled); Pixels(null);
            em.DestroyEntity(connection); Check(Math.Abs(Pixels(null) - baseline) < 20, "Disconnect stops rendering");
            Check(skins.All(group => group.All(s => !s.updateWhenOffscreen)), "Disconnect restores animation budgets");
            var result = new { checks, baseline, outlined, physical, scope = "Single Editor isolated client world and native URP preview; no transport, account or multiplayer session" };
            File.WriteAllText(Output + "/review.txt", result.ToString());
            return result;
        }
        finally
        {
            world.Dispose();
            if (target) { target.Release(); Object.DestroyImmediate(target); }
            if (surface) Object.DestroyImmediate(surface);
            EditorSceneManager.ClosePreviewScene(scene);
        }
    }
}
