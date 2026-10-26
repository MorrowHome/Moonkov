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
        root.style.flexDirection=FlexDirection.Row;root.style.backgroundColor=(Color)new Color32(16,21,25,255);
        var dark=Column(new Color32(16,21,25,255));var light=Column(new Color32(112,122,129,255));
        Card(dark,"治疗进行中",true,true,false,true,1.6f,3,null,false);
        Card(dark,"搜索进行中",true,true,false,false,4.3f,6,null,false);
        Card(dark,"治疗完成",false,true,false,true,0,3,"TREATMENT COMPLETE",false);
        Card(light,"治疗起手",true,false,false,true,3,3,null,false);
        Card(light,"搜索收尾",true,true,false,false,0,2,null,false);
        Card(light,"受击中断",false,true,false,true,0,3,"INTERRUPTED / HIT",true);
        var font=AssetDatabase.LoadAssetAtPath<Font>("Assets/Localization/Fonts/NotoSansCJKsc-Regular.otf");
        if(font)root.Query<TextElement>().ForEach(t=>t.style.unityFontDefinition=FontDefinition.FromFont(font));
    }
    private VisualElement Column(Color color)
    {
        var e=new VisualElement();e.style.width=Length.Percent(50);e.style.paddingLeft=36;e.style.paddingRight=36;
        e.style.backgroundColor=color;rootVisualElement.Add(e);return e;
    }
    private static void Card(VisualElement parent,string caption,bool active,bool accepted,bool cancel,bool medical,float left,float duration,string receipt,bool failed)
    {
        var c=new Label(caption);c.style.color=(Color)new Color32(196,225,255,255);c.style.fontSize=12;c.style.marginTop=36;parent.Add(c);
        var view=new RaidTimedActionView(()=>{});view.style.position=Position.Relative;view.style.left=0;view.style.bottom=0;
        view.style.marginLeft=0;view.style.marginTop=14;view.style.width=Length.Percent(100);parent.Add(view);
        view.Present(active,accepted,cancel,medical,left,duration,receipt,failed,true);
        c.style.marginBottom=0;
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
        Assert(button.style.display==DisplayStyle.None && view.Q(className:"raid-action-track").style.display==DisplayStyle.None,"Receipt has no cancel control or false full progress bar");
        view.Present(true,true,false,true,1,3,null,false,false);
        Assert(view.style.display==DisplayStyle.None,"Pause or inactive raid hides action controls");
        view.Present(false,false,false,false,0,0,null,false,true);
        Assert(view.style.display==DisplayStyle.None,"Empty state stays quiet");
        return $"PASS: {count} timed action feedback checks.";
        }
        finally {view.RemoveFromHierarchy();window.Close();}
    }
}
