using System;
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.Localization;
using UnityEngine.Localization.Settings;
using UnityEngine.UIElements;
using Unity.MP_FPS.Client;
using Unity.MP_FPS.Inventory;

// One native Editor panel, with isolated inventory and no profile/network writes.
public sealed class MooncoreResearchReview : EditorWindow
{
    private Locale m_PreviousLocale;
    private double m_Ready;
    public static string Status { get; private set; } = "Not started";
    [MenuItem("Tools/Moonkov/Mooncore/Review Research Layout")]
    public static void Start()
    {
        if(EditorApplication.isPlayingOrWillChangePlaymode)throw new InvalidOperationException("Requires idle Editor.");
        var window=CreateInstance<MooncoreResearchReview>();
        window.titleContent=new GUIContent("Mooncore / Native UI review");
        window.position=new Rect(40,70,1440,800);window.minSize=new Vector2(1100,700);
        window.ShowUtility();window.Focus();
        window.m_Ready=EditorApplication.timeSinceStartup+1.2;
        EditorApplication.update+=window.Capture;Status="Rendering";
    }
    public void CreateGUI()
    {
        m_PreviousLocale=LocalizationSettings.SelectedLocale;
        LocalizationSettings.SelectedLocale=LocalizationSettings.AvailableLocales.GetLocale("zh-Hans");
        var root=rootVisualElement;
        root.AddToClassList("stash-root");root.AddToClassList("moonkov-terminal");root.AddToClassList("archive-ui");
        root.style.flexGrow=1;root.style.paddingLeft=20;root.style.paddingRight=20;root.style.paddingTop=20;
        root.styleSheets.Add(AssetDatabase.LoadAssetAtPath<StyleSheet>("Assets/UI Toolkit/GameUI/MoonkovTerminal.uss"));
        root.styleSheets.Add(Resources.Load<StyleSheet>("Moonkov/UI/ApprovedPages"));
        root.Add(new Label("月核研究 / 原生界面示例库存 · 不会写入玩家档案") {style={fontSize=18,marginBottom=16}});
        var graph=InventoryGraph.Create(starter:false);
        graph.Items.Add(new InventoryItem {Id=Guid.NewGuid().ToString("D"),Code="mooncore_fragment",Parent="stash",Region="main",FoundInRaid=true,MissionRecovered=true,
            MissionWorldId=Guid.NewGuid().ToString("D"),MissionOperationId=Guid.NewGuid().ToString("D"),MissionContentVersion=1});
        graph.AddSupply("circuit_board",1,"stash");
        var view=new MoonkovResearchView(null,()=>{});root.Add(view.Root);view.Present(graph,true,false,false);
        var font=AssetDatabase.LoadAssetAtPath<UnityEngine.TextCore.Text.FontAsset>("Assets/Localization/Fonts/NotoSansCJKsc.asset");
        root.Query<TextElement>().ForEach(t=>{t.style.unityFontDefinition=FontDefinition.FromSDFFont(font);t.style.letterSpacing=0;});
    }
    private void Capture()
    {
        Repaint();if(EditorApplication.timeSinceStartup<m_Ready)return;
        EditorApplication.update-=Capture;
        if(focusedWindow!=this || !UnityEditorInternal.InternalEditorUtility.isApplicationActive)
        {Status="Review window ready; capture skipped because Unity is not foreground.";return;}
        var texture=new Texture2D((int)position.width,(int)position.height,TextureFormat.RGB24,false);
        try
        {
            texture.SetPixels(UnityEditorInternal.InternalEditorUtility.ReadScreenPixel(position.position,texture.width,texture.height));texture.Apply();
            string directory=Path.GetFullPath(Path.Combine(Application.dataPath,"../Library/BuildValidation/Mooncore"));Directory.CreateDirectory(directory);
            File.WriteAllBytes(Path.Combine(directory,"research-zh.png"),texture.EncodeToPNG());
            Status="Rendered research-zh.png";Debug.Log(Status);Close();
        }
        finally{DestroyImmediate(texture);}
    }
    private void OnDisable()
    {
        EditorApplication.update-=Capture;
        if(m_PreviousLocale!=null)LocalizationSettings.SelectedLocale=m_PreviousLocale;
    }
}
