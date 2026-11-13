using System;
using System.Collections.Generic;
using Unity.Networking.Transport;

/// <summary>
/// What the game needs from the platform it runs on: who the player is, the Rich Presence friends see, which friends are
/// playing, and how to reach their games. Two versions: LocalPlatform (no platform: development, join by IP) and
/// SteamPlatform. Gameplay never calls Steam directly; it goes through Platform.Current.
/// </summary>
public interface IPlatform
{
    /// <summary>"Local" or "Steam".</summary>
    string Name { get; }

    /// <summary>The player's name on the platform (Steam persona), or null without one.</summary>
    string PlayerName { get; }

    /// <summary>Other players can join this machine's games through the platform (the host listens on it).</summary>
    bool HostsOnline { get; }

    /// <summary>
    /// What friends join the game this machine is in by (Steam: the host's SteamID), or null when the platform can't
    /// tell (Local, or in a game joined by IP).
    /// </summary>
    string JoinId { get; }

    /// <summary>Where to connect to join the game with this JoinId.</summary>
    bool TryGetJoinEndpoint(string joinId, out NetworkEndpoint endpoint);

    /// <summary>The player asked the platform to join a friend's game (Steam's Join Game, an invite): its JoinId.</summary>
    event Action<string> JoinRequested;

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
    /// <summary>The JoinId of their game (its host's SteamID), or empty.</summary>
    public string Host;

    /// <summary>Their game takes another player now: they're in town, it isn't full, and it can be reached.</summary>
    public bool CanJoin => Area == PresenceKeys.AreaTown && Players > 0 && Players < NetSession.MaxPlayers && !string.IsNullOrEmpty(Host);
}

/// <summary>
/// The Rich Presence keys every game publishes and friends' games read (see MultiplayerArchitecture.md, Platform).
/// </summary>
public static class PresenceKeys
{
    public const string Area = "pl_area";
    public const string Players = "pl_players";
    /// <summary>The JoinId of the game (its host's SteamID).</summary>
    public const string Host = "pl_host";
    /// <summary>Steam's own: what its Join Game and invites hand to the friend's game ("+connect_host &lt;JoinId&gt;").</summary>
    public const string Connect = "connect";
    /// <summary>Steam's own: what the friends list shows under the name (a plain text needs our own App ID to show).</summary>
    public const string Status = "status";

    public const string AreaTown = "town";
    public const string AreaDungeon = "dungeon";
    public const string AreaMenu = "menu";

    public const string ConnectPrefix = "+connect_host ";
}

/// <summary>
/// The platform this run uses: Steam when it runs (and the run isn't a Multiplayer Play Mode virtual player or started
/// with -nosteam), else Local. Made on first use, which may be while the first world makes its network drivers
/// (NetDriverConstructor), so the host can listen on Steam from the start.
/// </summary>
public static class Platform
{
    static IPlatform _current;

    public static IPlatform Current => _current ??= Create();

    static IPlatform Create()
    {
#if STEAMWORKS_NET
        var args = Environment.GetCommandLineArgs();
        bool noSteam = Array.IndexOf(args, "-nosteam") >= 0 || Array.IndexOf(args, "--virtual-project-clone") >= 0;
        if (!noSteam)
        {
            var steam = SteamPlatform.TryCreate();
            if (steam != null) return steam;
        }
#endif
        return new LocalPlatform();
    }

    /// <summary>On quit (PlatformManager): shuts the platform down; a later use starts it again.</summary>
    public static void Shutdown()
    {
        _current?.Shutdown();
        _current = null;
    }

    [UnityEngine.RuntimeInitializeOnLoadMethod(UnityEngine.RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics() => _current = null;
}
