using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.Serialization;
using Unity.Collections;
using Unity.Entities;
using Unity.NetCode;
using Unity.Mathematics;
using UnityEngine.InputSystem.LowLevel;
using Unity.MP_FPS;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

// Native UI fixture only. No account, scene, game World, transport or Play Mode.
public sealed class RaidChatReview : EditorWindow
{
    private RaidChatView m_View;
    private RaidChatClientState m_State;
    private VisualElement m_Surface;
    private int m_Sent, m_Cancelled;
    private static string Output => Path.GetFullPath("LocalData/RaidChatReview");
    [MenuItem("Tools/Moonkov/Review Raid Chat")]
    public static void Open() => ShowAt(1280,720);
    public static void ShowAt(int width,int height)
    {
        var window=GetWindow<RaidChatReview>();
        window.titleContent=new GUIContent("Raid Chat Review");
        window.minSize=new Vector2(640,420); window.maxSize=new Vector2(2400,1400);
        window.ShowUtility(); window.position=new Rect(20,50,width,height);window.Focus();window.Build();
    }
    private void Build()
    {
        m_View?.Dispose();rootVisualElement.Clear();
        rootVisualElement.style.backgroundColor=new Color(.07f,.075f,.09f);
        m_Surface=new VisualElement{name="chatReviewSurface"};m_Surface.style.flexGrow=1;
        rootVisualElement.Add(m_Surface);
        AssetDatabase.LoadAssetAtPath<VisualTreeAsset>("Assets/UI Toolkit/GameUI/PlayerHUD.uxml").CloneTree(m_Surface);
        Resources.Load<VisualTreeAsset>("Moonkov/RaidUI").CloneTree(m_Surface);
        m_State=new RaidChatClientState();m_State.Receive(new RaidChatResultRpc{Session=1,TeamAvailable=true});
        for(uint i=1;i<=100;i++)m_State.Receive(new RaidChatMessageRpc{Session=1,Sequence=i,Channel=i%3==0?RaidChatChannel.Global:RaidChatChannel.Team,
            Name=i%3==0?"长昵称测试_月面勘探队员":"月球队友",Dead=i%4==0,
            Text=i==100?"入口已经清理。先去配电间，等所有人到齐后再启动取样机。":i==99?"<b>这段标签会原样显示</b>":"收到，沿原路撤离。"});
        m_View=new RaidChatView(m_Surface,Send,Cancel);
        MoonkovLocalization.Bind(m_Surface);
        m_View.Present(m_State,true);m_View.SetOpen(true);
        m_View.Input.value="等我一下，我在楼梯口。";
        rootVisualElement.schedule.Execute(()=>CheckLayout()).StartingIn(800);
    }
    private void Send()
    {
        m_Sent++;
        var result=RaidChatRules.Normalize(m_State.Draft,out string text);
        if(result!=RaidChatResult.Accepted){m_State.Feedback=result;m_State.Revision++;return;}
        m_State.PendingRequest=1;
        m_State.Receive(new RaidChatResultRpc{Session=1,RequestId=1,Result=RaidChatResult.Accepted});
        m_State.Receive(new RaidChatMessageRpc{Session=1,Sequence=m_State.LastSequence+1,Name="你",Text=text,Channel=m_State.Channel});
        m_View.SetOpen(false);m_View.Present(m_State,true);
    }
    private void Cancel(){m_Cancelled++;m_View.SetOpen(false);m_View.Present(m_State,true);}
    public static void SetState(string state)
    {
        var w=GetWindow<RaidChatReview>();
        w.m_View.Present(w.m_State,true);
        if(state=="compact"){w.m_State.Receive(new RaidChatMessageRpc{Session=1,Sequence=w.m_State.LastSequence+1,Name="队友",Text="准备撤离。"});w.m_View.Present(w.m_State,true);w.m_View.SetOpen(false);}
        else if(state=="open")w.m_View.SetOpen(true);
        else if(state=="light")w.rootVisualElement.style.backgroundColor=new Color(.78f,.78f,.75f);
        else if(state=="dark")w.rootVisualElement.style.backgroundColor=new Color(.07f,.075f,.09f);
        else if(state=="no-team"){w.m_State.TeamAvailable=false;w.m_State.Revision++;w.m_View.Present(w.m_State,true);}
        else if(state=="long")
        {w.m_State.Receive(new RaidChatMessageRpc{Session=1,Sequence=w.m_State.LastSequence+1,Channel=RaidChatChannel.Team,Name="月面勘探队员长昵称",Text=new string('月',120)});w.m_View.Present(w.m_State,true);}
        w.Repaint();
    }
    public static object CheckLayout()
    {
        var w=GetWindow<RaidChatReview>();
        var panel=w.rootVisualElement.panel.visualTree.worldBound;
        var chat=w.m_View.Element.worldBound;
        if(chat.width<200||chat.xMin<0||chat.xMax>panel.xMax||chat.yMin<0||chat.yMax>panel.yMax)
            throw new InvalidOperationException("Chat exceeds native panel: "+panel+" / "+chat);
        foreach(var row in w.m_View.Element.Query<Label>(className:"chat-row").ToList())
            if(row.enableRichText||row.worldBound.width>chat.width)throw new InvalidOperationException("Chat row has rich text or overflows.");
        var result=new {window=w.position.ToString(),panel=panel.ToString(),chat=chat.ToString(),rows=w.m_State.Messages.Count,sent=w.m_Sent,cancelled=w.m_Cancelled};
        Directory.CreateDirectory(Output);File.AppendAllText(Path.Combine(Output,"layout.txt"),JsonUtility.ToJson(new LayoutRecord{Window=w.position,Panel=panel,Chat=chat})+"\n");
        return result;
    }
    [Serializable] private struct LayoutRecord{public Rect Window,Panel,Chat;}
    public static object CheckEditing()
    {
        var w=GetWindow<RaidChatReview>();w.m_View.Present(w.m_State,true);w.m_View.SetOpen(true);
        w.m_View.Input.value="取消后保留草稿";
        using(var evt=KeyDownEvent.GetPooled('\0',KeyCode.Escape,EventModifiers.None))w.m_View.Input.SendEvent(evt);
        if(w.m_View.IsOpen||w.m_State.Draft!="取消后保留草稿")throw new InvalidOperationException("Escape did not preserve draft.");
        w.m_View.SetOpen(true);w.m_View.Input.value="";
        using(var evt=KeyDownEvent.GetPooled('\n',KeyCode.Return,EventModifiers.None))w.m_View.Input.SendEvent(evt);
        if(!w.m_View.IsOpen||w.m_State.Messages.Count!=100)throw new InvalidOperationException("Empty message sent.");
        w.m_View.Input.value="发送验证";
        using(var evt=KeyDownEvent.GetPooled('\n',KeyCode.Return,EventModifiers.None))w.m_View.Input.SendEvent(evt);
        if(w.m_View.IsOpen||w.m_State.Draft!=""||w.m_State.Messages.Last().Text.ToString()!="发送验证")throw new InvalidOperationException("Confirmed send failed.");
        w.m_View.SetOpen(true);return new{escape=true,empty=true,confirmedSend=true,draft=true};
    }
    public static object CheckInputIsolation()
    {
        var w=GetWindow<RaidChatReview>();
        const BindingFlags hidden=BindingFlags.Instance|BindingFlags.NonPublic;
        const BindingFlags statics=BindingFlags.Static|BindingFlags.NonPublic;
        var active=typeof(RaidHUD).GetField("s_Active",statics);
        var settings=typeof(GameSettings).GetField("<Instance>k__BackingField",statics);
        var previousActive=active.GetValue(null);var previousSettings=settings.GetValue(null);
        var scene=UnityEditor.SceneManagement.EditorSceneManager.NewPreviewScene();
        var holder=new GameObject("Isolated chat input check");
        UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(holder,scene);holder.SetActive(false);
        var hud=holder.AddComponent<RaidHUD>();
        try
        {
            settings.SetValue(null,FormatterServices.GetUninitializedObject(typeof(GameSettings)));
            typeof(RaidHUD).GetField("m_ChatView",hidden).SetValue(hud,w.m_View);
            active.SetValue(null,hud);
            w.m_View.SetOpen(false);
            using var world=new World("Chat input buffer check",WorldFlags.GameClient);
            var em=world.EntityManager;var time=em.CreateEntity(typeof(NetworkTime));
            em.SetComponentData(time,new NetworkTime{ServerTick=new NetworkTick(5)});
            var input=em.CreateEntity(typeof(ClientInput),typeof(ClientMovementInput));em.AddBuffer<ClientCommandInput>(input);
            var firing=new PlayerInput{MoveInput=new float2(1,1)};firing.SetFlag(PlayerInput.InputFlag.Shoot,true);
            em.SetComponentData(input,new ClientMovementInput{PlayerInput=firing});
            var sender=world.GetOrCreateSystemManaged<ClientInputSenderSystem>();sender.Update();
            w.m_View.SetOpen(true);
            if(!RaidHUD.GameplayActionsBlocked||!RaidHUD.ChatInputBlocked)throw new InvalidOperationException("Chat did not enter the shared gameplay blocker.");
            world.GetOrCreateSystemManaged<ClientInputReaderSystem>().Update();
            var neutral=em.GetComponentData<ClientMovementInput>(input).PlayerInput;
            if(neutral.Shoot||math.lengthsq(neutral.MoveInput)>0)throw new InvalidOperationException("Chat leaked player movement/fire.");
            sender.Update();
            em.GetBuffer<ClientCommandInput>(input).GetDataAtTick(new NetworkTick(5),out var command);
            if(command.PlayerInput.Shoot||math.lengthsq(command.PlayerInput.MoveInput)>0)throw new InvalidOperationException("Current-tick command retained fire.");
            em.SetComponentData(time,new NetworkTime{ServerTick=new NetworkTick(6)});sender.Update();
            em.GetBuffer<ClientCommandInput>(input).GetDataAtTick(new NetworkTick(6),out command);
            if(command.PlayerInput.Shoot)throw new InvalidOperationException("Buffered shot survived into next tick.");
            // Composition callbacks and native key events share the same view guard.
            var composition=typeof(RaidChatView).GetMethod("Composition",hidden);
            composition.Invoke(w.m_View,new object[]{new IMECompositionString("zhong")});
            int before=w.m_Sent;
            using(var evt=KeyDownEvent.GetPooled('\n',KeyCode.Return,EventModifiers.None))w.m_View.Input.SendEvent(evt);
            if(w.m_Sent!=before||!w.m_View.IsOpen)throw new InvalidOperationException("Composition Enter sent a message.");
            composition.Invoke(w.m_View,new object[]{new IMECompositionString("")});
            using(var evt=KeyDownEvent.GetPooled('\n',KeyCode.Return,EventModifiers.None))w.m_View.Input.SendEvent(evt);
            if(w.m_Sent!=before)throw new InvalidOperationException("Composition completion frame sent a message.");
            typeof(RaidChatView).GetField("m_ImeFrame",hidden).SetValue(w.m_View,-1);
            return new{sharedInputBlocker=true,movementAndFireCleared=true,currentTickCleared=true,nextTickCleared=true,compositionEnter=true,compositionCompletionFrame=true};
        }
        finally
        {
            active.SetValue(null,previousActive);settings.SetValue(null,previousSettings);
            typeof(RaidHUD).GetField("m_ChatView",hidden).SetValue(hud,null);
            DestroyImmediate(holder);UnityEditor.SceneManagement.EditorSceneManager.ClosePreviewScene(scene);w.m_View.SetOpen(true);
        }
    }
    private void OnDisable(){m_View?.Dispose();m_View=null;}
}
