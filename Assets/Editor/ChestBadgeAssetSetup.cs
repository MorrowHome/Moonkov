using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;
using Unity.MP_FPS;
using UnityEditor;
using UnityEditor.Localization;
using UnityEngine;
using UnityEngine.Localization.Tables;

// Scoped authoring path. Never rebuilds the item catalog, fonts or player data.
public static class ChestBadgeAssetSetup
{
    private const string Root="Assets/Resources/Moonkov/Items";
    private static readonly string[] Codes={"scout_rig","rig","field_rig"};

    internal static Dictionary<string,Material> EnsureMaterials()
    {
        var shader=Shader.Find("Universal Render Pipeline/Lit");
        if(!shader)throw new InvalidOperationException("Badge URP shader unavailable.");
        var result=new Dictionary<string,Material>();
        foreach(var spec in new[] {
            (name:"Badge_Alloy",color:new Color(.66f,.72f,.77f),metal:.8f,smooth:.75f),
            (name:"Badge_Enamel",color:new Color(.88f,.87f,.82f),metal:.05f,smooth:.70f),
            (name:"Badge_Scout",color:new Color(.22f,.56f,.70f),metal:.15f,smooth:.70f),
            (name:"Badge_Standard",color:new Color(.50f,.40f,.72f),metal:.15f,smooth:.70f),
            (name:"Badge_Field",color:new Color(.78f,.52f,.20f),metal:.15f,smooth:.70f) })
        {
            string path=Root+"/Materials/"+spec.name+".mat";
            var material=AssetDatabase.LoadAssetAtPath<Material>(path);
            if(!material){material=new Material(shader);AssetDatabase.CreateAsset(material,path);}
            material.name=spec.name;material.SetColor("_BaseColor",spec.color);
            material.SetFloat("_Metallic",spec.metal);material.SetFloat("_Smoothness",spec.smooth);
            material.enableInstancing=true;
            EditorUtility.SetDirty(material);AssetDatabase.SaveAssetIfDirty(material);
            result.Add(spec.name,material);
        }
        return result;
    }

    public static string Import(string stagingDirectory="LocalData/ChestBadgeReview/Staging")
    {
        if(EditorApplication.isPlayingOrWillChangePlaymode || BuildPipeline.isBuildingPlayer)
            throw new InvalidOperationException("Import badges outside Play mode and Player builds.");
        foreach(string code in Codes)
            foreach(string ext in new[]{".fbx",".png",".blend"})
                if(!File.Exists(Path.Combine(stagingDirectory,code+ext)))throw new FileNotFoundException(code+ext);
        foreach(string code in Codes)
        {
            File.Copy(Path.Combine(stagingDirectory,code+".fbx"),Root+"/Models/"+code+".fbx",true);
            File.Copy(Path.Combine(stagingDirectory,code+".png"),Root+"/Icons/"+code+".png",true);
            File.Copy(Path.Combine(stagingDirectory,code+".blend"),"SourceData/Models/PhysicalItems/"+code+".blend",true);
        }
        AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
        var materials=EnsureMaterials();
        foreach(string code in Codes)
        {
            string path=Root+"/Models/"+code+".fbx";
            var importer=(ModelImporter)AssetImporter.GetAtPath(path);
            importer.importAnimation=false;importer.importCameras=false;importer.importLights=false;importer.addCollider=false;
            importer.isReadable=false;importer.meshCompression=ModelImporterMeshCompression.Off;
            // Remove just the old tactical material remaps. The asset GUID stays.
            foreach(var key in importer.GetExternalObjectMap().Keys.Where(k=>k.type==typeof(Material)).ToArray())importer.RemoveRemap(key);
            var source=AssetDatabase.LoadAssetAtPath<GameObject>(path);
            foreach(var renderer in source.GetComponentsInChildren<Renderer>(true))
                foreach(var material in renderer.sharedMaterials)
                    if(material && materials.TryGetValue(material.name.Split('.')[0],out var shared))
                        importer.AddRemap(new AssetImporter.SourceAssetIdentifier(typeof(Material),material.name),shared);
            importer.SaveAndReimport();
            source=AssetDatabase.LoadAssetAtPath<GameObject>(path);
            var instance=UnityEngine.Object.Instantiate(source);instance.name=code;
            try{PrefabUtility.SaveAsPrefabAsset(instance,Root+"/Prefabs/"+code+".prefab");}
            finally{UnityEngine.Object.DestroyImmediate(instance);}
            AssetDatabase.ImportAsset(Root+"/Icons/"+code+".png",ImportAssetOptions.ForceSynchronousImport);
        }
        return PhysicalItemAssetSetup.Validate();
    }

    public static string InstallLocalization()
    {
        var translations=JObject.Parse(File.ReadAllText("Assets/Localization/Translations.json"));
        var collection=LocalizationEditorSettings.GetStringTableCollection("MoonkovUI");
        if(collection==null)throw new InvalidOperationException("MoonkovUI collection missing.");
        int count=0;
        // Existing descriptions of other supplies must also refer to the new
        // badge. Touch only these entries, without rebuilding fonts or settings.
        foreach(var pair in translations.Properties())
        {
            string value=pair.Value.Value<string>();
            if(!value.Contains("胸章") && !pair.Name.Contains("badge"))continue;
            foreach(var reference in collection.Tables)
            {
                var table=(StringTable)reference.asset;
                table.AddEntry(pair.Name,table.LocaleIdentifier.Code=="zh-Hans"?value:pair.Name);
                EditorUtility.SetDirty(table);
            }
            count++;
        }
        foreach(var reference in collection.Tables)AssetDatabase.SaveAssetIfDirty(reference.asset);
        EditorUtility.SetDirty(collection.SharedData);AssetDatabase.SaveAssetIfDirty(collection.SharedData);
        return "Chest badge localization: "+count+" entries updated in both locales.";
    }
}
