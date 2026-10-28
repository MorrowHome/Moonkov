using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using Unity.MP_FPS;

public static class RaidActionBlenderImport
{
 [Serializable] private class Export { public Prop[] props; }
 [Serializable] private class Prop { public string code; public float[] grip,contact; public Part[] parts; }
 [Serializable] private class Part { public string name; public float[] vertices,normals,uv; public Submesh[] submeshes; }
 [Serializable] private class Submesh { public string material; public int[] triangles; }
 private const string Folder="Assets/Resources/Moonkov/Actions/Props/";
 [MenuItem("Tools/Moonkov/Actions/Import reviewed Blender props")]
 public static void Import()
 {
  if(EditorApplication.isPlayingOrWillChangePlaymode)throw new InvalidOperationException("Import in Edit mode.");
  var data=JsonUtility.FromJson<Export>(File.ReadAllText("SourceData/Animations/Survival/BlenderExport.json"));
  Directory.CreateDirectory(Folder+"Textures");Directory.CreateDirectory(Folder+"Materials");
  File.Copy("ArtSource/DollSinger/SurvivalActions/RationPrint.png",Folder+"Textures/RationPrint.png",true);AssetDatabase.ImportAsset(Folder+"Textures/RationPrint.png");
  var textureImporter=(TextureImporter)AssetImporter.GetAtPath(Folder+"Textures/RationPrint.png");textureImporter.mipmapEnabled=true;textureImporter.textureCompression=TextureImporterCompression.Compressed;textureImporter.SaveAndReimport();
  foreach(var prop in data.props)Build(prop);
  foreach(string code in new[]{"vitality_shot","stamina_shot","antidote_shot"})UpdateInjector(code);
  AssetDatabase.SaveAssets();AssetDatabase.Refresh();Debug.Log("Imported reviewed Blender meshes, anchors, detached pieces and 10 mm plunger travel.");
 }
 private static Material Material(string name)
 {
  string original=name.Replace("Survival_","");
  if(original!="Ration inventory print")return AssetDatabase.LoadAssetAtPath<Material>("Assets/Resources/Moonkov/Items/Materials/"+original+".mat")??throw new InvalidOperationException("Missing source material "+name);
  string path=Folder+"Materials/RationPrint.mat";var mat=AssetDatabase.LoadAssetAtPath<Material>(path);if(!mat){mat=new Material(Shader.Find("Universal Render Pipeline/Lit"));AssetDatabase.CreateAsset(mat,path);}mat.SetColor("_BaseColor",Color.white);mat.SetTexture("_BaseMap",AssetDatabase.LoadAssetAtPath<Texture2D>(Folder+"Textures/RationPrint.png"));mat.SetFloat("_Smoothness",.25f);EditorUtility.SetDirty(mat);return mat;
 }
 private static void Build(Prop prop)
 {
  var root=new GameObject(prop.code+" reviewed action prop");
  try{
   var config=root.AddComponent<RaidActionProp>();config.Authored=true;var parts=new List<RaidActionProp.Part>();
   foreach(var part in prop.parts){
    var mesh=new Mesh{name=prop.code+" "+part.name};var vertices=new Vector3[part.vertices.Length/3];var normals=new Vector3[vertices.Length];var uv=new Vector2[vertices.Length];
    for(int i=0;i<vertices.Length;i++){vertices[i]=new Vector3(part.vertices[i*3],part.vertices[i*3+1],part.vertices[i*3+2]);normals[i]=new Vector3(part.normals[i*3],part.normals[i*3+1],part.normals[i*3+2]);uv[i]=new Vector2(part.uv[i*2],part.uv[i*2+1]);}
    mesh.vertices=vertices;mesh.normals=normals;mesh.uv=uv;mesh.subMeshCount=part.submeshes.Length;for(int s=0;s<part.submeshes.Length;s++)mesh.SetTriangles(part.submeshes[s].triangles,s);mesh.RecalculateBounds();
    string path=Folder+prop.code+"-"+part.name+".asset";var previous=AssetDatabase.LoadAssetAtPath<Mesh>(path);if(previous){EditorUtility.CopySerialized(mesh,previous);UnityEngine.Object.DestroyImmediate(mesh);mesh=previous;}else AssetDatabase.CreateAsset(mesh,path);
    var child=new GameObject(part.name);child.transform.SetParent(root.transform,false);child.AddComponent<MeshFilter>().sharedMesh=mesh;child.AddComponent<MeshRenderer>().sharedMaterials=part.submeshes.Select(s=>Material(s.material)).ToArray();parts.Add(new RaidActionProp.Part{Name=part.name,Transform=child.transform});
    if(part.name=="Sealed")config.Sealed=child;else if(part.name=="Opened"||part.name=="Body")config.Opened=child;else if(part.name=="Cap")config.Cap=child.transform;else if(part.name=="Plunger")config.Plunger=child.transform;else if(part.name=="Needle")config.Needle=child;
   }
   config.Parts=parts.ToArray();config.PlungerTravel=.01f;
   Transform Anchor(string name,float[] point){var t=new GameObject(name).transform;t.SetParent(root.transform,false);t.localPosition=new Vector3(point[0],point[1],point[2]);return t;}
   config.Grip=Anchor("Grip",prop.grip);config.Contact=Anchor("Contact",prop.contact);PrefabUtility.SaveAsPrefabAsset(root,Folder+prop.code+".prefab");
  }finally{UnityEngine.Object.DestroyImmediate(root);}
 }
 private static void UpdateInjector(string code)
 {
  string path=Folder+code+".prefab";var root=PrefabUtility.LoadPrefabContents(path);
  try{
   var config=root.GetComponent<RaidActionProp>();config.Authored=true;config.PlungerTravel=.01f;config.Grip.localPosition=new Vector3(0,.1345f,0);
   var reference=AssetDatabase.LoadAssetAtPath<GameObject>(Folder+"medkit.prefab").GetComponent<RaidActionProp>();var existing=root.transform.Find("NeedleSheath");var cap=existing?existing.gameObject:new GameObject("NeedleSheath");cap.transform.SetParent(root.transform,false);var original=reference.FindPart("NeedleSheath");
   var filter=cap.GetComponent<MeshFilter>();if(!filter)filter=cap.AddComponent<MeshFilter>();filter.sharedMesh=original.GetComponent<MeshFilter>().sharedMesh;
   var renderer=cap.GetComponent<MeshRenderer>();if(!renderer)renderer=cap.AddComponent<MeshRenderer>();renderer.sharedMaterials=original.GetComponent<MeshRenderer>().sharedMaterials;
   config.Parts=new[]{new RaidActionProp.Part{Name="Body",Transform=config.Opened.transform},new RaidActionProp.Part{Name="Plunger",Transform=config.Plunger},new RaidActionProp.Part{Name="Needle",Transform=config.Needle.transform},new RaidActionProp.Part{Name="NeedleSheath",Transform=cap.transform}};
   PrefabUtility.SaveAsPrefabAsset(root,path);
  }finally{PrefabUtility.UnloadPrefabContents(root);}
 }
}
