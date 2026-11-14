using System.Collections.Generic;

/// <summary>
/// This account's best finish per dungeon and player count, kept in its save (AccountSaves, Leaderboards.json) whether or
/// not the game runs on Steam: what the leaderboard panel shows without a platform. Read when first needed, and again
/// for another account.
/// </summary>
public static class LocalLeaderboard
{
    static string _accountId;
    static readonly List<LeaderboardResult> _bests = new List<LeaderboardResult>();

    [UnityEngine.RuntimeInitializeOnLoadMethod(UnityEngine.RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics()
    {
        _accountId = null;
        _bests.Clear();
    }

    /// <summary>The account's best for this dungeon and player count, or null.</summary>
    public static LeaderboardResult Best(string dungeonId, int players)
    {
        Load();
        return _bests.Find(best => best.DungeonId == dungeonId && best.Players == players);
    }

    /// <summary>Keeps the result when it beats the account's best for its dungeon and player count (and saves). True when it did.</summary>
    public static bool Record(LeaderboardResult result)
    {
        if (result == null || string.IsNullOrEmpty(result.DungeonId) || !Load()) return false;
        var best = Best(result.DungeonId, result.Players);
        if (best != null && Leaderboards.Score(best) >= Leaderboards.Score(result)) return false;
        if (best != null) _bests.Remove(best);
        _bests.Add(result);
        AccountSaves.WriteLeaderboards(_accountId, _bests);
        return true;
    }

    // The current account's bests; false without an account
    static bool Load()
    {
        var account = AccountManager.Instance;
        if (account == null) return false;
        if (_accountId == account.AccountId) return true;
        _accountId = account.AccountId;
        AccountSaves.ReadLeaderboards(_accountId, _bests);
        return true;
    }
}
