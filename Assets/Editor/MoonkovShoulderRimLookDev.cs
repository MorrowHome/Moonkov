using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

/// <summary>Localized native lilToon rim authoring; source UVs, rig and shaders are preserved.</summary>
public static class MoonkovShoulderRimLookDev
{
    public const string Root=MoonkovLilToonLookDev.Root+"/ShoulderRim";
    public const string Grade=Root+"/Grade.asset", BeforeGrade=Root+"/BeforeGrade.asset";
    public const float BloomIntensity=.65f,BloomScatter=.68f;
    public static readonly string[] Names={"Body","InnerDress"};
    public static Material Get(string name,bool before=false)=>AssetDatabase.LoadAssetAtPath<Material>($"{Root}/{(before?"Before":"Materials")}/{name}.mat");

    [MenuItem("Tools/Moonkov/lilToon/Shoulder Rim/Build Copies")]
    public static void Build()
    {
        RequireEdit();
        foreach(string path in new[]{Root,Root+"/Before",Root+"/Materials",Root+"/Maps"})
            if(!AssetDatabase.IsValidFolder(path))AssetDatabase.CreateFolder(Path.GetDirectoryName(path).Replace('\\','/'),Path.GetFileName(path));
        foreach(string name in Names)
        {
            if(!Get(name,true))AssetDatabase.CopyAsset(MoonkovLilToonLookDev.Source+name+".mat",$"{Root}/Before/{name}.mat");
            if(!Get(name))AssetDatabase.CopyAsset($"{Root}/Before/{name}.mat",$"{Root}/Materials/{name}.mat");
        }
        if(!File.Exists(BeforeGrade))AssetDatabase.CopyAsset(MoonkovAnimeLightingSetup.ProfilePath,BeforeGrade);
        if(!File.Exists(Grade))AssetDatabase.CopyAsset(BeforeGrade,Grade);
        WriteMasks();
        foreach(string name in Names)
        {
            var m=Get(name);m.CopyPropertiesFromMaterial(Get(name,true));
            m.SetFloat("_UseRim",1);m.SetFloat("_RimEnableLighting",1);
            // MoonToon's fd.lightColor already receives actual sun and lamp visibility.
            // Toon shadowmix also contains N.L, which would erase a backlit contour.
            m.SetFloat("_RimShadowMask",0);m.SetFloat("_RimDirStrength",1);
            m.SetFloat("_RimDirRange",0);m.SetFloat("_RimFresnelPower",2.2f);
            m.SetFloat("_RimBorder",.45f);m.SetFloat("_RimBlur",.65f);
            m.SetFloat("_RimMainStrength",.1f);m.SetFloat("_RimBackfaceMask",1);
            m.SetColor("_RimColor",name=="Body"?new Color(5.5f,5.1f,4.7f,.75f):new Color(5.1f,5.4f,5.6f,.55f));
            m.SetColor("_RimIndirColor",new Color(1,1,1,0));
            m.SetTexture("_RimColorTex",AssetDatabase.LoadAssetAtPath<Texture2D>($"{Root}/Maps/{name}Rim.png"));
            EditorUtility.SetDirty(m);AssetDatabase.SaveAssetIfDirty(m);
        }
        var grade=AssetDatabase.LoadAssetAtPath<VolumeProfile>(Grade);
        grade.TryGet<Bloom>(out var bloom);bloom.intensity.Override(BloomIntensity);bloom.scatter.Override(BloomScatter);
        EditorUtility.SetDirty(bloom);AssetDatabase.SaveAssetIfDirty(bloom);
    }

    [MenuItem("Tools/Moonkov/lilToon/Shoulder Rim/Apply To Game")]
    public static void Apply()=>CopyToGame(false);
    [MenuItem("Tools/Moonkov/lilToon/Shoulder Rim/Restore Previous Look")]
    public static void Restore()=>CopyToGame(true);
    static void RequireEdit(){if(EditorApplication.isPlayingOrWillChangePlaymode)throw new InvalidOperationException("Edit Mode required");}
    static void CopyToGame(bool before)
    {
        RequireEdit();
        var grade=AssetDatabase.LoadAssetAtPath<VolumeProfile>(before?BeforeGrade:Grade);
        var targets=new[]{AssetDatabase.LoadAssetAtPath<VolumeProfile>(MoonkovAnimeLightingSetup.ProfilePath),AssetDatabase.LoadAssetAtPath<VolumeProfile>(MoonkovLilToonLookDev.Grade)};
        if(!grade||targets.Any(p=>!p)||Names.Any(n=>!Get(n,before)||!AssetDatabase.LoadAssetAtPath<Material>(MoonkovLilToonLookDev.Source+n+".mat")||!MoonkovLilToonLookDev.Get(n)))throw new InvalidOperationException("Build shoulder copies first");
        foreach(string name in Names)
        foreach(var m in new[]{AssetDatabase.LoadAssetAtPath<Material>(MoonkovLilToonLookDev.Source+name+".mat"),MoonkovLilToonLookDev.Get(name)})
        {
            if(m.shader!=Get(name,before).shader)throw new InvalidOperationException("Shader mismatch: "+name);
        }
        foreach(string name in Names)
        foreach(var m in new[]{AssetDatabase.LoadAssetAtPath<Material>(MoonkovLilToonLookDev.Source+name+".mat"),MoonkovLilToonLookDev.Get(name)})
        {Undo.RecordObject(m,"Shoulder rim look");m.CopyPropertiesFromMaterial(Get(name,before));EditorUtility.SetDirty(m);AssetDatabase.SaveAssetIfDirty(m);}
        foreach(var target in targets)
        foreach(var component in target.components)
        {
            var source=grade.components.FirstOrDefault(c=>c.GetType()==component.GetType());if(!source)continue;
            Undo.RecordObject(component,"Shoulder rim grade");EditorUtility.CopySerialized(source,component);EditorUtility.SetDirty(component);AssetDatabase.SaveAssetIfDirty(component);
        }
    }
    public static void SetLook(GameObject actor,bool before)
    {
        foreach(var r in actor.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            r.sharedMaterials=r.sharedMaterials.Select(m=>m&&Names.Contains(m.name.Replace(" (Instance)",""))?Get(m.name.Replace(" (Instance)",""),before):m).ToArray();
    }

    static float Region(Vector3 p,string name)
    {
        // Fixed-pose body-space shoulder region, baked into UVs (no runtime world-space mask).
        float x=(Mathf.Abs(p.x)-(name=="Body"?.10f:.13f))/.075f,y=(p.y-(name=="Body"?1.09f:1.165f))/.105f,z=p.z/.13f;
        return Mathf.Exp(-1.5f*(x*x+y*y+z*z))*Mathf.SmoothStep(0,1,Mathf.InverseLerp(.025f,.065f,Mathf.Abs(p.x)));
    }
    static void WriteMasks()
    {
        var actor=UnityEngine.Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Resources/Moonkov/MenuCharacterVisual.prefab"));var mesh=new Mesh();
        try
        {
            foreach(var b in actor.GetComponentsInChildren<MonoBehaviour>(true))b.enabled=false;
            MoonkovLilToonLookDev.Pose(actor);var renderer=actor.GetComponentInChildren<SkinnedMeshRenderer>();renderer.BakeMesh(mesh,true);
            // This FBX has a 100x renderer transform. BakeMesh(useScale:true) is essential.
            var points=mesh.vertices.Select(p=>renderer.transform.TransformPoint(p)).ToArray();var uv=mesh.uv;
            const int size=512;
            foreach(string name in Names)
            {
                int slot=Array.FindIndex(renderer.sharedMaterials,m=>m&&m.name==name);if(slot<0)throw new Exception("Missing slot: "+name);
                var values=new float[size*size];var triangles=mesh.GetTriangles(slot);
                for(int t=0;t<triangles.Length;t+=3)
                {
                    int a=triangles[t],b=triangles[t+1],c=triangles[t+2];var A=uv[a]*size;var B=uv[b]*size;var C=uv[c]*size;
                    float den=(B.y-C.y)*(A.x-C.x)+(C.x-B.x)*(A.y-C.y);if(Mathf.Abs(den)<.00001f)continue;
                    int xmin=Mathf.Max(0,Mathf.FloorToInt(Mathf.Min(A.x,Mathf.Min(B.x,C.x)))),xmax=Mathf.Min(size-1,Mathf.CeilToInt(Mathf.Max(A.x,Mathf.Max(B.x,C.x))));
                    int ymin=Mathf.Max(0,Mathf.FloorToInt(Mathf.Min(A.y,Mathf.Min(B.y,C.y)))),ymax=Mathf.Min(size-1,Mathf.CeilToInt(Mathf.Max(A.y,Mathf.Max(B.y,C.y))));
                    for(int y=ymin;y<=ymax;y++)for(int x=xmin;x<=xmax;x++)
                    {
                        float w=((B.y-C.y)*(x+.5f-C.x)+(C.x-B.x)*(y+.5f-C.y))/den;
                        float q=((C.y-A.y)*(x+.5f-C.x)+(A.x-C.x)*(y+.5f-C.y))/den;float z=1-w-q;if(w<0||q<0||z<0)continue;
                        int i=y*size+x;values[i]=Mathf.Max(values[i],Region(points[a]*w+points[b]*q+points[c]*z,name));
                    }
                }
                // Pad island boundaries for bilinear/mip sampling, retaining a soft spatial fade.
                for(int pass=0;pass<3;pass++)
                {var previous=(float[])values.Clone();for(int y=1;y<size-1;y++)for(int x=1;x<size-1;x++){int i=y*size+x;values[i]=Mathf.Max(previous[i],Mathf.Max(Mathf.Max(previous[i-1],previous[i+1]),Mathf.Max(previous[i-size],previous[i+size])));}}
                var texture=new Texture2D(size,size,TextureFormat.RGBA32,false,true);
                try
                {
                    texture.SetPixels(values.Select(v=>new Color(1,1,1,v)).ToArray());texture.Apply();string path=$"{Root}/Maps/{name}Rim.png";
                    byte[] bytes=texture.EncodeToPNG();if(!File.Exists(path)||!File.ReadAllBytes(path).SequenceEqual(bytes))File.WriteAllBytes(path,bytes);
                    AssetDatabase.ImportAsset(path,ImportAssetOptions.ForceSynchronousImport);
                    var importer=(TextureImporter)AssetImporter.GetAtPath(path);importer.sRGBTexture=false;importer.mipmapEnabled=true;importer.isReadable=false;
                    importer.wrapMode=TextureWrapMode.Clamp;importer.alphaSource=TextureImporterAlphaSource.FromInput;importer.alphaIsTransparency=false;importer.textureCompression=TextureImporterCompression.CompressedHQ;importer.SaveAndReimport();
                }
                finally{UnityEngine.Object.DestroyImmediate(texture);}
            }
        }
        finally{UnityEngine.Object.DestroyImmediate(mesh);UnityEngine.Object.DestroyImmediate(actor);}
    }
}
