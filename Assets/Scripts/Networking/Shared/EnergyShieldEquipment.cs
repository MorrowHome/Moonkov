using UnityEngine;
using Unity.Collections;
using Unity.MP_FPS.Inventory;

namespace Unity.MP_FPS
{
    public static class EnergyShieldEquipment
    {
        public const string ShieldCode="energy_shield_generator", HeadDomeCode="energy_head_dome";
        public const string StandardHeadDomeCode="helmet";
        public static bool IsHeadDome(string code) => code==HeadDomeCode || code=="helmet";
        private static float Load(float value) => value==-1?100:EnergyShieldRules.Resource(value);

        // Like SaveActiveAmmo: server resource changes do not invalidate structural inventory actions.
        // Call before every move/drop/death/settlement/checkpoint, before a subtree leaves this graph.
        public static void Flush(ref PredictedPlayerGhost state,InventoryGraph graph)
        {
            if(!state.ProtectionInitialized || graph==null)return;
            var shield=state.ShieldItem.Length>0?graph.Find(state.ShieldItem.ToString()):null;
            if(shield?.Code==ShieldCode)
            {shield.ShieldCharge=EnergyShieldRules.Resource(state.ShieldCharge);shield.ShieldEnergy=EnergyShieldRules.Resource(state.ShieldEnergy);}
            var dome=state.HeadDomeItem.Length>0?graph.Find(state.HeadDomeItem.ToString()):null;
            if(dome!=null && IsHeadDome(dome.Code))dome.SuitOxygen=EnergyShieldRules.Resource(state.Oxygen);
        }
        public static void Synchronize(ref PredictedPlayerGhost state,InventoryGraph graph)
        {
            Flush(ref state,graph);
            var shield=graph?.Equipped("Shield");if(shield?.Code!=ShieldCode)shield=null;
            var dome=graph?.Equipped("Helmet");if(!IsHeadDome(dome?.Code))dome=null;
            string shieldId=shield?.Id??"",domeId=dome?.Id??"";
            if(!state.ProtectionInitialized || state.ShieldItem!=new FixedString64Bytes(shieldId))
            {
                state.ShieldCharge=shield==null?0:Load(shield.ShieldCharge);
                state.ShieldEnergy=shield==null?0:Load(shield.ShieldEnergy);
                // Swapping a damaged generator cannot bypass its out-of-combat delay.
                state.ShieldRecoveryDelay=Mathf.Max(state.ShieldRecoveryDelay,EnergyShieldRules.RecoveryDelay);
                state.ShieldRegenerating=false;state.ShieldItem.CopyFromTruncated(shieldId);
            }
            if(!state.ProtectionInitialized || state.HeadDomeItem!=new FixedString64Bytes(domeId))
            {
                state.Oxygen=dome==null?0:Load(dome.SuitOxygen);state.HeadDomeItem.CopyFromTruncated(domeId);
                if(state.Oxygen>0){state.HypoxiaSeconds=0;state.OxygenGraceRemaining=0;}
            }
            state.ShieldEquipped=shield!=null;state.HeadDomeEquipped=dome!=null;state.HeadDomeStandard=dome?.Code==StandardHeadDomeCode;state.ProtectionInitialized=true;
            Flush(ref state,graph);
        }
        // AI and real players share this exact rule; caller owns alive/active/connected eligibility.
        public static void Tick(ref PredictedPlayerGhost state,InventoryGraph graph,float dt,bool canRecover=true)
        {Synchronize(ref state,graph);EnergyShieldRules.Tick(ref state,dt,canRecover);Flush(ref state,graph);}
    }
    public static class EnergyShieldInventory
    {
        public static bool CanRecharge(in PredictedPlayerGhost state,InventoryGraph graph,InventoryItem cell) =>
            graph!=null && EnergyShieldRules.Alive(state) && state.ShieldEquipped &&
            graph.Equipped("Shield")?.Code==EnergyShieldEquipment.ShieldCode && float.IsFinite(state.ShieldEnergy) &&
            state.ShieldEnergy<=EnergyShieldRules.EnergyCapacity-1 && cell!=null && BatteryEnergy.IsCell(cell) &&
            graph.Carried(cell) && (cell.Parent==InventoryCatalog.Pockets || cell.Parent==graph.Equipped("ChestRig")?.Id) && BatteryEnergy.Stored(cell)>0;
        // Existing RaidConsumableUseRpc dispatches real cells here. Failure consumes nothing.
        public static InventoryError Recharge(ref PredictedPlayerGhost state,InventoryGraph graph,string cellId,int expectedVersion)
        {
            if(graph==null)return InventoryError.Invalid;
            if(graph.Version!=expectedVersion)return InventoryError.Stale;
            if(!EnergyShieldRules.Alive(state))return InventoryError.Inaccessible;
            var shield=graph.Equipped("Shield");var cell=graph.Find(cellId);
            if(shield?.Code!=EnergyShieldEquipment.ShieldCode || !state.ShieldEquipped || state.ShieldItem!=new FixedString64Bytes(shield.Id))return InventoryError.Inaccessible;
            if(cell==null)return InventoryError.Missing;
            if(!BatteryEnergy.IsCell(cell) || !graph.Carried(cell) || (cell.Parent!=InventoryCatalog.Pockets && cell.Parent!=graph.Equipped("ChestRig")?.Id))return InventoryError.Inaccessible;
            float power=EnergyShieldRules.Resource(state.ShieldEnergy);
            int transferred=Mathf.Min(Mathf.FloorToInt(EnergyShieldRules.EnergyCapacity-power+.0001f),BatteryEnergy.Stored(cell));
            if(transferred<=0)return InventoryError.Invalid;
            BatteryEnergy.Spend(cell,transferred);if(cell.Quantity==0)graph.Items.Remove(cell);
            state.ShieldEnergy=Mathf.Min(EnergyShieldRules.EnergyCapacity,power+transferred);
            EnergyShieldEquipment.Flush(ref state,graph);graph.Version++;return InventoryError.None;
        }
    }
}
