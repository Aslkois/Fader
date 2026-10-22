namespace Fader.Core.Apps;

public enum AppCategory
{
    Other,
    Game,
    Chat,
    Music,
    Browser,
    System,
}

/// <summary>
/// Editable starting classification so profiles can say "chat apps" or "browsers" without
/// naming executables. Anything not listed is <see cref="AppCategory.Other"/>; games are chosen by
/// the user (or detected as the foreground app when LOCK IN starts), never guessed from a list.
/// </summary>
public static class KnownApps
{
    private static readonly HashSet<string> Chat = new(StringComparer.OrdinalIgnoreCase)
    {
        "discord.exe", "discordptb.exe", "discordcanary.exe", "discorddevelopment.exe", "vesktop.exe", "legcord.exe",
        "teamspeak.exe", "ts3client_win64.exe", "mumble.exe", "ms-teams.exe", "teams.exe", "zoom.exe",
        "slack.exe", "skype.exe", "telegram.exe", "element.exe", "guilded.exe", "whatsapp.exe", "signal.exe",
    };

    private static readonly HashSet<string> Music = new(StringComparer.OrdinalIgnoreCase)
    {
        "spotify.exe", "tidal.exe", "deezer.exe", "itunes.exe", "applemusic.exe", "musicbee.exe", "foobar2000.exe",
        "aimp.exe", "winamp.exe", "amazon music.exe", "youtube music desktop app.exe",
    };

    private static readonly HashSet<string> Browsers = new(StringComparer.OrdinalIgnoreCase)
    {
        "chrome.exe", "msedge.exe", "firefox.exe", "brave.exe", "opera.exe", "opera_gx.exe", "vivaldi.exe",
        "arc.exe", "zen.exe", "librewolf.exe", "waterfox.exe", "iexplore.exe", "chromium.exe",
    };

    public static readonly IReadOnlyList<string> DefaultChatTargets =
        ["discord.exe", "discordptb.exe", "discordcanary.exe", "vesktop.exe", "legcord.exe"];

    public static AppCategory Classify(AppIdentity identity)
    {
        if (identity.IsSystemSounds) return AppCategory.System;
        var exe = identity.ExeName;
        if (Chat.Contains(exe)) return AppCategory.Chat;
        if (Music.Contains(exe)) return AppCategory.Music;
        if (Browsers.Contains(exe)) return AppCategory.Browser;
        return AppCategory.Other;
    }

    public static string CategoryLabel(AppCategory c) => c switch
    {
        AppCategory.Game => "Current game",
        AppCategory.Chat => "Chat apps",
        AppCategory.Music => "Music",
        AppCategory.Browser => "Browsers",
        AppCategory.System => "System sounds",
        _ => "Other apps",
    };
}
