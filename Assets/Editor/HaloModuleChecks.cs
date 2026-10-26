using System;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEditor.Localization;
using UnityEditor.SceneManagement;
using UnityEngine;
using Unity.MP_FPS;
using Unity.MP_FPS.DollSinger;
using Unity.MP_FPS.Inventory;

public static class HaloModuleChecks
{
    private static void Check(bool condition, string message)
    { if(!condition) throw new InvalidOperationException("Halo module check: "+message); }
    [MenuItem("Tools/Doll Singer/Check Halo Modules")]
    public static void Run()
    {
        var registry=AssetDatabase.LoadAssetAtPath<WeaponRegistry>("Assets/Data/Weapons/WeaponRegistry.asset");
        var baseShotgun=registry.GetWeaponData(DollSingerWeapons.Shotgun);
        var capacitor=registry.GetWeaponData(DollSingerWeapons.Shotgun,(byte)HaloModule.Capacitor);
        var bridge=registry.GetWeaponData(DollSingerWeapons.Shotgun,(byte)HaloModule.ChargeBridge);
        var focus=registry.GetWeaponData(DollSingerWeapons.Shotgun,(byte)HaloModule.FocusLens);
        var fan=registry.GetWeaponData(DollSingerWeapons.Shotgun,(byte)HaloModule.FanArray);
        var marked=registry.GetWeaponData(DollSingerWeapons.Shotgun,(byte)(HaloModule.ChargeBridge|HaloModule.HitMarker));
        Check(capacitor.MagazineSize==6 && Mathf.Approximately(capacitor.ReloadTime,2.7f),"capacitor tradeoff");
        Check(bridge.EnergyPerRound==18 && Mathf.Approximately(bridge.ReloadTime,1.2f),"bridge tradeoff");
        Check(focus.MagazineSize==3 && Mathf.Approximately(focus.SpreadDegrees,2.25f),"focus tradeoff");
        Check(fan.PelletCount==12 && Mathf.Approximately(fan.Damage*fan.PelletCount,baseShotgun.Damage*baseShotgun.PelletCount),"fan total damage");
        Check(marked.EnergyPerRound==23 && baseShotgun.MagazineSize==4 && baseShotgun.PelletCount==8,"combined energy rounds up and asset unchanged");
        Vector3 first=WeaponSpread.Direction(Vector3.forward,fan,3,120);
        Check(first==WeaponSpread.Direction(Vector3.forward,fan,3,120) && Vector3.Angle(Vector3.forward,first)<=fan.SpreadDegrees,"shared deterministic cone");
        var rifle=registry.GetWeaponData(DollSingerWeapons.Halo);
        var state=new PredictedPlayerGhost {CurrentHealth=100,EquippedWeaponID=DollSingerWeapons.Halo,EquippedModules=(byte)HaloModule.PulseController,CurrentAmmo=10,WeaponCooldown=1};
        Check(HaloModuleFire.TryFire(ref state,rifle,true,true,false) && state.BurstRemaining==2 && state.BurstSequence==0,"burst starts");
        state.WeaponCooldown=rifle.CooldownInMs;
        Check(HaloModuleFire.TryFire(ref state,rifle,false,true,false) && state.BurstSequence==1,"burst continues after release");
        Check(HaloModuleFire.TryFire(ref state,rifle,false,true,false) && state.BurstRemaining==0 && state.BurstSequence==2,"three releases");
        state.WeaponCooldown=.2f;
        Check(!HaloModuleFire.TryFire(ref state,rifle,true,true,false),"inter-burst pause");
        state.BurstRemaining=2;
        Check(!HaloModuleFire.TryFire(ref state,rifle,false,false,false) && state.BurstRemaining==0,"aim loss cancels");
        state.BurstRemaining=2;state.ControllerState.IsReloadingState=true;
        Check(!HaloModuleFire.TryFire(ref state,rifle,false,true,false) && state.BurstRemaining==0,"charge cancels");
        CheckPrepaidChargeSurvivesTemporaryEffects(registry);
        CheckReloadQueuedDuringDraw(registry);
        var root=new GameObject("Module geometry check");
        try
        {
            var visual=root.AddComponent<HaloModuleVisual>();visual.Configure(false);visual.SetAimBlend(1);
            visual.SetState((byte)(HaloModule.Capacitor|HaloModule.FocusLens|HaloModule.HitMarker),4,5,false,0,5,1);
            var sprites=root.GetComponentsInChildren<SpriteRenderer>(true).Where(renderer=>renderer.gameObject.activeSelf).ToArray();
            Check(sprites.Length==6 && sprites.GroupBy(renderer=>renderer.sprite).All(pair=>pair.Count()==2),"three module slots produce three complete Sprite pairs");
            foreach(var pair in sprites.GroupBy(renderer=>renderer.sprite))
            {
                var halves=pair.ToArray();
                Check(pair.Key && halves[0].sharedMaterial && halves[0].sharedMaterial==halves[1].sharedMaterial &&
                    halves[0].sharedMaterial.shader.name=="DollSinger/RifleHaloInk","shared flat ink Sprite artwork");
                var expected=HaloModuleVisual.ArtworkColor(HaloModules.FromCode(pair.Key.name));var tint=halves[0].color;
                float brightness=Mathf.Max(tint.r,tint.g,tint.b)/Mathf.Max(expected.r,expected.g,expected.b);
                Check(tint==halves[1].color && Mathf.Approximately(tint.a,1f) && brightness>0 &&
                    Mathf.Abs(tint.r-expected.r*brightness)<.002f && Mathf.Abs(tint.g-expected.g*brightness)<.002f &&
                    Mathf.Abs(tint.b-expected.b*brightness)<.002f,"paired artwork uses its slot color and preserves parent alpha");
                foreach(var vertex in pair.Key.vertices)
                {
                    Vector3 Point(SpriteRenderer renderer)=>root.transform.InverseTransformPoint(renderer.transform.TransformPoint(
                        new Vector3(renderer.flipX?-vertex.x:vertex.x,renderer.flipY?-vertex.y:vertex.y,0)));
                    var a=Point(halves[0]);var b=Point(halves[1]);
                    Check(Vector3.Distance(new Vector3(-a.x,a.y,a.z),b)<.0001f,"complete module drawings are mirrored");
                    Check(a.x*a.x+a.y*a.y>.01f,"module leaves the central sight clear");
                }
            }
            var mesh=root.GetComponent<MeshFilter>().sharedMesh;
            Check(mesh.vertexCount==0 && !root.GetComponent<MeshRenderer>().enabled,"entity modules do not create an extra ring or procedural outline");
            visual.SetState((ushort)HaloModule.BiologicalCapacity,4,5,false,0,5,1);visual.Step(0);
            Check(mesh.vertexCount>0 && root.GetComponent<MeshRenderer>().enabled,"biology still has its separate geometry");
            foreach(var vertex in mesh.vertices) Check(vertex.sqrMagnitude>.01f && Mathf.Abs(vertex.z+.003f)<.00001f,"flat biology geometry leaves central sight clear");
            Check(root.GetComponentsInChildren<SpriteRenderer>(true).All(renderer=>!renderer.gameObject.activeSelf),"biology does not leave entity modules visible");
        }
        finally {UnityEngine.Object.DestroyImmediate(root);}
        Debug.Log("Halo module checks passed: stats, energy composition, damage budget, deterministic spread, burst lifecycle, paired flat artwork, biology geometry and sight clearance.");
    }
    private static void CheckPrepaidChargeSurvivesTemporaryEffects(WeaponRegistry registry)
    {
        var graph=InventoryGraph.Create(false);
        Check(graph.AddSupply("cells",1,InventoryCatalog.Pockets,cellCharge:100)==InventoryError.None,"charge fixture battery");
        var state=new PredictedPlayerGhost {CurrentHealth=100,EquippedWeaponSlot=1,EquippedWeaponID=DollSingerWeapons.None,AlienSurgeSeconds=5};
        DollSingerWeapons.SyncEquipment(ref state,graph,registry);
        state.CurrentAmmo=5;DollSingerWeapons.SaveActiveAmmo(ref state,graph);
        var weapon=registry.GetWeaponData(state.EquippedWeaponID,state.EquippedModules);
        int before=graph.CellEnergy(accessibleOnly:true);
        int target=0;
        Check(weapon!=null && graph.TryRecharge(state.CurrentAmmo,weapon.MagazineSize,weapon.EnergyPerRound,out target),"reserve real weapon charge");
        // The reservation already spent energy. Synchronizing transient effects
        // must keep its target until the existing charge finishes.
        int paid=graph.CellEnergy(accessibleOnly:true);
        Check(paid<before,"energy was reserved");
        state.ControllerState.IsReloadingState=true;state.ReloadTimer=1.5f;
        state.ReloadTargetAmmo=target;
        int expected=state.ReloadTargetAmmo;
        state.AlienSurgeSeconds=0;state.AlienFoodEffect=AlienFoodEffect.NeuralOverdrive;state.AlienFoodEffectSeconds=5;
        DollSingerWeapons.SyncEquipment(ref state,graph,registry);
        Check(state.ControllerState.IsReloadingState && state.ReloadTimer==1.5f && state.ReloadTargetAmmo==expected,"surge expiry and food activation keep the charge");
        state.AlienFoodEffectSeconds=0;
        DollSingerWeapons.SyncEquipment(ref state,graph,registry);
        Check(state.ControllerState.IsReloadingState && state.ReloadTargetAmmo==expected,"food expiry keeps the charge");
        DollSingerWeapons.CompleteReload(ref state,registry.GetWeaponData(state.EquippedWeaponID,state.EquippedModules));
        Check(state.CurrentAmmo==expected && graph.CellEnergy(accessibleOnly:true)==paid,"charge completes once without another energy deduction");
        state.CurrentAmmo=4;DollSingerWeapons.SaveActiveAmmo(ref state,graph);
        weapon=registry.GetWeaponData(state.EquippedWeaponID,state.EquippedModules);
        Check(graph.TryRecharge(state.CurrentAmmo,weapon.MagazineSize,weapon.EnergyPerRound,out target),"reserve extraction charge");
        paid=graph.CellEnergy(accessibleOnly:true);
        state.ControllerState.IsReloadingState=true;state.ReloadTimer=1;state.ReloadTargetAmmo=target;
        Check(DollSingerWeapons.CompleteExtractedCharge(ref state,graph,weapon.MagazineSize) && state.CurrentAmmo==target &&
            graph.Find(state.EquippedWeaponItem.ToString()).LoadedAmmo==target && graph.CellEnergy(accessibleOnly:true)==paid,
            "extraction settles real weapon charge into the carried item");
    }
    private static void CheckReloadQueuedDuringDraw(WeaponRegistry registry)
    {
        var graph=InventoryGraph.Create(false);
        Check(graph.AddSupply("cells",1,InventoryCatalog.Pockets)==InventoryError.None,"draw queue battery fixture");
        var state=new PredictedPlayerGhost {CurrentHealth=100,EquippedWeaponSlot=1,EquippedWeaponID=DollSingerWeapons.None};
        DollSingerWeapons.SyncEquipment(ref state,graph,registry);state.CurrentAmmo=5;
        var weapon=registry.GetWeaponData(state.EquippedWeaponID,state.EquippedModules);
        int before=graph.CellEnergy(accessibleOnly:true);
        Check(weapon!=null && state.WeaponEquipRemaining>0 && !HaloModuleFire.ReadyToReload(ref state,true,weapon.MagazineSize) &&
            graph.CellEnergy(accessibleOnly:true)==before,"real draw queues charge without energy debit");
        HaloModuleFire.AdvanceTimers(ref state,state.WeaponEquipRemaining);
        Check(HaloModuleFire.ReadyToReload(ref state,false,weapon.MagazineSize) &&
            graph.TryRecharge(state.CurrentAmmo,weapon.MagazineSize,weapon.EnergyPerRound,out int target) && target>state.CurrentAmmo,
            "real charge can reserve energy after draw");
    }
    [MenuItem("Tools/Doll Singer/Update Module Translations")]
    public static void UpdateTranslations()
    {
        var translations=JObject.Parse(File.ReadAllText("Assets/Localization/Translations.json"));
        var collection=LocalizationEditorSettings.GetStringTableCollection("MoonkovUI");
        foreach(var table in collection.StringTables)
        {
            foreach(var pair in translations.Properties())
            {
                string value=table.LocaleIdentifier.Code=="zh-Hans" ? pair.Value.Value<string>() : pair.Name;
                if(table.GetEntry(pair.Name)?.Value==value) continue;
                table.AddEntry(pair.Name,value);EditorUtility.SetDirty(table);
            }
            AssetDatabase.SaveAssetIfDirty(table);
        }
        EditorUtility.SetDirty(collection.SharedData);AssetDatabase.SaveAssetIfDirty(collection.SharedData);
    }
    [MenuItem("Tools/Doll Singer/Preview Halo Modules")]
    public static void Preview()
    {
        var scene=EditorSceneManager.NewPreviewScene();
        var registry=AssetDatabase.LoadAssetAtPath<WeaponRegistry>("Assets/Data/Weapons/WeaponRegistry.asset");
        string output=Path.GetFullPath("Build/HaloModules");Directory.CreateDirectory(output);
        const int size=384;
        var atlas=new Texture2D(size*3,size*3,TextureFormat.RGB24,false);
        var target=new RenderTexture(size,size,24);
        var cameraRoot=new GameObject("Module preview camera");UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(cameraRoot,scene);
        var camera=cameraRoot.AddComponent<Camera>();camera.enabled=false;camera.orthographic=true;camera.orthographicSize=.61f;
        camera.transform.position=new Vector3(0,0,-3);camera.clearFlags=CameraClearFlags.SolidColor;camera.backgroundColor=new Color(.018f,.023f,.03f);
        camera.cullingMask=1<<5;camera.targetTexture=target;
        camera.scene=scene;camera.overrideSceneCullingMask=EditorSceneManager.GetSceneCullingMask(scene);
        HaloModule[] masks={HaloModule.None,HaloModule.Capacitor,HaloModule.ChargeBridge,HaloModule.FocusLens,HaloModule.FanArray,
            HaloModule.Capacitor|HaloModule.FocusLens,HaloModule.PulseController,HaloModule.HitMarker,HaloModule.ChargeBridge|HaloModule.FanArray|HaloModule.HitMarker};
        string[] names={"00 Standard","01 Capacitor","02 Charge bridge","03 Focus lens","04 Fan array","05 Capacitor + focus","06 Rifle pulse","07 Hit marker","08 Bridge + fan + marker"};
        try
        {
            for(int index=0;index<masks.Length;index++)
            {
                bool rifle=masks[index]==HaloModule.PulseController;
                var prefab=AssetDatabase.LoadAssetAtPath<GameObject>(rifle ? "Assets/DollSinger/Prefabs/RifleHalo.prefab" : "Assets/DollSinger/Prefabs/ShotgunHalo.prefab");
                var root=UnityEngine.Object.Instantiate(prefab);UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(root,scene);
                root.transform.SetPositionAndRotation(Vector3.zero,Quaternion.identity);root.transform.localScale=Vector3.one;
                foreach(var child in root.GetComponentsInChildren<Transform>(true)) child.gameObject.layer=5;
                var weapon=registry.GetWeaponData(rifle?DollSingerWeapons.Halo:DollSingerWeapons.Shotgun,(byte)masks[index]);
                bool charge=(masks[index]&HaloModule.ChargeBridge)!=0;
                var shotgun=root.GetComponent<ShotgunHaloVisual>();
                if(shotgun) {shotgun.SetModuleProfile((byte)masks[index],weapon.MagazineSize);shotgun.SetAimBlend(1);shotgun.SetLocalState(weapon.MagazineSize,charge,.5f);shotgun.Step(.5f);}
                var rifleVisual=root.GetComponent<RifleHaloVisual>();
                if(rifleVisual) {rifleVisual.SetAimBlend(1);rifleVisual.SetLocalState(30,false,0);rifleVisual.Step(0);}
                var moduleRoot=new GameObject("Modification layer");moduleRoot.layer=5;moduleRoot.transform.SetParent(root.transform,false);
                var visual=moduleRoot.AddComponent<HaloModuleVisual>();visual.Configure(rifle);visual.SetAimBlend(1);
                visual.SetState((byte)masks[index],weapon.MagazineSize,weapon.MagazineSize,charge,.5f,weapon.MagazineSize,1,1);
                if(rifle)visual.SetState((byte)masks[index],weapon.MagazineSize,weapon.MagazineSize,false,0,weapon.MagazineSize,2,1);
                visual.Step(.03f);
                var labelRoot=new GameObject("Preview label");UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(labelRoot,scene);labelRoot.layer=5;
                labelRoot.transform.position=new Vector3(-.55f,-.52f,-.1f);
                var label=labelRoot.AddComponent<TextMesh>();label.text=names[index];label.fontSize=48;label.characterSize=.013f;label.color=new Color(.6f,.67f,.73f);
                camera.Render();var previous=RenderTexture.active;RenderTexture.active=target;
                var image=new Texture2D(size,size,TextureFormat.RGB24,false);image.ReadPixels(new Rect(0,0,size,size),0,0);image.Apply();RenderTexture.active=previous;
                File.WriteAllBytes(Path.Combine(output,names[index]+".png"),image.EncodeToPNG());
                atlas.SetPixels(index%3*size,(2-index/3)*size,size,size,image.GetPixels());
                UnityEngine.Object.DestroyImmediate(image);UnityEngine.Object.DestroyImmediate(root);UnityEngine.Object.DestroyImmediate(labelRoot);
            }
            atlas.Apply();File.WriteAllBytes(Path.Combine(output,"HaloModulePreview.png"),atlas.EncodeToPNG());
        }
        finally
        { camera.targetTexture=null;UnityEngine.Object.DestroyImmediate(target);UnityEngine.Object.DestroyImmediate(atlas);EditorSceneManager.ClosePreviewScene(scene); }
        Debug.Log("Halo module preview: "+Path.Combine(output,"HaloModulePreview.png"));
    }
}
