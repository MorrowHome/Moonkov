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
        public static byte BackpackStyle(string code) => code=="sling_pack"?(byte)1:code=="backpack"?(byte)2:code=="cargo_pack"?(byte)3:code=="small_pack"?(byte)4:(byte)0;
        public static byte BadgeStyle(string code) => code=="scout_rig"?(byte)1:code=="rig"?(byte)2:code=="field_rig"?(byte)3:(byte)0;
        public static string BadgeCode(byte style) => style==1?"scout_rig":style==2?"rig":style==3?"field_rig":null;
        private static float Load(float value) => value==-1?100:EnergyShieldRules.Resource(value);

        // Like SaveActiveAmmo: server resource changes do not invalidate structural inventory actions.
        // Call before every move/drop/death/settlement/checkpoint, before a subtree leaves this graph.
        public static void Flush(ref PredictedPlayerGhost state,InventoryGraph graph)
        {
            if(!state.ProtectionInitialized || graph==null)return;
            var dome=state.HeadDomeItem.Length>0?graph.Find(state.HeadDomeItem.ToString()):null;
            if(dome!=null && IsHeadDome(dome.Code))dome.SuitOxygen=EnergyShieldRules.Resource(state.Oxygen);
        }
        public static void Synchronize(ref PredictedPlayerGhost state,InventoryGraph graph)
        {
            Flush(ref state,graph);
            state.SubspaceBackpack=BackpackStyle(graph?.Equipped("Backpack")?.Code);
            state.SubspaceBadge=BadgeStyle(graph?.Equipped("ChestRig")?.Code);
            HaloEnergyRules.Synchronize(ref state,graph);
            var dome=graph?.Equipped("Helmet");if(!IsHeadDome(dome?.Code))dome=null;
            string domeId=dome?.Id??"";
            if(!state.ProtectionInitialized || state.HeadDomeItem!=new FixedString64Bytes(domeId))
            {
                state.Oxygen=dome==null?0:Load(dome.SuitOxygen);state.HeadDomeItem.CopyFromTruncated(domeId);
                if(state.Oxygen>0){state.HypoxiaSeconds=0;state.OxygenGraceRemaining=0;}
            }
            state.HeadDomeEquipped=dome!=null;state.HeadDomeStandard=dome?.Code==StandardHeadDomeCode;state.ProtectionInitialized=true;
            Flush(ref state,graph);
        }
        // AI and real players share this exact rule; caller owns alive/active/connected eligibility.
        public static void Tick(ref PredictedPlayerGhost state,InventoryGraph graph,float dt,bool canRecover=true)
        {Synchronize(ref state,graph);EnergyShieldRules.Tick(ref state,dt,canRecover);Flush(ref state,graph);}
    }
    // Kept as a source-compatible entry point for old callers. No generator resource remains.
    public static class EnergyShieldInventory
    {
        public static bool CanRecharge(in PredictedPlayerGhost state,InventoryGraph graph,InventoryItem cell) =>
            HaloEnergyInventory.CanRecharge(state,graph,cell);
        public static InventoryError Recharge(ref PredictedPlayerGhost state,InventoryGraph graph,string cellId,int expectedVersion) =>
            HaloEnergyInventory.Recharge(ref state,graph,cellId,expectedVersion);
    }
}
