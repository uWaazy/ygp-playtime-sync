## YGP Playtime Sync - Plugin Settings

This document describes how plugin settings are implemented and managed.

### Overview

The plugin uses Playnite's built-in settings system through the `ISettings` interface.
Settings are stored in Playnite's settings database and are automatically persisted.

### Settings Class

```csharp
public class PluginSettings : ISettings
{
    // General settings
    public bool AutoSyncEnabled { get; set; }
    public bool SyncExistingPlaytimeEnabled { get; set; }
    public int SyncIntervalMinutes { get; set; }
    // ... more settings
}
```

### Settings Persistence

Settings are automatically saved when `Settings` property is assigned in the plugin:

```csharp
// In plugin class:
public override ISettings GetSettings(bool firstTimeSettings)
{
    return _settings;
}

public override UserControl GetSettingsControl(bool firstTimeSettings)
{
    return new YgpSyncSettingsControl(_settings, this, _localization);
}

// When user saves settings in UI:
public void SaveSettings(PluginSettings settings)
{
    Settings = settings; // This triggers Playnite's persistence
}
```

### Settings UI

The settings UI is implemented as a WPF UserControl (`YgpSyncSettingsControl`).
It provides a tabbed interface with the following sections:

1. **General** - Auto-sync, interval, sync behavior
2. **Account** - YGP connection status and controls
3. **Synchronization** - Queue management, batch settings
4. **Privacy** - Logging, data management
5. **Advanced** - Debug mode, mock provider, mappings

### Localization

All UI strings are localized using the `Localization` class.
Supported languages:
- English (en-US) - Default
- Portuguese Brazil (pt-BR)
- Spanish (es-ES)

Language is auto-detected from system settings on first load.

### Mock Provider Mode

For development and testing, the plugin supports a Mock Provider mode.
When enabled:
- All API calls are simulated locally
- No actual network requests are made
- Sessions can be synced without real YGP API

This mode is clearly indicated in the UI.

### Validation

Settings values are validated:
- Sync interval: 1-60 minutes
- Batch size: 1-50 sessions
- Max retries: 1-20 attempts

Invalid values are clamped to valid ranges.
