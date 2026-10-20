using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Unity.MP_FPS;
using Unity.MP_FPS.DollSinger;

public static class HaloArsenalSetup
{
    private const string Art="Assets/DollSinger/Art/HaloArsenal/";
    [MenuItem("Tools/Moonkov/Install Halo Arsenal Weapons")]
    public static void Install() => InstallOne(0);
    public static void InstallOne(uint onlyId)
    {
        if(EditorApplication.isPlayingOrWillChangePlaymode)throw new InvalidOperationException("Stop Play before installing halo assets.");
        AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
        var selected=HaloArsenal.Entries.Where(e=>onlyId==0||e.Id==onlyId).ToArray();
        foreach(var entry in selected)
        foreach(string part in new[]{"Shell","Sigil","Node"}) ImportSprite(Art+entry.Code+"-"+part+".png");
        var registry=AssetDatabase.LoadAssetAtPath<WeaponRegistry>("Assets/Data/Weapons/WeaponRegistry.asset");
        foreach(var entry in selected)
        {
            string title=char.ToUpperInvariant(entry.Code[0])+entry.Code.Substring(1);
            string materialPath=Art+title+".mat";
            var material=AssetDatabase.LoadAssetAtPath<Material>(materialPath);
            if(!material){material=new Material(Shader.Find("Moonkov/Halo Arsenal Sprite"));AssetDatabase.CreateAsset(material,materialPath);}
            material.SetColor("_Color",Color.white);EditorUtility.SetDirty(material);
            string weaponPath="Assets/DollSinger/"+title+"Weapon.asset";
            var data=AssetDatabase.LoadAssetAtPath<WeaponData>(weaponPath);
            if(!data){data=UnityEngine.Object.Instantiate(registry.GetWeaponData(DollSingerWeapons.Halo));data.name=title+"Weapon";AssetDatabase.CreateAsset(data,weaponPath);}
            data.WeaponName=entry.Name;data.MagazineSize=entry.Capacity;data.EnergyPerRound=entry.Energy;
            data.CooldownInMs=entry.Interval;data.Damage=entry.Damage;data.ReloadTime=entry.Reload;
            data.EquipTime=entry.Equip;data.ProjectileSpeed=entry.Speed;data.ProjectileGravity=entry.Gravity;
            data.PelletCount=entry.Pellets;data.SpreadDegrees=entry.Spread;data.Automatic=entry.Automatic;
            data.BurstCount=entry.BurstCount;data.BurstInterval=entry.BurstCount>1?entry.Interval:.085f;data.BurstCooldown=entry.BurstCooldown;
            data.AutoReloadWhenEmpty=true;data.ShowProjectileBody=true;data.Type=WeaponType.Projectile;
            data.ProjectileLifetime=entry.BlastRadius>0?6:4;data.ProjectileRadius=entry.BlastRadius>0?.055f:.009f;
            data.Behavior=entry.BlastRadius>0?ProjectileBehavior.AreaOfEffect:ProjectileBehavior.DirectDamage;data.AoeRadius=entry.BlastRadius>0?entry.BlastRadius:3.5f;
            EditorUtility.SetDirty(data);
            while(registry.Weapons.Count<=entry.Id)registry.Weapons.Add(null);registry.Weapons[(int)entry.Id]=data;
            var root=new GameObject(title+" Halo");
            try
            {
                var visual=root.AddComponent<ArsenalHaloVisual>();visual.weapon=entry.Gesture;visual.magnification=entry.Scope;
                visual.recoil=entry.Id==DollSingerWeapons.Aegis?.035f:entry.Id==DollSingerWeapons.Sundown?.05f:.018f;
                visual.lightColor=(entry.Id==DollSingerWeapons.Vesper||entry.Id==DollSingerWeapons.Nocturne)?new Color(.83f,.90f,1):new Color(1,.88f,.64f);
                visual.core=Layer(root.transform,"Shell",entry.Code,material);visual.accent=Layer(root.transform,"Sigil",entry.Code,material);
                visual.sequenceNodes=entry.BurstCount>1;
                int count=entry.BurstCount>1?entry.BurstCount:entry.Id==DollSingerWeapons.Thorn?5:6;
                visual.nodes=new SpriteRenderer[count];
                for(int i=0;i<count;i++)
                {
                    var node=Layer(root.transform,"Node",entry.Code,material);visual.nodes[i]=node;
                    float a=(-90+i*360f/count)*Mathf.Deg2Rad;
                    node.transform.localPosition=new Vector3(Mathf.Cos(a)*.235f,Mathf.Sin(a)*.235f,-.003f);
                }
                PrefabUtility.SaveAsPrefabAsset(root,"Assets/DollSinger/Prefabs/"+title+"Halo.prefab");
            }
            finally{UnityEngine.Object.DestroyImmediate(root);}
        }
        EditorUtility.SetDirty(registry);
        foreach(string path in new[]{"Assets/DollSinger/Prefabs/DollSingerPlayer.prefab","Assets/DollSinger/Prefabs/DollSingerNetworkPlayer.prefab"})
        {
            var root=PrefabUtility.LoadPrefabContents(path);
            try
            {
                var halo=root.GetComponentInChildren<DollSingerHaloAim>(true);var visuals=new ArsenalHaloVisual[HaloArsenal.Entries.Length];
                foreach(var entry in HaloArsenal.Entries)
                {
                    int index=(int)entry.Id-6;
                    var existing=halo.GetComponentsInChildren<ArsenalHaloVisual>(true).FirstOrDefault(v=>v.weapon==entry.Gesture);
                    if(!existing)
                    {
                        string title=char.ToUpperInvariant(entry.Code[0])+entry.Code.Substring(1);
                        var child=(GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/DollSinger/Prefabs/"+title+"Halo.prefab"),halo.haloVisual);
                        existing=child.GetComponent<ArsenalHaloVisual>();
                    }
                    existing.gameObject.SetActive(false);visuals[index]=existing;
                }
                halo.arsenalVisuals=visuals;EditorUtility.SetDirty(halo);PrefabUtility.SaveAsPrefabAsset(root,path);
                if(path.EndsWith("/DollSingerPlayer.prefab"))
                {
                    var local=halo.GetComponent<LocalHaloArsenalDriver>();if(!local)local=halo.gameObject.AddComponent<LocalHaloArsenalDriver>();
                    local.registry=registry;PrefabUtility.SaveAsPrefabAsset(root,path);
                }
            }
            finally{PrefabUtility.UnloadPrefabContents(root);}
        }
        var previous=EditorSceneManager.GetActiveScene().path;
        var scene=EditorSceneManager.OpenScene("Assets/DollSinger/Scenes/WeaponGestureLab.unity");
        var lab=UnityEngine.Object.FindAnyObjectByType<WeaponGestureLab>();var serialized=new SerializedObject(lab);
        var weapons=serialized.FindProperty("m_Weapons");weapons.arraySize=registry.Weapons.Count-2;
        for(int i=0;i<weapons.arraySize;i++)weapons.GetArrayElementAtIndex(i).objectReferenceValue=registry.GetWeaponData((uint)(i+2));
        serialized.ApplyModifiedPropertiesWithoutUndo();EditorSceneManager.MarkSceneDirty(scene);EditorSceneManager.SaveScene(scene);
        if(!string.IsNullOrEmpty(previous))EditorSceneManager.OpenScene(previous);
        AssetDatabase.SaveAssets();Debug.Log("Arsenal definitions, sprite assemblies, two production prefabs and weapon lab installed.");
    }
    private static void ImportSprite(string path)
    {
        var importer=(TextureImporter)AssetImporter.GetAtPath(path);importer.textureType=TextureImporterType.Sprite;
        importer.spriteImportMode=SpriteImportMode.Single;importer.spritePixelsPerUnit=1000;importer.alphaIsTransparency=true;
        importer.mipmapEnabled=false;importer.textureCompression=TextureImporterCompression.Uncompressed;importer.SaveAndReimport();
    }
    private static SpriteRenderer Layer(Transform parent,string part,string code,Material material)
    {
        var child=new GameObject(part);child.transform.SetParent(parent,false);
        var renderer=child.AddComponent<SpriteRenderer>();renderer.sprite=AssetDatabase.LoadAssetAtPath<Sprite>(Art+code+"-"+part+".png");renderer.sharedMaterial=material;
        renderer.shadowCastingMode=UnityEngine.Rendering.ShadowCastingMode.Off;renderer.receiveShadows=false;
        return renderer;
    }
}
