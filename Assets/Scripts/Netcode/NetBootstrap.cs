using System;
using Unity.Entities;
using Unity.NetCode;
using Unity.Networking.Transport;
using UnityEngine;

/// <summary>
/// Which world the game runs in. Every game goes through Netcode: normally a single-world host (one world runs the
/// server and this machine's own players, so playing alone is exactly as before), or a client world when joining
/// someone. A host listens for players while NetSession.Listen is on. GameSceneLoader rebuilds the world through this
/// same bootstrap on every scene change.
/// Joining: the Hub's game menu (Join a game, by IP); for development also a Multiplayer Play Mode virtual player
/// (-name Player2..4) or a build started with -netclient [address], which joins the host at that address (this machine
/// by default).
/// </summary>
public class NetBootstrap : ClientServerBootstrap
{
    public override bool Initialize(string defaultWorldName)
    {
        NetSession.ReadCommandLine();
        CreateWorld();
        return true;
    }

    /// <summary>
    /// Makes the game world for NetSession.Role, as the default world: around the connections kept through a scene change
    /// (NetSceneSync), or fresh.
    /// </summary>
    public static World CreateWorld()
    {
        AutoConnectPort = 0;    // Connecting and listening are started explicitly (NetStartSystem)
        // UDP and the online platform's driver (Steam)
        NetworkStreamReceiveSystem.DriverConstructor = new NetDriverConstructor();
        bool client = NetSession.Role == NetSession.Roles.Client;
        var world = NetSceneSync.TakeMigratedWorld();
        if (world != null)
        {
            // The same systems CreateClientWorld and CreateSingleWorldHost add, added after the connections are in
            var flags = WorldSystemFilterFlags.ClientSimulation | WorldSystemFilterFlags.Presentation;
            if (!client) flags |= WorldSystemFilterFlags.ServerSimulation;
            var systems = DefaultWorldInitialization.GetAllSystemTypeIndices(flags);
            DefaultWorldInitialization.AddSystemsToRootLevelSystemGroups(world, systems);
            ScriptBehaviourUpdateOrder.AppendWorldToCurrentPlayerLoop(world);
        }
        else world = client ? CreateClientWorld("ClientWorld") : CreateSingleWorldHost("HostWorld");
        World.DefaultGameObjectInjectionWorld = world;
        return world;
    }
}

/// <summary>
/// The network session this process is in: its role, whether a host accepts players, and where a client connects.
/// Plain statics, decided at startup (command line) or by joining and leaving (Join, Leave), and kept through scene
/// changes. A change of role starts over in a fresh world in the Hub (GameSceneLoader.Restart).
/// </summary>
public static class NetSession
{
    public enum Roles { Host, Client }

    public const ushort DefaultPort = 7979;

    /// <summary>Players in one game at most, couch players included (the host turns down joins beyond it).</summary>
    public const int MaxPlayers = 4;

    /// <summary>How long a notice shows (Notice).</summary>
    const float NoticeSeconds = 8f;

    /// <summary>Host: this machine runs the game; Client: it plays in someone else's.</summary>
    public static Roles Role { get; private set; } = Roles.Host;

    /// <summary>A host accepts players. On in the editor and development builds; later only while hosting friends (Steam lobby).</summary>
    public static bool Listen { get; set; } = Debug.isDebugBuild;

    /// <summary>Where a client connects.</summary>
    public static NetworkEndpoint ConnectTo { get; private set; } = NetworkEndpoint.LoopbackIpv4.WithPort(DefaultPort);

    public static ushort Port => DefaultPort;

    public static bool IsHost => Role == Roles.Host;

    /// <summary>The host: players other machines brought into its game (NetJoinSystem keeps it).</summary>
    public static int RemotePlayers { get; internal set; }

    /// <summary>A message for this machine's players about the session ("The host left the game"), or null.</summary>
    public static string Notice => Time.unscaledTime < _noticeUntil ? _notice : null;

    static string _notice;
    static float _noticeUntil;

    // Statics survive entering Play mode when domain reload is off
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics()
    {
        Role = Roles.Host;
        Listen = Debug.isDebugBuild;
        ConnectTo = NetworkEndpoint.LoopbackIpv4.WithPort(DefaultPort);
        RemotePlayers = 0;
        _notice = null;
        _noticeUntil = 0f;
        _commandLineRead = false;
    }

    public static void ShowNotice(string text)
    {
        _notice = text;
        _noticeUntil = Time.unscaledTime + NoticeSeconds;
    }

    /// <summary>
    /// Plays in someone else's game: this machine becomes a client of the address and starts over in the Hub, where it
    /// connects and follows the host. False when the address can't be read.
    /// </summary>
    public static bool Join(string address)
    {
        if (!TryParseAddress(address, out var endpoint)) return false;
        Join(endpoint, address.Trim());
        return true;
    }

    /// <summary>Plays in someone else's game at this endpoint (an IP address, or a friend's SteamID: Platform), named as given.</summary>
    public static void Join(NetworkEndpoint endpoint, string name)
    {
        ShowNotice($"Joining {name}...");
        // Changed only as the world is replaced: this world still runs as a host until then
        NetSceneSync.RestartLater(GameBootstrap.TownScene, () =>
        {
            Role = Roles.Client;
            ConnectTo = endpoint;
            RemotePlayers = 0;
        });
    }

    /// <summary>An IP address, IPv4 with an optional :port ("192.168.1.20" or "192.168.1.20:7979") or IPv6.</summary>
    public static bool TryParseAddress(string text, out NetworkEndpoint endpoint)
    {
        text = text?.Trim() ?? "";
        ushort port = DefaultPort;
        int colon = text.LastIndexOf(':');
        // One colon: IPv4 with a port (IPv6 addresses have several)
        if (colon > 0 && text.IndexOf(':') == colon)
        {
            if (!ushort.TryParse(text.Substring(colon + 1), out port)) { endpoint = default; return false; }
            text = text.Substring(0, colon);
        }
        return NetworkEndpoint.TryParse(text, port, out endpoint) || NetworkEndpoint.TryParse(text, port, out endpoint, NetworkFamily.Ipv6);
    }

    /// <summary>
    /// Out of someone else's game (left, turned down, or the connection was lost): back to this machine's own game in
    /// its Hub, with the notice saying why (or none).
    /// </summary>
    public static void Leave(string notice)
    {
        if (IsHost) return;
        if (notice != null) ShowNotice(notice);
        NetSceneSync.RestartLater(GameBootstrap.TownScene, () => Role = Roles.Host);
    }

    /// <summary>Going to the main menu ends the session: the next game is this machine's own.</summary>
    internal static void End() => Role = Roles.Host;

    // Once per run: every world is made through the bootstrap, and a player who left a game stays a host
    static bool _commandLineRead;

    internal static void ReadCommandLine()
    {
        if (_commandLineRead) return;
        _commandLineRead = true;
        var args = Environment.GetCommandLineArgs();
        for (int i = 0; i < args.Length; i++)
        {
            if (args[i] == "-netclient")
            {
                Role = Roles.Client;
                if (i + 1 < args.Length && NetworkEndpoint.TryParse(args[i + 1], DefaultPort, out var address)) ConnectTo = address;
            }
            // Multiplayer Play Mode passes -name Player1 for the main editor and Player2..4 for virtual players
            if (args[i] == "-name" && i + 1 < args.Length && args[i + 1].StartsWith("Player") && args[i + 1] != "Player1")
                Role = Roles.Client;
        }
    }
}
