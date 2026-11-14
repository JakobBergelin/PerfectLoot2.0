using System.Collections.Generic;
using LevelGeneration;
using Unity.Collections;
using Unity.Entities;
using UnityEngine;

/// <summary>
/// Dungeon difficulty: a whole number from 1 up, with no end. Finishing a dungeon at N unlocks N + 1 for the character,
/// and each character remembers the difficulty it last ran each dungeon at (AccountCharacter.CompletedDungeons).
/// The host's player 1 picks it at the dungeon's portal (PortalHUD), from what their character has unlocked; the pick is
/// kept here by dungeon id, starts at their last one, and goes to the clients (PortalDifficultyRpc) so their portal cards
/// show it. In the dungeon, enemies spawn stronger (EnemyDefinition.StatsAt, SkillsAt, PassivesAt) and in larger numbers
/// (DungeonDefinition.EnemyCount). Only the host spawns, so only the host's picks matter.
/// </summary>
public static class DungeonDifficulty
{
    // The host's picks, and on a client the host's picks as last heard (kept apart, so a client that hosts later starts clean)
    static readonly Dictionary<string, int> _picks = new Dictionary<string, int>();
    static readonly Dictionary<string, int> _hostPicks = new Dictionary<string, int>();

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics()
    {
        _picks.Clear();
        _hostPicks.Clear();
    }

    /// <summary>The difficulty enemies spawn at here: the dungeon's in a dungeon, else 1.</summary>
    public static int Current => TryGetDungeon(out var dungeon) ? Of(dungeon.Id) : 1;

    /// <summary>A level spawner group's size here: grown with the dungeon's difficulty in a dungeon, else as it is.</summary>
    public static int EnemyCount(int count) => TryGetDungeon(out var dungeon) ? dungeon.EnemyCount(count, Of(dungeon.Id)) : count;

    /// <summary>
    /// The difficulty this dungeon's next (or current) run is at: the host's pick, else its player 1's last one, else 1.
    /// On a client the host's pick, or 0 while it's not known yet.
    /// </summary>
    public static int Of(string dungeonId)
    {
        if (string.IsNullOrEmpty(dungeonId)) return 1;
        if (!NetSession.IsHost) return _hostPicks.TryGetValue(dungeonId, out int heard) ? heard : 0;
        if (!_picks.TryGetValue(dungeonId, out int picked))
        {
            var record = Record(dungeonId);
            picked = record != null ? record.Last : 1;
        }
        // Another character may have fewer unlocked
        return Mathf.Clamp(picked, 1, Unlocked(dungeonId));
    }

    /// <summary>The highest difficulty the host's player 1 can pick for this dungeon: one above the highest they finished.</summary>
    public static int Unlocked(string dungeonId)
    {
        var record = Record(dungeonId);
        return record != null ? Mathf.Max(1, record.Highest + 1) : 1;
    }

    /// <summary>The host: picks this dungeon's difficulty (kept within 1 and Unlocked). True when it changed; the clients are told.</summary>
    public static bool Pick(string dungeonId, int difficulty)
    {
        if (!NetSession.IsHost || string.IsNullOrEmpty(dungeonId)) return false;
        int before = Of(dungeonId);
        int after = Mathf.Clamp(difficulty, 1, Unlocked(dungeonId));
        _picks[dungeonId] = after;
        if (after == before) return false;
        var world = World.DefaultGameObjectInjectionWorld;
        if (world != null && world.IsCreated) Send(world.EntityManager, dungeonId);
        return true;
    }

    /// <summary>The host: tells the clients the pick for this dungeon (on a change, and every second for each portal in the level).</summary>
    public static void Send(EntityManager entityManager, string dungeonId)
    {
        if (!NetSession.IsHost || string.IsNullOrEmpty(dungeonId)) return;
        var message = new PortalDifficultyRpc { Difficulty = Of(dungeonId) };
        message.DungeonId.CopyFromTruncated(dungeonId);
        NetPortal.Send(entityManager, message);
    }

    /// <summary>A client: the host's pick for a dungeon (NetDungeonReceiveSystem).</summary>
    public static void Receive(PortalDifficultyRpc message)
    {
        if (NetSession.IsHost || message.DungeonId.IsEmpty) return;
        _hostPicks[message.DungeonId.ToString()] = Mathf.Max(1, message.Difficulty);
    }

    // The host's player 1's character's record of the dungeon, or null
    static AccountCharacter.CompletedDungeon Record(string dungeonId)
    {
        var character = AccountManager.Instance != null ? AccountManager.Instance.SelectedCharacterOf(0) : null;
        return character?.CompletedDungeons.Find(entry => entry.DungeonId == dungeonId);
    }

    static bool TryGetDungeon(out DungeonDefinition dungeon)
    {
        dungeon = null;
        if (!GameArea.TryGet(out var area) || area.Kind != AreaKind.Dungeon) return false;
        dungeon = area.Dungeon.Value;
        return dungeon != null;
    }
}
