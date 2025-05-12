using UnityEngine;
using Harmony;
using System.Collections.Generic;

/// <summary>
/// Harmony patches for monitoring villager changes and triggering Discord webhook updates
/// </summary>
public static class VillagerPatches
{
    // Reference to helper for logging
    private static KCModHelper _helper;
    
    // Initialize logging helper
    public static void Initialize(KCModHelper helper)
    {
        _helper = helper;
        _helper.Log("DEBUG: VillagerPatches initialized and ready to monitor villager events");
    }
    
    // Helper method for logging
    private static void LogDebug(string message)
    {
        if (_helper != null)
        {
            _helper.Log("DEBUG: " + message);
        }
    }

    // Track new villagers
    [HarmonyPatch(typeof(Villager))]
    [HarmonyPatch("Init")]
    public static class VillagerInitPatch
    {
        static void Postfix(Villager __instance)
        {
            // A villager has been initialized (either new or respawned)
            if (__instance != null && __instance.enabled)
            {
                LogDebug($"Villager initialized: {__instance.name} (ID: {__instance.guid})");
                // We could trigger an immediate update here, but it's better to let
                // the normal update cycle handle this to avoid spamming Discord
            }
        }
    }
    
    // Track villager job changes
    [HarmonyPatch(typeof(Villager))]
    [HarmonyPatch("QuitJob")]
    public static class VillagerQuitJobPatch
    {
        static void Postfix(Villager __instance)
        {
            // Villager quit their job
            if (__instance != null && __instance.enabled)
            {
                LogDebug($"Villager quit job: {__instance.name} (ID: {__instance.guid})");
                // Job changes are detected in the normal update cycle
            }
        }
    }
    
    // Track when a villager gets sick
    [HarmonyPatch(typeof(Villager))]
    [HarmonyPatch("BecomeSick")]
    public static class VillagerBecomeSickPatch
    {
        static void Postfix(Villager __instance)
        {
            // Villager became sick
            if (__instance != null && __instance.enabled && __instance.sick)
            {
                LogDebug($"Villager became sick: {__instance.name} (ID: {__instance.guid}) - Sending urgent notification");
                
                GameObject webhookObject = GameObject.Find("DiscordWebhook");
                if (webhookObject != null)
                {
                    DiscordWebhook webhook = webhookObject.GetComponent<DiscordWebhook>();
                    if (webhook != null)
                    {
                        try
                        {
                            // Create a focused update just for this sick villager
                            List<Villager> sickVillager = new List<Villager> { __instance };
                            Dictionary<string, object> embed = new Dictionary<string, object>
                            {
                                ["title"] = "🤒 Villager Became Sick",
                                ["description"] = $"**{__instance.name}** has fallen ill and needs medical attention!",
                                ["color"] = 16738740, // Orange
                                ["fields"] = new List<Dictionary<string, object>>
                                {
                                    new Dictionary<string, object>
                                    {
                                        ["name"] = "Health Status",
                                        ["value"] = "Sick - Requires medical care",
                                        ["inline"] = true
                                    }
                                }
                            };
                            
                            // Add a job field if they have one
                            string jobDesc = "Unemployed";
                            if (__instance.job != null)
                            {
                                // Get job description and sanitize it
                                jobDesc = __instance.job.GetDescription();
                                LogDebug($"Sick villager job (raw): {jobDesc}");
                                
                                // We need to access StripHtmlTags from DiscordWebhook
                                jobDesc = jobDesc.Replace("<b>", "").Replace("</b>", "");
                                
                                // Try to remove any sprite tags using a simple approach
                                int spriteStart = jobDesc.IndexOf("<sprite");
                                while (spriteStart >= 0)
                                {
                                    int spriteEnd = jobDesc.IndexOf(">", spriteStart);
                                    if (spriteEnd >= 0)
                                    {
                                        jobDesc = jobDesc.Remove(spriteStart, spriteEnd - spriteStart + 1);
                                    }
                                    else
                                    {
                                        break;
                                    }
                                    spriteStart = jobDesc.IndexOf("<sprite");
                                }
                                
                                if (string.IsNullOrWhiteSpace(jobDesc))
                                    jobDesc = "Unemployed";
                                
                                LogDebug($"Sick villager job (sanitized): {jobDesc}");
                            }
                            
                            ((List<Dictionary<string, object>>)embed["fields"]).Add(new Dictionary<string, object>
                            {
                                ["name"] = "Job",
                                ["value"] = jobDesc,
                                ["inline"] = true
                            });
                            
                            // Add age field
                            float years = __instance.timeAlive / Weather.inst.TimeInYear();
                            int age = Mathf.FloorToInt(years);
                            ((List<Dictionary<string, object>>)embed["fields"]).Add(new Dictionary<string, object>
                            {
                                ["name"] = "Age",
                                ["value"] = $"{age} years",
                                ["inline"] = true
                            });
                            
                            // Add skills field
                            string skillInfo = "";
                            if (__instance.skills != null && __instance.skills.Count > 0)
                            {
                                for (int j = 0; j < System.Math.Min(__instance.skills.Count, 2); j++)
                                {
                                    if (j > 0) skillInfo += ", ";
                                    skillInfo += __instance.skills.data[j].name;
                                }
                            }
                            
                            ((List<Dictionary<string, object>>)embed["fields"]).Add(new Dictionary<string, object>
                            {
                                ["name"] = "Skills",
                                ["value"] = string.IsNullOrEmpty(skillInfo) ? "None" : skillInfo,
                                ["inline"] = true
                            });
                            
                            // Add thought field
                            string thought = __instance.GetThought();
                            if (string.IsNullOrEmpty(thought))
                            {
                                thought = "None";
                            }
                            else if (thought.Length > 50)
                            {
                                thought = thought.Substring(0, 47) + "...";
                            }
                            
                            ((List<Dictionary<string, object>>)embed["fields"]).Add(new Dictionary<string, object>
                            {
                                ["name"] = "Thoughts",
                                ["value"] = thought,
                                ["inline"] = true
                            });
                            
                            // Manual update for important events
                            List<Dictionary<string, object>> embeds = new List<Dictionary<string, object>> { embed };
                            webhook.SendCustomEmbed(embeds);
                            LogDebug("Sick villager notification sent to Discord");
                        }
                        catch (System.Exception ex)
                        {
                            LogDebug($"Error creating sick villager notification: {ex.Message}");
                        }
                    }
                    else
                    {
                        LogDebug("Failed to send sick villager notification: DiscordWebhook component not found");
                    }
                }
                else
                {
                    LogDebug("Failed to send sick villager notification: DiscordWebhook object not found");
                }
            }
        }
    }
    
    // Track when a villager dies or is removed
    [HarmonyPatch(typeof(Villager))]
    [HarmonyPatch("Shutdown")]
    public static class VillagerShutdownPatch
    {
        static void Prefix(Villager __instance)
        {
            // Villager is being removed from the game
            if (__instance != null && __instance.enabled)
            {
                // Removed villagers are detected in the normal update cycle
                // by comparing the current villagers to the previous snapshot
                LogDebug($"Villager is being removed from the game: {__instance.name} (ID: {__instance.guid})");
            }
        }
    }
    
    // Track when a player destroys a villager (death)
    [HarmonyPatch(typeof(Player))]
    [HarmonyPatch("DestroyPerson")]
    public static class DestroyPersonPatch
    {
        static void Prefix(Villager vilgr, bool showCorpse)
        {
            if (vilgr != null && vilgr.enabled)
            {
                LogDebug($"Villager '{vilgr.name}' is being destroyed (death). showCorpse={showCorpse}");
                
                // Try to collect more information about the death
                string deathReason = "Unknown";
                
                // Health status
                if (vilgr.sick)
                {
                    deathReason = "Plague";
                }
                
                // Starvation
                if (vilgr.missedMeal > 1)
                {
                    deathReason = "Starvation";
                }
                
                // Age check
                float lifeExpectancy = vilgr.life / Weather.inst.TimeInYear();
                float currentAge = vilgr.timeAlive / Weather.inst.TimeInYear();
                if (currentAge >= lifeExpectancy - 5 || currentAge >= 65)
                {
                    if (currentAge >= 90)
                        deathReason = "Extreme Old Age (90+ years)";
                    else if (currentAge >= 80)
                        deathReason = "Very Old Age (80+ years)";
                    else if (currentAge >= 70)
                        deathReason = "Old Age (70+ years)";
                    else
                        deathReason = "Natural Causes";
                }
                
                // Environmental checks
                if (DragonSpawn.inst != null && DragonSpawn.inst.currentDragons != null && DragonSpawn.inst.currentDragons.Count > 0)
                {
                    // Check for dragons in close proximity
                    for (int i = 0; i < DragonSpawn.inst.currentDragons.Count; i++)
                    {
                        var dragon = DragonSpawn.inst.currentDragons.data[i];
                        if (dragon != null && Vector3.Distance(dragon.transform.position, vilgr.Pos) < 15f)
                        {
                            deathReason = "Dragon Attack";
                            break;
                        }
                    }
                }
                
                // Water check (death by drowning)
                Cell cell = World.inst.GetCellData(vilgr.Pos);
                if (cell != null && cell.z > 0 && cell.z < World.inst.GridHeight && cell.x > 0 && cell.x < World.inst.GridWidth)
                {
                    // Check if cell is surrounded by water, which might indicate drowning
                    Cell[] neighbors = new Cell[4];
                    World.inst.GetNeighborCells(cell, ref neighbors);
                    bool nearWater = false;
                    for (int i = 0; i < neighbors.Length; i++)
                    {
                        // Check if any neighbor cells are water
                        if (neighbors[i] != null && neighbors[i].deepWater)
                        {
                            nearWater = true;
                            break;
                        }
                    }
                    
                    if (nearWater)
                        deathReason = "Drowning";
                }
                
                LogDebug($"Determined death reason for {vilgr.name}: {deathReason}");
            }
        }
    }
    
    // Monitor home changes
    [HarmonyPatch(typeof(Villager))]
    [HarmonyPatch("SetHome")]
    public static class VillagerSetHomePatch
    {
        static void Postfix(Villager __instance)
        {
            // Villager got a new home
            if (__instance != null && __instance.enabled && __instance.Residence != null)
            {
                // Home changes are detected in the normal update cycle
            }
        }
    }
} 