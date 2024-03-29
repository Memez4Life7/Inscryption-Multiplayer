﻿using DiskCardGame;
using HarmonyLib;
using inscryption_multiplayer.Networking;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;

namespace inscryption_multiplayer
{
    [HarmonyPatch]
    public class Map_Patches
    {
        [HarmonyPatch(typeof(CardBattleNodeData), MethodType.Constructor)]
        [HarmonyPostfix]
        public static void ApplyMultiplayerBattleSequencer(CardBattleNodeData __instance)
        {
            if (Plugin.MultiplayerActive)
                __instance.specialBattleId = nameof(Multiplayer_Battle_Sequencer);
        }


        [HarmonyPatch(typeof(RunState), nameof(RunState.GenerateMapDataForCurrentRegion))]
        [HarmonyPrefix]
        public static bool GenerateMapDataForCurrentRegionPrefix(RunState __instance, ref PredefinedNodes predefinedNodes)
        {
            if (!Plugin.MultiplayerActive)
            {
                return true;
            }

            int num = GameSettings.Current.NodeLength + 1;
            __instance.map = MapGenerator.GenerateMap(RunState.CurrentMapRegion, GameSettings.Current.NodeWidth, num, predefinedNodes);
            __instance.currentNodeId = __instance.map.RootNode.id;
            return false;
        }


        [HarmonyPatch(typeof(MapGenerator), nameof(MapGenerator.ChooseSpecialNodeFromPossibilities))]
        [HarmonyPrefix]
        public static void RemoveNodeData(ref List<NodeData> possibilities)
        {
            if (Plugin.MultiplayerActive)
            {
                if (!GameSettings.Current.AllowTotems)
                {
                    possibilities.RemoveAll(n => n is BuildTotemNodeData);
                }
                if (!GameSettings.Current.AllowItems)
                {
                    possibilities.RemoveAll(n => n is GainConsumablesNodeData);
                }
            }
        }

        [HarmonyPatch(typeof(RunState), nameof(RunState.CurrentMapRegion), MethodType.Getter)]
        [HarmonyPrefix]
        public static bool MapProgressionPatch(ref RegionData __result)
        {
            if (Plugin.MultiplayerActive)
            {
                if (RunState.Run.regionTier < GameSettings.Current.MapsUsed)
                    __result = RegionProgression.Instance.regions[
                        RunState.Run.regionOrder[RunState.Run.regionTier % RunState.Run.regionOrder.Length]];
                else __result = RegionProgression.Instance.ascensionFinalRegion;
                return false;
            }
            return true;
        }

        [HarmonyPatch(typeof(MapGenerator), nameof(MapGenerator.GenerateMap))]
        [HarmonyPrefix]
        public static void RemoveFinalBossNode(ref RegionData region)
        {
            if (Plugin.MultiplayerActive && region == RegionProgression.Instance.ascensionFinalRegion)
            {
                var bossNode = region.predefinedNodes.nodeRows[region.predefinedNodes.nodeRows.Count - 1][0];
                var newNode = new CardBattleNodeData();
                newNode.position = bossNode.position;
                newNode.specialBattleId = nameof(Multiplayer_Final_Battle_Sequencer);
                region.predefinedNodes.nodeRows[region.predefinedNodes.nodeRows.Count - 1][0] = newNode;
            }
        }

        [HarmonyPatch(typeof(MapGenerator), nameof(MapGenerator.ForceFirstNodeTraderForAscension))]
        [HarmonyPrefix]
        public static bool ForceTrader(ref bool __result, int rowIndex)
        {
            if (Plugin.MultiplayerActive)
            {
                __result = rowIndex == 1 && RunState.Run.regionTier == 0;
                return false;
            }
            return true;
        }

        private static NodeData CreateNodeData()
        {
            return Plugin.MultiplayerActive ? new CardBattleNodeData() : new TotemBattleNodeData();
        }

        private static bool ModifyFlag(bool flag)
        {
            return flag && !Plugin.MultiplayerActive;
        }

        [HarmonyPatch(typeof(MapGenerator), nameof(MapGenerator.CreateNode))]
        [HarmonyTranspiler]
        public static IEnumerable<CodeInstruction> RemoveTotemAndBossBattleNodeData(IEnumerable<CodeInstruction> instructions,
            ILGenerator generator)
        {
            var constructor = AccessTools.Constructor(typeof(TotemBattleNodeData));
            var instruction = instructions.FirstOrDefault(inst => inst.opcode == OpCodes.Newobj && inst.operand as ConstructorInfo == constructor);
            if (instruction is not null)
            {
                instruction.opcode = OpCodes.Call;
                instruction.operand = AccessTools.Method(typeof(Map_Patches), nameof(CreateNodeData));
            }
            foreach (var inst in instructions)
            {
                yield return inst;
                if (inst.opcode == OpCodes.Stloc_3)
                {
                    yield return new CodeInstruction(OpCodes.Ldloc_1);
                    yield return new CodeInstruction(OpCodes.Call,
                        AccessTools.Method(typeof(Map_Patches), nameof(ModifyFlag)));
                    yield return new CodeInstruction(OpCodes.Stloc_1);
                }
            }
        }
    }
}
