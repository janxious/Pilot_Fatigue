using System;
using System.Reflection;
using BattleTech;
using Harmony;
using BattleTech.UI;
using Newtonsoft.Json;
using System.IO;
using UnityEngine;
using System.Collections.Generic;


namespace Pilot_Fatigue
{
    public static class Pre_Control
    {
        public const string ModName = "Pilot_Fatigue";
        public const string ModId = "dZ.Zappo.Pilot_Fatigue";

        internal static ModSettings settings;
        internal static string ModDirectory;

        public static void Init(string directory, string modSettings)
        {
            ModDirectory = directory;
            try
            {
                settings = JsonConvert.DeserializeObject<ModSettings>(modSettings);
            }
            catch (Exception)
            {
                settings = new ModSettings();
            }

            var harmony = HarmonyInstance.Create(ModId);
            harmony.PatchAll(Assembly.GetExecutingAssembly());
        }


        [HarmonyPatch(typeof(AAR_UnitStatusWidget), "FillInPilotData")]
        public static class Add_Fatigue_To_Pilots_Prefix
        {
            public static void Prefix(AAR_UnitStatusWidget __instance, SimGameState ___simState)
            {
                UnitResult unitResult = Traverse.Create(__instance).Field("UnitData").GetValue<UnitResult>();
                if (unitResult.pilot.pilotDef.TimeoutRemaining > 0 && unitResult.pilot.Injuries == 0)
                {
                }
                else if (unitResult.pilot.pilotDef.TimeoutRemaining > 0 && unitResult.pilot.Injuries > 0)
                {
                    unitResult.pilot.pilotDef.SetTimeoutTime(0);
                    WorkOrderEntry_MedBayHeal workOrderEntry_MedBayHeal;
                    workOrderEntry_MedBayHeal = (WorkOrderEntry_MedBayHeal)___simState.MedBayQueue.GetSubEntry(unitResult.pilot.Description.Id);
                    ___simState.MedBayQueue.RemoveSubEntry(unitResult.pilot.Description.Id);
                }

            }
        }

        [HarmonyPatch(typeof(AAR_UnitStatusWidget), "FillInPilotData")]
        public static class Add_Fatigue_To_Pilots_Postfix
        {
            public static void Postfix(AAR_UnitStatusWidget __instance, SimGameState ___simState)
            {
                UnitResult unitResult = Traverse.Create(__instance).Field("UnitData").GetValue<UnitResult>();

                int FatigueTimeStart = settings.FatigueTimeStart;
                int GutsValue = unitResult.pilot.Guts;
                int TacticsValue = unitResult.pilot.Tactics;
                SimGameState simstate = Traverse.Create(__instance).Field("simState").GetValue<SimGameState>();
                int CurrentMorale = simstate.Morale;
                int MoraleDiff = CurrentMorale - simstate.Morale;
                int MoraleModifier = 0;

                if (MoraleDiff <= settings.MoraleNegativeTierTwo)
                {
                    MoraleModifier = -2;
                }
                if (MoraleDiff <= settings.MoraleNegativeTierOne && MoraleDiff > settings.MoraleNegativeTierTwo)
                {
                    MoraleModifier = -1;
                }
                if (MoraleDiff < settings.MoralePositiveTierTwo && MoraleDiff >= settings.MoralePositiveTierOne)
                {
                    MoraleModifier = 1;
                }
                if (MoraleDiff >= settings.MoralePositiveTierTwo)
                {
                    MoraleModifier = 2;
                }

                int FatigueTime = 1 + FatigueTimeStart - GutsValue / 2 - MoraleModifier;

                if (unitResult.pilot.pilotDef.PilotTags.Contains("pilot_athletic") && settings.QuirksEnabled)
                    FatigueTime = FatigueTime - settings.pilot_athletic_FatigueDaysReduction;

                if (FatigueTime <= (settings.FatigueMinimum + 1))
                {
                    FatigueTime = settings.FatigueMinimum + 1;
                }
                
                if (unitResult.pilot.Injuries == 0 && unitResult.pilot.pilotDef.TimeoutRemaining == 0)
                {
                    unitResult.pilot.pilotDef.SetTimeoutTime(FatigueTime);
                }
                else if (unitResult.pilot.Injuries == 0 && unitResult.pilot.pilotDef.TimeoutRemaining > 0)
                {
                    float roll = UnityEngine.Random.Range(1, 100);
                    float GutCheck = 10 * GutsValue;
                    int currenttime = unitResult.pilot.pilotDef.TimeoutRemaining;
                    unitResult.pilot.pilotDef.SetTimeoutTime(0);
                    WorkOrderEntry_MedBayHeal workOrderEntry_MedBayHeal;
                    workOrderEntry_MedBayHeal = (WorkOrderEntry_MedBayHeal)___simState.MedBayQueue.GetSubEntry(unitResult.pilot.Description.Id);
                    ___simState.MedBayQueue.RemoveSubEntry(unitResult.pilot.Description.Id);
                    unitResult.pilot.pilotDef.SetTimeoutTime(currenttime + FatigueTime);

                    if (roll > GutCheck)
                    {
                        unitResult.pilot.pilotDef.PilotTags.Add("pilot_companyCat");
                    }
                }
            }
        }
        [HarmonyPatch(typeof(Pilot))]
        [HarmonyPatch("CanPilot", PropertyMethod.Getter)]
        public static class BattleTech_Pilot_CanPilot_Prefix
        {
            private static void Postfix(Pilot __instance, ref bool __result)
            {
                if (__instance.Injuries == 0 && __instance.pilotDef.TimeoutRemaining > 0)
                {
                    __result = true;
                }
            }
        }
        [HarmonyPatch(typeof(Pilot))]
        [HarmonyPatch("Gunnery", PropertyMethod.Getter)]
        public class GunneryTimeModifier
        {
            public static void Postfix(Pilot __instance, ref int __result)
            {
                int Penalty = 0;
                int TimeOut = __instance.pilotDef.TimeoutRemaining;
                if(__instance.pilotDef.PilotTags.Contains("pilot_gladiator") && settings.QuirksEnabled)
                {
                    Penalty = (int)Math.Floor(TimeOut / settings.FatigueFactor);
                }
                else
                {
                    Penalty = (int)Math.Ceiling(TimeOut / settings.FatigueFactor);
                }
                
                if (settings.InjuriesHurt)
                {
                    Penalty = Penalty + __instance.Injuries;
                }
                int NewValue = __result - Penalty;
                if (NewValue < 1)
                {
                    NewValue = 1;
                }
                __result = NewValue;
            }
        }
        [HarmonyPatch(typeof(Pilot))]
        [HarmonyPatch("Piloting", PropertyMethod.Getter)]
        public class PilotingHealthModifier
        {
            public static void Postfix(Pilot __instance, ref int __result)
            {
                int TimeOut = __instance.pilotDef.TimeoutRemaining;
                int Penalty = (int)Math.Ceiling(TimeOut / settings.FatigueFactor);
                if (settings.InjuriesHurt)
                {
                    Penalty = Penalty + __instance.Injuries;
                }
                int NewValue = __result - Penalty;

                if (NewValue < 1)
                {
                    NewValue = 1;
                }
                __result = NewValue;
            }
        }

        [HarmonyPatch(typeof(Pilot))]
        [HarmonyPatch("Tactics", PropertyMethod.Getter)]
        public class TacticsHealthModifier
        {

            public static void Postfix(Pilot __instance, ref int __result)
            {
                int TimeOut = __instance.pilotDef.TimeoutRemaining;
                int Penalty = (int)Math.Ceiling(TimeOut / settings.FatigueFactor);
                if (settings.InjuriesHurt)
                {
                    Penalty = Penalty + __instance.Injuries;
                }
                int NewValue = __result - Penalty;
                if (NewValue < 1)
                {
                    NewValue = 1;
                }
                __result = NewValue;
            }
        }

        [HarmonyPatch(typeof(SimGameState), "OnDayPassed")]
        public static class CorrectTimeOut
        {
            public static void Postfix(SimGameState __instance)
            {   
                List<Pilot> list = new List<Pilot>(__instance.PilotRoster);
			    list.Add(__instance.Commander);
			    for (int j = 0; j < list.Count; j++)
			    {
				    Pilot pilot = list[j];
                    if (pilot.pilotDef.PilotTags.Contains("pilot_companyCat"))
                    {
                        pilot.StatCollection.ModifyStat<int>("Light Injury", 0, "Injuries", StatCollection.StatOperation.Int_Add, 1, -1, true);
                        pilot.pilotDef.PilotTags.Remove("pilot_companyCat");
                        int FatigueTime = pilot.pilotDef.TimeoutRemaining;
                        pilot.pilotDef.SetTimeoutTime(FatigueTime - 1);
                    }
                    if (pilot.pilotDef.TimeoutRemaining != 0)
				    {
                        int FatigueTime = pilot.pilotDef.TimeoutRemaining;
					    pilot.pilotDef.SetTimeoutTime(FatigueTime - 1);
				    }
                }
            }
        }

        public static class Helper
        {
            public static Settings LoadSettings()
            {
                Settings result;
                try
                {
                    using (StreamReader streamReader = new StreamReader("mods/Pilot_Fatigue/settings.json"))
                    {
                        result = JsonConvert.DeserializeObject<Settings>(streamReader.ReadToEnd());
                    }
                }
                catch (Exception ex)
                {
                    Logger.LogError(ex);
                    result = null;
                }
                return result;
            }
            public class Logger
            {
                public static void LogError(Exception ex)
                {
                    using (StreamWriter streamWriter = new StreamWriter("mods/Pilot_Fatigue/Log.txt", true))
                    {
                        streamWriter.WriteLine(string.Concat(new string[]
                        {
                        "Message :",
                        ex.Message,
                        "<br/>",
                        Environment.NewLine,
                        "StackTrace :",
                        ex.StackTrace,
                        Environment.NewLine,
                        "Date :",
                        DateTime.Now.ToString()
                        }));
                        streamWriter.WriteLine(Environment.NewLine + "-----------------------------------------------------------------------------" + Environment.NewLine);
                    }
                }

                public static void LogLine(string line)
                {
                    string path = "mods/Pilot_Fatigue/Log.txt";
                    using (StreamWriter streamWriter = new StreamWriter(path, true))
                    {
                        streamWriter.WriteLine(line + Environment.NewLine + "Date :" + DateTime.Now.ToString());
                        streamWriter.WriteLine(Environment.NewLine + "-----------------------------------------------------------------------------" + Environment.NewLine);
                    }
                }
            }
        }
        internal class ModSettings
        {
            public int FatigueTimeStart = 7;
            public int MoraleModifier = 5;
            public int StartingMorale = 25;
            public int FatigueMinimum = 0;
            public int MoralePositiveTierOne = 5;
            public int MoralePositiveTierTwo = 15;
            public int MoraleNegativeTierOne = -5;
            public int MoraleNegativeTierTwo = -15;
            public double FatigueFactor = 2.5;
            public bool InjuriesHurt = true;
            public int pilot_athletic_FatigueDaysReduction = 1;
            public bool QuirksEnabled = false;
        }
    }
}