// Needs Steamworks.NET (com.rlabrecque.steamworks.net) and the PERFECTLOOT_STEAM scripting define; without them the game
// runs on LocalPlatform. See Docs/MultiplayerArchitecture.md, Platform.
#if PERFECTLOOT_STEAM
using System.Collections.Generic;
using Steamworks;
using UnityEngine;

/// <summary>
/// Steam, through Steamworks.NET: the persona name, Rich Presence and the friends playing this game. Made by
/// PlatformManager only when Steam runs and the API starts (TryCreate); otherwise the game uses LocalPlatform.
/// Steam's networking, lobbies and join requests come in phase O6.
/// </summary>
public class SteamPlatform : IPlatform
{
    // Friends' presence is asked for again this often (Steam sends it for friends in the same game, but not always at once)
    const float PresenceRequestSeconds = 10f;

    readonly List<PlatformFriend> _friends = new List<PlatformFriend>();
    float _nextPresenceRequest;

    SteamPlatform() { }

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
        catch (System.DllNotFoundException e)
        {
            Debug.LogWarning($"SteamPlatform: steam_api64.dll couldn't be loaded ({e.Message}), so the game runs without Steam.");
            return null;
        }
        return new SteamPlatform();
    }

    public string Name => "Steam";
    public string PlayerName => SteamFriends.GetPersonaName();

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
                Lobby = SteamFriends.GetFriendRichPresence(id, PresenceKeys.Lobby),
            });
        }
        return _friends;
    }

    public void Update() => SteamAPI.RunCallbacks();

    public void Shutdown()
    {
        SteamFriends.ClearRichPresence();
        SteamAPI.Shutdown();
    }
}
#endif
