using Unity.MP_FPS;
using UnityEditor;
using UnityEngine;

public static class AlienVoidSetup
{
    [MenuItem("Tools/Moonkov/Aliens/Build black liquid appearance")]
    public static void Build()
    {
        const string folder="Assets/AlienCreatures/Materials";
        if(!AssetDatabase.IsValidFolder(folder))AssetDatabase.CreateFolder("Assets/AlienCreatures","Materials");
        var shader=AssetDatabase.LoadAssetAtPath<Shader>("Assets/Shaders/AlienVoidSkin.shader");
        Material Material(string name, bool plate=false, bool filament=false, bool drop=false)
        {
            string path=folder+"/"+name+".mat";
            var material=AssetDatabase.LoadAssetAtPath<Material>(path);
            var target=filament ? AssetDatabase.LoadAssetAtPath<Shader>("Assets/Shaders/AlienVoidFilament.shader") : shader;
            if(!material){material=new Material(target);AssetDatabase.CreateAsset(material,path);}
            material.shader=target;material.SetColor("_BaseColor",plate ? new Color(.025f,.027f,.033f,1) : new Color(.010f,.012f,.016f,1));
            material.SetColor("_VoidTint",new Color(.32f,.34f,.40f,1));
            if(!filament){material.SetFloat("_Smoothness",plate ? .58f : .83f);material.SetFloat("_Metallic",plate ? .24f : .08f);material.SetFloat("_VoidCharge",drop ? .65f : 0);}
            EditorUtility.SetDirty(material);AssetDatabase.SaveAssetIfDirty(material);return material;
        }
        const string resource="Assets/Resources/Moonkov/AlienVoid.asset";
        var style=AssetDatabase.LoadAssetAtPath<AlienVoidStyle>(resource);
        if(!style){style=ScriptableObject.CreateInstance<AlienVoidStyle>();AssetDatabase.CreateAsset(style,resource);}
        style.Skin=Material("VoidTissue");style.Plate=Material("VoidCarapace",true);
        style.Filament=Material("VoidAttackFilament",filament:true);style.Droplet=Material("VoidDroplet",drop:true);
        var sphere=GameObject.CreatePrimitive(PrimitiveType.Sphere);style.Sphere=sphere.GetComponent<MeshFilter>().sharedMesh;
        Object.DestroyImmediate(sphere);EditorUtility.SetDirty(style);AssetDatabase.SaveAssetIfDirty(style);
        Debug.Log("Black liquid appearance resources built. Existing formal-map enemy prefabs use them through AlienCreatureBody.");
    }
}
