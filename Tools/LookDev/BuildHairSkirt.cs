using System.IO;
using UnityEditor;
using UnityEngine;
public static class BuildHairSkirt {
 public const string Before=MoonkovFullBodyLookDev.Root+"/HairSkirtBefore";
 // Creates the pre-refinement snapshot only once. Run before first Apply.
 public static string Main(){
 if(!AssetDatabase.IsValidFolder(Before))AssetDatabase.CreateFolder(MoonkovFullBodyLookDev.Root,"HairSkirtBefore");
 foreach(string n in MoonkovFullBodyLookDev.Names)if(!File.Exists(Before+"/"+n+".mat"))AssetDatabase.CopyAsset(MoonkovLilToonLookDev.Source+n+".mat",Before+"/"+n+".mat");
 foreach(string a in new[]{"yifuRim","yifu2Rim"})if(!File.Exists(Before+"/"+a+".png"))AssetDatabase.CopyAsset(MoonkovFullBodyLookDev.Root+"/Maps/"+a+".png",Before+"/"+a+".png");
 foreach(string n in new[]{"Dress","InnerDress","DressDetail"}){
 var m=AssetDatabase.LoadAssetAtPath<Material>(Before+"/"+n+".mat");
 string a=n=="Dress"?"yifuRim":"yifu2Rim";
 m.SetTexture("_RimColorTex",AssetDatabase.LoadAssetAtPath<Texture2D>(Before+"/"+a+".png"));EditorUtility.SetDirty(m);AssetDatabase.SaveAssetIfDirty(m);
 }
 MoonkovFullBodyLookDev.Build();return "Previous game materials frozen; widened hair/skirt candidates built";
 }
}
