// Needs Steamworks.NET (com.rlabrecque.steamworks.net), whose editor scripts add the STEAMWORKS_NET scripting define;
// without it the game runs on LocalPlatform. See Docs/MultiplayerArchitecture.md, Platform.
#if STEAMWORKS_NET
using System;
using System.Collections.Generic;
using Steamworks;
using Unity.Networking.Transport;
using UnityEngine;

/// <summary>
/// Steam, through Steamworks.NET: the persona name, Rich Presence, the friends playing this game, and joining them.
/// A game is joined by its host's SteamID (JoinId): the host listens on Steam (SteamNetworkInterface, added by
/// NetDriverConstructor), and a client connects to the host's SteamID through Steam's relays. Steam's Join Game in the
/// friends list and invites hand over the connect string ("+connect_host &lt;SteamID&gt;") as JoinRequested.
/// Made by Platform only when Steam runs and the API starts (TryCreate); otherwise the game uses LocalPlatform.
/// </summary>
public class SteamPlatform : IPlatform
{
    // Friends' presence is asked for again this often (Steam sends it for friends in the same game, but not always at once)
    const float PresenceRequestSeconds = 10f;

    readonly List<PlatformFriend> _friends = new List<PlatformFriend>();
    float _nextPresenceRequest;
    Callback<GameRichPresenceJoinRequested_t> _joinRequested;

    public event Action<string> JoinRequested;

    SteamPlatform()
    {
        _joinRequested = Callback<GameRichPresenceJoinRequested_t>.Create(request =>
        {
            string connect = request.m_rgchConnect ?? "";
            int start = connect.IndexOf(PresenceKeys.ConnectPrefix, StringComparison.Ordinal);
            if (start < 0) return;
            JoinRequested?.Invoke(connect.Substring(start + PresenceKeys.ConnectPrefix.Length).Trim());
        });
    }

    /// <summary>A started Steam API, or null when Steam isn't running, the app isn't owned or the library is missing.</summary>
    public static SteamPlatform TryCreate()
    {
        try
        {
            if (!Packsize.Test() || !DllCheck.Test())
            {
                Debug.LogWarning("SteamPlatform: the Steamworks.NET library doesn't match this platform, so the game runs without Steam.");
                return null;
            }
            if (!SteamAPI.Init())
            {
                Debug.Log("SteamPlatform: Steam isn't running (or steam_appid.txt is missing in the editor), so the game runs without Steam.");
                return null;
            }
        }
        catch (DllNotFoundException e)
        {
            Debug.LogWarning($"SteamPlatform: steam_api64.dll couldn't be loaded ({e.Message}), so the game runs without Steam.");
            return null;
        }
        return new SteamPlatform();
    }

    public string Name => "Steam";
    public string PlayerName => SteamFriends.GetPersonaName();
    public bool HostsOnline => true;

    // The host is this machine, or the one a client joined through Steam
    public string JoinId => NetSession.IsHost ? SteamUser.GetSteamID().m_SteamID.ToString()
        : SteamNetworkInterface.TryGetSteamId(NetSession.ConnectTo, out var host) ? host.ToString() : null;

    public bool TryGetJoinEndpoint(string joinId, out NetworkEndpoint endpoint)
    {
        endpoint = default;
        if (!ulong.TryParse(joinId, out var steamId) || steamId == 0) return false;
        endpoint = SteamNetworkInterface.Endpoint(steamId);
        return true;
    }

    public void SetPresence(string key, string value) => SteamFriends.SetRichPresence(key, value ?? "");
    public void ClearPresence() => SteamFriends.ClearRichPresence();

    public IReadOnlyList<PlatformFriend> FriendsPlaying()
    {
        _friends.Clear();
        var app = SteamUtils.GetAppID();
        bool request = Time.unscaledTime >= _nextPresenceRequest;
        if (request) _nextPresenceRequest = Time.unscaledTime + PresenceRequestSeconds;

        int count = SteamFriends.GetFriendCount(EFriendFlags.k_EFriendFlagImmediate);
        for (int i = 0; i < count; i++)
        {
            var id = SteamFriends.GetFriendByIndex(i, EFriendFlags.k_EFriendFlagImmediate);
            if (!SteamFriends.GetFriendGamePlayed(id, out var game) || game.m_gameID.AppID() != app) continue;
            if (request) SteamFriends.RequestFriendRichPresence(id);
            int.TryParse(SteamFriends.GetFriendRichPresence(id, PresenceKeys.Players), out int players);
            _friends.Add(new PlatformFriend
            {
                Id = id.m_SteamID.ToString(),
                Name = SteamFriends.GetFriendPersonaName(id),
                Area = SteamFriends.GetFriendRichPresence(id, PresenceKeys.Area),
                Players = players,
                Host = SteamFriends.GetFriendRichPresence(id, PresenceKeys.Host),
            });
        }
        return _friends;
    }

    public void Update() => SteamAPI.RunCallbacks();

    public void Shutdown()
    {
        _joinRequested?.Dispose();
        _joinRequested = null;
        SteamFriends.ClearRichPresence();
        SteamAPI.Shutdown();
    }
}
#endif
