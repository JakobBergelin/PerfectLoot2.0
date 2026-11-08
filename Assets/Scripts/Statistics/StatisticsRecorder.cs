using Unity.Entities;
using UnityEngine;

/// <summary>
/// Adds what the statistics systems found (StatEvent) to the account's and the played character's statistics, and
/// counts what's easier to see from here: time played, dungeons cleared (DungeonManager's state) and damage dealt per
/// skill (the Damage Meter's totals, taken when the level is left). Saves on leaving a scene, on quit and every minute.
/// Lives on the GameRig, so it's kept between scenes. Everything it counts goes to the signed-in account.
/// </summary>
public class StatisticsRecorder : MonoBehaviour
{
    const float SaveInterval = 60f;

    float _sinceSave;
    World _queryWorld, _harvestedWorld;
    EntityQuery _events, _meter;
    DungeonManager _watchedDungeon;
    DungeonManager.RunState _dungeonState;

    void OnEnable() => GameSceneLoader.Loading += OnLeavingScene;
    void OnDisable() => GameSceneLoader.Loading -= OnLeavingScene;
    void OnApplicationQuit() => OnLeavingScene();

    void LateUpdate()
    {
        var account = AccountManager.Instance;
        if (account == null) return;
        var statistics = account.Statistics;
        var character = account.SelectedCharacter;

        ReadEvents(statistics, character);

        var player = CharacterManager.Instance != null ? CharacterManager.Instance.Player : Entity.Null;
        if (player != Entity.Null) statistics.Add(character, StatType.TimePlayed, Time.deltaTime);

        WatchDungeon(statistics, character);

        _sinceSave += Time.unscaledDeltaTime;
        if (_sinceSave >= SaveInterval) Save(account);
    }

    // Before the world is replaced (or the game quits): what's left of this level, then the save
    void OnLeavingScene()
    {
        var account = AccountManager.Instance;
        if (account == null) return;
        ReadEvents(account.Statistics, account.SelectedCharacter);
        ReadDamageMeter(account.Statistics, account.SelectedCharacter);
        Save(account);
    }

    void Save(AccountManager account)
    {
        account.SaveStatistics();
        _sinceSave = 0f;
    }

    bool UpdateQueries()
    {
        var world = World.DefaultGameObjectInjectionWorld;
        if (world == null || !world.IsCreated) return false;
        if (_queryWorld != world)
        {
            _events = world.EntityManager.CreateEntityQuery(ComponentType.ReadWrite<StatEvent>());
            _meter = world.EntityManager.CreateEntityQuery(ComponentType.ReadOnly<DamageMeter>());
            _queryWorld = world;
        }
        return true;
    }

    void ReadEvents(AccountStatistics statistics, AccountCharacter character)
    {
        if (!UpdateQueries() || _events.IsEmpty) return;
        var events = _queryWorld.EntityManager.GetBuffer<StatEvent>(_events.GetSingletonEntity());
        foreach (var stat in events)
        {
            var subject = stat.Subject.Value;
            string id = subject != null ? subject.name : null;
            string name = subject is IUnitDescription unit ? unit.DisplayName : id;
            statistics.Add(character, stat.Type, stat.Amount, id, name);
        }
        events.Clear();
    }

    // The meter has everything since the level started, so it's read once, when the level is left
    void ReadDamageMeter(AccountStatistics statistics, AccountCharacter character)
    {
        if (!UpdateQueries() || _meter.IsEmpty || _harvestedWorld == _queryWorld) return;
        _harvestedWorld = _queryWorld;
        var meter = _meter.GetSingleton<DamageMeter>();
        if (!meter.Rows.IsCreated) return;
        foreach (var row in meter.Rows)
        {
            var key = row.Key;
            if (key.Stat != MeterStat.DamageDone || !key.BySource) continue;
            var label = key.Name.Label;
            if (label == DamageLabel.None || label == DamageLabel.Other) continue;
            string id = label.ToString();
            statistics.Add(character, StatType.DamageDealt, row.Value.Total, id, StatisticsSet.Spaced(id));
        }
    }

    // A dungeon run that turned Complete since the last frame
    void WatchDungeon(AccountStatistics statistics, AccountCharacter character)
    {
        var dungeon = DungeonManager.Instance;
        if (dungeon != _watchedDungeon)
        {
            _watchedDungeon = dungeon;
            _dungeonState = dungeon != null ? dungeon.State : DungeonManager.RunState.None;
            return;
        }
        if (dungeon == null || dungeon.State == _dungeonState) return;
        _dungeonState = dungeon.State;
        if (_dungeonState == DungeonManager.RunState.Complete && dungeon.Dungeon != null)
            statistics.Add(character, StatType.DungeonsCleared, 1, dungeon.Dungeon.Id, dungeon.Dungeon.DisplayName);
    }
}
