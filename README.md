# Kingdoms and Castles - Discord Villager Webhook

This mod allows you to track your villagers in Kingdoms and Castles by sending updates to a Discord channel via webhooks. You'll receive beautiful, formatted embeds with information about your villagers, including their name, age, job, health status, and more!

## Features

- Periodic updates of all villagers or only changed villagers
- Immediate notifications for important events (e.g., villager becomes sick)
- Configurable update interval
- Respects Discord's rate limits and embed limits
- Properly formatted embeds with all relevant villager information
- Tracks villager changes (new villagers, job changes, health status, home status)

## Installation

1. Download the mod files and place them in your Kingdoms and Castles mods folder:
   - `<GameDirectory>\KingdomsAndCastles_Data\mods\`

2. Edit the `Core.cs` file to set your Discord webhook URL (see Configuration section below).

## Setting Up Discord Webhook

1. In your Discord server, go to Server Settings -> Integrations -> Webhooks
2. Click "New Webhook"
3. Choose a name and channel for your webhook
4. Copy the webhook URL
5. Open `Core.cs` and paste your webhook URL in the appropriate variable (see Configuration section below)

## Configuration

The mod uses hardcoded values in the `Core.cs` file. Open this file and modify the following variables at the top of the class:

```csharp
// Replace this URL with your Discord webhook URL
private string discordWebhookUrl = "https://discord.com/api/webhooks/your-webhook-url-here";

// Update interval in seconds (how often to send updates)
private float updateInterval = 60f; // Default: 1 minute

// If true, sends all villagers; if false, only sends changed villagers
private bool sendFullUpdates = false;

// Set to false to disable Discord webhook functionality
private bool enableDiscordWebhook = true;
```

Configuration options:
- `discordWebhookUrl`: Your Discord webhook URL
- `updateInterval`: How often to send updates (in seconds)
- `sendFullUpdates`: 
  - `true`: Send information about all villagers in every update
  - `false`: Only send information about villagers that have changed since the last update
- `enableDiscordWebhook`: Set to `true` to enable Discord updates, `false` to disable

## Discord Embed Examples

The mod will send different types of embeds to your Discord channel:

1. **Summary Embed**: Shows the total number of villagers in your kingdom
2. **Villager Detail Embeds**: Lists villagers with their stats, organized by job type
3. **Event Embeds**: Sent when specific events occur, like a villager getting sick

## Limits and Considerations

- The mod respects Discord's rate limits and embed limits
- If you have a large number of villagers, they will be spread across multiple embeds
- For very large kingdoms, only the most recently changed villagers will be included in updates

## Troubleshooting

If you're having issues with the mod:

1. Check the mod's log file in the mod folder
2. Ensure the webhook URL is correct and the webhook has permission to post in the channel
3. Try increasing the update interval if you're hitting rate limits

## Credits

Mod Created by [DohmBoy64]

Based on the Kingdoms and Castles modding template and Discord webhook integration.

## License

This mod is provided as-is. Feel free to use and modify for personal use. 
