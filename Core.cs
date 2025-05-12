using UnityEngine;
using System.Reflection;
using Harmony;
using System.Collections.Generic;
using System;

public class Core : MonoBehaviour
{
    private static Core _instance;
    private static KCModHelper _helper;
    
    // Replace this URL with your Discord webhook URL
    private string discordWebhookUrl = "https://discord.com/api/webhooks/";
    
    // Default URL for comparison
    private string defaultWebhookUrl = "https://discord.com/api/webhooks/your-webhook-url-here";
    
    // Update interval in seconds (how often to send updates)
    private float updateInterval = 60f; // Default: 1 minute
    
    // If true, sends all villagers; if false, only sends changed villagers
    private bool sendFullUpdates = false;
    
    // Maximum number of villagers to show in Discord updates
    private int maxVillagersToShow = 25; // Default: 25 villagers (reduced from 50 to be more conservative)
    
    // Set to false to disable Discord webhook functionality
    private bool enableDiscordWebhook = true;
    
    // Called before the main game scene loads
    public void Preload(KCModHelper helper)
    {
        _helper = helper;
        helper.Log("Villager Discord Webhook mod loading...");
        _instance = this;
        
        // Set up Harmony
        var harmony = HarmonyInstance.Create("com.yourname.villagerdiscord");
        harmony.PatchAll(Assembly.GetExecutingAssembly());
        
        // Initialize VillagerPatches with the helper for logging
        VillagerPatches.Initialize(helper);
    }

    // Called after the main game scene loads
    public void SceneLoaded(KCModHelper helper)
    {
        _helper = helper;
        helper.Log("Scene loaded!");
        
        // Initialize Discord webhook if enabled
        if (enableDiscordWebhook && !string.IsNullOrEmpty(discordWebhookUrl) && discordWebhookUrl != defaultWebhookUrl)
        {
            DiscordWebhook.Initialize(helper, discordWebhookUrl, updateInterval, sendFullUpdates, maxVillagersToShow);
            helper.Log("Discord webhook initialized with URL: " + discordWebhookUrl);
            helper.Log("Update interval: " + updateInterval + " seconds");
            helper.Log("Send full updates: " + sendFullUpdates);
            helper.Log("Max villagers to show: " + maxVillagersToShow);
            
            // Send a startup notification to Discord
            SendStartupNotification();
        }
        else if (enableDiscordWebhook)
        {
            if (discordWebhookUrl == defaultWebhookUrl)
            {
                helper.Log("Discord webhook not initialized: Please replace the default URL with your actual webhook URL in Core.cs!");
            }
            else
            {
                helper.Log("Discord webhook not initialized: Webhook URL is empty!");
            }
        }
        else
        {
            helper.Log("Discord webhook is disabled.");
        }
    }
    
    /// <summary>
    /// Sends a mod startup notification to Discord
    /// </summary>
    private void SendStartupNotification()
    {
        _helper.Log("Sending startup notification to Discord webhook...");
        
        GameObject webhookObject = GameObject.Find("DiscordWebhook");
        if (webhookObject != null)
        {
            DiscordWebhook webhook = webhookObject.GetComponent<DiscordWebhook>();
            if (webhook != null)
            {
                // Create a nicely formatted startup embed
                Dictionary<string, object> embed = new Dictionary<string, object>
                {
                    ["title"] = "🏰 Villager Discord Webhook Started",
                    ["description"] = "The Villager Discord Webhook mod has been initialized and is now monitoring your kingdom!",
                    ["color"] = 5763719, // Royal purple
                    ["fields"] = new List<Dictionary<string, object>>
                    {
                        new Dictionary<string, object>
                        {
                            ["name"] = "Update Interval",
                            ["value"] = $"{updateInterval} seconds",
                            ["inline"] = true
                        },
                        new Dictionary<string, object>
                        {
                            ["name"] = "Update Mode",
                            ["value"] = sendFullUpdates ? "Full Updates" : "Changed Villagers Only",
                            ["inline"] = true
                        },
                        new Dictionary<string, object>
                        {
                            ["name"] = "Max Villagers",
                            ["value"] = $"{maxVillagersToShow}",
                            ["inline"] = true
                        },
                        new Dictionary<string, object>
                        {
                            ["name"] = "Game Time",
                            ["value"] = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),
                            ["inline"] = true
                        }
                    },
                    ["footer"] = new Dictionary<string, object>
                    {
                        ["text"] = "Experimental"
                    }
                };
                
                List<Dictionary<string, object>> embeds = new List<Dictionary<string, object>> { embed };
                webhook.SendCustomEmbed(embeds);
                
                _helper.Log("Startup notification sent to Discord webhook");
            }
            else
            {
                _helper.Log("Failed to send startup notification: DiscordWebhook component not found");
            }
        }
        else
        {
            _helper.Log("Failed to send startup notification: DiscordWebhook object not found");
        }
    }
    
    /// <summary>
    /// Force sends an update to Discord
    /// </summary>
    public static void SendUpdate()
    {
        // This could be connected to an in-game UI button or command
        GameObject webhookObject = GameObject.Find("DiscordWebhook");
        if (webhookObject != null)
        {
            DiscordWebhook webhook = webhookObject.GetComponent<DiscordWebhook>();
            if (webhook != null)
            {
                webhook.SendVillagerUpdate(true);
                _helper.Log("Manual update sent to Discord");
            }
        }
    }
}

