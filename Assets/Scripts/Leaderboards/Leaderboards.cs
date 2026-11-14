using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>One player of a leaderboard result's group.</summary>
[Serializable]
public class LeaderboardMember
{
    public ulong PlatformId;    // Their machine's Steam account; 0 without one
    public int LocalIndex;      // 0: their machine's first player; 1 and up: a couch player on it
    public ClassType Class;
    public string Name;         // Their machine's name when the run finished (Steam name, else player 1's character name)
    public bool Owner;          // On the machine whose result this is (the entry's owner and their couch player)
}

/// <summary>A finished dungeon run, as the leaderboards keep it.</summary>
[Serializable]
public class LeaderboardResult
{
    public string DungeonId;
    public int Players;         // The category: the most players in the group at any point of the run (1 to 4)
    public int Difficulty;
    public float Seconds;       // Of the dungeon's clock used, death costs included (DungeonManager.TimeUsed)
    public List<LeaderboardMember> Members = new List<LeaderboardMember>();
}

/// <summary>A row of a leaderboard as the panel shows it.</summary>
public class LeaderboardRow
{
    public int Rank;            // 1 is the best of the rows read
    public ulong OwnerId;       // The entry's Steam account; 0 for this machine's own result without Steam
    public bool Mine;           // This machine's own entry
    public LeaderboardResult Result;
}

/// <summary>
/// Dungeon leaderboards: one per dungeon and player count (1 to 4), keeping each account's best finish. A higher
/// difficulty always beats a faster time (Score). With a platform that has leaderboards (Steam) they're read for this
/// player and their friends; without one, this account's own bests (LocalLeaderboard, kept either way) are shown.
/// DungeonManager records a finish on every machine of the group (Record); LeaderboardPanelHUD shows them.
/// </summary>
public static class Leaderboards
{
    public const int MaxPlayers = 4;
    // Centiseconds a time can have: up to 2 h 46 min; anything longer counts as that
    const int TimeSlots = 1000000;
    const int MaxDifficulty = 2000;     // Score stays within an int

    // Steam's details: a version, the run, then each member (its Steam id in two halves, class, local index + owner flag)
    const int DetailsVersion = 1, HeaderInts = 5, IntsPerMember = 4, OwnerFlag = 256;
    public const int MaxDetails = HeaderInts + IntsPerMember * MaxPlayers;

    /// <summary>Higher is better: the difficulty first, then the shorter time.</summary>
    public static int Score(int difficulty, float seconds)
    {
        int centiseconds = Mathf.Clamp(Mathf.RoundToInt(seconds * 100f), 0, TimeSlots - 1);
        return Mathf.Clamp(difficulty, 1, MaxDifficulty) * TimeSlots + (TimeSlots - 1 - centiseconds);
    }

    public static int Score(LeaderboardResult result) => Score(result.Difficulty, result.Seconds);

    /// <summary>The platform's leaderboard for a dungeon and player count.</summary>
    public static string BoardName(string dungeonId, int players) => $"PerfectLoot_{dungeonId}_{Mathf.Clamp(players, 1, MaxPlayers)}p";

    /// <summary>
    /// Records a finish: kept as this account's best when it is one, and sent to the platform's leaderboard. done gets
    /// whether it's a new best (the platform's answer when it has leaderboards).
    /// </summary>
    public static void Record(LeaderboardResult result, Action<bool> done)
    {
        bool localBest = LocalLeaderboard.Record(result);
        var platform = Platform.Current;
        if (!platform.HasLeaderboards)
        {
            done?.Invoke(localBest);
            return;
        }
        var board = BoardName(result.DungeonId, result.Players);
        platform.SubmitScore(board, Score(result), ToDetails(result), changed =>
        {
            Debug.Log($"Leaderboards: {board} difficulty {result.Difficulty} in {FormatTime(result.Seconds)}, " +
                      (changed ? "a new best" : "not better than the best"));
            done?.Invoke(changed);
        });
    }

    /// <summary>A dungeon's leaderboard for a player count, best first: this player and their friends, or without a platform their own best.</summary>
    public static void Read(string dungeonId, int players, Action<List<LeaderboardRow>> done)
    {
        var platform = Platform.Current;
        if (!platform.HasLeaderboards)
        {
            var rows = new List<LeaderboardRow>();
            var best = LocalLeaderboard.Best(dungeonId, players);
            if (best != null) rows.Add(new LeaderboardRow { Rank = 1, Mine = true, Result = best });
            done?.Invoke(rows);
            return;
        }
        platform.ReadScores(BoardName(dungeonId, players), scores =>
        {
            var rows = new List<LeaderboardRow>();
            if (scores != null)
            {
                ulong me = platform.PlayerId;
                foreach (var score in scores)
                {
                    var result = FromDetails(dungeonId, players, score.Score, score.Details);
                    rows.Add(new LeaderboardRow { OwnerId = score.PlayerId, Mine = score.PlayerId == me, Result = result });
                }
                rows.Sort((a, b) => Score(b.Result).CompareTo(Score(a.Result)));
                for (int i = 0; i < rows.Count; i++) rows[i].Rank = i + 1;
            }
            done?.Invoke(scores == null ? null : rows);
        });
    }

    /// <summary>This machine's name for the others in a group: its Steam name, else player 1's character name.</summary>
    public static string MachineName()
    {
        string name = Platform.Current.PlayerName;
        if (!string.IsNullOrEmpty(name)) return name;
        var character = AccountManager.Instance != null ? AccountManager.Instance.SelectedCharacterOf(0) : null;
        return character != null && !string.IsNullOrEmpty(character.Name) ? character.Name : "Player";
    }

    /// <summary>A member as a row shows them: their Steam name (as Steam knows it now), else the name kept; a couch player as theirs.</summary>
    public static string MemberName(LeaderboardMember member)
    {
        string name = member.PlatformId != 0 ? Platform.Current.NameOf(member.PlatformId) : null;
        if (string.IsNullOrEmpty(name)) name = string.IsNullOrEmpty(member.Name) ? "Player" : member.Name;
        return member.LocalIndex > 0 ? $"{name}'s couch player" : name;
    }

    /// <summary>"4:05.32": rounded to hundredths, as the platform keeps it</summary>
    public static string FormatTime(float seconds)
    {
        int centiseconds = Mathf.Max(0, Mathf.RoundToInt(seconds * 100f));
        int hours = centiseconds / 360000, minutes = centiseconds / 6000 % 60, whole = centiseconds / 100 % 60, hundredths = centiseconds % 100;
        return hours > 0 ? $"{hours}:{minutes:00}:{whole:00}.{hundredths:00}" : $"{minutes}:{whole:00}.{hundredths:00}";
    }

    public static int[] ToDetails(LeaderboardResult result)
    {
        int count = Mathf.Min(result.Members.Count, MaxPlayers);
        var details = new int[HeaderInts + IntsPerMember * count];
        details[0] = DetailsVersion;
        details[1] = Mathf.Clamp(Mathf.RoundToInt(result.Seconds * 100f), 0, TimeSlots - 1);
        details[2] = result.Difficulty;
        details[3] = result.Players;
        details[4] = count;
        for (int i = 0; i < count; i++)
        {
            var member = result.Members[i];
            int at = HeaderInts + IntsPerMember * i;
            details[at] = unchecked((int)(uint)(member.PlatformId & 0xFFFFFFFF));
            details[at + 1] = unchecked((int)(uint)(member.PlatformId >> 32));
            details[at + 2] = (int)member.Class;
            details[at + 3] = member.LocalIndex + (member.Owner ? OwnerFlag : 0);
        }
        return details;
    }

    /// <summary>A result from a platform entry; the score alone when its details can't be read.</summary>
    public static LeaderboardResult FromDetails(string dungeonId, int players, int score, int[] details)
    {
        var result = new LeaderboardResult
        {
            DungeonId = dungeonId,
            Players = players,
            Difficulty = score / TimeSlots,
            Seconds = (TimeSlots - 1 - score % TimeSlots) / 100f,
        };
        if (details == null || details.Length < HeaderInts || details[0] != DetailsVersion) return result;
        int count = Mathf.Clamp(details[4], 0, Mathf.Min(MaxPlayers, (details.Length - HeaderInts) / IntsPerMember));
        for (int i = 0; i < count; i++)
        {
            int at = HeaderInts + IntsPerMember * i;
            result.Members.Add(new LeaderboardMember
            {
                PlatformId = (uint)details[at] | ((ulong)(uint)details[at + 1] << 32),
                Class = (ClassType)details[at + 2],
                LocalIndex = details[at + 3] % OwnerFlag,
                Owner = details[at + 3] >= OwnerFlag,
            });
        }
        return result;
    }
}
