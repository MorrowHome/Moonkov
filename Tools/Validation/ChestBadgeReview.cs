using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Unity.MP_FPS;
using Unity.MP_FPS.Client;
using Unity.MP_FPS.Inventory;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UIElements;
using Object=UnityEngine.Object;

// Pipeline run_script. Isolated preview scenes; never loads a player's profile.
public static class ChestBadgeReview
{
    [Serializable] private sealed class BadgeString {public string key="",value="";public long id;}
    [Serializable] private sealed class BadgeStrings {public BadgeString[] entries;}
    private const BindingFlags Private=BindingFlags.Instance|BindingFlags.NonPublic;
    private static readonly string[] Codes={"scout_rig","rig","field_rig"};
    private static int checks;
    private static readonly HashSet<UnityEngine.SceneManagement.Scene> Scenes=new();
    private static void Require(bool value,string message)
    {checks++;if(!value)throw new InvalidOperationException("Chest badge: "+message);}
    private static T Field<T>(object owner,string name)
    {
        if(owner is MenuCharacterView view && view.DisplayScene.IsValid())Scenes.Add(view.DisplayScene);
        return (T)owner.GetType().GetField(name,Private).GetValue(owner);
    }
    private static void Simulate(EnergyShieldPresentation protection)=>typeof(EnergyShieldPresentation).GetMethod("SimulatePresentation",Private).Invoke(protection,null);
    private static InventoryItem Item(InventoryGraph graph,string code,string parent,string region)
    {
        var item=new InventoryItem{Id=Guid.NewGuid().ToString("D"),Code=code,Parent=parent,Region=region};
        graph.Items.Add(item);return item;
    }

    public static string Run(bool allowRuntime=false)
    {
        if(!allowRuntime && EditorApplication.isPlayingOrWillChangePlaymode || BuildPipeline.isBuildingPlayer)throw new InvalidOperationException("Review outside Play mode and builds.");
        checks=0;
        int scenes=EditorSceneManager.previewSceneCount;
        var graph=InventoryGraph.Create(true,false);
        var badge=Item(graph,"rig",InventoryCatalog.Equipment,"ChestRig");
        var cell=Item(graph,"cells",badge.Id,"center");
        var state=new PredictedPlayerGhost{CurrentHealth=100};
        foreach(string code in Codes)
        {
            badge.Code=code;
            string before=JsonUtility.ToJson(graph);
            EnergyShieldEquipment.Synchronize(ref state,graph);
            Require(state.SubspaceBadge==EnergyShieldEquipment.BadgeStyle(code),"server lost equipped variant "+code);
            Require(graph.CellEnergy(accessibleOnly:true)>0,"badge no longer provides usable cells "+code);
            Require(JsonUtility.ToJson(graph)==before,"appearance changed the stored item tree");
            var appearance=Unity.MP_FPS.Online.LobbyAppearanceInventory.FromInventory(graph,2,-1);
            Require(appearance.ChestBadge==code,"squad projection lost badge variant");
            Require(Unity.MP_FPS.Online.LobbyAppearanceInventory.PreviewInventory(appearance).Equipped("ChestRig")?.Code==code,"squad portrait lost badge slot");
        }
        var move=new InventoryCommand{Operation=InventoryOperation.Move,ItemId=badge.Id,Parent=InventoryCatalog.Stash,Region="main",ExpectedVersion=graph.Version};
        Require(graph.TryApply(move)==InventoryError.None,"could not unequip badge with its contents");
        EnergyShieldEquipment.Synchronize(ref state,graph);
        Require(state.SubspaceBadge==0,"stored badge remains worn");
        Require(graph.Find(cell.Id)?.Parent==badge.Id,"unequip lost badge contents");
        Require(graph.CellEnergy(accessibleOnly:true)==0,"stored badge supplies remained accessible");
        move.Parent=InventoryCatalog.Equipment;move.Region="ChestRig";move.ExpectedVersion=graph.Version;
        Require(graph.TryApply(move)==InventoryError.None,"could not re-equip old ChestRig slot");
        EnergyShieldEquipment.Synchronize(ref state,graph);
        Require(state.SubspaceBadge==3,"re-equip lost appearance");

        foreach(int character in new[]{GameSettings.DollSingerCharacterIndex,GameSettings.HatsuneMikuCharacterIndex,GameSettings.ShianCharacterIndex})
        {
            using var view=new MenuCharacterView(idleOnly:true,characterIndex:character);
            var inventory=InventoryGraph.Create(false,false);
            var worn=Item(inventory,"rig",InventoryCatalog.Equipment,"ChestRig");
            view.Advance(0);
            var model=Field<GameObject>(view,"m_Model");
            foreach(string code in Codes)
            {
                worn.Code=code;string before=JsonUtility.ToJson(inventory);
                view.SetEquipmentInventory(inventory);view.Advance(0);
                var protection=model.GetComponent<EnergyShieldPresentation>();
                var root=Field<Transform>(protection,"m_Badge");
                Require(root && root.gameObject.activeInHierarchy,"badge-only loadout missing on character "+character);
                var renderers=root.GetComponentsInChildren<MeshRenderer>();
                Require(renderers.Length==1,"expected one worn mesh "+code);
                var renderer=renderers.Single();
                var prefab=Resources.Load<GameObject>("Moonkov/Items/Prefabs/"+code);
                Require(renderer.GetComponent<MeshFilter>().sharedMesh==prefab.GetComponentInChildren<MeshFilter>().sharedMesh,"wearable differs from inventory/loot model");
                Require(renderer.sharedMaterials.All(m=>m && m.name.StartsWith("Badge_")),"missing badge materials");
                var size=Vector3.Scale(renderer.GetComponent<MeshFilter>().sharedMesh.bounds.size,renderer.transform.lossyScale);
                Require(size.magnitude>.07f && size.magnitude<.15f,"wrong worn metre scale "+character+" "+code+" "+size);
                Require(root.GetComponentsInChildren<Collider>(true).Length==0,"cosmetic badge added a hitbox");
                view.SetEquipmentInventory(inventory);view.Advance(0);
                Require(root.GetComponentInChildren<MeshRenderer>()==renderer,"unchanged badge recreated its mesh");
                var anchor=Field<Transform>(protection,"m_BadgeAnchor");
                Vector3 local=anchor.InverseTransformPoint(root.position);
                Quaternion localFrame=Quaternion.Inverse(anchor.rotation)*root.rotation;
                Quaternion rotation=anchor.localRotation;
                anchor.localRotation*=Quaternion.Euler(12,18,-8);Simulate(protection);
                Require(Vector3.Distance(anchor.InverseTransformPoint(root.position),local)<.0001f,"badge detached during torso motion");
                Require(Quaternion.Angle(Quaternion.Inverse(anchor.rotation)*root.rotation,localFrame)<.05f,"badge orientation detached from chest");
                anchor.localRotation=rotation;view.Advance(0);
                Require(JsonUtility.ToJson(inventory)==before,"preview changed inventory");
            }
            var presentation=model.GetComponent<EnergyShieldPresentation>();
            var badgeRoot=Field<Transform>(presentation,"m_Badge");
            using(var postured=new MenuCharacterView(idleOnly:true,characterIndex:character))
            {
                postured.Advance(2.25f);
                var otherInventory=InventoryGraph.Create(false,false);Item(otherInventory,"field_rig",InventoryCatalog.Equipment,"ChestRig");
                postured.SetEquipmentInventory(otherInventory);postured.Advance(0);
                var other=Field<GameObject>(postured,"m_Model").GetComponent<EnergyShieldPresentation>();
                Require(Vector3.Distance(Field<Vector3>(presentation,"m_BadgeOffset"),Field<Vector3>(other,"m_BadgeOffset"))<.001f,"first animation pose changed badge fit");
                Require(Quaternion.Angle(Field<Quaternion>(presentation,"m_BadgeFrame"),Field<Quaternion>(other,"m_BadgeFrame"))<.05f,"first animation pose changed badge orientation");
            }
            var snapshot=new PredictedPlayerGhost{CurrentHealth=100,SubspaceBadge=1};
            view.SetEquipmentSnapshot(snapshot);view.Advance(0);
            Require(badgeRoot.GetComponentInChildren<MeshFilter>().sharedMesh==Resources.Load<GameObject>("Moonkov/Items/Prefabs/scout_rig").GetComponentInChildren<MeshFilter>().sharedMesh,"live snapshot did not replace badge");
            var camera=view.DisplayScene.GetRootGameObjects().SelectMany(o=>o.GetComponentsInChildren<Camera>()).First();
            presentation.Present(snapshot,true,true,camera);Simulate(presentation);
            typeof(EnergyShieldPresentation).GetMethod("BeginCamera",Private).Invoke(presentation,new object[]{camera});
            Require(badgeRoot.GetComponentsInChildren<Renderer>().All(r=>r.forceRenderingOff),"owner camera shows chest hardware");
            typeof(EnergyShieldPresentation).GetMethod("EndCamera",Private).Invoke(presentation,new object[]{camera});
            Require(badgeRoot.GetComponentsInChildren<Renderer>().All(r=>!r.forceRenderingOff),"owner camera hid other views");
            presentation.ResetPresentation();
            Require(!badgeRoot.gameObject.activeInHierarchy,"reset retained badge");
            presentation.Present(snapshot,false,false,null);Simulate(presentation);
            Require(badgeRoot.gameObject.activeInHierarchy,"pooled presentation failed to restore badge");
            snapshot.SubspaceBadge=0;view.SetEquipmentSnapshot(snapshot);view.Advance(0);
            Require(!badgeRoot.gameObject.activeInHierarchy,"empty snapshot retained worn badge");
            view.SetEquipmentInventory(InventoryGraph.Create(false,false));view.Advance(0);
            Require(!badgeRoot.gameObject.activeInHierarchy,"empty inventory retained worn badge");
            using(var snapshotOnly=new MenuCharacterView(idleOnly:true,characterIndex:character))
            {
                snapshot.SubspaceBadge=2;snapshotOnly.SetEquipmentSnapshot(snapshot);snapshotOnly.Advance(0);
                var live=Field<GameObject>(snapshotOnly,"m_Model").GetComponent<EnergyShieldPresentation>();
                Require(live && Field<Transform>(live,"m_Badge").gameObject.activeInHierarchy,"badge-only initial snapshot was ignored");
            }
            CheckCorpse(character);
        }
        Require(EditorSceneManager.previewSceneCount==scenes,"preview scene leaked");
        string result="Chest badges: PASS ("+checks+" assertions, three characters and three tiers; equip/unequip, contents, snapshots, animation attachment, cameras and cleanup).";
        Directory.CreateDirectory("LocalData/ChestBadgeReview");File.WriteAllText("LocalData/ChestBadgeReview/checks.txt",result);
        return result;
    }

    private static void CheckCorpse(int character)
    {
        var fallen=InventoryGraph.Create(false,false);
        var badge=Item(fallen,"field_rig",InventoryCatalog.Equipment,"ChestRig");
        Item(fallen,"cells",badge.Id,"center");
        var slots=fallen.Children(InventoryCatalog.Equipment).ToDictionary(i=>i.Region,i=>i.Id);
        var cache=LootInventoryExchange.DropOnDeath(fallen);
        var graph=LootInventoryExchange.Snapshot(InventoryGraph.Create(false,false),cache);
        string before=JsonUtility.ToJson(graph);
        using var inventory=new ContainerInventoryView(new VisualElement(),false,_=>{});
        inventory.Present(graph,lootId:RaidLootContainers.FirstDeathBagId,corpseCharacterIndex:character,corpseEquipmentSlots:slots);
        inventory.Show();
        var corpse=Field<MenuCharacterView>(inventory,"m_CorpseCharacter");corpse.Advance(0);
        var protection=Field<GameObject>(corpse,"m_Model").GetComponent<EnergyShieldPresentation>();
        Require(protection && Field<Transform>(protection,"m_Badge").gameObject.activeInHierarchy,"corpse lost its badge");
        Require(JsonUtility.ToJson(graph)==before,"corpse portrait changed loot contents");
        var next=graph.Clone();next.Items.RemoveAll(i=>i.Id==badge.Id || i.Parent==badge.Id);next.Version++;
        inventory.Present(next,lootId:RaidLootContainers.FirstDeathBagId,corpseCharacterIndex:character,corpseEquipmentSlots:slots);
        corpse=Field<MenuCharacterView>(inventory,"m_CorpseCharacter");corpse.Advance(0);
        protection=Field<GameObject>(corpse,"m_Model").GetComponent<EnergyShieldPresentation>();
        Require(!protection || !Field<Transform>(protection,"m_Badge") || !Field<Transform>(protection,"m_Badge").gameObject.activeInHierarchy,"looted badge stayed on corpse");
    }

    public static object Review()
    {
        string rules=Run();
        var assets=VerifyAssets();
        string captures=Capture();
        return new{rules,assets,captures};
    }

    public static object TryReview()
    {
        if(EditorApplication.isPlayingOrWillChangePlaymode || BuildPipeline.isBuildingPlayer || EditorApplication.isCompiling)
            return new{status="editor_busy"};
        return Review();
    }

    public static async Task<object> ReviewCurrentEditor(bool capture=true)
    {
        if(BuildPipeline.isBuildingPlayer || EditorApplication.isCompiling)throw new InvalidOperationException("Editor is compiling or building.");
        Scenes.Clear();
        string rules=Run(true);
        var assets=VerifyAssets();
        string captures=capture?Capture(true):"Kept existing validated character captures.";
        double deadline=EditorApplication.timeSinceStartup+12;
        while(Scenes.Any(s=>s.IsValid() && s.isLoaded) && EditorApplication.timeSinceStartup<deadline)
        {
            var update=new TaskCompletionSource<bool>();
            EditorApplication.CallbackFunction callback=null;
            callback=()=>{EditorApplication.update-=callback;update.TrySetResult(true);};
            EditorApplication.update+=callback;
            await update.Task;
        }
        Require(!Scenes.Any(s=>s.IsValid() && s.isLoaded),"runtime portrait scenes leaked");
        string result="Chest badge runtime cleanup: PASS; "+Scenes.Count+" owned portrait scenes unloaded.";
        File.WriteAllText("LocalData/ChestBadgeReview/runtime-cleanup.txt",result);
        return new{rules,assets,captures,cleanup=result};
    }

    public static string Capture(bool allowRuntime=false)
    {
        if(!allowRuntime && EditorApplication.isPlayingOrWillChangePlaymode || BuildPipeline.isBuildingPlayer)throw new InvalidOperationException("Capture outside Play mode and builds.");
        const string directory="LocalData/ChestBadgeReview/Characters";
        Directory.CreateDirectory(directory);
        foreach(int character in new[]{GameSettings.DollSingerCharacterIndex,GameSettings.HatsuneMikuCharacterIndex,GameSettings.ShianCharacterIndex})
        {
            using var view=new MenuCharacterView(idleOnly:true,characterIndex:character);
            var inventory=InventoryGraph.Create(false,false);
            var badge=Item(inventory,"rig",InventoryCatalog.Equipment,"ChestRig");
            Item(inventory,"backpack",InventoryCatalog.Equipment,"Backpack");
            foreach(string code in Codes)
            {
                badge.Code=code;view.SetEquipmentInventory(inventory);view.Advance(.1f);
                view.Render(new Vector2(640,960));
                Save(view.Target,directory+"/character-"+character+"-"+code+".png");
                var model=Field<GameObject>(view,"m_Model");
                foreach(var skin in model.GetComponentsInChildren<SkinnedMeshRenderer>())skin.forceMatrixRecalculationPerRender=true;
                var camera=Field<Camera>(view,"m_Camera");
                var worn=Field<Transform>(model.GetComponent<EnergyShieldPresentation>(),"m_Badge");
                var originalPosition=camera.transform.position;var originalRotation=camera.transform.rotation;
                var originalTarget=camera.targetTexture;
                var close=new RenderTexture(640,640,24);
                try
                {
                    close.Create();camera.targetTexture=close;
                    var center=worn.position-model.transform.right*.01f+model.transform.up*.02f;
                    camera.transform.position=center+model.transform.forward*.75f;
                    camera.transform.rotation=Quaternion.LookRotation(center-camera.transform.position,model.transform.up);
                    camera.Render();Save(close,directory+"/detail-"+character+"-"+code+".png");
                }
                finally
                {
                    camera.targetTexture=originalTarget;camera.transform.SetPositionAndRotation(originalPosition,originalRotation);
                    close.Release();Object.DestroyImmediate(close);
                }
                // A bind-pose comparison keeps the arms clear for fit inspection.
                // The real menu gesture captures above stay intact.
                var standing=new RenderTexture(640,640,24);
                try
                {
                    SampleBindPose(model);Simulate(model.GetComponent<EnergyShieldPresentation>());
                    standing.Create();camera.targetTexture=standing;
                    var center=worn.position-model.transform.right*.01f+model.transform.up*.035f;
                    camera.transform.position=center+model.transform.forward*.85f;
                    camera.transform.rotation=Quaternion.LookRotation(center-camera.transform.position,model.transform.up);
                    camera.Render();Save(standing,directory+"/bind-"+character+"-"+code+".png");
                }
                finally
                {
                    camera.targetTexture=originalTarget;camera.transform.SetPositionAndRotation(originalPosition,originalRotation);
                    standing.Release();Object.DestroyImmediate(standing);
                }
            }
        }
        return "Saved nine full character views, nine gesture details and nine bind-pose details to "+directory;
    }

    private static void SampleBindPose(GameObject model)
    {
        var poses=new Dictionary<Transform,Matrix4x4>();
        foreach(var renderer in model.GetComponentsInChildren<SkinnedMeshRenderer>())
        {
            renderer.forceMatrixRecalculationPerRender=true;
            var bones=renderer.bones;var bind=renderer.sharedMesh.bindposes;
            for(int i=0;i<Math.Min(bones.Length,bind.Length);i++)
                if(bones[i])poses.TryAdd(bones[i],renderer.localToWorldMatrix*bind[i].inverse);
        }
        foreach(var entry in poses.OrderBy(p=>Depth(p.Key)))
            entry.Key.SetPositionAndRotation(entry.Value.GetColumn(3),entry.Value.rotation);
    }

    private static int Depth(Transform node)
    {int depth=0;while(node.parent){depth++;node=node.parent;}return depth;}

    public static object InstallAndValidate()
    {
        if(EditorApplication.isPlayingOrWillChangePlaymode || BuildPipeline.isBuildingPlayer)throw new InvalidOperationException("Install outside Play mode and builds.");
        foreach(string file in new[]{"MoonkovUI Shared Data.asset","MoonkovUI_en.asset","MoonkovUI_zh-Hans.asset"})
        {
            string before="LocalData/ChestBadgeReview/CommitBaseline/Assets/Localization/Tables/"+file;
            Directory.CreateDirectory(Path.GetDirectoryName(before));File.Copy("Assets/Localization/Tables/"+file,before,true);
        }
        string localization=ChestBadgeAssetSetup.InstallLocalization();
        foreach(string file in new[]{"MoonkovUI Shared Data.asset","MoonkovUI_en.asset","MoonkovUI_zh-Hans.asset"})
        {
            string after="LocalData/ChestBadgeReview/CommitAfter/Assets/Localization/Tables/"+file;
            Directory.CreateDirectory(Path.GetDirectoryName(after));File.Copy("Assets/Localization/Tables/"+file,after,true);
        }
        string physical=PhysicalItemAssetSetup.Validate();
        return new{localization,physical,failed=EditorUtility.scriptCompilationFailed};
    }

    public static object VerifyAssets()
    {
        var shared=AssetDatabase.LoadAssetAtPath<UnityEngine.Localization.Tables.SharedTableData>("Assets/Localization/Tables/MoonkovUI Shared Data.asset");
        if(!shared)throw new InvalidOperationException("Missing MoonkovUI shared entries.");
        // Keep run_script independent of the duplicate Newtonsoft editor
        // assemblies. Decode each ordinary JSON property through JsonUtility.
        string source=File.ReadAllText("Assets/Localization/Translations.json");
        const string pattern=@"^\s*(""(?:\\.|[^""\\])*"")\s*:\s*(""(?:\\.|[^""\\])*"")\s*,?\s*$";
        var strings=new BadgeStrings{entries=System.Text.RegularExpressions.Regex.Matches(source,pattern,System.Text.RegularExpressions.RegexOptions.Multiline)
            .Cast<System.Text.RegularExpressions.Match>().Select(m=>JsonUtility.FromJson<BadgeString>("{\"key\":"+m.Groups[1].Value+",\"value\":"+m.Groups[2].Value+"}"))
            .Where(p=>p.value.Contains("胸章") || p.key.Contains("badge")).ToArray()};
        var keys=strings.entries;
        foreach(string locale in new[]{"en","zh-Hans"})
        {
            var table=AssetDatabase.LoadAssetAtPath<UnityEngine.Localization.Tables.StringTable>("Assets/Localization/Tables/MoonkovUI_"+locale+".asset");
            if(!table)throw new InvalidOperationException("Missing badge locale table "+locale);
            foreach(var pair in keys)
            {
                string expected=table.LocaleIdentifier.Code=="zh-Hans"?pair.value:pair.key;
                if(table.GetEntry(pair.key)?.LocalizedValue!=expected)throw new InvalidOperationException("Badge translation missing: "+pair.key);
            }
        }
        // The existing UI font uses a dynamic atlas, so inspect its source face.
        var font=AssetDatabase.LoadAssetAtPath<Font>("Assets/Localization/Fonts/NotoSansCJKsc-Regular.otf");
        foreach(char glyph in "胸章")
            if(!font || !font.HasCharacter(glyph))throw new InvalidOperationException("Badge glyph missing: "+glyph);
        foreach(var pair in keys)pair.id=shared.GetEntry(pair.key).Id;
        File.WriteAllLines("LocalData/ChestBadgeReview/feature-ids.txt",keys.Select(p=>p.id.ToString()));
        var result=new{localization=keys.Length+" entries in both locales; chest/badge glyphs present",physical=PhysicalItemAssetSetup.Validate(),failed=EditorUtility.scriptCompilationFailed};
        File.WriteAllText("LocalData/ChestBadgeReview/asset-checks.txt",result.ToString());
        return result;
    }

    private static void Save(RenderTexture target,string path)
    {
        var previous=RenderTexture.active;
        var image=new Texture2D(target.width,target.height,TextureFormat.RGBA32,false);
        try{RenderTexture.active=target;image.ReadPixels(new Rect(0,0,image.width,image.height),0,0);image.Apply();File.WriteAllBytes(path,image.EncodeToPNG());}
        finally{RenderTexture.active=previous;Object.DestroyImmediate(image);}
    }

}
