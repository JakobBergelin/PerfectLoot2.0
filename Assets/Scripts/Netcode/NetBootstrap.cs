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
/// Joining for development: a Multiplayer Play Mode virtual player (-name Player2..4) or a build started with
/// -netclient [address] joins the host at that address (this machine by default).
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
/// Plain statics, decided at startup (command line) and kept through scene changes.
/// </summary>
public static class NetSession
{
    public enum Roles { Host, Client }

    public const ushort DefaultPort = 7979;

    /// <summary>Host: this machine runs the game; Client: it plays in someone else's.</summary>
    public static Roles Role { get; private set; } = Roles.Host;

    /// <summary>A host accepts players. On in the editor and development builds; later only while hosting friends (Steam lobby).</summary>
    public static bool Listen { get; set; } = Debug.isDebugBuild;

    /// <summary>Where a client connects.</summary>
    public static NetworkEndpoint ConnectTo { get; private set; } = NetworkEndpoint.LoopbackIpv4.WithPort(DefaultPort);

    public static ushort Port => DefaultPort;

    public static bool IsHost => Role == Roles.Host;

    // Statics survive entering Play mode when domain reload is off
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics()
    {
        Role = Roles.Host;
        Listen = Debug.isDebugBuild;
        ConnectTo = NetworkEndpoint.LoopbackIpv4.WithPort(DefaultPort);
    }

    internal static void ReadCommandLine()
    {
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
