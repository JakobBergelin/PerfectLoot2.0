using Passives;
using System.Linq;
using System.Collections.Generic;
using Unity.Collections;
using Unity.Entities;
using UnityEngine;

public class PassiveSkillManager : MonoBehaviour
{
    static readonly PassiveSkillManager[] _players = new PassiveSkillManager[GameInput.MaxPlayers];

    /// <summary>Player 1's.</summary>
    public static PassiveSkillManager Instance => _players[0];

    /// <summary>A local player's (0 is player 1), or null when that player isn't in the game.</summary>
    public static PassiveSkillManager For(int playerIndex) =>
        playerIndex >= 0 && playerIndex < _players.Length ? _players[playerIndex] : null;

    [Tooltip("Which local player's passives these are (CharacterManager.PlayerIndex)")]
    public int PlayerIndex;
    public PassiveSkillSlot Slot1, Slot2, Slot3;

    [Header("Every passive that can be rolled into an empty slot at start")]
    public List<PassiveSkill> AllPassives = new List<PassiveSkill>();

    [Header("Active passives (read-only, rebuilt whenever a slot changes)")]
    public List<PassiveSkill> Active = new List<PassiveSkill>();

    // Passives currently applied to the player entity, so deselected ones can be removed
    readonly List<PassiveSkill> _applied = new List<PassiveSkill>();
    bool _dirty;

    PassiveSkillSlot[] Slots => new[] { Slot1, Slot2, Slot3 };

    void Awake()
    {
        if (For(PlayerIndex) != null && For(PlayerIndex) != this) { Destroy(gameObject); return; }
        _players[PlayerIndex] = this;

        foreach (var slot in Slots)
            if (slot != null) slot.Changed += OnSlotChanged;
        CharacterManager.PlayerSpawned += OnPlayerSpawned;
    }

    // The character picked in Character Select, whose passives the slots show and save; null when the scene was played
    // without the main menu (the rig's own slots, not saved)
    AccountCharacter _character;

    // The character's saved passives, then random ones in the slots still empty (saved too, so they stay)
    void Start()
    {
        LoadCharacterPassives();
        RandomizeEmptySlots();
        SaveCharacterPassives();
        RebuildPassives();
    }

    // Set directly instead of EquipPassive so Start pushes once instead of once per slot
    void LoadCharacterPassives()
    {
        _character = AccountManager.Instance != null ? AccountManager.Instance.SelectedCharacterOf(PlayerIndex) : null;
        if (_character == null) return;
        var slots = Slots;
        for (int i = 0; i < slots.Length && i < _character.Passives.Count; i++)
        {
            string type = _character.Passives[i];
            if (slots[i] == null || string.IsNullOrEmpty(type)) continue;
            var passive = SelectablePassives.FirstOrDefault(candidate => candidate.Type.ToString() == type);
            if (passive == null) Debug.LogWarning($"{_character.Name}'s passive '{type}' can't be equipped anymore, so the slot is filled at random.", this);
            slots[i].EquippedPassive = passive;
        }
    }

    void SaveCharacterPassives()
    {
        if (_character == null || AccountManager.Instance == null) return;
        AccountManager.Instance.SetSkills(_character, null, Slots.Select(slot => slot != null && !slot.IsEmpty ? slot.EquippedPassive.Type.ToString() : ""));
    }

    // This player's entity (CharacterManager with the same PlayerIndex), or Entity.Null
    Entity OwnPlayer
    {
        get
        {
            var characters = CharacterManager.For(PlayerIndex);
            return characters != null ? characters.Player : Entity.Null;
        }
    }

    /// <summary>The passives players can equip.</summary>
    public IEnumerable<PassiveSkill> SelectablePassives => AllPassives.Where(passive => passive != null && passive.PlayerSelectable);

    /// <summary>Fills empty slots with random passives from SelectablePassives, never repeating a PassiveType.</summary>
    void RandomizeEmptySlots()
    {
        var taken = new HashSet<PassiveType>();
        foreach (var slot in Slots)
            if (slot != null && !slot.IsEmpty) taken.Add(slot.EquippedPassive.Type);

        var pool = new List<PassiveSkill>();
        foreach (var passive in SelectablePassives)
            if (taken.Add(passive.Type)) pool.Add(passive);

        foreach (var slot in Slots)
        {
            if (slot == null || !slot.IsEmpty) continue;
            if (pool.Count == 0)
            {
                Debug.LogWarning("Not enough different passives in AllPassives to fill every passive slot.");
                return;
            }

            int index = Random.Range(0, pool.Count);
            // Set directly instead of EquipPassive so Start pushes once instead of once per slot
            slot.EquippedPassive = pool[index];
            pool.RemoveAt(index);
        }
    }

#if UNITY_EDITOR
    void OnValidate()
    {
        var seen = new HashSet<PassiveType>();
        foreach (var passive in AllPassives)
        {
            if (passive == null) { Debug.LogWarning($"{name}: AllPassives has an empty entry.", this); continue; }
            if (!seen.Add(passive.Type)) Debug.LogWarning($"{name}: {passive.Type} is in AllPassives more than once.", this);
        }
        foreach (PassiveType type in System.Enum.GetValues(typeof(PassiveType)))
            if (type != PassiveType.None && !seen.Contains(type))
                Debug.LogWarning($"{name}: AllPassives is missing {type}. Run Tools > Create missing passive assets.", this);
    }
#endif

    void OnDestroy()
    {
        foreach (var slot in Slots)
            if (slot != null) slot.Changed -= OnSlotChanged;
        CharacterManager.PlayerSpawned -= OnPlayerSpawned;
        if (For(PlayerIndex) == this) _players[PlayerIndex] = null;
    }

    void OnSlotChanged(PassiveSkillSlot slot)
    {
        RebuildPassives();
        SaveCharacterPassives();
    }

    // New player entity (spawn/respawn): none of the passives are on it yet
    void OnPlayerSpawned(Entity player)
    {
        if (player != OwnPlayer) return;
        _applied.Clear();
        _dirty = true;
        TryPushPassives();
    }

    // The player entity may not exist yet when slots change (subscene still loading), so retry until it does.
    void Update()
    {
        if (_dirty) TryPushPassives();
    }

    /// <summary>Rebuilds Active from the slots and pushes it to the player.</summary>
    public void RebuildPassives()
    {
        Active.Clear();
        foreach (var slot in Slots)
        {
            if (slot == null || slot.IsEmpty) continue;
            var passive = slot.EquippedPassive;
            if (Active.Exists(p => p.Type == passive.Type))
            {
                Debug.LogWarning($"{passive.Type} is equipped in more than one passive slot, it only applies once.");
                continue;
            }
            Active.Add(passive);
        }

        _dirty = true;
        TryPushPassives();
    }

    /// <summary>Re-applies all passives, e.g. after editing values on a passive asset during play.</summary>
    [ContextMenu("Push passives")]
    void PushPassives()
    {
        _dirty = true;
        TryPushPassives();
    }

    void TryPushPassives()
    {
        var world = World.DefaultGameObjectInjectionWorld;
        if (world == null || !world.IsCreated) return;

        var entityManager = world.EntityManager;
        var player = OwnPlayer;
        if (player == Entity.Null || !entityManager.Exists(player) || !entityManager.HasBuffer<Passive>(player)) return;

        foreach (var passive in _applied)
            if (!Active.Contains(passive)) passive.Remove(entityManager, player);

        foreach (var passive in Active)
            passive.Apply(entityManager, player);

        // Apply/Remove are structural changes, so fetch the buffer afterwards
        var buffer = entityManager.GetBuffer<Passive>(player);
        buffer.Clear();
        foreach (var passive in Active)
            buffer.Add(new Passive { Type = passive.Type });

        _applied.Clear();
        _applied.AddRange(Active);
        _dirty = false;
    }
}
