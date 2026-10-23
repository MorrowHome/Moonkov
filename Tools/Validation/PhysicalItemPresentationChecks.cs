using System;
using System.IO;
using UnityEngine;
using UnityEditor;
using Unity.MP_FPS;
using Unity.MP_FPS.Client;
using Unity.MP_FPS.Inventory;
using UnityEngine.UIElements;

public static class PhysicalItemPresentationChecks
{
    public static string Run()
    {
        if(EditorApplication.isPlaying)throw new InvalidOperationException("Edit Mode required");
        int covered=0;
        foreach(var d in InventoryCatalog.Definitions.Values)
        {
            if(!PhysicalItemVisuals.IsPhysical(d.Code))continue;
            var icon=new StashItemArt(d.Code);
            if(icon.Q<Image>()?.image==null)throw new InvalidOperationException("No inventory image for "+d.Code);
            var go=PhysicalItemVisuals.Create(d.Code,null);
            try
            {
                var r=go.GetComponentInChildren<Renderer>();
                if(r.bounds.min.y < -.002f || r.bounds.min.y > .002f)throw new InvalidOperationException("Ground pivot: "+d.Code+" / "+r.bounds);
                if(r.bounds.size.magnitude<.01f || r.bounds.size.magnitude>2)throw new InvalidOperationException("Metre scale: "+d.Code);
            }
            finally{UnityEngine.Object.DestroyImmediate(go);}
            covered++;
        }
        Directory.CreateDirectory("Docs/Art/UnityPreviews");
        foreach(string code in new[]{"oxygen_reserve","medkit","copper_coil","field_rig","energy_head_dome","alien_neural_ganglion","black_box","cargo_pack"})
            Render(code);
        return covered+" inventory icons and grounded prefabs passed; 8 Unity preview renders saved.";
    }
    private static void Render(string code)
    {
        var preview=new PreviewRenderUtility();
        Texture2D image=null;
        try
        {
            var go=PhysicalItemVisuals.Create(code,null);preview.AddSingleGO(go);
            var bounds=go.GetComponentInChildren<Renderer>().bounds;
            preview.camera.transform.position=bounds.center+new Vector3(.8f,.6f,1.5f).normalized+bounds.size.y*Vector3.up*.6f;
            preview.camera.transform.LookAt(bounds.center);
            preview.camera.orthographic=true;preview.camera.orthographicSize=Mathf.Max(bounds.size.x,bounds.size.y,bounds.size.z)*.72f;
            preview.camera.nearClipPlane=.01f;preview.camera.farClipPlane=10;
            preview.camera.clearFlags=CameraClearFlags.SolidColor;preview.camera.backgroundColor=new Color(.09f,.115f,.14f);
            preview.lights[0].intensity=1.4f;preview.lights[0].transform.rotation=Quaternion.Euler(40,35,0);
            preview.lights[1].intensity=1.0f;preview.lights[1].transform.rotation=Quaternion.Euler(325,215,0);
            preview.ambientColor=new Color(.35f,.35f,.35f);
            preview.BeginStaticPreview(new Rect(0,0,512,512));preview.Render(true);image=preview.EndStaticPreview();
            File.WriteAllBytes("Docs/Art/UnityPreviews/"+code+".png",image.EncodeToPNG());
        }
        finally{if(image)UnityEngine.Object.DestroyImmediate(image);preview.Cleanup();}
    }
}
