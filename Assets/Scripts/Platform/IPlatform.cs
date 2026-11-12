using System.Collections.Generic;

/// <summary>
/// What the game needs from the platform it runs on: who the player is, the Rich Presence friends see, and which friends
/// are playing. Two versions: LocalPlatform (no platform: development, direct IP) and SteamPlatform. Gameplay never calls
/// Steam directly; it goes through Platform.Current (PlatformManager picks it).
/// </summary>
public interface IPlatform
{
    /// <summary>"Local" or "Steam".</summary>
    string Name { get; }

    /// <summary>The player's name on the platform (Steam persona), or null without one.</summary>
    string PlayerName { get; }

    /// <summary>Sets one Rich Presence key for friends to read (PresenceKeys); null or empty clears it.</summary>
    void SetPresence(string key, string value);

    /// <summary>Clears every Rich Presence key (leaving a game, quitting).</summary>
    void ClearPresence();

    /// <summary>Friends playing this game now, with what their Rich Presence says. Empty without a platform.</summary>
    IReadOnlyList<PlatformFriend> FriendsPlaying();

    /// <summary>Every frame: lets the platform run its callbacks.</summary>
    void Update();

    /// <summary>On quit.</summary>
    void Shutdown();
}

/// <summary>A friend playing this game, as their Rich Presence describes it (PresenceKeys).</summary>
public struct PlatformFriend
{
    /// <summary>The platform's id for them (a SteamID as text).</summary>
    public string Id;
    public string Name;
    /// <summary>PresenceKeys.AreaTown, AreaDungeon or AreaMenu.</summary>
    public string Area;
    /// <summary>Players in their game, couch players included.</summary>
    public int Players;
    /// <summary>Their game's lobby, to join through (phase O6); empty until then.</summary>
    public string Lobby;

    /// <summary>Their game takes another player now: they're in town and it isn't full.</summary>
    public bool CanJoin => Area == PresenceKeys.AreaTown && Players > 0 && Players < NetSession.MaxPlayers;
}

/// <summary>
/// The Rich Presence keys every game publishes and friends' games read (see MultiplayerPlan.md, Online with Steam).
/// </summary>
public static class PresenceKeys
{
    public const string Area = "pl_area";
    public const string Players = "pl_players";
    public const string Lobby = "pl_lobby";
    /// <summary>Steam's own: what the friends list shows under the name (a plain text needs our own App ID to show).</summary>
    public const string Status = "status";

    public const string AreaTown = "town";
    public const string AreaDungeon = "dungeon";
    public const string AreaMenu = "menu";
}

/// <summary>The platform this run uses (PlatformManager sets it; LocalPlatform until then).</summary>
public static class Platform
{
    static IPlatform _current;

    public static IPlatform Current
    {
        get => _current ??= new LocalPlatform();
        internal set => _current = value;
    }

    [UnityEngine.RuntimeInitializeOnLoadMethod(UnityEngine.RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics() => _current = null;
}
