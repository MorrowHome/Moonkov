using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using Unity.MP_FPS;
using Unity.MP_FPS.Inventory;

public static class PhysicalItemAssetSetup
{
    private const string Root="Assets/Resources/Moonkov/Items";
    private static readonly string[] Names={"ceramic","dark","steel","gold","copper","red","blue","green","amber","violet","tissue","bone"};
    private static readonly Color[] Colors={new(.57f,.61f,.58f),new(.028f,.04f,.045f),new(.38f,.44f,.47f),new(.58f,.31f,.08f),new(.62f,.23f,.09f),new(.42f,.045f,.035f),new(.06f,.28f,.39f),new(.10f,.25f,.15f),new(.75f,.39f,.05f),new(.30f,.15f,.40f),new(.28f,.055f,.12f),new(.66f,.57f,.35f)};
    private static readonly float[] Roughness={.36f,.52f,.28f,.32f,.30f,.40f,.30f,.48f,.40f,.35f,.26f,.42f};

    public static string Import(string stagingDirectory)
    {
        if(EditorApplication.isPlaying)throw new InvalidOperationException("Import physical item assets outside Play Mode.");
        var definitions=InventoryCatalog.Definitions.Values.Where(d=>PhysicalItemVisuals.IsPhysical(d.Code)).ToArray();
        foreach(var d in definitions)
            foreach(string ext in new[]{".fbx",".png"})
                if(!File.Exists(Path.Combine(stagingDirectory,d.Code+ext)))throw new FileNotFoundException(d.Code+ext);
        foreach(string folder in new[]{"Models","Icons","Materials","Prefabs"})Directory.CreateDirectory(Root+"/"+folder);
        foreach(var d in definitions)
        {
            File.Copy(Path.Combine(stagingDirectory,d.Code+".fbx"),Root+"/Models/"+d.Code+".fbx",true);
            File.Copy(Path.Combine(stagingDirectory,d.Code+".png"),Root+"/Icons/"+d.Code+".png",true);
        }
        AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
        var shader=Shader.Find("Universal Render Pipeline/Lit");
        if(!shader)throw new InvalidOperationException("URP Lit shader unavailable");
        var materials=new System.Collections.Generic.Dictionary<string,Material>();
        for(int i=0;i<Names.Length;i++)
        {
            string path=Root+"/Materials/Item_"+Names[i]+".mat";
            var m=AssetDatabase.LoadAssetAtPath<Material>(path);
            if(!m){m=new Material(shader);AssetDatabase.CreateAsset(m,path);}
            m.name="Item_"+Names[i];m.SetColor("_BaseColor",Colors[i]);
            m.SetFloat("_Metallic",i>=2 && i<=4 ? 1:0);m.SetFloat("_Smoothness",1-Roughness[i]);m.enableInstancing=true;
            EditorUtility.SetDirty(m);materials.Add(m.name,m);
        }
        foreach(var d in definitions)
        {
            string modelPath=Root+"/Models/"+d.Code+".fbx";
            var importer=(ModelImporter)AssetImporter.GetAtPath(modelPath);
            importer.importAnimation=false;importer.importCameras=false;importer.importLights=false;importer.addCollider=false;
            importer.isReadable=false;importer.meshCompression=ModelImporterMeshCompression.Off;
            importer.materialImportMode=ModelImporterMaterialImportMode.ImportStandard;
            var source=AssetDatabase.LoadAssetAtPath<GameObject>(modelPath);
            foreach(var renderer in source.GetComponentsInChildren<Renderer>())
                foreach(var m in renderer.sharedMaterials)
                    if(m && materials.TryGetValue(m.name.Split('.')[0],out var shared))
                        importer.AddRemap(new AssetImporter.SourceAssetIdentifier(typeof(Material),m.name),shared);
            importer.SaveAndReimport();
            var icon=(TextureImporter)AssetImporter.GetAtPath(Root+"/Icons/"+d.Code+".png");
            icon.textureType=TextureImporterType.Default;icon.alphaIsTransparency=true;icon.mipmapEnabled=false;
            icon.npotScale=TextureImporterNPOTScale.None;icon.maxTextureSize=512;icon.textureCompression=TextureImporterCompression.CompressedHQ;icon.isReadable=false;icon.SaveAndReimport();
            source=AssetDatabase.LoadAssetAtPath<GameObject>(modelPath);
            var instance=UnityEngine.Object.Instantiate(source);instance.name=d.Code;
            try
            {
                PrefabUtility.SaveAsPrefabAsset(instance,Root+"/Prefabs/"+d.Code+".prefab");
            }
            finally{UnityEngine.Object.DestroyImmediate(instance);}
        }
        AssetDatabase.SaveAssets();return Validate();
    }

    [MenuItem("Tools/Moonkov/Check Physical Item Assets")]
    public static void Check()=>Debug.Log(Validate());
    public static string Validate()
    {
        int count=0,triangles=0;
        foreach(var d in InventoryCatalog.Definitions.Values.Where(d=>PhysicalItemVisuals.IsPhysical(d.Code)))
        {
            var prefab=Resources.Load<GameObject>("Moonkov/Items/Prefabs/"+d.Code);
            var icon=PhysicalItemVisuals.Icon(d.Code);
            if(!prefab || !icon)throw new InvalidOperationException("Missing physical presentation: "+d.Code);
            var renderers=prefab.GetComponentsInChildren<MeshRenderer>();
            if(renderers.Length!=1 || prefab.GetComponentsInChildren<Collider>().Length!=0)
                throw new InvalidOperationException("Expected one static renderer and no gameplay collider: "+d.Code);
            foreach(var r in renderers)
            {
                if(r.sharedMaterials.Any(m=>!m || !m.shader || m.shader.name!="Universal Render Pipeline/Lit"))
                    throw new InvalidOperationException("Broken URP materials: "+d.Code);
                var mesh=r.GetComponent<MeshFilter>().sharedMesh;
                if(!mesh || mesh.vertexCount==0 || r.bounds.size.magnitude<.005f || r.bounds.size.magnitude>2)
                    throw new InvalidOperationException("Invalid mesh or scale: "+d.Code);
                for(int s=0;s<mesh.subMeshCount;s++)triangles+=(int)mesh.GetIndexCount(s)/3;
            }
            count++;
        }
        return $"Physical items: {count} models/icons; {triangles} triangles in complete catalog; URP materials and metre scale valid.";
    }
}
