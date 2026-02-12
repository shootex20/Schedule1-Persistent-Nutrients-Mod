using MelonLoader;
using HarmonyLib;
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using Il2CppScheduleOne.ObjectScripts;
using Il2CppScheduleOne.Persistence;
using Il2CppScheduleOne.Employees;
using Il2CppScheduleOne.Growing;
using Newtonsoft.Json;
using MelonLoader.Utils;
using Il2CppScheduleOne.ItemFramework;

[assembly: MelonInfo(typeof(PersistentNutrients.PersistentNutrients), "PersistentNutrients Mod", "1.0.0", "Shootex20")]
namespace PersistentNutrients
{
    public class PersistentNutrients : MelonMod
    {
        private static readonly Dictionary<Guid, SavedPotFertilizerData> persistentFertilizers = new Dictionary<Guid, SavedPotFertilizerData>();
        private static string currentSaveSlot = "default_slot";
        public static bool DebugMode = false;

        private static string DataFilePath
        {
            get
            {
                string sanitized = currentSaveSlot;

                foreach (char c in Path.GetInvalidFileNameChars())
                {
                    sanitized = sanitized.Replace(c, '_');
                }

                return Path.Combine(MelonEnvironment.UserDataDirectory, $"PersistentNutrients_Fertilizers_{sanitized}.json");
            }
        }

        public override void OnInitializeMelon()
        {
            LoggerInstance.Msg("PersistentNutrients Mod initialized - Fertilizer will now persist with soil!");
        }

        public override void OnApplicationQuit()
        {
            SaveFertilizerData();
        }

        [HarmonyPatch(typeof(Pot), "OnPlantFullyHarvested")]
        public static class OnPlantFullyHarvested_Patch
        {
            [HarmonyPrefix]
            public static void Prefix(Pot __instance)
            {
                if (__instance == null || __instance.GUID == null)
                    return;

                Guid potGuid = new Guid(__instance.GUID.ToByteArray());

                // Save fertilizer data if soil still has uses remaining
                if (__instance._remainingSoilUses > 0 && __instance.AppliedAdditives != null && __instance.Plant != null)
                {
                    SaveFertilizers(__instance, potGuid);
                }
                // Clean up if soil is depleted
                else if (__instance._remainingSoilUses <= 0)
                {
                    if (persistentFertilizers.ContainsKey(potGuid))
                    {
                        persistentFertilizers.Remove(potGuid);
                        MelonLogger.Msg($"[PersistentNutrientsMod] Soil depleted, cleared fertilizer data for pot {potGuid}");
                    }
                }
            }
        }

        [HarmonyPatch(typeof(Pot), "PlantSeed_Server")]
        public static class PlantSeed_Patch
        {
            [HarmonyPostfix]
            public static void Postfix(Pot __instance, string seedID, float normalizedSeedProgress)
            {
                if (__instance == null || __instance.GUID == null)
                    return;

                Guid potGuid = new Guid(__instance.GUID.ToByteArray());

                if (persistentFertilizers.TryGetValue(potGuid, out var fertData))
                {
                    MelonLogger.Msg($"[PersistentNutrients Mod] Found saved fertilizer data for pot {potGuid}, starting delayed restoration...");
                    MelonCoroutines.Start(WaitAndRestore(__instance, fertData, potGuid));
                }
            }

            private static IEnumerator WaitAndRestore(Pot pot, SavedPotFertilizerData fertData, Guid potGuid)
            {
                int attempts = 0;
                while (attempts < 50 && (pot == null || pot.Plant == null))
                {
                    yield return null;
                    attempts++;
                }

                if (pot != null && pot.Plant != null)
                {
                    RestoreFertilizers(pot, fertData);
                }
                else
                {
                    MelonLogger.Warning($"[PersistentNutrients Mod] Failed to restore fertilizers for pot {potGuid} - Plant not created after {attempts} frames");
                }
            }
        }

        [HarmonyPatch(typeof(Pot), "CanApplyAdditive")]
        public static class CanApplyAdditive_Patch
        {
            [HarmonyPrefix]
            public static bool Prefix(Pot __instance, AdditiveDefinition additiveDef, ref string invalidReason, ref bool __result)
            {
                if (__instance == null || __instance.GUID == null || additiveDef == null)
                    return true;

                // Only check for fertilizers, allow other additives like Speed Grow
                if (!additiveDef.Name.ToLower().Contains("fertilizer"))
                    return true;

                Guid potGuid = new Guid(__instance.GUID.ToByteArray());

                // Check if this soil already has saved fertilizer data
                if (persistentFertilizers.ContainsKey(potGuid))
                {
                    invalidReason = "This soil is already fertilized!";
                    __result = false;
                    return false;
                }

                return true;
            }
        }

        [HarmonyPatch(typeof(Botanist), "GetGrowContainersForAdditives")]
        public static class BotanistGetGrowContainersForAdditives_Patch
        {
            [HarmonyPostfix]
            public static void Postfix(ref Il2CppSystem.Collections.Generic.List<GrowContainer> __result)
            {
                if (__result == null || __result.Count == 0)
                    return;

                // Filter out pots that already have saved fertilizer data
                var containersToRemove = new List<GrowContainer>();

                foreach (var container in __result)
                {
                    if (container == null || container.GUID == null)
                        continue;

                    // Check if it's a pot
                    var pot = container.TryCast<Pot>();
                    if (pot != null)
                    {
                        Guid potGuid = new Guid(pot.GUID.ToByteArray());

                        // If this pot already has fertilizer data, don't let botanist fertilize it
                        if (persistentFertilizers.ContainsKey(potGuid))
                        {
                            containersToRemove.Add(container);
                            MelonLogger.Msg($"[PersistentNutrients Mod] Prevented botanist from fertilizing pot {potGuid} - soil already fertilized");
                        }
                    }
                }

                foreach (var container in containersToRemove)
                {
                    __result.Remove(container);
                }
            }
        }

        private static void SaveFertilizers(Pot pot, Guid potGuid)
        {
            if (pot.AppliedAdditives == null || pot.AppliedAdditives.Count == 0 || pot.Plant == null)
                return;

            var savedFertilizers = new List<AdditiveData>();
            bool hasSpeedGrow = false;

            foreach (var additive in pot.AppliedAdditives)
            {
                if (additive != null && additive.Name.ToLower().Contains("fertilizer"))
                {
                    savedFertilizers.Add(new AdditiveData
                    {
                        Name = additive.Name,
                        YieldChange = additive.YieldMultiplier,
                        QualityChange = additive.QualityChange,
                        InstantGrowth = additive.InstantGrowth
                    });
                }

                if (additive != null && additive.InstantGrowth > 0f)
                {
                    hasSpeedGrow = true;
                }
            }

            if (savedFertilizers.Count > 0)
            {
                persistentFertilizers[potGuid] = new SavedPotFertilizerData
                {
                    Fertilizers = savedFertilizers,
                    YieldLevel = pot.Plant.YieldMultiplier,
                    QualityLevel = pot.Plant.QualityLevel,
                    HadSpeedGrow = hasSpeedGrow
                };

                MelonLogger.Msg($"[PersistentNutrients Mod] Saved {savedFertilizers.Count} fertilizer(s) for pot {potGuid} (Yield: {pot.Plant.YieldMultiplier:F2}, Quality: {pot.Plant.QualityLevel:F2}, SpeedGrow: {hasSpeedGrow})");
            }
        }

        private static void RestoreFertilizers(Pot pot, SavedPotFertilizerData fertData)
        {
            if (pot.Plant == null || fertData?.Fertilizers == null)
                return;

            pot.Plant.YieldMultiplier = fertData.YieldLevel;
            pot.Plant.QualityLevel = fertData.QualityLevel;

            if (fertData.HadSpeedGrow)
            {
                pot.Plant.SetNormalizedGrowthProgress(0.5f);
            }
            else
            {
                pot.Plant.SetNormalizedGrowthProgress(0f);
            }

            MelonLogger.Msg($"[PersistentNutrients Mod] Restored fertilizers to pot: Yield {fertData.YieldLevel:F2}, Quality {fertData.QualityLevel:F2}, SpeedGrow: {fertData.HadSpeedGrow}");
        }

        [HarmonyPatch(typeof(SaveManager), "Save", new Type[] { typeof(string) })]
        public static class SaveManager_SaveWithPath_Patch
        {
            [HarmonyPrefix]
            public static void Prefix(string saveFolderPath)
            {
                MelonLogger.Msg($"[PersistentNutrients Mod] Game saving, persisting fertilizer data...");
                SaveFertilizerData();
            }
        }

        [HarmonyPatch(typeof(SaveManager), "Save", new Type[] { })]
        public static class SaveManager_SaveNoArgs_Patch
        {
            [HarmonyPrefix]
            public static void Prefix()
            {
                MelonLogger.Msg($"[PersistentNutrients Mod] Game saving, persisting fertilizer data...");
                SaveFertilizerData();
            }
        }

        [HarmonyPatch(typeof(LoadManager), "StartGame")]
        public static class LoadManager_StartGame_Patch
        {
            [HarmonyPostfix]
            public static void Postfix(SaveInfo info, bool allowLoadStacking)
            {
                if (DebugMode)
                {
                    MelonLogger.Msg("[PersistentNutrients Mod] LoadManager.StartGame Postfix triggered.");
                }

                if (info != null)
                {
                    string slotIdentifier = $"SaveGame_{info.SaveSlotNumber}";

                    if (DebugMode)
                    {
                        MelonLogger.Msg($"[PersistentNutrients Mod] Using derived slotIdentifier: '{slotIdentifier}'");
                    }

                    currentSaveSlot = slotIdentifier;
                    LoadFertilizerData();
                }
                else
                {
                    if (DebugMode)
                    {
                        MelonLogger.Msg("[PersistentNutrients Mod] LoadManager.StartGame: SaveInfo was null. Using 'NewGame_Initial'.");
                    }

                    currentSaveSlot = "NewGame_Initial";
                    persistentFertilizers.Clear();
                }
            }
        }

        private static void SaveFertilizerData()
        {
            try
            {
                var dataToSave = new FertilizerDataCollection();
                foreach (var kvp in persistentFertilizers)
                {
                    dataToSave.Entries.Add(new FertilizerEntry
                    {
                        PotGUID = kvp.Key.ToString(),
                        Data = kvp.Value
                    });
                }

                string json = JsonConvert.SerializeObject(dataToSave, Formatting.Indented);
                File.WriteAllText(DataFilePath, json);

                MelonLogger.Msg($"[PersistentNutrients Mod] Saved {dataToSave.Entries.Count} pot(s) with persistent fertilizers to {DataFilePath}");
            }
            catch (Exception ex)
            {
                MelonLogger.Error($"[PersistentNutrients Mod] Error saving fertilizer data: {ex.Message}");
            }
        }

        private static void LoadFertilizerData()
        {
            try
            {
                if (!File.Exists(DataFilePath))
                {
                    MelonLogger.Msg($"[PersistentNutrients Mod] No save file found at {DataFilePath}, starting fresh.");
                    persistentFertilizers.Clear();
                    return;
                }

                string json = File.ReadAllText(DataFilePath);
                var loadedData = JsonConvert.DeserializeObject(json, typeof(FertilizerDataCollection)) as FertilizerDataCollection;

                persistentFertilizers.Clear();

                if (loadedData?.Entries != null)
                {
                    foreach (var entry in loadedData.Entries)
                    {
                        if (Guid.TryParse(entry.PotGUID, out Guid guid))
                        {
                            persistentFertilizers[guid] = entry.Data;
                        }
                    }
                }

                MelonLogger.Msg($"[PersistentNutrients Mod] Loaded {persistentFertilizers.Count} pot(s) with persistent fertilizers from {DataFilePath}");
            }
            catch (Exception ex)
            {
                MelonLogger.Error($"[PersistentNutrients Mod] Error loading fertilizer data: {ex.Message}");
                persistentFertilizers.Clear();
            }
        }
    }

    [Serializable]
    public class AdditiveData
    {
        public string Name { get; set; }
        public float YieldChange { get; set; }
        public float QualityChange { get; set; }
        public float InstantGrowth { get; set; }
    }

    [Serializable]
    public class SavedPotFertilizerData
    {
        public List<AdditiveData> Fertilizers { get; set; }
        public float YieldLevel { get; set; }
        public float QualityLevel { get; set; }
        public bool HadSpeedGrow { get; set; }
    }

    [Serializable]
    public class FertilizerEntry
    {
        public string PotGUID { get; set; }
        public SavedPotFertilizerData Data { get; set; }
    }

    [Serializable]
    public class FertilizerDataCollection
    {
        public List<FertilizerEntry> Entries { get; set; } = new List<FertilizerEntry>();
    }
}
