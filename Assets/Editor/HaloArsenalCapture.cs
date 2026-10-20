using System;
using System.Collections;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using Unity.MP_FPS;
using Unity.MP_FPS.DollSinger;

/// <summary>Four actual RenderTextures, frame-locked at 24fps for external lossless-to-MP4 encoding.</summary>
public static class HaloArsenalCapture
{
    private static uint selected;
    public static void RunWeapon(uint id) { selected=id;Start(); }
    public static string Status {get;private set;}="Not run";
    [MenuItem("Tools/Moonkov/Capture Six Arsenal Action Clips")]
    public static void Run() { selected=0;Start(); }
    private static void Start()
    {
        var lab=UnityEngine.Object.FindAnyObjectByType<WeaponGestureLab>();
        if(!EditorApplication.isPlaying||!lab)throw new InvalidOperationException("Play the weapon gesture lab first.");
        if(Status=="Running"||BlenderGestureValidation.Status=="Running")throw new InvalidOperationException("Finish the current capture/validation first.");
        Status="Running";lab.StartCoroutine(Guard(Capture(lab)));
    }
    private static IEnumerator Guard(IEnumerator routine)
    {
        using(routine as IDisposable)
        while(true)
        {
            object value;
            try{if(!routine.MoveNext())yield break;value=routine.Current;}
            catch(Exception e){Status="FAILED: "+e;Debug.LogException(e);yield break;}
            yield return value;
        }
    }
    private static IEnumerator Capture(WeaponGestureLab lab)
    {
        float step=Time.captureDeltaTime;
        var output=new Texture2D(1280,720,TextureFormat.RGB24,false);var small=new RenderTexture(640,360,0);
        var ui=UnityEngine.Object.FindAnyObjectByType<UIDocument>().rootVisualElement;
        var cameras=new[]{lab.PlayerView.camera}.Concat(lab.Observers).ToArray();
        try
        {
            lab.SetPaused(false);ui.Q<Slider>("speed").value=1;ui.Q<Slider>("pitch").value=0;ui.Q<Slider>("orbit").value=0;
            ui.Q<Toggle>("aim").value=true;ui.Q<Toggle>("firstPerson").value=true;ui.Q<Toggle>("repeatFire").value=false;
            lab.PlayerView.camera.aspect=16f/9;lab.FocusView(-1);Time.captureDeltaTime=1f/24;
            foreach(var entry in HaloArsenal.Entries.Where(e=>selected==0||e.Id==selected))
            {
                lab.SelectWeapon((int)entry.Id-2);yield return new WaitForSeconds(.5f);
                string path="Temp/HaloArsenal/Clips/"+entry.Code;Directory.CreateDirectory(path);foreach(var previousFrame in Directory.GetFiles(path,"*.png"))File.Delete(previousFrame);
                var weapon=AssetDatabase.LoadAssetAtPath<WeaponRegistry>("Assets/Data/Weapons/WeaponRegistry.asset").GetWeaponData(entry.Id);
                int total=Mathf.CeilToInt((1.25f+weapon.ReloadTime+.4f)*24);
                for(int frame=0;frame<total;frame++)
                {
                    if(frame==12||frame==18&&weapon.Automatic)lab.Fire();
                    if(frame==30)lab.Reload();
                    yield return new WaitForEndOfFrame();
                    var previous=RenderTexture.active;
                    for(int view=0;view<4;view++)
                    {
                        Graphics.Blit(cameras[view].targetTexture,small);RenderTexture.active=small;
                        output.ReadPixels(new Rect(0,0,640,360),(view%2)*640,(1-view/2)*360,false);
                    }
                    output.Apply(false);RenderTexture.active=previous;
                    File.WriteAllBytes(path+"/"+frame.ToString("D4")+".png",output.EncodeToPNG());
                }
                uint lighting=lab.Halo.LightingSettings;
                lab.Halo.LightingSettings=HaloLighting.Pack(lab.Halo.lightRange,0,0);
                yield return new WaitForEndOfFrame();yield return new WaitForEndOfFrame();
                BlenderGestureValidation.SaveViews(lab,entry.Code+"-unlit");
                lab.Halo.LightingSettings=lighting;
            }
            Status="PASS";
        }
        finally
        {Time.captureDeltaTime=step;UnityEngine.Object.Destroy(output);small.Release();UnityEngine.Object.Destroy(small);}
    }
}
