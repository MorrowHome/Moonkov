using System;
using System.Reflection;
using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine.SceneManagement;
using Unity.MP_FPS.Moon;
using Unity.MP_FPS.DollSinger;
using Object=UnityEngine.Object;
public static class MoonkovPresentationBudgetChecks
{
 [MenuItem("Tools/Moonkov/Performance/Check presentation distance budgets")]
 public static void RunMenu() => Debug.Log(Run());
 public static string Run()
 {
  if(EditorApplication.isPlaying)throw new InvalidOperationException("Use the Edit Mode fixture.");
  var original=SceneManager.GetActiveScene();var fixture=EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Additive);
  int passed=0;
  try{
   SceneManager.SetActiveScene(fixture);
   var viewer=new GameObject("Fixture camera").AddComponent<Camera>();viewer.tag="MainCamera";
   var light=new GameObject("Standalone light").AddComponent<Light>();light.range=10;light.intensity=3;light.shadows=LightShadows.Soft;
   var sun=new GameObject("Directional light").AddComponent<Light>();sun.type=LightType.Directional;
   var unsafeLight=GameObject.CreatePrimitive(PrimitiveType.Cube).AddComponent<Light>();
   var inactive=new GameObject("Authored inactive light").AddComponent<Light>();inactive.gameObject.SetActive(false);
   var gate=new GameObject("Fixture light gate").AddComponent<LocalLightVisibility>();
   gate.Configure(new[]{light,light,sun,unsafeLight,inactive});Call(gate,"Initialize");
   viewer.transform.position=Vector3.right*200;Call(gate,"RefreshVisibility",viewer);
   Check(!light.gameObject.activeSelf,"Far light must be culled.");
   Check(sun.gameObject.activeSelf&&unsafeLight.gameObject.activeSelf&&!inactive.gameObject.activeSelf,"Skip directional, gameplay mesh and authored inactive objects.");
   Check(light.enabled&&light.intensity==3&&light.shadows==LightShadows.Soft,"Preserve authored light properties.");
   viewer.transform.position=Vector3.right*140;Call(gate,"RefreshVisibility",viewer);Check(!light.gameObject.activeSelf,"Remain culled in the hysteresis band.");
   light.enabled=false;viewer.transform.position=Vector3.right*120;Call(gate,"RefreshVisibility",viewer);
   Check(light.gameObject.activeSelf&&!light.enabled,"A power-off command during culling must persist when returning.");
   viewer.transform.position=Vector3.right*140;Call(gate,"RefreshVisibility",viewer);Check(light.gameObject.activeSelf,"Visible light must remain visible in the hysteresis band.");
   viewer.transform.position=Vector3.right*200;Call(gate,"RefreshVisibility",viewer);Call(gate,"RefreshVisibility",new object[]{null});
   Check(light.gameObject.activeSelf&&!light.enabled,"A missing camera must restore visibility without restoring power.");
   Call(gate,"RefreshVisibility",viewer);Call(gate,"OnDisable");Check(light.gameObject.activeSelf&&!light.enabled,"Disabling the gate restores its leaves only.");
   Call(gate,"Initialize");viewer.fieldOfView=15;Call(gate,"RefreshVisibility",viewer);
   Check(light.gameObject.activeSelf,"Scoped views must retain distant lights.");viewer.fieldOfView=60;
   var spring=new GameObject("Fixture springs").AddComponent<SecondaryBoneSpring>();
   var hair=new GameObject("Hair root").transform;hair.SetParent(spring.transform,false);
   var tip=new GameObject("Hair tip").transform;tip.SetParent(hair,false);tip.localPosition=Vector3.up;
   spring.hairRoots=new[]{hair};Call(spring,"OnEnable");viewer.transform.position=Vector3.zero;
   spring.transform.position=Vector3.right*100;Step();Check(Suspended(),"Far secondary springs must suspend.");
   spring.transform.position=Vector3.right*70;Step();Check(Suspended(),"Spring hysteresis must retain suspension.");
   viewer.fieldOfView=15;Step();Check(!Suspended(),"Scoped views must resume visible distant springs.");viewer.fieldOfView=60;
   spring.transform.position=Vector3.right*50;Step();Check(!Suspended()&&float.IsFinite(hair.rotation.x),"Near springs must resume with a finite pose after travel.");
   spring.simulationDistance=0;spring.transform.position=Vector3.right*500;Step();Check(!Suspended(),"Zero distance opts out of cosmetic culling.");
   return "Presentation distance budgets: PASS ("+passed+" assertions; light power/geometry safety, hysteresis, restoration, spring resumption and opt-out).";
   void Step(){spring.GetType().GetField("m_NextDistanceCheck",BindingFlags.NonPublic|BindingFlags.Instance).SetValue(spring,0f);Call(spring,"LateUpdate");}
   bool Suspended(){return (bool)spring.GetType().GetField("m_DistanceSuspended",BindingFlags.NonPublic|BindingFlags.Instance).GetValue(spring);}
   void Check(bool condition,string message){if(!condition)throw new InvalidOperationException(message);passed++;}
  }finally{SceneManager.SetActiveScene(original);EditorSceneManager.CloseScene(fixture,true);}
 }
 static void Call(object target,string name,params object[] args){target.GetType().GetMethod(name,BindingFlags.Instance|BindingFlags.NonPublic).Invoke(target,args);}
}
