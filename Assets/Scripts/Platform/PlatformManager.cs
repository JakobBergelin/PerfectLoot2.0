using System.Collections.Generic;
using Unity.Collections;
using Unity.Entities;
using Unity.NetCode;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// On the Services (GameBootstrap, next to AccountManager): picks the platform for this run, Steam when it runs (needs
/// Steamworks.NET and the PERFECTLOOT_STEAM define) or else Local, and keeps this game's Rich Presence up to date for
/// friends: its area (town, dungeon or menu), how many players are in it, and a status line. Only changes are sent.
/// </summary>
public class PlatformManager : MonoBehaviour
{
    [Tooltip("Use Steam when it runs; off: always the Local platform (join by IP)")]
    public bool UseSteam = true;
    [Tooltip("Seconds between Rich Presence updates")]
    public float PresenceInterval = 1f;

    static PlatformManager _instance;
    float _nextPresence;
    readonly Dictionary<string, string> _published = new Dictionary<string, string>();

    void Awake()
    {
        if (_instance != null && _instance != this)
        {
            Destroy(this);
            return;
        }
        _instance = this;
        Platform.Current = Create();
        Debug.Log($"PlatformManager: running on the {Platform.Current.Name} platform.");
    }

    IPlatform Create()
    {
#if PERFECTLOOT_STEAM
        if (UseSteam)
        {
            var steam = SteamPlatform.TryCreate();
            if (steam != null) return steam;
        }
#endif
        return new LocalPlatform();
    }

    void Update()
    {
        Platform.Current.Update();
        if (Time.unscaledTime < _nextPresence) return;
        _nextPresence = Time.unscaledTime + PresenceInterval;
        Publish();
    }

    void Publish()
    {
        string area = GameBootstrap.IsMenuScene(SceneManager.GetActiveScene().name) ? PresenceKeys.AreaMenu
            : GameArea.IsTown ? PresenceKeys.AreaTown : PresenceKeys.AreaDungeon;
        int players = area == PresenceKeys.AreaMenu ? 0 : PlayersInGame();
        Set(PresenceKeys.Area, area);
        Set(PresenceKeys.Players, players > 0 ? players.ToString() : null);
        Set(PresenceKeys.Status, area == PresenceKeys.AreaMenu ? "In the main menu"
            : $"{(area == PresenceKeys.AreaTown ? "In town" : "In a dungeon")} ({players}/{NetSession.MaxPlayers})");
    }

    void Set(string key, string value)
    {
        if (_published.TryGetValue(key, out var old) && old == value) return;
        _published[key] = value;
        Platform.Current.SetPresence(key, value);
    }

    // The host knows its game's players; a client counts the player ghosts it sees (owned by a connection, so not
    // Mirror Image copies)
    static int PlayersInGame()
    {
        if (NetSession.IsHost) return GameInput.PlayerCount + NetSession.RemotePlayers;
        var world = World.DefaultGameObjectInjectionWorld;
        if (world == null || !world.IsCreated) return 0;
        using var query = world.EntityManager.CreateEntityQuery(ComponentType.ReadOnly<NetPlayer>(), ComponentType.ReadOnly<GhostOwner>());
        using var owners = query.ToComponentDataArray<GhostOwner>(Allocator.Temp);
        int count = 0;
        foreach (var owner in owners)
            if (owner.NetworkId != 0) count++;
        return count;
    }

    void OnDestroy()
    {
        if (_instance != this) return;
        _instance = null;
        Platform.Current.Shutdown();
        Platform.Current = new LocalPlatform();
    }

    [ContextMenu("Log presence and friends playing")]
    void LogPresence()
    {
        var text = $"PlatformManager ({Platform.Current.Name}): presence";
        foreach (var (key, value) in _published) text += $" {key}={value}";
        var friends = Platform.Current.FriendsPlaying();
        text += $"; {friends.Count} friend(s) playing";
        foreach (var friend in friends) text += $" | {friend.Name}: {friend.Area}, {friend.Players} players{(friend.CanJoin ? ", can join" : "")}";
        Debug.Log(text);
    }
}
