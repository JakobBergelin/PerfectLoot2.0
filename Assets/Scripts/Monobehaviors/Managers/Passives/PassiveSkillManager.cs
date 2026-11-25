using Passives;
using System.Linq;
using System.Collections.Generic;
using Unity.Collections;
using Unity.Entities;
using UnityEngine;

public class PassiveSkillManager : MonoBehaviour, IPerPlayer
{
    static readonly PassiveSkillManager[] _players = new PassiveSkillManager[GameInput.MaxPlayers];

    /// <summary>Player 1's.</summary>
    public static PassiveSkillManager Instance => _players[0];

    /// <summary>A local player's (0 is player 1), or null when that player isn't in the game.</summary>
    public static PassiveSkillManager For(int playerIndex) =>
        playerIndex >= 0 && playerIndex < _players.Length ? _players[playerIndex] : null;

    /// <summary>Which local player's this is: 0 is player 1 (the rig's own); player 2's copy is made by CoopManager.</summary>
    public int PlayerIndex { get; set; }
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

    // The character's saved passives; a new character gets a random one in its first slot (saved, so it stays), and its
    // other slots stay empty for the player to fill. Without a character every slot is filled at random.
    void Start()
    {
        LoadCharacterPassives();
        if (_character == null) RandomizeEmptySlots(Slots.Length);
        else
        {
            if (_character.Passives.Count == 0) RandomizeEmptySlots(1);
            _shownLevel = _character.Level;
        }
        SaveCharacterPassives();
        RebuildPassives();
    }

    // ---------- Slots unlocked by level ----------

    /// <summary>The level each passive slot unlocks at, by slot.</summary>
    public static readonly int[] SlotUnlockLevels = { 1, 5, 10 };

    // The character's level the slots were last checked at
    int _shownLevel;

    /// <summary>
    /// Whether this slot (0 is the first) is open at the character's level; only open slots apply their passive. Every
    /// slot is open without a character.
    /// </summary>
    public bool IsUnlocked(int slotIndex) =>
        slotIndex >= 0 && slotIndex < SlotUnlockLevels.Length && (_character == null || _character.Level >= SlotUnlockLevels[slotIndex]);

    /// <summary>True while a slot that's open at the character's level has no passive.</summary>
    public bool HasEmptySlot
    {
        get
        {
            for (int i = 0; i < Slots.Length; i++)
                if (IsUnlocked(i) && Slots[i] != null && Slots[i].IsEmpty) return true;
            return false;
        }
    }

    // A level up that opens a slot: its passive (one saved from before) applies, and a nudge to the Skills panel
    void CheckLevel()
    {
        if (_character == null || _character.Level == _shownLevel) return;
        int previous = _shownLevel;
        _shownLevel = _character.Level;
        bool opened = false;
        foreach (int level in SlotUnlockLevels)
            if (level > previous && level <= _shownLevel) opened = true;
        if (!opened) return;
        RebuildPassives();
        PlayerNotices.Post(PlayerIndex, $"New passive slot: open Skills ({PlayerNotices.SkillsKey(PlayerIndex)}){(GameArea.CanChangeLoadout ? "" : " in town")} to choose one");
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
            if (passive == null) Debug.LogWarning($"{_character.Name}'s passive '{type}' can't be equipped anymore, so the slot is left empty.", this);
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

    /// <summary>Fills the first count slots that are empty with random passives from SelectablePassives, never repeating a PassiveType.</summary>
    void RandomizeEmptySlots(int count)
    {
        var taken = new HashSet<PassiveType>();
        foreach (var slot in Slots)
            if (slot != null && !slot.IsEmpty) taken.Add(slot.EquippedPassive.Type);

        var pool = new List<PassiveSkill>();
        foreach (var passive in SelectablePassives)
            if (taken.Add(passive.Type)) pool.Add(passive);

        var slots = Slots;
        for (int i = 0; i < slots.Length && i < count; i++)
        {
            var slot = slots[i];
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
        CheckLevel();
    }

    /// <summary>Rebuilds Active from the open slots and pushes it to the player.</summary>
    public void RebuildPassives()
    {
        Active.Clear();
        var slots = Slots;
        for (int i = 0; i < slots.Length; i++)
        {
            var slot = slots[i];
            if (slot == null || slot.IsEmpty || !IsUnlocked(i)) continue;
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
        // In someone else's game the host runs this player: the loadout is sent to it instead (NetLoadoutSendSystem)
        if (!NetSession.IsHost)
        {
            NetLoadoutSendSystem.MarkDirty(PlayerIndex);
            _dirty = false;
            return;
        }

        var world = World.DefaultGameObjectInjectionWorld;
        if (world == null || !world.IsCreated) return;

        var entityManager = world.EntityManager;
        var player = OwnPlayer;
        if (player == Entity.Null || !entityManager.Exists(player) || !entityManager.HasBuffer<Passive>(player)) return;

        ApplyPassives(entityManager, player, Active, _applied);
        _dirty = false;
    }

    /// <summary>
    /// Gives a player entity these passives (each type once) and removes the ones applied before that aren't among them
    /// (applied, updated). Used for this machine's players and, on the host, for remote ones (NetLoadoutReceiveSystem).
    /// </summary>
    public static void ApplyPassives(EntityManager entityManager, Entity player, IReadOnlyList<PassiveSkill> active, List<PassiveSkill> applied)
    {
        foreach (var passive in applied)
            if (!active.Contains(passive)) passive.Remove(entityManager, player);

        foreach (var passive in active)
            passive.Apply(entityManager, player);

        // Apply/Remove are structural changes, so fetch the buffer afterwards
        var buffer = entityManager.GetBuffer<Passive>(player);
        buffer.Clear();
        foreach (var passive in active)
            buffer.Add(new Passive { Type = passive.Type });

        applied.Clear();
        applied.AddRange(active);
    }
}
