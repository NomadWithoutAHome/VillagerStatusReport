using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;
using System.Linq;
using Harmony;
using System.Reflection;

/// <summary>
/// Handles sending information to Discord via webhooks
/// </summary>
public class DiscordWebhook : MonoBehaviour
{
    private static DiscordWebhook _instance;
    private static KCModHelper _helper;
    
    // Discord webhook URL - set this in Core.cs
    private string webhookUrl = "";
    
    // Discord embed limits
    private const int MAX_EMBEDS_PER_MESSAGE = 10;
    private const int MAX_EMBED_TITLE_LENGTH = 256;
    private const int MAX_EMBED_DESCRIPTION_LENGTH = 4096;
    private const int MAX_EMBED_FIELDS = 25;
    private const int MAX_EMBED_FIELD_NAME_LENGTH = 256;
    private const int MAX_EMBED_FIELD_VALUE_LENGTH = 1024;
    private const int MAX_EMBED_FOOTER_LENGTH = 2048;
    private const int MAX_TOTAL_CHARACTERS = 6000;
    
    // Tracking villagers for change detection
    private Dictionary<Guid, VillagerSnapshot> villagerSnapshots = new Dictionary<Guid, VillagerSnapshot>();
    
    // Timer for periodic updates
    private float updateTimer = 0f;
    private float updateInterval = 30f; // Update every 30 seconds by default
    private bool sendFullUpdates = false; // If true, sends all villagers; if false, only sends changed villagers
    private int maxVillagersToShow = 50; // Maximum number of villagers to show, default 50
    
    /// <summary>
    /// Initializes the DiscordWebhook system
    /// </summary>
    public static void Initialize(KCModHelper helper, string webhookUrl, float updateInterval = 30f, bool sendFullUpdates = false, int maxVillagersToShow = 50)
    {
        if (_instance != null)
            return;
            
        GameObject go = new GameObject("DiscordWebhook");
        _instance = go.AddComponent<DiscordWebhook>();
        _helper = helper;
        
        _instance.webhookUrl = webhookUrl;
        _instance.updateInterval = updateInterval;
        _instance.sendFullUpdates = sendFullUpdates;
        _instance.maxVillagersToShow = maxVillagersToShow;
        
        DontDestroyOnLoad(go);
        
        _helper.Log("Discord webhook initialized with URL: " + webhookUrl);
    }
    
    /// <summary>
    /// Captures a snapshot of the current state of all villagers for change tracking
    /// </summary>
    private void CaptureVillagerSnapshots()
    {
        Dictionary<Guid, VillagerSnapshot> newSnapshots = new Dictionary<Guid, VillagerSnapshot>();
        
        if (Villager.villagers != null)
        {
            _helper.Log($"DEBUG: Capturing snapshots for {Villager.villagers.Count} villagers");
            
            for (int i = 0; i < Villager.villagers.Count; i++)
            {
                Villager v = Villager.villagers.data[i];
                if (v != null && v.enabled)
                {
                    VillagerSnapshot snapshot = new VillagerSnapshot(v);
                    newSnapshots[v.guid] = snapshot;
                    _helper.Log($"DEBUG: Captured snapshot for villager {v.name} (ID: {v.guid}), job: {(v.job != null ? v.job.GetDescription() : "None")}, age: {v.timeAlive / Weather.inst.TimeInYear()} years");
                }
            }
        }
        else
        {
            _helper.Log("DEBUG: No villagers found when capturing snapshots");
        }
        
        villagerSnapshots = newSnapshots;
    }
    
    private void Update()
    {
        updateTimer += Time.deltaTime;
        
        if (updateTimer >= updateInterval)
        {
            updateTimer = 0f;
            
            if (sendFullUpdates)
            {
                SendVillagerUpdate(true);
            }
            else
            {
                SendChangedVillagersUpdate();
            }
        }
    }
    
    /// <summary>
    /// Sends a villager update to Discord
    /// </summary>
    public void SendVillagerUpdate(bool forceFullUpdate = false)
    {
        if (string.IsNullOrEmpty(webhookUrl))
        {
            _helper.Log("Discord webhook URL is not set!");
            return;
        }
        
        if (Villager.villagers == null || Villager.villagers.Count == 0)
        {
            _helper.Log("No villagers to send update for");
            return;
        }
        
        _helper.Log($"DEBUG: Preparing villager update for {Villager.villagers.Count} villagers (forceFullUpdate: {forceFullUpdate})");
        
        List<Dictionary<string, object>> embeds = CreateVillagerEmbeds();
        
        if (embeds.Count > 0)
        {
            _helper.Log($"DEBUG: Sending {embeds.Count} embeds to Discord webhook");
            SendWebhookWithEmbeds(embeds);
            
            // Update our snapshots after sending
            CaptureVillagerSnapshots();
        }
        else
        {
            _helper.Log("DEBUG: No embeds to send in this update");
        }
    }
    
    /// <summary>
    /// Sends only information about villagers that have changed since last update
    /// </summary>
    public void SendChangedVillagersUpdate()
    {
        if (string.IsNullOrEmpty(webhookUrl))
        {
            _helper.Log("Discord webhook URL is not set!");
            return;
        }
        
        if (Villager.villagers == null || Villager.villagers.Count == 0)
        {
            _helper.Log("No villagers to send update for");
            return;
        }
        
        // If no snapshots exist yet, capture them all now and send a full update
        if (villagerSnapshots.Count == 0)
        {
            CaptureVillagerSnapshots();
            SendVillagerUpdate(true);
            return;
        }
        
        List<Villager> changedVillagers = new List<Villager>();
        List<Guid> removedVillagers = new List<Guid>(villagerSnapshots.Keys);
        
        // Check for changed or new villagers
        for (int i = 0; i < Villager.villagers.Count; i++)
        {
            Villager v = Villager.villagers.data[i];
            if (v != null && v.enabled)
            {
                removedVillagers.Remove(v.guid);
                
                if (villagerSnapshots.TryGetValue(v.guid, out VillagerSnapshot snapshot))
                {
                    if (HasVillagerChanged(v, snapshot))
                    {
                        changedVillagers.Add(v);
                    }
                }
                else
                {
                    // New villager
                    changedVillagers.Add(v);
                }
            }
        }
        
        // Create embeds for changed villagers 
        List<Dictionary<string, object>> embeds = new List<Dictionary<string, object>>();
        
        if (changedVillagers.Count > 0)
        {
            embeds.AddRange(CreateEmbedsForVillagers(changedVillagers));
        }
        
        // Add embeds for removed villagers
        if (removedVillagers.Count > 0)
        {
            foreach (Guid guid in removedVillagers)
            {
                if (villagerSnapshots.TryGetValue(guid, out VillagerSnapshot snapshot))
                {
                    // Sanitize job description to remove HTML and sprite tags
                    string jobDesc = StripHtmlTags(snapshot.JobDescription);
                    
                    // Handle empty job descriptions
                    if (string.IsNullOrWhiteSpace(jobDesc))
                        jobDesc = "Unemployed";
                    
                    // Try to get a more accurate death reason from kingdom logs
                    string deathReason = GetDeathReasonFromKingdomLogs(snapshot.Name);
                    
                    // If we couldn't find a reason in logs, use the inferred reason from the snapshot
                    if (string.IsNullOrEmpty(deathReason))
                        deathReason = snapshot.DeathReason;
                    
                    Dictionary<string, object> embed = new Dictionary<string, object>
                    {
                        ["title"] = TruncateString($"Villager Lost: {snapshot.Name}", MAX_EMBED_TITLE_LENGTH),
                        ["description"] = TruncateString(
                            $"A villager has left or died.\n" +
                            $"**Name:** {snapshot.Name}\n" +
                            $"**Age:** {Math.Floor(snapshot.Age)} years\n" +
                            $"**Profession:** {jobDesc}\n" +
                            $"**Cause:** {deathReason}",
                            MAX_EMBED_DESCRIPTION_LENGTH),
                        ["color"] = 16711680 // Red color
                    };
                    
                    embeds.Add(embed);
                    
                    if (embeds.Count >= MAX_EMBEDS_PER_MESSAGE)
                        break;
                }
            }
        }
        
        // Send the embeds if any
        if (embeds.Count > 0)
        {
            SendWebhookWithEmbeds(embeds);
            
            // Update our snapshots after sending
            CaptureVillagerSnapshots();
        }
    }
    
    /// <summary>
    /// Checks if a villager has changed in a meaningful way since the last snapshot
    /// </summary>
    private bool HasVillagerChanged(Villager v, VillagerSnapshot snapshot)
    {
        // Check for job change
        string currentJobDesc = v.job != null ? v.job.GetDescription() : "";
        if (currentJobDesc != snapshot.JobDescription)
            return true;
            
        // Check for home change
        bool hasHome = v.Residence != null;
        if (hasHome != snapshot.HasHome)
            return true;
            
        // Check for significant health change
        if (v.sick != snapshot.IsSick)
            return true;
            
        // Check if skills have changed significantly
        if (v.skills.Count != snapshot.SkillCount)
            return true;
            
        // Check age difference (only report if a year has passed)
        float years = v.timeAlive / Weather.inst.TimeInYear();
        if (Math.Floor(years) > Math.Floor(snapshot.Age))
            return true;
            
        return false;
    }
    
    /// <summary>
    /// Creates embeds for all villagers
    /// </summary>
    private List<Dictionary<string, object>> CreateVillagerEmbeds()
    {
        List<Villager> allVillagers = new List<Villager>();
        
        for (int i = 0; i < Villager.villagers.Count; i++)
        {
            Villager v = Villager.villagers.data[i];
            if (v != null && v.enabled)
            {
                allVillagers.Add(v);
            }
        }
        
        return CreateEmbedsForVillagers(allVillagers);
    }
    
    /// <summary>
    /// Creates embeds for a specific list of villagers
    /// </summary>
    private List<Dictionary<string, object>> CreateEmbedsForVillagers(List<Villager> villagers)
    {
        List<Dictionary<string, object>> embeds = new List<Dictionary<string, object>>();
        
        // Sort villagers by various criteria to group them logically
        villagers.Sort((a, b) => {
            // First by job category
            string jobA = a.job != null ? a.job.GetDescription() : "";
            string jobB = b.job != null ? b.job.GetDescription() : "";
            int jobComp = string.Compare(jobA, jobB);
            if (jobComp != 0) return jobComp;
            
            // Then by name
            return string.Compare(a.name, b.name);
        });
        
        // Limit the number of villagers to avoid Discord's payload limits
        int maxVillagersToInclude = Math.Min(villagers.Count, maxVillagersToShow);
        if (villagers.Count > maxVillagersToInclude)
        {
            _helper.Log($"WARNING: Limiting webhook to {maxVillagersToInclude} villagers out of {villagers.Count} total to respect the configured limit");
            villagers = villagers.GetRange(0, maxVillagersToInclude);
        }
        
        // Log a breakdown of job types to help understand what kinds of villagers we're including
        if (villagers.Count > 10)
        {
            Dictionary<string, int> jobCounts = new Dictionary<string, int>();
            foreach (var v in villagers)
            {
                string job = v.job != null ? StripHtmlTags(v.job.GetDescription()) : "Unemployed";
                if (string.IsNullOrWhiteSpace(job))
                    job = "Unemployed";
                
                if (!jobCounts.ContainsKey(job))
                    jobCounts[job] = 0;
                
                jobCounts[job]++;
            }
            
            _helper.Log($"DEBUG: Villager job breakdown for this update:");
            foreach (var pair in jobCounts.OrderByDescending(p => p.Value))
            {
                _helper.Log($"DEBUG:   - {pair.Key}: {pair.Value} villagers");
            }
        }
        
        // Create summary embed
        Dictionary<string, object> summaryEmbed = new Dictionary<string, object>
        {
            ["title"] = "Villager Status Report",
            ["description"] = $"Total Villagers: {Villager.villagers.Count}" + 
                (villagers.Count < Villager.villagers.Count ? $" (showing {villagers.Count} out of {Villager.villagers.Count})" : ""),
            ["color"] = 3447003, // Blue
            ["timestamp"] = DateTime.UtcNow.ToString("o")
        };
        embeds.Add(summaryEmbed);
        
        int totalCharCount = summaryEmbed["title"].ToString().Length + summaryEmbed["description"].ToString().Length;
        int remainingCharBudget = MAX_TOTAL_CHARACTERS - totalCharCount;
        
        // Create villager detail embeds, but monitor character count
        int villagersProcessed = 0;
        // Use a separate list to collect all villager embeds
        List<Dictionary<string, object>> villagerEmbeds = new List<Dictionary<string, object>>();
        
        while (villagersProcessed < villagers.Count && remainingCharBudget > 500 && villagerEmbeds.Count < MAX_EMBEDS_PER_MESSAGE - 1)
        {
            Dictionary<string, object> embed = new Dictionary<string, object>
            {
                // Use a temporary title that will be updated later
                ["title"] = $"Villagers",
                ["color"] = 3447003, // Blue
                ["fields"] = new List<Dictionary<string, object>>()
            };
            
            remainingCharBudget -= embed["title"].ToString().Length;
            
            var fields = (List<Dictionary<string, object>>)embed["fields"];
            
            int maxPossibleInThisEmbed = Math.Min(10, villagers.Count - villagersProcessed);
            int villagersInThisEmbed = 0;
            
            for (int i = 0; i < maxPossibleInThisEmbed; i++)
            {
                int villagerIndex = villagersProcessed + i;
                if (villagerIndex >= villagers.Count)
                    break;
                    
                Villager v = villagers[villagerIndex];
                
                // Calculate age in years
                float years = v.timeAlive / Weather.inst.TimeInYear();
                int age = Mathf.FloorToInt(years);
                
                // Get job information (strip HTML tags for Discord)
                string jobDesc = v.job != null ? StripHtmlTags(v.job.GetDescription()) : "Unemployed";
                if (string.IsNullOrWhiteSpace(jobDesc))
                    jobDesc = "Unemployed";
                
                // Get home information
                string homeInfo = v.Residence != null ? "Has Home" : "Homeless";
                
                // Get health information
                string healthInfo = v.sick ? "Sick" : "Healthy";
                
                // Get top skills - limit to 2 max
                string skillInfo = "";
                if (v.skills != null && v.skills.Count > 0)
                {
                    for (int j = 0; j < Math.Min(v.skills.Count, 2); j++)
                    {
                        if (j > 0) skillInfo += ", ";
                        skillInfo += v.skills.data[j].name;
                    }
                }
                
                // Get current thought/status but truncate it to keep embed sizes reasonable
                string thought = v.GetThought();
                if (string.IsNullOrEmpty(thought))
                {
                    thought = "None";
                }
                else if (thought.Length > 50)
                {
                    thought = thought.Substring(0, 47) + "...";
                }
                
                string villagerName = TruncateString(v.name, MAX_EMBED_FIELD_NAME_LENGTH);
                string villagerInfo = TruncateString(
                    $"**Age:** {age} years\n" +
                    $"**Job:** {jobDesc}\n" +
                    $"**Status:** {homeInfo}, {healthInfo}\n" +
                    $"**Skills:** {(string.IsNullOrEmpty(skillInfo) ? "None" : skillInfo)}\n" +
                    $"**Thoughts:** {thought}",
                    MAX_EMBED_FIELD_VALUE_LENGTH
                );
                
                // Check if adding this villager would exceed our character budget
                int villagerCharCount = villagerName.Length + villagerInfo.Length;
                if (remainingCharBudget - villagerCharCount < 200) // Keep a 200 char safety buffer
                {
                    _helper.Log($"DEBUG: Character limit approaching, stopping at {villagersProcessed + villagersInThisEmbed} villagers with {remainingCharBudget} chars left");
                    break;
                }
                
                fields.Add(new Dictionary<string, object>
                {
                    ["name"] = villagerName,
                    ["value"] = villagerInfo,
                    ["inline"] = false
                });
                
                remainingCharBudget -= villagerCharCount;
                villagersInThisEmbed++;
                
                // Ensure we don't exceed the field limit
                if (fields.Count >= MAX_EMBED_FIELDS)
                    break;
            }
            
            if (fields.Count > 0)
            {
                villagerEmbeds.Add(embed);
                villagersProcessed += villagersInThisEmbed;
                
                // Update the summary to reflect actual number shown
                if (villagersProcessed < villagers.Count)
                {
                    summaryEmbed["description"] = $"Total Villagers: {Villager.villagers.Count} (showing {villagersProcessed} to avoid message size limits)";
                }
            }
            else
            {
                // No villagers could fit in this embed due to character limits
                break;
            }
        }
        
        // Now that we know the exact number of embeds, update their titles
        int totalEmbeds = villagerEmbeds.Count;
        for (int i = 0; i < totalEmbeds; i++)
        {
            villagerEmbeds[i]["title"] = $"Villagers ({i + 1}/{totalEmbeds})";
        }
        
        // Add all villager embeds to the main embeds list
        embeds.AddRange(villagerEmbeds);
        
        _helper.Log($"DEBUG: Final character count estimate: {MAX_TOTAL_CHARACTERS - remainingCharBudget} out of {MAX_TOTAL_CHARACTERS} max");
        _helper.Log($"DEBUG: Created {totalEmbeds} villager embeds with {villagersProcessed} villagers total");
        
        return embeds;
    }
    
    /// <summary>
    /// Strips HTML tags from text to make it safe for Discord
    /// </summary>
    private string StripHtmlTags(string input)
    {
        if (string.IsNullOrEmpty(input))
            return input;
            
        // Simple regex-free HTML tag stripping
        bool inTag = false;
        System.Text.StringBuilder sb = new System.Text.StringBuilder();
        string tagContent = "";
        
        // Look for action words to bold, but only when they appear as part of an action phrase
        // e.g., "Searching: Wood" but not as part of "Fishmonger" or other profession names
        string[] actionWords = new string[] { 
            "Returning", "Fetching", "Planting", "Searching", 
            "Gathering", "Patrolling", "Building", "Harvesting", 
            "Hunting", "Working", "Collecting", "Waiting", 
            "Traveling", "Chopping", "Cutting", "Repairing", 
            "Idle", "Constructing", "Defending"
        };
        
        // Apply bold formatting to action words in the input
        foreach (string action in actionWords)
        {
            // Check for typical action patterns: either "Action:" or "Action something"
            if (input.Contains(action + ":") || input.StartsWith(action + " "))
            {
                input = input.Replace(action + ":", "**" + action + ":**");
                input = input.Replace(action + " ", "**" + action + "** ");
            }
        }
        
        foreach (char c in input)
        {
            if (c == '<')
            {
                inTag = true;
                tagContent = "";
                continue;
            }
            
            if (inTag)
            {
                tagContent += c;
                
                if (c == '>')
                {
                    inTag = false;
                    
                    // Handle sprite tags by replacing them with text
                    if (tagContent.StartsWith("sprite name="))
                    {
                        // Extract the icon name
                        int startIdx = "sprite name=".Length;
                        int endIdx = tagContent.IndexOf('>', startIdx);
                        if (endIdx > startIdx)
                        {
                            string iconName = tagContent.Substring(startIdx, endIdx - startIdx).Trim();
                            
                            // Replace common icons with text descriptions
                            if (iconName.Contains("apple"))
                                sb.Append("Apple");
                            else if (iconName.Contains("charcoal"))
                                sb.Append("Charcoal");
                            else if (iconName.Contains("wheat") || iconName.Contains("grain"))
                                sb.Append("Wheat");
                            else if (iconName.Contains("fish"))
                                sb.Append("Fish");
                            else if (iconName.Contains("stone"))
                                sb.Append("Stone");
                            else if (iconName.Contains("wood"))
                                sb.Append("Wood");
                            else if (iconName.Contains("iron"))
                                sb.Append("Iron");
                            else if (iconName.Contains("wool"))
                                sb.Append("Wool");
                            else if (iconName.Contains("meat"))
                                sb.Append("Meat");
                            else if (iconName.Contains("gold") || iconName.Contains("money"))
                                sb.Append("Gold");
                            else
                                sb.Append(iconName.Replace("icon_", "").Replace("_", " ").Replace("=", "").Trim());
                        }
                    }
                    
                    // Handle formatting tags
                    else if (tagContent.StartsWith("b>"))
                    {
                        sb.Append("**");
                    }
                    else if (tagContent.StartsWith("/b>"))
                    {
                        sb.Append("**");
                    }
                    
                    tagContent = "";
                }
                
                continue;
            }
            
            sb.Append(c);
        }
        
        return sb.ToString().Replace("  ", " ").Trim();
    }
    
    /// <summary>
    /// Sends a webhook message with the specified embeds
    /// </summary>
    private void SendWebhookWithEmbeds(List<Dictionary<string, object>> embeds)
    {
        // Ensure we don't exceed the embed limit
        if (embeds.Count > MAX_EMBEDS_PER_MESSAGE)
        {
            embeds = embeds.GetRange(0, MAX_EMBEDS_PER_MESSAGE);
            _helper.Log($"Warning: Truncated embeds to {MAX_EMBEDS_PER_MESSAGE} to respect Discord's limit");
        }
        
        // We no longer need to calculate character count here since we handle it proactively in CreateEmbedsForVillagers
        _helper.Log($"DEBUG: Sending {embeds.Count} embeds to Discord");
        
        Dictionary<string, object> payload = new Dictionary<string, object>
        {
            ["embeds"] = embeds
        };
        
        string json = MiniJSON.Json.Serialize(payload);
        
        // Double check the payload size before sending
        if (json.Length > 8000)
        {
            _helper.Log($"ERROR: Webhook payload is still too large ({json.Length} bytes) despite dynamic limiting. Reducing to bare minimum.");
            
            // Keep only the summary embed with an error message
            if (embeds.Count > 1)
            {
                var summaryEmbed = embeds[0];
                embeds = new List<Dictionary<string, object>> { summaryEmbed };
                
                // Add an error field to the summary
                if (!summaryEmbed.ContainsKey("fields"))
                {
                    summaryEmbed["fields"] = new List<Dictionary<string, object>>();
                }
                
                ((List<Dictionary<string, object>>)summaryEmbed["fields"]).Add(new Dictionary<string, object>
                {
                    ["name"] = "⚠️ Warning",
                    ["value"] = "Message was too large for Discord and had to be truncated. Try reducing the maximum villager count in settings.",
                    ["inline"] = false
                });
                
                payload["embeds"] = embeds;
                json = MiniJSON.Json.Serialize(payload);
            }
        }
        
        StartCoroutine(SendWebhookRequest(json));
    }
    
    /// <summary>
    /// Sends custom embeds to Discord
    /// </summary>
    public void SendCustomEmbed(List<Dictionary<string, object>> embeds)
    {
        if (string.IsNullOrEmpty(webhookUrl))
        {
            _helper.Log("Discord webhook URL is not set!");
            return;
        }
        
        if (embeds == null || embeds.Count == 0)
        {
            _helper.Log("No embeds to send!");
            return;
        }
        
        SendWebhookWithEmbeds(embeds);
    }
    
    /// <summary>
    /// Sends the webhook request to Discord
    /// </summary>
    private IEnumerator SendWebhookRequest(string json)
    {
        _helper.Log($"DEBUG: Sending webhook request to {webhookUrl}, payload size: {json.Length} characters");
        
        if (json.Length > 8000)
        {
            _helper.Log($"ERROR: Discord webhook payload is too large ({json.Length} bytes). Maximum allowed is ~8000 bytes. Aborting request.");
            yield break;
        }
        
        using (UnityWebRequest request = new UnityWebRequest(webhookUrl, "POST"))
        {
            byte[] bodyRaw = Encoding.UTF8.GetBytes(json);
            request.uploadHandler = new UploadHandlerRaw(bodyRaw);
            request.downloadHandler = new DownloadHandlerBuffer();
            request.SetRequestHeader("Content-Type", "application/json");
            
            _helper.Log("DEBUG: Webhook request headers and body prepared, sending now");
            
            yield return request.SendWebRequest();
            
            if (request.isNetworkError || request.isHttpError)
            {
                _helper.Log($"ERROR: Discord webhook request failed: {request.error}");
                
                if (request.responseCode == 429)
                {
                    _helper.Log("ERROR: Rate limited by Discord! Wait before sending more requests.");
                }
                else if (request.responseCode == 400)
                {
                    _helper.Log("ERROR: Discord rejected the request (400 Bad Request). This is usually due to malformed JSON or invalid embed structure.");
                    _helper.Log($"ERROR: Response: {request.downloadHandler.text}");
                    
                    // Log a small portion of the payload for debugging
                    if (json.Length > 100)
                        _helper.Log($"DEBUG: First 100 chars of payload: {json.Substring(0, 100)}...");
                }
                else if (request.responseCode == 404)
                {
                    _helper.Log("ERROR: Webhook URL not found (404). Check if the webhook URL is correct and still valid.");
                }
                else
                {
                    _helper.Log($"ERROR: Unexpected response code: {request.responseCode}");
                    _helper.Log($"ERROR: Response: {request.downloadHandler.text}");
                }
            }
            else
            {
                _helper.Log($"DEBUG: Discord webhook request successful, response code: {request.responseCode}");
            }
        }
    }
    
    /// <summary>
    /// Truncates a string to the specified maximum length
    /// </summary>
    private string TruncateString(string str, int maxLength)
    {
        if (string.IsNullOrEmpty(str))
            return str;
            
        if (str.Length <= maxLength)
            return str;
            
        return str.Substring(0, maxLength - 3) + "...";
    }
    
    /// <summary>
    /// Snapshot of a villager's state for change tracking
    /// </summary>
    private class VillagerSnapshot
    {
        public Guid Id { get; private set; }
        public string Name { get; private set; }
        public float Age { get; private set; }
        public string JobDescription { get; private set; }
        public bool HasHome { get; private set; }
        public bool IsSick { get; private set; }
        public int SkillCount { get; private set; }
        public string DeathReason { get; set; } = "Unknown"; // This will store the reason why a villager died
        
        public VillagerSnapshot(Villager v)
        {
            Id = v.guid;
            Name = v.name;
            Age = v.timeAlive / Weather.inst.TimeInYear();
            JobDescription = v.job != null ? v.job.GetDescription() : "";
            HasHome = v.Residence != null;
            IsSick = v.sick;
            SkillCount = v.skills.Count;
            
            // Infer the probable death reason based on villager's state
            DeathReason = DetermineLifeStatus(v);
        }
        
        /// <summary>
        /// Determines the most likely life status/death reason based on a villager's state
        /// </summary>
        private string DetermineLifeStatus(Villager v)
        {
            // Old age is typically when life timer runs out
            float lifeExpectancy = v.life / Weather.inst.TimeInYear();
            float currentAge = v.timeAlive / Weather.inst.TimeInYear();
            
            // Check for old age death (within 5 years of life expectancy or over 65)
            if (currentAge >= lifeExpectancy - 5 || currentAge >= 65)
            {
                // More specific age descriptions for old age deaths
                if (currentAge >= 90)
                    return "Extreme Old Age (90+ years)";
                else if (currentAge >= 80)
                    return "Very Old Age (80+ years)";
                else if (currentAge >= 70)
                    return "Old Age (70+ years)";
                else
                    return "Natural Causes";
            }
                
            // Check for sickness
            if (v.sick && v.sickTime > 8f) // Sickness progressed significantly
                return "Plague";
                
            // Check for starvation (missed multiple meals)
            if (v.missedMeal > 1)
                return "Starvation";
            
            // Check low health
            if (v.health < 0.2f)
                return "Poor Health";
            
            // Environmental checks - check if dragons are active in the game
            if (DragonSpawn.inst != null && DragonSpawn.inst.currentDragons != null && DragonSpawn.inst.currentDragons.Count > 0)
            {
                // Check proximity to dragons using array access instead of foreach
                for (int i = 0; i < DragonSpawn.inst.currentDragons.Count; i++)
                {
                    var dragon = DragonSpawn.inst.currentDragons.data[i];
                    if (dragon != null && Vector3.Distance(dragon.transform.position, v.Pos) < 10f)
                        return "Dragon Attack";
                }
            }
            
            // Check for wolf dens nearby
            var wolfDens = UnityEngine.Object.FindObjectsOfType<WolfDen>();
            if (wolfDens != null && wolfDens.Length > 0)
            {
                foreach (var den in wolfDens)
                {
                    if (Vector3.Distance(den.transform.position, v.Pos) < 15f)
                        return "Wolf Attack";
                }
            }
            
            // Check for water (using Cell.IsWater instead of Cell.type)
            Cell cell = World.inst.GetCellData(v.Pos);
            if (cell != null && cell.z > 0 && cell.z < World.inst.GridHeight && cell.x > 0 && cell.x < World.inst.GridWidth)
            {
                // Check if cell is surrounded by water, which might indicate drowning
                Cell[] neighbors = new Cell[4];
                World.inst.GetNeighborCells(cell, ref neighbors);
                bool nearWater = false;
                for (int i = 0; i < neighbors.Length; i++)
                {
                    // Check if any neighbor cells are water (we can't use IsWaterTile directly)
                    if (neighbors[i] != null && neighbors[i].deepWater)
                    {
                        nearWater = true;
                        break;
                    }
                }
                
                if (nearWater)
                    return "Drowning";
            }
            
            // Check for specific job-related deaths
            if (v.job != null)
            {
                string jobDesc = v.job.GetDescription();
                if (jobDesc.Contains("Wood") || jobDesc.Contains("Tree") || jobDesc.Contains("Forest"))
                    return "Woodcutting Accident";
                    
                if (jobDesc.Contains("Ston") || jobDesc.Contains("Quarry"))
                    return "Stonecutting Accident";
                    
                if (jobDesc.Contains("Moat"))
                    return "Moat Construction Accident";
                    
                if (jobDesc.Contains("Wall") || jobDesc.Contains("Tower"))
                    return "Construction Accident";
                    
                if (jobDesc.Contains("Mine"))
                    return "Mining Accident";
                    
                if (jobDesc.Contains("Hunt"))
                    return "Hunting Accident";
            }
            
            // Remove references to BarbarianManager and DiplomacyScreen as they might not be accessible
            
            // Default if we can't determine
            return "Unknown Causes";
        }
    }
    
    /// <summary>
    /// Attempts to find death reason from recent kingdom log entries
    /// </summary>
    private string GetDeathReasonFromKingdomLogs(string villagerName)
    {
        try
        {
            _helper.Log($"Attempting to find death reason for {villagerName}");
            
            // Access the KingdomLog.logQueue using reflection through Harmony's AccessTools
            if (KingdomLog.inst != null)
            {
                _helper.Log("KingdomLog.inst is available");
                
                // Use AccessTools to get the private log field from KingdomLog
                var logField = AccessTools.Field(typeof(KingdomLog), "log");
                
                if (logField != null)
                {
                    _helper.Log("Successfully accessed the 'log' field via reflection");
                    
                    // Get the value of the log field from the instance
                    var logQueue = logField.GetValue(KingdomLog.inst);
                    
                    if (logQueue != null)
                    {
                        _helper.Log("Log queue is not null");
                        
                        // Get the nested LogEntry class
                        Type logEntryType = AccessTools.Inner(typeof(KingdomLog), "LogEntry");
                        
                        if (logEntryType != null)
                        {
                            _helper.Log($"Found LogEntry type: {logEntryType.FullName}");
                            
                            // Get the id field from the LogEntry class
                            FieldInfo idField = AccessTools.Field(logEntryType, "id");
                            
                            if (idField != null)
                            {
                                _helper.Log("Found 'id' field in LogEntry");
                                
                                // Access the log as IList since we know it implements it
                                var logList = logQueue as System.Collections.IList;
                                if (logList != null && logList.Count > 0)
                                {
                                    _helper.Log($"Found {logList.Count} log entries to check for death reasons");
                                    
                                    // Loop through the list and check each entry
                                    for (int i = 0; i < logList.Count; i++)
                                    {
                                        var entry = logList[i];
                                        if (entry != null)
                                        {
                                            try
                                            {
                                                string id = idField.GetValue(entry) as string;
                                                
                                                if (id != null)
                                                {
                                                    _helper.Log($"Log entry found with id: {id}");
                                                    
                                                    if (id == "dragonkill")
                                                        return "Dragon Attack";
                                                    if (id == "starvedeath")
                                                        return "Starvation";
                                                    if (id == "plaguedeath")
                                                        return "Plague";
                                                    if (id.Contains("wolf") && id.Contains("kill"))
                                                        return "Wolf Attack";
                                                }
                                            }
                                            catch (Exception ex)
                                            {
                                                _helper.Log($"Error accessing log entry {i}: {ex.Message}");
                                            }
                                        }
                                    }
                                }
                                else
                                {
                                    _helper.Log("Log queue couldn't be accessed as an IList or was empty");
                                }
                            }
                            else
                            {
                                _helper.Log("Could not find 'id' field in LogEntry type");
                            }
                        }
                        else
                        {
                            _helper.Log("Could not find LogEntry inner type");
                        }
                    }
                    else
                    {
                        _helper.Log("Log queue value is null");
                    }
                }
                else
                {
                    _helper.Log("Could not find 'log' field in KingdomLog");
                }
            }
            else
            {
                _helper.Log("KingdomLog.inst is null");
            }
            
            _helper.Log("Falling back to alternate death reason detection methods");
            
            // Since we can't directly access KingdomLog entries, check for common death causes based on game state
            
            // Check if there was a recent dragon attack by checking for active dragons
            if (DragonSpawn.inst != null && DragonSpawn.inst.currentDragons != null && DragonSpawn.inst.currentDragons.Count > 0)
            {
                for (int i = 0; i < DragonSpawn.inst.currentDragons.Count; i++)
                {
                    var dragon = DragonSpawn.inst.currentDragons.data[i];
                    if (dragon != null && dragon.HP() <= 0)
                    {
                        return "Dragon Attack";
                    }
                }
            }
                
            // Check the current game state for plague or other events
            if (Player.inst != null)
            {
                // Check for plague by looking for sick villagers
                bool plagueActive = false;
                for (int i = 0; i < Villager.villagers.Count; i++)
                {
                    Villager v = Villager.villagers.data[i];
                    if (v != null && v.enabled && v.sick)
                    {
                        plagueActive = true;
                        break;
                    }
                }
                
                if (plagueActive)
                    return "Plague";
                    
                // Check for starvation deaths
                bool potentialStarvation = false;
                
                // Check if any villagers have missed multiple meals, which indicates starvation risk
                for (int i = 0; i < Villager.villagers.Count; i++)
                {
                    Villager v = Villager.villagers.data[i];
                    if (v != null && v.enabled && v.missedMeal > 1)
                    {
                        potentialStarvation = true;
                        break;
                    }
                }
                
                if (potentialStarvation)
                    return "Starvation";
            }
            
            // We could also check for recent wolf attacks here, but the game doesn't expose that information as easily
        }
        catch (Exception ex)
        {
            // Log exception but don't crash
            _helper.Log($"Error checking death reasons: {ex.Message}");
            _helper.Log($"Stack trace: {ex.StackTrace}");
        }
        
        return null; // Couldn't determine from game state
    }
}

/// <summary>
/// Simple JSON serialization utility - minimal implementation
/// </summary>
public class MiniJSON
{
    public static class Json
    {
        public static string Serialize(object obj)
        {
            return new Serializer().SerializeValue(obj);
        }
        
        private class Serializer
        {
            StringBuilder builder;
            
            public Serializer()
            {
                builder = new StringBuilder();
            }
            
            public string SerializeValue(object value)
            {
                builder.Length = 0;
                SerializeValueInternal(value);
                return builder.ToString();
            }
            
            private void SerializeValueInternal(object value)
            {
                if (value == null)
                {
                    builder.Append("null");
                }
                else if (value is string)
                {
                    SerializeString((string)value);
                }
                else if (value is bool)
                {
                    builder.Append((bool)value ? "true" : "false");
                }
                else if (value is int || value is long || value is float || value is double || value is decimal)
                {
                    builder.Append(value.ToString());
                }
                else if (value is Dictionary<string, object>)
                {
                    SerializeObject((Dictionary<string, object>)value);
                }
                else if (value is List<object> || value is List<Dictionary<string, object>>)
                {
                    SerializeArray((IList)value);
                }
            }
            
            private void SerializeObject(Dictionary<string, object> obj)
            {
                bool first = true;
                
                builder.Append('{');
                
                foreach (var pair in obj)
                {
                    if (!first)
                    {
                        builder.Append(',');
                    }
                    
                    SerializeString(pair.Key);
                    builder.Append(':');
                    SerializeValueInternal(pair.Value);
                    
                    first = false;
                }
                
                builder.Append('}');
            }
            
            private void SerializeArray(IList array)
            {
                builder.Append('[');
                
                bool first = true;
                
                foreach (var item in array)
                {
                    if (!first)
                    {
                        builder.Append(',');
                    }
                    
                    SerializeValueInternal(item);
                    
                    first = false;
                }
                
                builder.Append(']');
            }
            
            private void SerializeString(string str)
            {
                builder.Append('\"');
                
                foreach (char c in str)
                {
                    switch (c)
                    {
                        case '\"':
                            builder.Append("\\\"");
                            break;
                        case '\\':
                            builder.Append("\\\\");
                            break;
                        case '\b':
                            builder.Append("\\b");
                            break;
                        case '\f':
                            builder.Append("\\f");
                            break;
                        case '\n':
                            builder.Append("\\n");
                            break;
                        case '\r':
                            builder.Append("\\r");
                            break;
                        case '\t':
                            builder.Append("\\t");
                            break;
                        default:
                            if (c < ' ')
                            {
                                builder.Append("\\u");
                                builder.Append(((int)c).ToString("x4"));
                            }
                            else
                            {
                                builder.Append(c);
                            }
                            break;
                    }
                }
                
                builder.Append('\"');
            }
        }
    }
} 