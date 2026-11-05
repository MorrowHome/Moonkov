using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Unity.Collections;
using Unity.Entities;
using Unity.MP_FPS;
using Unity.MP_FPS.DollSinger;
using Unity.NetCode;
using UnityEditor;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

// Real HUD + InputSystem + network presentation in the existing disconnected UI fixture.
// Acknowledgements are injected explicitly; server behavior is tested separately by GrenadeHandlingChecks.
public static class GrenadeInputPlayChecks
{
    private const BindingFlags Hidden=BindingFlags.Instance|BindingFlags.NonPublic;
    private static int checks;
    private static void Check(bool ok,string label){if(!ok)throw new InvalidOperationException(label);checks++;}
    private static async Task Frames(int count=3)
    {
        int start=Time.frameCount;double end=EditorApplication.timeSinceStartup+8;
        while(Time.frameCount<start+count)
        {if(EditorApplication.timeSinceStartup>end)throw new InvalidOperationException("Game frames stopped advancing");await Task.Delay(15);}
    }
    private static async Task SettleHands()
    {float until=Time.time+.25f;do{await Frames();}while(Time.time<until);}
    public static async Task<string> Run()
    {
        Check(EditorApplication.isPlaying&&BountyClashUIFixture.ClientWorld?.IsCreated==true,"Use the disconnected UI fixture");
        checks=0;var world=BountyClashUIFixture.ClientWorld;var em=world.EntityManager;
        using var players=em.CreateEntityQuery(typeof(PredictedPlayerGhost),typeof(GhostOwnerIsLocal));
        Entity player;using(var owned=players.ToEntityArray(Allocator.Temp))
        {Check(owned.Length==1,"Exactly one local fixture character");player=owned[0];}
        using var inventories=em.CreateEntityQuery(typeof(RaidInventoryClientState));var inventory=em.GetComponentObject<RaidInventoryClientState>(inventories.GetSingletonEntity());
        var hud=BountyClashUIFixture.Hud;var input=BountyClashUIFixture.Input;
        var bridge=em.GetComponentObject<GhostGameObjectLink>(player).LinkedInstance;
        var presentation=bridge.GetComponent<DollSingerNetworkPresentation>();
        var hands=bridge.GetComponentInChildren<GrenadePresentation>(true);
        var view=bridge.GetComponentInChildren<DollSingerView>(true);
        var camera=presentation.OwnedCamera;
        var priorMouse=Mouse.current;var priorKeyboard=Keyboard.current;
        var mouse=InputSystem.AddDevice<Mouse>("GrenadeCheckMouse");var keyboard=InputSystem.AddDevice<Keyboard>("GrenadeCheckKeyboard");
        uint hold=0;int startFrame=Time.frameCount;string heldCode=null;
        RaidGrenadeThrowRpc[] Requests()
        {using var query=em.CreateEntityQuery(typeof(RaidGrenadeThrowRpc));using var values=query.ToComponentDataArray<RaidGrenadeThrowRpc>(Allocator.Temp);return values.ToArray();}
        void Keys(params Key[] keys)=>InputSystem.QueueStateEvent(keyboard,new KeyboardState(keys));
        void MouseButtons(bool left=false,bool right=false)
        {var state=new MouseState();if(left)state=state.WithButton(MouseButton.Left);if(right)state=state.WithButton(MouseButton.Right);InputSystem.QueueStateEvent(mouse,state);}
        async Task Press(Key key){Keys(key);await Frames();Keys();await Frames();}
        void State(GrenadePhase phase,float remaining=0,bool low=false,bool acknowledge=true)
        {
            var health=em.GetComponentData<PredictedPlayerGhost>(player);
            health.GrenadeHoldId=hold;health.GrenadePhase=(byte)phase;health.GrenadeItem=phase==0?default:heldCode;
            health.GrenadePhaseRemaining=remaining;health.GrenadeFuseRemaining=GrenadeHandling.Live(phase)?2:0;health.GrenadeUnderhand=low;
            em.SetComponentData(player,health);
            if(acknowledge)BountyClashUIFixture.AcknowledgeFixture();
        }
        async Task Animate(GrenadePhase phase,bool low=false)
        {
            float duration=GrenadeHandling.Duration(phase),start=Time.time;
            do{State(phase,Mathf.Max(0,duration-(Time.time-start)),low);await Frames(1);}while(Time.time-start<duration);
        }
        void Capture(string name)
        {
            var target=new RenderTexture(960,540,24);var pixels=new Texture2D(960,540,TextureFormat.RGB24,false);var previous=RenderTexture.active;
            try
            {
                RenderPipeline.SubmitRenderRequest(camera,new UniversalRenderPipeline.SingleCameraRequest{destination=target});
                RenderTexture.active=target;pixels.ReadPixels(new Rect(0,0,960,540),0,0,false);pixels.Apply();
                Directory.CreateDirectory("LocalData/GrenadeReview");File.WriteAllBytes("LocalData/GrenadeReview/"+name+".png",pixels.EncodeToPNG());
            }
            finally{RenderTexture.active=previous;target.Release();UnityEngine.Object.Destroy(target);UnityEngine.Object.Destroy(pixels);}
        }
        try
        {
            view.SetFirstPerson(true);Keys();MouseButtons();await Frames(5);
            typeof(DollSingerInput).GetMethod("SetCapture",Hidden).Invoke(input,new object[]{true});await Frames();
            Check(input.CanReadPlayerInput,"Real player input is captured");
            BountyClashUIFixture.ClearIntents();await Press(Key.G);
            var equip=Requests().Single();Check(equip.Command==GrenadeCommand.Equip,"G draws instead of immediately throwing");
            hold=equip.RequestId;heldCode=inventory.Graph.Find(equip.ItemId.ToString()).Code;
            await Animate(GrenadePhase.Drawing);State(GrenadePhase.Safe);await Frames(8);
            Check(hands.OwnsHands&&hands.PropVisible,"Production network bridge shows held prop");
            Vector3 onScreen=camera.WorldToViewportPoint(hands.PropPosition);
            Check(onScreen.z>0&&onScreen.x>0&&onScreen.x<1&&onScreen.y>0&&onScreen.y<1,"Held grenade is visible from the real first-person eye: "+onScreen);
            Capture("FirstPerson-Safe");
            Check(!presentation.WeaponHandsAvailable,"Holding grenade hides weapon readiness");
            await Frames(8);Check(Requests().Length==1,"Holding safely does not auto-prime");
            MouseButtons(left:true);await Frames();
            Check(Requests().Count(r=>r.Command==GrenadeCommand.Prime)==1,"Mouse down requests one activation");
            Check(!input.FireHeld&&!input.FirePressed&&!input.AimHeld&&!input.ReloadPressed,"Grenade input never leaks into gun controls");
            MouseButtons();await Frames();
            Check(Requests().All(r=>r.Command!=GrenadeCommand.Release),"Quick tap buffers release until activation acknowledgement");
            State(GrenadePhase.Priming,GrenadeHandling.PrimeSeconds);await Frames(5);
            Check(Requests().Count(r=>r.Command==GrenadeCommand.Release)==1,"Buffered release sends exactly once after acknowledgement");
            await Animate(GrenadePhase.Priming);await Animate(GrenadePhase.Throwing);
            State(GrenadePhase.Recovering,GrenadeHandling.RecoverySeconds);await Frames(4);
            Check(!hands.PropVisible,"Held prop disappears on confirmed release");
            await Animate(GrenadePhase.Recovering);State(GrenadePhase.None);await SettleHands();
            Check(!hands.OwnsHands&&!RaidHUD.GrenadeInHands,"Recovery returns hands to weapons");
            Check(presentation.WeaponHandsAvailable,"Weapon hand ownership resumes after recovery");
            Check(Requests().Count(r=>r.Command==GrenadeCommand.Prime)==1&&Requests().Count(r=>r.Command==GrenadeCommand.Release)==1,"Repeated snapshots never resend actions");
            BountyClashUIFixture.ClearIntents();await Press(Key.G);hold=Requests().Single().RequestId;
            await Animate(GrenadePhase.Drawing);State(GrenadePhase.Safe);await Frames();
            MouseButtons(right:true);await Frames();
            Check(Requests().Single(r=>r.Command==GrenadeCommand.Prime).Underhand,"Right mouse selects low throw");
            await Animate(GrenadePhase.Priming,true);State(GrenadePhase.Live,low:true);await Frames(10);
            Check(Requests().All(r=>r.Command!=GrenadeCommand.Release),"Holding the physical button keeps cooking despite gun suppression");
            Capture("FirstPerson-Live");
            await Press(Key.G);Check(Requests().All(r=>r.Command!=GrenadeCommand.Stow),"G cannot put a live grenade away");
            Keys(Key.X);await Frames(4);
            Check(Requests().Count(r=>r.Command==GrenadeCommand.Drop)==1,"X drops a live grenade once");
            Check(input.Stance!=DollSingerStance.Tiptoe,"Drop key does not also start tiptoe");
            State(GrenadePhase.Recovering,GrenadeHandling.RecoverySeconds,true);MouseButtons();await Animate(GrenadePhase.Recovering,true);
            State(GrenadePhase.None);await Frames(10);Check(input.Stance!=DollSingerStance.Tiptoe,"Holding X through acknowledgement stays consumed");Keys();await Frames();
            BountyClashUIFixture.ClearIntents();await Press(Key.G);hold=Requests().Single().RequestId;
            await Animate(GrenadePhase.Drawing);State(GrenadePhase.Safe);await Frames();
            await Press(Key.Digit1);Check(Requests().Count(r=>r.Command==GrenadeCommand.Stow)==1,"Weapon selection stows a safe grenade first");
            await Animate(GrenadePhase.Stowing);State(GrenadePhase.None);await SettleHands();
            Check(!hands.OwnsHands&&!RaidHUD.GrenadeInHands,"Safe stow completes without throw");
            Check(Requests().All(r=>r.Command!=GrenadeCommand.Prime&&r.Command!=GrenadeCommand.Release),"Cancelled draw never primes or throws");
            Check(Time.frameCount>startFrame+60,"Real Play frames advanced through the sequence");
            return "PASS "+checks+" live HUD, input and hand animation checks (explicit local acknowledgements, no transport)";
        }
        finally
        {
            Keys();MouseButtons();State(GrenadePhase.None);BountyClashUIFixture.ClearIntents();
            InputSystem.RemoveDevice(mouse);InputSystem.RemoveDevice(keyboard);
            if(priorMouse?.added==true)priorMouse.MakeCurrent();if(priorKeyboard?.added==true)priorKeyboard.MakeCurrent();
        }
    }
}
