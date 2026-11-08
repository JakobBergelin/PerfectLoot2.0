using System.Collections.Generic;
using System.Text;

/// <summary>What a statistic counts. Each has a total, and some also count per subject (an enemy, dungeon or skill).</summary>
public enum StatType
{
    TimePlayed,         // Seconds with a player in a game scene
    EnemiesKilled,      // Per enemy
    BossesKilled,       // Per boss
    Deaths,             // Per enemy that landed the killing blow ("Killed you")
    DungeonsCleared,    // Per dungeon
    ExperienceEarned,
    DamageDealt,        // Per skill, status effect or power (DamageLabel), as the Damage Meter counts it
}

/// <summary>
/// One set of statistics: the account's, or one character's. A total per StatType plus one number per subject, which
/// is saved with its display name so rows can be shown without the asset. Subjects are ids: an enemy's asset name, a
/// dungeon's Id, a DamageLabel's name.
/// </summary>
public class StatisticsSet
{
    public class Entry
    {
        public StatType Type;
        public string Subject;  // Empty for the total
        public string Name;     // Display name of the subject
        public double Value;
    }

    readonly Dictionary<(StatType, string), Entry> _entries = new Dictionary<(StatType, string), Entry>();

    public IEnumerable<Entry> Entries => _entries.Values;

    /// <summary>The total, or the subject's number. 0 for anything never counted.</summary>
    public double Get(StatType type, string subject = "") =>
        _entries.TryGetValue((type, subject ?? ""), out var entry) ? entry.Value : 0.0;

    /// <summary>Adds to the total, and to the subject's number when there is one.</summary>
    public void Add(StatType type, double amount, string subject = null, string name = null)
    {
        Find(type, "", null).Value += amount;
        if (!string.IsNullOrEmpty(subject)) Find(type, subject, name).Value += amount;
    }

    /// <summary>Sets a saved number (loading).</summary>
    public void Set(StatType type, string subject, string name, double value) => Find(type, subject ?? "", name).Value = value;

    Entry Find(StatType type, string subject, string name)
    {
        if (!_entries.TryGetValue((type, subject), out var entry))
            _entries[(type, subject)] = entry = new Entry { Type = type, Subject = subject, Name = name ?? subject };
        else if (!string.IsNullOrEmpty(name)) entry.Name = name;   // The latest name, in case it was renamed
        return entry;
    }

    /// <summary>"FrozenOrb" as "Frozen Orb".</summary>
    public static string Spaced(string name)
    {
        var spaced = new StringBuilder(name.Length + 4);
        foreach (char c in name)
        {
            if (char.IsUpper(c) && spaced.Length > 0) spaced.Append(' ');
            spaced.Append(c);
        }
        return spaced.ToString();
    }
}

/// <summary>An account's statistics: the account's own set and one per character. Saved by AccountSaves (Statistics.json).</summary>
public class AccountStatistics
{
    public readonly StatisticsSet Account = new StatisticsSet();
    readonly Dictionary<string, StatisticsSet> _characters = new Dictionary<string, StatisticsSet>();

    public IReadOnlyDictionary<string, StatisticsSet> Characters => _characters;

    /// <summary>The character's set (empty when it has none yet).</summary>
    public StatisticsSet Character(string guid)
    {
        if (string.IsNullOrEmpty(guid)) return null;
        if (!_characters.TryGetValue(guid, out var set)) _characters[guid] = set = new StatisticsSet();
        return set;
    }

    public void RemoveCharacter(string guid)
    {
        if (!string.IsNullOrEmpty(guid)) _characters.Remove(guid);
    }

    /// <summary>Counts on the account and on the character (when there is one).</summary>
    public void Add(AccountCharacter character, StatType type, double amount, string subject = null, string name = null)
    {
        if (amount == 0.0) return;
        Account.Add(type, amount, subject, name);
        if (character != null) Character(character.Guid).Add(type, amount, subject, name);
    }
}
