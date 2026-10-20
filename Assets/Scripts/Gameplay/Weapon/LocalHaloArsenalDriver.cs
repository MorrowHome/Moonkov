using UnityEngine;
using Unity.MP_FPS.DollSinger;
using Unity.MP_FPS.Inventory;

namespace Unity.MP_FPS
{
    /// <summary>Standalone cosmetic character uses the same magazine/burst/energy rules as gameplay.</summary>
    public sealed class LocalHaloArsenalDriver : MonoBehaviour, ILocalArsenalDriver
    {
        public WeaponRegistry registry;
        private readonly PredictedPlayerGhost[] states=new PredictedPlayerGhost[HaloArsenal.Entries.Length];
        private InventoryGraph battery;
        private int active=-1;
        private uint shotTick,reloadTick;
        public bool Reloading=>active>=0&&states[active].ControllerState.IsReloadingState;
        private void Initialize()
        {
            if(battery!=null)return;
            battery=InventoryGraph.Create(starter:false);battery.AddSupply("cells",4,"pockets");
            for(int i=0;i<states.Length;i++)states[i]=new PredictedPlayerGhost{CurrentHealth=100,EquippedWeaponID=(uint)(i+6),CurrentAmmo=registry.GetWeaponData((uint)(i+6)).MagazineSize,WeaponCooldown=10,EquippedWeaponSlot=1};
        }
        public int Ammo(DollSingerHaloAim.HaloWeapon weapon)
        {Initialize();int i=(int)weapon-5;return i>=0&&i<states.Length?states[i].CurrentAmmo:0;}
        public void CancelBurst(){if(active>=0)HaloModuleFire.Reset(ref states[active]);active=-1;}
        public bool Step(DollSingerHaloAim halo,DollSingerHaloAim.HaloWeapon gesture,bool held,bool pressed,bool reload,bool aiming,float deltaTime)
        {
            Initialize();int index=(int)gesture-5;var weapon=registry.GetWeaponData((uint)(index+6));
            ref var state=ref states[index];
            if(active!=index){if(active>=0)HaloModuleFire.Reset(ref states[active]);active=index;HaloModuleFire.BeginEquip(ref state,weapon);}
            for(int i=0;i<states.Length;i++)HaloModuleFire.AdvanceTimers(ref states[i],deltaTime);
            if(state.ControllerState.IsReloadingState)
            {state.ReloadTimer=Mathf.Max(0,state.ReloadTimer-deltaTime);if(state.ReloadTimer==0)DollSingerWeapons.CompleteReload(ref state,weapon);}
            if(!state.ControllerState.IsReloadingState&&(reload||state.CurrentAmmo==0)&&battery.TryRecharge(state.CurrentAmmo,weapon.MagazineSize,weapon.EnergyPerRound,out int target))
            {state.ControllerState.IsReloadingState=true;state.ReloadTimer=weapon.ReloadTime;state.ReloadTargetAmmo=target;reloadTick++;HaloModuleFire.Reset(ref state);}
            bool fired=HaloModuleFire.TryFire(ref state,weapon,weapon.Automatic||weapon.BurstCount>1?held:pressed,aiming,false);
            if(fired){state.CurrentAmmo--;shotTick++;HaloModuleFire.CommitShot(ref state,weapon,deltaTime);}
            halo.SetNetworkWeapon(gesture,state.CurrentAmmo,state.ControllerState.IsReloadingState,
                state.ControllerState.IsReloadingState?1-state.ReloadTimer/weapon.ReloadTime:0,shotTick,reloadTick,state.ReloadTargetAmmo,0,weapon.MagazineSize,state.BurstSequence);
            return fired;
        }
    }
}
