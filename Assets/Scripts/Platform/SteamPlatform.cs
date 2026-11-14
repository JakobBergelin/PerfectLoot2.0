// Needs Steamworks.NET (com.rlabrecque.steamworks.net), whose editor scripts add the STEAMWORKS_NET scripting define;
// without it the game runs on LocalPlatform. See Docs/MultiplayerArchitecture.md, Platform.
#if STEAMWORKS_NET
using System;
using System.Collections.Generic;
using Steamworks;
using Unity.Networking.Transport;
using UnityEngine;

/// <summary>
/// Steam, through Steamworks.NET: the persona name, Rich Presence, the friends playing this game, joining them, and
/// leaderboards (made on first use; read for the player and their friends).
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

    // ---------- Accounts and leaderboards ----------

    public ulong PlayerId => SteamUser.GetSteamID().m_SteamID;

    public string NameOf(ulong playerId)
    {
        if (playerId == 0) return null;
        if (playerId == PlayerId) return SteamFriends.GetPersonaName();
        var id = new CSteamID(playerId);
        // Someone who isn't a friend: Steam fetches their name first (true while it does)
        if (SteamFriends.RequestUserInformation(id, true)) return null;
        string name = SteamFriends.GetFriendPersonaName(id);
        return string.IsNullOrEmpty(name) || name == "[unknown]" ? null : name;
    }

    public bool HasLeaderboards => true;

    // Leaderboards found so far, by name, and the calls waiting for Steam's answer (kept so they aren't collected)
    readonly Dictionary<string, SteamLeaderboard_t> _boards = new Dictionary<string, SteamLeaderboard_t>();
    readonly List<object> _pending = new List<object>();

    public void SubmitScore(string board, int score, int[] details, Action<bool> done)
    {
        FindBoard(board, handle =>
        {
            if (handle == null) { done?.Invoke(false); return; }
            var call = SteamUserStats.UploadLeaderboardScore(handle.Value, ELeaderboardUploadScoreMethod.k_ELeaderboardUploadScoreMethodKeepBest,
                score, details, details?.Length ?? 0);
            Await<LeaderboardScoreUploaded_t>(call, (uploaded, failed) =>
            {
                bool ok = !failed && uploaded.m_bSuccess != 0;
                if (!ok) Debug.LogWarning($"SteamPlatform: couldn't send the score to {board}.");
                done?.Invoke(ok && uploaded.m_bScoreChanged != 0);
            });
        });
    }

    public void ReadScores(string board, Action<List<PlatformScore>> done)
    {
        FindBoard(board, handle =>
        {
            if (handle == null) { done?.Invoke(null); return; }
            var call = SteamUserStats.DownloadLeaderboardEntries(handle.Value, ELeaderboardDataRequest.k_ELeaderboardDataRequestFriends, 0, 0);
            Await<LeaderboardScoresDownloaded_t>(call, (downloaded, failed) =>
            {
                if (failed) { done?.Invoke(null); return; }
                var scores = new List<PlatformScore>();
                for (int i = 0; i < downloaded.m_cEntryCount; i++)
                {
                    var details = new int[Leaderboards.MaxDetails];
                    if (!SteamUserStats.GetDownloadedLeaderboardEntry(downloaded.m_hSteamLeaderboardEntries, i, out var entry, details, details.Length)) continue;
                    Array.Resize(ref details, Mathf.Clamp(entry.m_cDetails, 0, details.Length));
                    scores.Add(new PlatformScore { PlayerId = entry.m_steamIDUser.m_SteamID, Score = entry.m_nScore, Details = details });
                }
                done?.Invoke(scores);
            });
        });
    }

    // The leaderboard's handle (made when missing: higher scores first), or null when Steam couldn't find or make it
    void FindBoard(string board, Action<SteamLeaderboard_t?> then)
    {
        if (_boards.TryGetValue(board, out var known)) { then(known); return; }
        var call = SteamUserStats.FindOrCreateLeaderboard(board, ELeaderboardSortMethod.k_ELeaderboardSortMethodDescending,
            ELeaderboardDisplayType.k_ELeaderboardDisplayTypeNumeric);
        Await<LeaderboardFindResult_t>(call, (found, failed) =>
        {
            if (failed || found.m_bLeaderboardFound == 0)
            {
                Debug.LogWarning($"SteamPlatform: couldn't find or make the leaderboard {board}.");
                then(null);
                return;
            }
            _boards[board] = found.m_hSteamLeaderboard;
            then(found.m_hSteamLeaderboard);
        });
    }

    // Runs then with Steam's answer to an asynchronous call (from RunCallbacks, on the main thread)
    void Await<T>(SteamAPICall_t call, Action<T, bool> then) where T : struct
    {
        CallResult<T> result = null;
        result = CallResult<T>.Create((answer, failed) =>
        {
            _pending.Remove(result);
            then(answer, failed);
        });
        result.Set(call);
        _pending.Add(result);
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
