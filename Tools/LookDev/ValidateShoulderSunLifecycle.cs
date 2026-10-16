using System;
using System.Linq;
using UnityEngine;
using UnityEditor;
using UnityEngine.Rendering;
using Unity.MP_FPS;
using Unity.MP_FPS.Moon;
public static class ValidateShoulderSunLifecycle {
 public static object Main(){
 var lights=UnityEngine.Object.FindObjectsByType<MoonSceneLighting>(FindObjectsSortMode.None).Where(c=>c.enabled).ToArray();
 var rows=lights.Select(c=>{var serialized=new SerializedObject(c);var sun=serialized.FindProperty("sun").objectReferenceValue as Light;if(!sun)throw new Exception("No sun");var pipeline=QualitySettings.renderPipeline;float applied=sun.shadowNearPlane;c.enabled=false;float restored=sun.shadowNearPlane;string restoredPipe=QualitySettings.renderPipeline?QualitySettings.renderPipeline.name:"null";c.enabled=true;float reapplied=sun.shadowNearPlane;if(applied<16||reapplied<16)throw new Exception("Sun near plane was not applied");return new {component=c.name,applied,restored,reapplied,restoredPipe,activePipeline=QualitySettings.renderPipeline?QualitySettings.renderPipeline.name:"null"};}).ToArray();
 if(rows.Length==0)throw new Exception("No enabled MoonSceneLighting in game");
 return new {pass=true,state=GameSettings.Instance.GameState.ToString(),rows,sceneCount=UnityEngine.SceneManagement.SceneManager.sceneCount,characterCount=UnityEngine.Object.FindObjectsByType<SkinnedMeshRenderer>(FindObjectsSortMode.None).Count(r=>r.name=="DollSinger_Body")};
 }
}
