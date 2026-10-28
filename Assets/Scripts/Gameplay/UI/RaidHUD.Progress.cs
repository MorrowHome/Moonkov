using Unity.Entities;
using Unity.MP_FPS.DollSinger;
using Unity.MP_FPS.Inventory;
using UnityEngine;

namespace Unity.MP_FPS
{
    public sealed partial class RaidHUD
    {
        private uint m_ProgressTacticalRequest;
        private float m_ProgressTacticalDuration;
        private void CancelProgressAction()
        {
            if(TimedActionActive)CancelTimedAction();
            else if(QuickTacticalActionPending && !QuickTacticalCancelPending)SendQuickTactical(TacticalItemAction.Cancel);
        }
        private bool PresentOtherProgress(bool visible)
        {
            if(!visible || m_PlayerQuery.IsEmptyIgnoreFilter)return false;
            var player=m_PlayerQuery.GetSingleton<PredictedPlayerGhost>();
            if(player.CurrentHealth<=0)return false;
            var owner=QuickOwner;
            if(QuickTacticalActionPending && !m_InventoryVisible)
            {
                if(m_ProgressTacticalRequest!=owner.RequestId)
                {
                    m_ProgressTacticalRequest=owner.RequestId;
                    var item=QuickGraph?.Find(owner.ItemId.ToString());
                    var definition=TacticalItemRules.Find(item?.Code);
                    bool use=m_QuickTrackedTacticalRequest!=owner.RequestId || m_QuickRequestedAction==TacticalItemAction.Use;
                    m_ProgressTacticalDuration=use && definition!=null ? definition.UseSeconds : 3;
                }
                ShowProgress("USING ITEM",owner.Remaining>0 ? 1-owner.Remaining/Mathf.Max(.01f,m_ProgressTacticalDuration) : 0,owner.Remaining,owner.Remaining>0,true,QuickTacticalCancelPending);
                return true;
            }
            if(m_MissionHolding>=0 && RaidMissionSite.Active && !m_InventoryVisible)
            {
                int node=m_MissionHolding;
                float progress=node<4 ? m_MissionSnapshot.ProgressA[node] : m_MissionSnapshot.ProgressB[node-4];
                ShowProgress("OPERATING",progress,0,false,false,false);return true;
            }
            if(m_Snapshot.ExtractionRemaining>=0 && MoonRaidMap.Active && !m_ClashMode && !m_InventoryVisible)
            {
                ShowProgress("EXTRACTION",1-m_Snapshot.ExtractionRemaining/Mathf.Max(.01f,MoonRaidMap.Active.ExtractionSeconds),
                    m_Snapshot.ExtractionRemaining,true,false,false);return true;
            }
            if(player.ControllerState.IsReloadingState && !m_InventoryVisible && WeaponManager.Instance!=null)
            {
                var weapon=WeaponManager.Instance.WeaponRegistry.GetWeaponData(player.EquippedWeaponID,player.EquippedModules);
                if(weapon!=null)
                {
                    float duration=HaloReload.Total(player,weapon);
                    ShowProgress(DollSingerWeapons.IsHalo(player.EquippedWeaponID) ? "CHARGING" : "RELOADING",
                        1-player.ReloadTimer/Mathf.Max(.01f,duration),player.ReloadTimer,true,false,false);return true;
                }
            }
            return false;
        }
        private void ShowProgress(string title,float fraction,float remaining,bool time,bool cancel,bool cancelling)
            => m_ActionView.PresentProgress(title,Mathf.Min(fraction,.97f),remaining,true,time,cancel && m_InventoryVisible,cancelling,false,true);
    }
}
