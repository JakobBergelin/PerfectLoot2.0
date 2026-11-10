using System.Collections.Generic;
using Unity.Entities;
using UnityEngine;

/// <summary>
/// Adds what the statistics systems found (StatEvent) to the account's and the played characters' statistics, and
/// counts what's easier to see from here: time played, dungeons cleared (DungeonManager's state) and damage dealt per
/// skill (the Damage Meter's totals, taken when the level is left). Saves on leaving a scene, on quit and every minute.
/// Lives on the GameRig, so it's kept between scenes. Everything it counts goes to the signed-in account. In co-op, what
/// the players share (kills, dungeons cleared) counts for both characters but once for the account; deaths, experience,
/// time and damage count for the player they're about.
/// </summary>
public class StatisticsRecorder : MonoBehaviour
{
    const float SaveInterval = 60f;

    float _sinceSave;
    World _queryWorld, _harvestedWorld;
    EntityQuery _events, _meter;
    DungeonManager _watchedDungeon;
    DungeonManager.RunState _dungeonState;
    readonly List<AccountCharacter> _characters = new List<AccountCharacter>();

    void OnEnable() => GameSceneLoader.Loading += OnLeavingScene;
    void OnDisable() => GameSceneLoader.Loading -= OnLeavingScene;
    void OnApplicationQuit() => OnLeavingScene();

    void LateUpdate()
    {
        var account = AccountManager.Instance;
        if (account == null) return;
        var statistics = account.Statistics;

        ReadEvents(account);

        // Each player's character while they're in the level; the account once while anyone is
        bool anyone = false;
        for (int i = 0; i < GameInput.MaxPlayers; i++)
        {
            var characters = CharacterManager.For(i);
            if (characters == null || characters.Player == Entity.Null) continue;
            var character = account.SelectedCharacterOf(i);
            if (character != null) statistics.Character(character.Guid).Add(StatType.TimePlayed, Time.deltaTime);
            anyone = true;
        }
        if (anyone) statistics.Account.Add(StatType.TimePlayed, Time.deltaTime);

        WatchDungeon(account);

        _sinceSave += Time.unscaledDeltaTime;
        if (_sinceSave >= SaveInterval) Save(account);
    }

    // Before the world is replaced (or the game quits): what's left of this level, then the save
    void OnLeavingScene()
    {
        var account = AccountManager.Instance;
        if (account == null) return;
        ReadEvents(account);
        ReadDamageMeter(account);
        Save(account);
    }

    void Save(AccountManager account)
    {
        account.SaveStatistics();
        _sinceSave = 0f;
    }

    // The characters the players play (player 1's first)
    List<AccountCharacter> PlayedCharacters(AccountManager account)
    {
        _characters.Clear();
        for (int i = 0; i < GameInput.PlayerCount; i++)
        {
            var character = account.SelectedCharacterOf(i);
            if (character != null) _characters.Add(character);
        }
        return _characters;
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

    void ReadEvents(AccountManager account)
    {
        if (!UpdateQueries() || _events.IsEmpty) return;
        var statistics = account.Statistics;
        var events = _queryWorld.EntityManager.GetBuffer<StatEvent>(_events.GetSingletonEntity());
        foreach (var stat in events)
        {
            var subject = stat.Subject.Value;
            string id = subject != null ? subject.name : null;
            string name = subject is IUnitDescription unit ? unit.DisplayName : id;
            if (stat.Player == StatEvent.Everyone) statistics.Add(PlayedCharacters(account), stat.Type, stat.Amount, id, name);
            else statistics.Add(account.SelectedCharacterOf(stat.Player), stat.Type, stat.Amount, id, name);
        }
        events.Clear();
    }

    // The meter has everything since the level started, so it's read once, when the level is left. Each skill row says
    // whose it is (MeterName.Player)
    void ReadDamageMeter(AccountManager account)
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
            var character = account.SelectedCharacterOf(Mathf.Max(0, key.Name.Player - 1));
            account.Statistics.Add(character, StatType.DamageDealt, row.Value.Total, id, StatisticsSet.Spaced(id));
        }
    }

    // A dungeon run that turned Complete since the last frame
    void WatchDungeon(AccountManager account)
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
            account.Statistics.Add(PlayedCharacters(account), StatType.DungeonsCleared, 1, dungeon.Dungeon.Id, dungeon.Dungeon.DisplayName);
    }
}
