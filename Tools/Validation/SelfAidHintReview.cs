using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Threading.Tasks;
using Unity.MP_FPS;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Localization.Settings;
using UnityEngine.Localization.Tables;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;

public sealed class SelfAidHintReview : EditorWindow
{
    public static async Task<object> Run()
    {
        if(EditorApplication.isPlayingOrWillChangePlaymode)throw new InvalidOperationException("Use the existing idle Editor.");
        var failures=new List<string>();int checks=0;
        void Check(bool value,string label){checks++;if(!value)failures.Add(label);}
        var scene=EditorSceneManager.NewPreviewScene();var window=CreateInstance<SelfAidHintReview>();
        var oldLocale=await LocalizationSettings.SelectedLocaleAsync.Task;
        try
        {
            var go=new GameObject("Read-only HUD fixture");go.SetActive(false);SceneManager.MoveGameObjectToScene(go,scene);var hud=go.AddComponent<RaidHUD>();
            var hidden=BindingFlags.Instance|BindingFlags.NonPublic;
            void Set(string name,object value)=>typeof(RaidHUD).GetField(name,hidden).SetValue(hud,value);
            void Present(float bleeding,bool visible)=>typeof(RaidHUD).GetMethod("PresentSelfAidHint",hidden).Invoke(hud,new object[]{new TacticalItemOwnerRpc{BleedSeconds=bleeding},visible});
            window.titleContent=new GUIContent("Moonkov / Bleeding hint");window.position=new Rect(70,60,800,600);window.ShowUtility();window.Focus();
            var root=window.rootVisualElement;root.style.backgroundColor=new Color(.11f,.13f,.16f);
            AssetDatabase.LoadAssetAtPath<VisualTreeAsset>("Assets/Resources/Moonkov/RaidUI.uxml").CloneTree(root);
            var hint=root.Q<Label>("raidSelfAidHint");Set("m_Root",root);Set("m_SelfAidHint",hint);
            var telemetry=root.Q<Label>("raidTelemetry");telemetry.text="60 FPS  |  Ping 40 ms\nLoss 0.0%  |  Connection P2P";telemetry.style.display=DisplayStyle.Flex;
            Present(0,true);Check(hint.style.display==DisplayStyle.None,"No bleeding hides the prompt");
            Present(3,false);Check(hint.style.display==DisplayStyle.None,"Inactive/menu/action gating hides the prompt");
            foreach(string locale in new[]{"zh-Hans","en"})
            {
                LocalizationSettings.SelectedLocale=LocalizationSettings.AvailableLocales.GetLocale(locale);
                await LocalizationSettings.StringDatabase.GetTableAsync(MoonkovLocalization.Strings).Task;
                Set("m_SelfAidKey",null);Present(3,true);await Task.Delay(250);
                var table=AssetDatabase.LoadAssetAtPath<StringTable>("Assets/Localization/Tables/MoonkovUI_"+locale+".asset");
                Check(hint.text==string.Format(table.GetEntry("Hold {0} to stop bleeding").LocalizedValue,"F"),"Actual bound prompt localizes: "+locale);
                foreach(int width in new[]{800,1280})
                {
                    window.position=new Rect(70,60,width,600);window.Repaint();await Task.Delay(350);Present(3,true);await Task.Delay(150);
                    var textSize=hint.MeasureTextSize(hint.text,0,VisualElement.MeasureMode.Undefined,0,VisualElement.MeasureMode.Undefined);
                    Check(hint.resolvedStyle.display==DisplayStyle.Flex && hint.resolvedStyle.fontSize>=14 && hint.worldBound.width>=textSize.x &&
                        root.worldBound.Contains(hint.worldBound.min) && root.worldBound.Contains(hint.worldBound.max),"Prompt remains readable and inside viewport: "+locale+width);
                    Check(!hint.worldBound.Overlaps(telemetry.worldBound),"Prompt does not cover telemetry: "+locale+width);
                }
                if(locale=="zh-Hans")
                {
                    if(!UnityEditorInternal.InternalEditorUtility.isApplicationActive || focusedWindow!=window)
                        throw new InvalidOperationException("The native review must be in front before capturing its pixels.");
                    var rect=window.position;rect.position*=EditorGUIUtility.pixelsPerPoint;rect.size*=EditorGUIUtility.pixelsPerPoint;
                    var image=new Texture2D((int)rect.width,(int)rect.height,TextureFormat.RGB24,false);
                    try{image.SetPixels(UnityEditorInternal.InternalEditorUtility.ReadScreenPixel(rect.position,image.width,image.height));image.Apply();File.WriteAllBytes("LocalData/ActionReview20261108/bleeding-hint.png",image.EncodeToPNG());}
                    finally{DestroyImmediate(image);}
                }
            }
            Present(0,true);Check(hint.style.display==DisplayStyle.None,"Ending bleeding clears the persistent hint");
        }
        finally{LocalizationSettings.SelectedLocale=oldLocale;window.Close();EditorSceneManager.ClosePreviewScene(scene);}
        return new{passed=failures.Count==0,checks,failures,scope="Actual HUD presenter and native UI Toolkit layouts, two languages; no Play or transport."};
    }
}
