#pragma warning disable CS0649
using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using UnityEditor;

public static class ImportR39Detached {
 const string Root="LocalData/AI/AssetHandoffs/TheophilusDenseRing/R35_R39/";
 const string Output="Assets/MoonEnvironment/Variants/TheophilusBase/DisplayIncrements/R39";
 const string Formal="Assets/MoonEnvironment/Variants/TheophilusBase/Scenes/TheophilusGameScene.unity";
 public class Manifest {public Entry[] objects;public Patch patch;}
 public class Patch {public string new_material;public Dictionary<string,string> textures;}
 public class Entry {public string name,key,sha256;public int vertices,triangles;public string[] materials;}
 public class Payload {public Vector3[] positions,normals;public Dictionary<int,Vector2[]> uv=new Dictionary<int,Vector2[]>();public Vector2[] tangentUV;public int[][] indices;}
 static Type JsonType=>AppDomain.CurrentDomain.GetAssemblies().Single(a=>a.GetName().Name=="Newtonsoft.Json").GetType("Newtonsoft.Json.JsonConvert");
 static T Json<T>(string p)=>(T)JsonType.GetMethod("DeserializeObject",new[]{typeof(string),typeof(Type)}).Invoke(null,new object[]{File.ReadAllText(p),typeof(T)});
 static string Serialize(object o)=>(string)JsonType.GetMethod("SerializeObject",new[]{typeof(object)}).Invoke(null,new[]{o});
 static void Need(bool b,string m){if(!b)throw new Exception(m);}
 static string Sha(string p){using(var h=System.Security.Cryptography.SHA256.Create())return BitConverter.ToString(h.ComputeHash(File.ReadAllBytes(p))).Replace("-","").ToLowerInvariant();}
 static string Scenes(){var rows=new List<string>();for(int i=0;i<SceneManager.sceneCount;i++){var s=SceneManager.GetSceneAt(i);rows.Add(s.path+":"+s.isLoaded+":"+s.isDirty);}return string.Join("\n",rows);}
 static void Ensure(string p){if(AssetDatabase.IsValidFolder(p))return;Need(!Directory.Exists(p),"Unregistered existing directory "+p);int i=p.LastIndexOf('/');Ensure(p.Substring(0,i));AssetDatabase.CreateFolder(p.Substring(0,i),p.Substring(i+1));}
 static Material Existing(string n){string p;var b="Assets/MoonEnvironment/Variants/TheophilusBase/DisplayIncrements/";
 if(n.StartsWith("R39_"))p=Output+"/Materials/"+n+".mat";
 else if(n.StartsWith("R34_"))p=b+"R35/Materials/"+n+".mat";
 else if(n.StartsWith("R26V2_")||n.StartsWith("R27_")||n.StartsWith("R28_"))p=b+"R29/Industrial/Materials/"+n+".mat";
 else if(n.StartsWith("R27S_"))p=b+"R29/LocalGround/Materials/"+n+".mat";
 else if(n.StartsWith("R24_"))p=b+"R24/Materials/"+n+".mat";
 else if(n.StartsWith("R23_"))p=b+"R23/Materials/"+n+".mat";
 else if(n.StartsWith("R13_")||n.StartsWith("R3_"))p=b+"R21/Materials/"+n+".mat";
 else p="Assets/MoonEnvironment/Variants/TheophilusShoulderReturn/DenseRingConceptB/"+(n.StartsWith("R6_")?"R6Transfer06":"R8VisualSurfaces")+"/Materials/"+n+".mat";
 var m=AssetDatabase.LoadAssetAtPath<Material>(p);Need(m&&m.shader&&m.shader.isSupported,"Missing exact existing material "+n);return m;}
 static Payload Read(Entry e){string p=Root+"PreparedR39Recovered/"+e.key+".bin";Need(Sha(p)==e.sha256,"Native payload hash differs");var d=new Payload();using(var r=new BinaryReader(File.OpenRead(p))){int n=r.ReadInt32();Need(n==e.vertices,"Vertex count differs");d.positions=new Vector3[n];d.normals=new Vector3[n];for(int i=0;i<n;i++)d.positions[i]=new Vector3(r.ReadSingle(),r.ReadSingle(),r.ReadSingle());for(int i=0;i<n;i++)d.normals[i]=new Vector3(r.ReadSingle(),r.ReadSingle(),r.ReadSingle());int channels=r.ReadInt32();for(int j=0;j<channels;j++){int c=r.ReadInt32();Need(c==0,"Unexpected UV channel");var v=new Vector2[n];for(int i=0;i<n;i++)v[i]=new Vector2(r.ReadSingle(),r.ReadSingle());d.uv[c]=v;}d.tangentUV=new Vector2[n];for(int i=0;i<n;i++)d.tangentUV[i]=new Vector2(r.ReadSingle(),r.ReadSingle());d.indices=new int[r.ReadInt32()][];for(int j=0;j<d.indices.Length;j++){d.indices[j]=new int[r.ReadInt32()];for(int i=0;i<d.indices[j].Length;i++){int v=r.ReadInt32();Need(v>=0&&v<n,"Invalid index");d.indices[j][i]=v;}}Need(r.BaseStream.Position==r.BaseStream.Length&&d.indices.Length==e.materials.Length,"Payload structure differs");}return d;}

 static void CreateMaterial(Patch spec){
  Ensure(Output+"/Materials");Ensure(Output+"/Textures");var textures=new Dictionary<string,Texture2D>();
  foreach(var pair in spec.textures){
   string src=Root+"CompatWork511Recovered/R39/textures/"+pair.Key;Need(Sha(src)==pair.Value,"R39 source texture hash differs");string dst=Output+"/Textures/"+pair.Key;File.Copy(src,dst,false);AssetDatabase.ImportAsset(dst,ImportAssetOptions.ForceSynchronousImport);
   var t=(TextureImporter)AssetImporter.GetAtPath(dst);bool normal=pair.Key.Contains("Normal");t.sRGBTexture=pair.Key.Contains("BaseColor");t.textureType=normal?TextureImporterType.NormalMap:TextureImporterType.Default;t.flipGreenChannel=false;t.npotScale=TextureImporterNPOTScale.None;t.wrapMode=TextureWrapMode.Clamp;t.mipmapEnabled=true;t.maxTextureSize=2048;t.textureCompression=TextureImporterCompression.CompressedHQ;t.anisoLevel=4;t.SaveAndReimport();Need(Sha(dst)==pair.Value,"R39 texture bytes changed");textures[pair.Key]=AssetDatabase.LoadAssetAtPath<Texture2D>(dst);
  }
  var shader=Shader.Find("Universal Render Pipeline/Lit");Need(shader&&shader.isSupported,"URP Lit unavailable");var m=new Material(shader){name=spec.new_material};m.SetColor("_BaseColor",Color.white);m.SetTexture("_BaseMap",textures.Single(p=>p.Key.Contains("BaseColor")).Value);m.SetTexture("_BumpMap",textures.Single(p=>p.Key.Contains("Normal")).Value);m.SetFloat("_BumpScale",1);m.SetTexture("_MetallicGlossMap",textures.Single(p=>p.Key.Contains("MetallicSmoothness")).Value);m.SetFloat("_Metallic",1);m.SetFloat("_Smoothness",1);m.SetFloat("_SmoothnessTextureChannel",0);m.EnableKeyword("_NORMALMAP");m.EnableKeyword("_METALLICSPECGLOSSMAP");AssetDatabase.CreateAsset(m,Output+"/Materials/"+m.name+".mat");
 }

 public static object Run(){
  Need(Application.dataPath.Replace('\\','/').Equals("E:/UnityProjects/FPS_Template/Assets",StringComparison.OrdinalIgnoreCase),"Wrong Unity project");
  Need(!EditorApplication.isPlayingOrWillChangePlaymode&&!EditorApplication.isCompiling&&!Lightmapping.isRunning,"Editor occupied");
  var manifest=Json<Manifest>(Root+"PreparedR39Recovered/Manifest.json");Need(manifest.objects.Length==1,"Expected one source surface patch candidate");Need(!Directory.Exists(Output),"Destination exists; do not overwrite");
  var entries=manifest.objects;var data=entries.Select(Read).ToArray();foreach(var e in entries)foreach(var n in e.materials.Where(n=>!n.StartsWith("R39_")))Existing(n);
  string beforeScene=Sha(Formal),beforeState=Scenes();var formal=SceneManager.GetSceneByPath(Formal);bool dirty=formal.IsValid()&&formal.isDirty;
  Ensure(Output+"/Meshes");CreateMaterial(manifest.patch);
  for(int i=0;i<entries.Length;i++){
   var e=entries[i];var d=data[i];var mesh=new Mesh{name=e.key,indexFormat=IndexFormat.UInt32};mesh.vertices=d.positions;mesh.normals=d.normals;
   foreach(var uv in d.uv)mesh.SetUVs(uv.Key,uv.Value);mesh.subMeshCount=d.indices.Length;for(int j=0;j<d.indices.Length;j++)mesh.SetTriangles(d.indices[j],j,false);
   Need(mesh.uv.SequenceEqual(d.tangentUV),"Tangent UV basis differs");mesh.RecalculateTangents();mesh.RecalculateBounds();AssetDatabase.CreateAsset(mesh,Output+"/Meshes/"+e.key+".asset");
   var loaded=AssetDatabase.LoadAssetAtPath<Mesh>(Output+"/Meshes/"+e.key+".asset");Need(loaded&&loaded.vertexCount==e.vertices&&loaded.subMeshCount==e.materials.Length&&loaded.tangents.Length==e.vertices&&loaded.uv2.Length==0,"Imported mesh invalid");
   foreach(var uv in d.uv){var values=new List<Vector2>();loaded.GetUVs(uv.Key,values);Need(values.SequenceEqual(uv.Value),"Imported UV changed");}
  }
  Need(Sha(Formal)==beforeScene&&Scenes()==beforeState,"Loaded scene state changed during independent import");
  var result=new{exportedSourceSurfacePatches=1,importedIndependentMeshCandidates=1,newMaterials=1,newTextures=4,sceneBoundPatches=0,nativeCandidateRequiresExplicitExistingTransformMappingBeforeBinding=true,formalSceneWasDirty=dirty,formalSceneDirtyPreserved=true,formalSceneFileUnchanged=true,sceneSaveReloadCalls=0,meshRootOffsetForFutureBinding=new[]{2015,0,2130},lightmapChannelReserved=1,sourceUVChannels=new[]{0},assets=entries.Select(e=>new{e.name,mesh=Output+"/Meshes/"+e.key+".asset",materials=e.materials.Select(n=>AssetDatabase.GetAssetPath(Existing(n))).ToArray()}).ToArray()};
  File.WriteAllText(Root+"R39IndependentUnityAssets.json",Serialize(result));return result;
 }
}
