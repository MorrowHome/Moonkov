using System;
using System.Reflection;
using Unity.MP_FPS;
using Unity.MP_FPS.Client;
using Unity.MP_FPS.Inventory;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

public sealed class RecoveryInboxPreviewChecks : EditorWindow
{
    private StashScreen m_Screen;
    private VisualElement m_Panel;
    public static void Open()
    {
        if(EditorApplication.isPlayingOrWillChangePlaymode)throw new InvalidOperationException("Run in Edit Mode.");
        var window=CreateInstance<RecoveryInboxPreviewChecks>();window.titleContent=new GUIContent("Recovery UI check");
        window.position=new Rect(80,80,480,350);window.Show();
        try
        {
            var host=AssetDatabase.LoadAssetAtPath<VisualTreeAsset>("Assets/UI Toolkit/GameUI/StashScreen.uxml").CloneTree();
            window.m_Screen=new StashScreen(host,()=>{},()=>{},()=>{},true);
            var inbox=new RecoveryInbox();var batch=InventoryGraph.Create(false);batch.AddSupply("medkit",2,batch.Equipped("Backpack").Id);inbox.Batches.Add(batch);
            typeof(StashScreen).GetMethod("PresentRecovery",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(window.m_Screen,new object[]{inbox,false});
            var inventory=host.Q("containerInventory");
            for(int i=0;i<inventory.styleSheets.count;i++)window.rootVisualElement.styleSheets.Add(inventory.styleSheets[i]);
            foreach(var className in inventory.GetClasses())window.rootVisualElement.AddToClassList(className);
            window.m_Panel=host.Q("stashRecovery");window.m_Panel.RemoveFromHierarchy();window.rootVisualElement.Add(window.m_Panel);
            MoonkovLocalization.Bind(window.rootVisualElement);
            window.Repaint();
        }
        catch {window.Close();throw;}
    }
    public static string Verify()
    {
        var window=GetWindow<RecoveryInboxPreviewChecks>();var panel=window.m_Panel;
        if(panel==null || panel.worldBound.width<100)throw new InvalidOperationException("Recovery panel has not laid out.");
        foreach(var button in panel.Query<Button>().ToList())
            if(button.layout.width>0 && (button.worldBound.x<panel.worldBound.x-1 || button.worldBound.xMax>panel.worldBound.xMax+1))
                throw new InvalidOperationException("Recovery button overflows narrow panel.");
        var list=panel.Q<ListView>();
        if(list.itemsSource.Count!=5 || panel.ElementAt(0).resolvedStyle.flexDirection!=FlexDirection.Column)
            throw new InvalidOperationException("Recovery list or narrow-header layout differs.");
        return "Recovery UI narrow check passed: width="+panel.worldBound.width+", roots="+list.itemsSource.Count+", stacked header, no horizontal button overflow.";
    }
    public static void Finish() {foreach(var window in Resources.FindObjectsOfTypeAll<RecoveryInboxPreviewChecks>())window.Close();}
    private void OnDisable() {m_Screen?.Dispose();m_Screen=null;}
}
