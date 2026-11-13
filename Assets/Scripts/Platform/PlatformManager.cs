using System.Collections.Generic;
using Unity.Collections;
using Unity.Entities;
using Unity.NetCode;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// On the Services (GameBootstrap, next to AccountManager): runs the platform (Platform.Current: Steam when it runs, else
/// Local) and keeps this game's Rich Presence up to date for friends: its area (town, dungeon or menu), how many players
/// are in it, how to join it (PresenceKeys.Host, Connect) and a status line. Only changes are sent. It also keeps the
/// friends whose games can be joined (JoinableFriends, for the Hub) and carries out the platform's join requests (Steam's
/// Join Game, invites), from town only: elsewhere the request waits until the player is back in town.
/// </summary>
public class PlatformManager : MonoBehaviour
{
    [Tooltip("Seconds between Rich Presence updates")]
    public float PresenceInterval = 1f;
    [Tooltip("Seconds between looks at the friends playing")]
    public float FriendsInterval = 2f;

    static PlatformManager _instance;
    static readonly List<PlatformFriend> _joinable = new List<PlatformFriend>();

    float _nextPresence, _nextFriends;
    string _pendingJoin;
    readonly Dictionary<string, string> _published = new Dictionary<string, string>();

    /// <summary>Friends whose games take another player now (in town, not full), for the Hub's join buttons.</summary>
    public static IReadOnlyList<PlatformFriend> JoinableFriends => _joinable;

    void Awake()
    {
        if (_instance != null && _instance != this)
        {
            Destroy(this);
            return;
        }
        _instance = this;
        _joinable.Clear();
        Platform.Current.JoinRequested += OnJoinRequested;
        Debug.Log($"PlatformManager: running on the {Platform.Current.Name} platform.");
    }

    void Update()
    {
        Platform.Current.Update();
        if (_pendingJoin != null) TryJoin();
        if (Time.unscaledTime >= _nextFriends)
        {
            _nextFriends = Time.unscaledTime + FriendsInterval;
            _joinable.Clear();
            foreach (var friend in Platform.Current.FriendsPlaying())
                if (friend.CanJoin) _joinable.Add(friend);
        }
        if (Time.unscaledTime < _nextPresence) return;
        _nextPresence = Time.unscaledTime + PresenceInterval;
        Publish();
    }

    void Publish()
    {
        bool menu = GameBootstrap.IsMenuScene(SceneManager.GetActiveScene().name);
        // A game scene's area is known once its subscenes have loaded; until then the last one stays
        if (!menu && !GameArea.TryGet(out _)) return;
        string area = menu ? PresenceKeys.AreaMenu : GameArea.IsTown ? PresenceKeys.AreaTown : PresenceKeys.AreaDungeon;
        int players = area == PresenceKeys.AreaMenu ? 0 : PlayersInGame();
        string host = menu ? null : Platform.Current.JoinId;
        Set(PresenceKeys.Area, area);
        Set(PresenceKeys.Players, players > 0 ? players.ToString() : null);
        Set(PresenceKeys.Host, host);
        Set(PresenceKeys.Connect, host != null ? PresenceKeys.ConnectPrefix + host : null);
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

    /// <summary>Joins a friend's game now, from town in this machine's own game (Hub join buttons).</summary>
    public static void Join(PlatformFriend friend)
    {
        if (Platform.Current.TryGetJoinEndpoint(friend.Host, out var endpoint)) NetSession.Join(endpoint, friend.Name);
    }

    void OnJoinRequested(string joinId)
    {
        _pendingJoin = joinId;
        TryJoin();
    }

    // A game is joined from town, in this machine's own game, with a character picked (not from the main menu)
    void TryJoin()
    {
        if (!Platform.Current.TryGetJoinEndpoint(_pendingJoin, out var endpoint))
        {
            _pendingJoin = null;
            return;
        }
        bool menu = GameBootstrap.IsMenuScene(SceneManager.GetActiveScene().name);
        if (!NetSession.IsHost || menu || !GameArea.TryGet(out _) || !GameArea.IsTown)
        {
            string notice = !NetSession.IsHost ? "Leave this game to join another one."
                : menu ? "Pick a character: you'll join your friend's game in town."
                : GameArea.IsTown ? null : "Go back to town to join your friend's game.";
            if (notice != null && NetSession.Notice != notice) NetSession.ShowNotice(notice);
            return;
        }
        _pendingJoin = null;
        NetSession.Join(endpoint, "your friend's game");
    }

    void OnDestroy()
    {
        if (_instance != this) return;
        _instance = null;
        Platform.Current.JoinRequested -= OnJoinRequested;
        Platform.Shutdown();
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
