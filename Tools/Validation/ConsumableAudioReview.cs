using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Unity.MP_FPS;
using Unity.MP_FPS.DollSinger;
using UnityEngine;

// Use CoughVoiceReview.Begin/Restore around this isolated, single-Editor playback check.
public static class ConsumableAudioReview
{
    public static async Task<string> Run()
    {
        if(!Application.isPlaying||GameManager.Instance)throw new InvalidOperationException("Use isolated empty Play scene");
        const BindingFlags hidden=BindingFlags.Instance|BindingFlags.NonPublic;
        var instance=typeof(GameManager).GetProperty("Instance",BindingFlags.Public|BindingFlags.Static);
        var holder=new GameObject("Consumable review manager");holder.SetActive(false);
        var manager=holder.AddComponent<GameManager>();
        var actor=new GameObject("Consumable review actor");actor.AddComponent<AudioListener>();
        var sources=new GameObject("Consumable review sources");
        var pool=(SoundGameObjectPool)System.Runtime.Serialization.FormatterServices.GetUninitializedObject(typeof(SoundGameObjectPool));
        var system=new SoundSystem();var foley=new RaidActionFoley();int checks=0;
        bool background=Application.runInBackground;Application.runInBackground=true;
        void Check(bool value,string message){checks++;if(!value)throw new InvalidOperationException(message);}
        try
        {
            typeof(SoundGameObjectPool).GetField("m_SourceHolder",hidden).SetValue(pool,sources);
            pool.SoundGameObjectList=new List<SoundGameObject>();
            for(int i=0;i<16;i++)pool.SoundGameObjectList.Add(pool.Create());
            for(int i=0;i<16;i++)pool.SoundGameObjectList[i].NextAvailable=pool.SoundGameObjectList[(i+1)%16];
            pool.NextSoundGameObject=pool.SoundGameObjectList[0];pool.AvailableSoundObjectCount=16;
            var lib=MoonkovAudio.Library;system.Init(actor.transform,16,pool,lib.EatBite.UnityAudioMixer);
            instance.SetValue(null,manager);typeof(GameManager).GetField("m_SoundSystem",hidden).SetValue(manager,system);
            uint seq=1;
            foreach(bool owner in new[]{true,false})
            foreach(var code in new[]{"ration","water","electrolyte","energy_gel","alien_raw_meat","medkit","vitality_shot","med_gel","trauma_kit","hemostatic_bandage","rescue_kit","oxygen_canister","oxygen_reserve"})
            {
                foley.Stop();system.KillAll();foley=new RaidActionFoley();uint current=seq++;
                bool supply=Unity.MP_FPS.Inventory.ConsumableCatalog.Get(code)!=null;
                var kind=RaidHandActionSystem.Kind(code,supply);
                void Observe(float phase,bool active=true)=>foley.Observe(kind,current,phase,.5f,code,actor.transform.position,owner,active,.08f,.64f,actor.transform);
                bool Has(SoundDef bank)=>system.m_SoundEmitterActiveList.Any(e=>e.Active&&e.Definition==bank);
                int Last()=>system.m_SoundEmitterActiveList.Select(e=>e.SeqId).DefaultIfEmpty(-1).Max();
                Observe(0);Observe(.1f);Observe(.51f);
                var expected=kind==HandAction.RefillOxygen?lib.OxygenCanisterFlow:kind==HandAction.Inject?lib.InjectorPress:
                    kind==HandAction.Treat?(code=="med_gel"?lib.MedicalGel:lib.MedicalHandling):kind==HandAction.Eat&&code=="ration"?lib.EatBite:lib.DrinkSip;
                Check(Has(expected),code+" contact missing");
                var emitter=system.m_SoundEmitterActiveList.First(e=>e.Active&&e.Definition==expected);
                var source=emitter.ActiveSoundGameObjects[0].m_AudioSource;
                Check(source.isPlaying&&source.volume>0&&source.outputAudioMixerGroup.name=="SFX",code+" native playback/routing");
                if(kind==HandAction.Treat)Check(source.loop==(code!="med_gel"),code+" must distinguish continuous wrapping from applying gel");
                Check(Mathf.Abs(source.spatialBlend-(owner?0:1))<.001f&&source.transform.IsChildOf(actor.transform),code+" ownership/follow");
                int before=Last();for(int i=0;i<100;i++)Observe(.51f);
                Check(Last()==before,code+" duplicate snapshots retriggered");
                Observe(.6f);
                if(kind==HandAction.Eat||kind==HandAction.Drink)Check(Last()>before,code+" sustained mouth movement missing");
                Observe(.76f);if(code=="water")Check(Has(lib.BottleClose),"Bottle close reused opening cue");
                Observe(.9f);Observe(.9f,false);
                await Task.Delay(180);system.UpdateSoundSystem(false);
                Check(!system.m_SoundEmitterActiveList.Any(e=>e.Active),code+" ended/cancelled sounds retained");
            }
            foley=new RaidActionFoley();system.KillAll();
            foley.Observe(HandAction.Drink,seq++,.6f,.5f,"water",Vector3.zero,true,true);
            Check(!system.m_SoundEmitterActiveList.Any(e=>e.Active),"Late join replayed mouth/open markers");
            foley.Stop();system.KillAll();
            foley.Observe(HandAction.RefillOxygen,seq++,.6f,.45f,"oxygen_canister",Vector3.zero,true,true);
            Check(system.m_SoundEmitterActiveList.Count(e=>e.Active)==1&&system.m_SoundEmitterActiveList.Any(e=>e.Active&&e.Definition==lib.OxygenCanisterFlow),
                "Mid-refill must resume only the continuous flow");
            foley.Stop();await Task.Delay(180);system.UpdateSoundSystem(false);
            Check(pool.AvailableSoundObjectCount==16,"Interrupted oxygen flow leaked sources");
            foley=new RaidActionFoley();system.KillAll();
            foley.Observe(HandAction.Treat,seq++,.6f,.63f,"trauma_kit",Vector3.zero,true,true);
            Check(system.m_SoundEmitterActiveList.Count(e=>e.Active)==1&&system.m_SoundEmitterActiveList.Any(e=>e.Active&&e.Definition==lib.MedicalHandling),
                "Mid-bandage must continue cloth movement without replaying the tear");
            foley.Stop();await Task.Delay(180);system.UpdateSoundSystem(false);
            Check(pool.AvailableSoundObjectCount==16,"Interrupted wrapping leaked sources");
            return "PASS "+checks+" native consumable playback, owner/remote, repeated snapshots, late join and cleanup checks";
        }
        finally
        {
            foley.Stop();system.KillAll();instance.SetValue(null,null);Application.runInBackground=background;
            UnityEngine.Object.DestroyImmediate(holder);UnityEngine.Object.DestroyImmediate(actor);UnityEngine.Object.DestroyImmediate(sources);
        }
    }
}
