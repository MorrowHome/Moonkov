using System;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.Localization.Settings;
using UnityEngine.UIElements;
using Unity.MP_FPS.Client;

public sealed class RaidTimedActionReview : EditorWindow
{
    private UnityEngine.Localization.Locale m_Previous;
    [MenuItem("Tools/Moonkov/Review Timed Actions")]
    public static void Open()
    {
        var w=GetWindow<RaidTimedActionReview>(); w.titleContent=new GUIContent("Timed action review");
        w.position=new Rect(40,70,980,610);w.ShowUtility();w.Focus();
    }
    public void CreateGUI()
    {
        m_Previous=LocalizationSettings.SelectedLocale;
        LocalizationSettings.InitializationOperation.WaitForCompletion();
        LocalizationSettings.SelectedLocale=LocalizationSettings.AvailableLocales.GetLocale("zh-Hans");
        var root=rootVisualElement;root.Clear();root.AddToClassList("raid-ui");
        root.styleSheets.Add(AssetDatabase.LoadAssetAtPath<StyleSheet>("Assets/Resources/Moonkov/RaidUI.uss"));
        root.style.flexDirection=FlexDirection.Row;
        float[] fractions={.25f,.5f,.75f};
        for(int i=0;i<fractions.Length;i++)
        {
            var panel=new VisualElement();panel.style.width=Length.Percent(100f/3);panel.style.height=Length.Percent(100);
            panel.style.backgroundColor=(Color)(i==1 ? new Color32(105,112,120,255) : new Color32(23,28,35,255));root.Add(panel);
            var caption=new Label((fractions[i]*100)+"%");caption.style.color=Color.white;caption.style.fontSize=12;
            caption.style.marginLeft=20;caption.style.marginTop=20;panel.Add(caption);
            var cross=new Image {image=AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/AddressableAssetsData/Art/Textures/reticle_cross.png")};
            cross.style.position=Position.Absolute;cross.style.left=Length.Percent(50);cross.style.top=Length.Percent(50);
            cross.style.marginLeft=-40;cross.style.marginTop=-40;cross.style.width=cross.style.height=80;panel.Add(cross);
            var view=new RaidTimedActionView(()=>{});panel.Add(view);
            view.PresentProgress("TREATING",fractions[i],3*(1-fractions[i]),true,true,false,false,false,true);
        }
        var font=AssetDatabase.LoadAssetAtPath<Font>("Assets/Localization/Fonts/NotoSansCJKsc-Regular.otf");
        if(font)root.Query<TextElement>().ForEach(t=>t.style.unityFontDefinition=FontDefinition.FromFont(font));
    }
    private void OnDisable()
    { rootVisualElement.Clear();if(m_Previous!=null)LocalizationSettings.SelectedLocale=m_Previous; }

    [MenuItem("Tools/Moonkov/Check Timed Action Feedback")]
    public static string Check()
    {
        int count=0;
        void Assert(bool value,string message){if(!value)throw new Exception(message);count++;}
        var request=new RaidActionReadout(false,false,2,3);
        Assert(request.Stage==RaidActionStage.Requesting && request.Progress==0,"Unacknowledged request cannot progress");
        var run=new RaidActionReadout(true,false,1.5f,3);
        Assert(run.Stage==RaidActionStage.Running && Mathf.Abs(run.Progress-.5f)<.001f,"Accepted action uses remaining time");
        var waiting=new RaidActionReadout(true,false,-1,3);
        Assert(waiting.Stage==RaidActionStage.Awaiting && waiting.Remaining==0,"Zero countdown waits for the server");
        Assert(new RaidActionReadout(true,true,1,3).Stage==RaidActionStage.Cancelling,"Cancel confirmation takes priority");
        Assert(new RaidActionReadout(true,false,9,3).Progress==0,"Corrected time cannot produce negative progress");
        Assert(float.IsFinite(new RaidActionReadout(true,false,float.NaN,0).Progress),"Invalid timer cannot corrupt layout");
        Assert(RaidActionReadout.Receipt("治疗完成。",false)=="TREATMENT COMPLETE","Treatment success comes from server receipt");
        Assert(RaidActionReadout.Receipt("搜索完成。",false)=="SEARCH COMPLETE","Search success comes from server receipt");
        Assert(RaidActionReadout.Receipt("治疗完成。",true)=="ACTION INTERRUPTED","Error cannot be styled as success");
        Assert(RaidActionReadout.Receipt("已取消操作；未消耗物品或获得收益。",false)=="ACTION CANCELLED","Cancellation is distinct from success");
        Assert(RaidActionReadout.Receipt("治疗中断：受到攻击。 未消耗医疗物品。",true)=="INTERRUPTED / HIT","Keep specific interruption reason");
        Assert(RaidActionReadout.Receipt(null,false)=="ACTION INTERRUPTED","Missing receipt cannot fabricate completion");
        var artwork=Resources.Load<HaloProgressArtwork>("Moonkov/UI/HaloProgressArtwork");
        Assert(artwork.Petal==AssetDatabase.LoadAssetAtPath<Sprite>("Assets/DollSinger/Art/Rifle/Petal.png") &&
            artwork.InnerArc==AssetDatabase.LoadAssetAtPath<Sprite>("Assets/DollSinger/Art/Rifle/InnerArc.png"),"Use actual head halo artwork, not redrawn approximations");
        var ring=new HaloProgressRing();ring.SetProgress(.25f);Assert(ring.Progress==.25f,"Ring uses continuous progress");
        ring.SetProgress(1);Assert(ring.Progress==1,"Full ring is exact");
        ring.SetProgress(float.NaN);Assert(ring.Progress==0,"Invalid progress cannot appear complete");
        ring.SetProgress(-1);Assert(ring.Progress==0,"Negative progress clamps empty");
        int cancels=0;var view=new RaidTimedActionView(()=>cancels++);
        var window=CreateInstance<RaidTimedActionReview>();window.ShowUtility();window.rootVisualElement.Add(view);
        try
        {
        view.Present(true,false,false,true,3,3,null,false,true);
        Assert(view.Q<Label>("raidActionTime").style.display==DisplayStyle.None,"No invented countdown before acceptance");
        Assert(!view.Q<Label>("raidActionTitle").text.Contains("SERVER") && !view.Q<Label>("raidActionTitle").text.Contains("确认"),"Network state stays out of the player interface");
        var button=view.Q<Button>("raidActionCancel");
        using(var submit=NavigationSubmitEvent.GetPooled()){submit.target=button;button.SendEvent(submit);}
        Assert(cancels==1,"Cancel submit reaches original callback");
        view.Present(true,true,true,true,1,3,null,false,true);
        Assert(!button.enabledSelf,"Pending cancellation disables repeat submit");
        view.Present(false,true,false,true,0,3,"ACTION CANCELLED",false,true);
        Assert(button.style.display==DisplayStyle.None && view.Q<HaloProgressRing>().style.display==DisplayStyle.None,"Receipt has no cancel control or false full progress bar");
        view.Present(true,true,false,true,1,3,null,false,false);
        Assert(view.style.display==DisplayStyle.None,"Pause or inactive raid hides action controls");
        view.Present(false,false,false,false,0,0,null,false,true);
        Assert(view.style.display==DisplayStyle.None,"Empty state stays quiet");
        return $"PASS: {count} timed action feedback checks.";
        }
        finally {view.RemoveFromHierarchy();window.Close();}
    }
}
