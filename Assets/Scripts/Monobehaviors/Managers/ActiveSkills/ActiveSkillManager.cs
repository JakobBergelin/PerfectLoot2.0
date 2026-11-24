using ActiveSkills;
using System.Collections.Generic;
using Unity.Collections;
using Unity.Entities;
using UnityEngine;

/// <summary>
/// Pushes the skills in Slot1..Slot4 to the player (Skill buffer + skill components) and mirrors each
/// skill's SkillState back into its slot every frame for UI. Activation itself is SkillActivationSystem.
/// The slots start with the selected character's saved skills and every change is saved with the character
/// (AccountManager.SetSkills); PassiveSkillManager does the same for passives. A new character is eased in: it starts
/// with its class's StartingSkill, gets its SecondSkill at the class's SecondSkillLevel, and after that is nudged to the Skills panel
/// (PlayerNotices, "New" on the wheel) while skills its level reached wait for an empty slot.
/// </summary>
public class ActiveSkillManager : MonoBehaviour, IPerPlayer
{
    static readonly ActiveSkillManager[] _players = new ActiveSkillManager[GameInput.MaxPlayers];

    /// <summary>Player 1's.</summary>
    public static ActiveSkillManager Instance => _players[0];

    /// <summary>A local player's (0 is player 1), or null when that player isn't in the game.</summary>
    public static ActiveSkillManager For(int playerIndex) =>
        playerIndex >= 0 && playerIndex < _players.Length ? _players[playerIndex] : null;

    /// <summary>Which local player's this is: 0 is player 1 (the rig's own); player 2's copy is made by CoopManager.</summary>
    public int PlayerIndex { get; set; }
    public ActiveSkillSlot Slot1, Slot2, Slot3, Slot4;

    [Header("Every skill that can be equipped")]
    public List<ActiveSkill> AllSkills = new List<ActiveSkill>();

    // Skills currently applied to the player entity, so unequipped ones can be removed
    readonly List<ActiveSkill> _applied = new List<ActiveSkill>();
    bool _dirty;

    ActiveSkillSlot[] Slots => new[] { Slot1, Slot2, Slot3, Slot4 };

    void Awake()
    {
        if (For(PlayerIndex) != null && For(PlayerIndex) != this) { Destroy(gameObject); return; }
        _players[PlayerIndex] = this;

        foreach (var slot in Slots)
            if (slot != null) slot.Changed += OnSlotChanged;
        CharacterManager.PlayerSpawned += OnPlayerSpawned;
    }

    // The character picked in Character Select, whose skills the slots show and save; null when the scene was played
    // without the main menu (the rig's own slots, not saved)
    AccountCharacter _character;

    // The character's saved skills (only its class's StartingSkill when it has none yet), and its SecondSkill when it's
    // reached the class's SecondSkillLevel without it. Its empty slots stay empty: the player fills them in the Skills panel. Without a
    // character every slot is filled.
    void Start()
    {
        LoadCharacterSkills();
        if (_character == null) FillEmptySlots();
        else
        {
            if (_character.ActiveSkills.Count == 0) GiveStartingSkill();
            _shownLevel = _character.Level;
            GiveSecondSkill(announce: false);
        }
        SaveCharacterSkills();
        PushSkills();
    }

    // ---------- Easing a new character into its skills ----------

    // The character's level the slots were last checked at
    int _shownLevel;

    void GiveStartingSkill()
    {
        var characterClass = CharacterClass();
        var skill = characterClass != null ? characterClass.StartingSkill : null;
        if (skill == null && characterClass != null)
        {
            var ordered = SkillRequirements.InUnlockOrder(AllSkills, characterClass);
            if (ordered.Count > 0) skill = ordered[0];
        }
        if (skill != null && Slot1 != null) Slot1.EquippedSkill = skill;
    }

    // Once, at the class's SecondSkillLevel: its SecondSkill in the first empty slot (not when it's equipped already or no
    // slot is free)
    void GiveSecondSkill(bool announce)
    {
        var characterClass = CharacterClass();
        if (_character == null || _character.GotSecondSkill || _character.Level < SecondSkillLevel()) return;
        _character.GotSecondSkill = true;
        var skill = characterClass != null ? characterClass.SecondSkill : null;
        var empty = System.Array.Find(Slots, slot => slot != null && slot.IsEmpty);
        if (skill != null && empty != null && !IsEquipped(skill))
        {
            if (announce)
            {
                empty.EquipSkill(skill);    // Pushed and saved by OnSlotChanged
                PlayerNotices.Post(PlayerIndex, $"New skill: {NameOf(skill)}");
            }
            else empty.EquippedSkill = skill;
        }
        if (AccountManager.Instance != null) AccountManager.Instance.SaveSkillProgress(_character);
    }

    // The level the class's SecondSkill is given at (2 without a class)
    int SecondSkillLevel()
    {
        var characterClass = CharacterClass();
        return characterClass != null ? characterClass.SecondSkillLevel : 2;
    }

    // A level up: the second skill, and after its level a nudge to the Skills panel while new skills wait for an empty slot
    void CheckLevel()
    {
        if (_character == null || _character.Level == _shownLevel) return;
        bool levelledUp = _character.Level > _shownLevel;
        _shownLevel = _character.Level;
        if (!levelledUp) return;
        GiveSecondSkill(announce: true);
        if (_character.Level <= SecondSkillLevel() ||!System.Array.Exists(Slots, slot => slot != null && slot.IsEmpty)) return;

        var names = new List<string>();
        foreach (var skill in AllSkills)
            if (IsNew(skill)) names.Add(NameOf(skill));
        if (names.Count == 0) return;
        const int MaxNames = 3;
        string list = names.Count <= MaxNames ? string.Join(", ", names)
                    : $"{string.Join(", ", names.GetRange(0, MaxNames))} and {names.Count - MaxNames} more";
        PlayerNotices.Post(PlayerIndex, $"New skill{(names.Count > 1 ? "s" : "")} to equip: {list}\n" +
                                        $"Open Skills ({PlayerNotices.SkillsKey(PlayerIndex)}) in town");
    }

    /// <summary>
    /// A skill the character's class reaches by its level that isn't equipped and the player hasn't seen in the Skills
    /// panel yet (MarkNewSkillsSeen). False without a character.
    /// </summary>
    public bool IsNew(ActiveSkill skill)
    {
        if (skill == null || _character == null || IsEquipped(skill) || _character.SeenSkills.Contains(skill.Type.ToString())) return false;
        var characterClass = CharacterClass();
        return characterClass != null && SkillRequirements.UnlockLevel(skill.Requirements, characterClass) <= _character.Level;
    }

    /// <summary>The Skills panel was looked at: its new skills aren't new anymore.</summary>
    public void MarkNewSkillsSeen()
    {
        if (_character == null) return;
        bool changed = false;
        foreach (var skill in AllSkills)
            if (IsNew(skill))
            {
                _character.SeenSkills.Add(skill.Type.ToString());
                changed = true;
            }
        if (changed && AccountManager.Instance != null) AccountManager.Instance.SaveSkillProgress(_character);
    }

    bool IsEquipped(ActiveSkill skill) => System.Array.Exists(Slots, slot => slot != null && slot.EquippedSkill == skill);

    static string NameOf(ActiveSkill skill) => string.IsNullOrEmpty(skill.Name) ? skill.name : skill.Name;

    // Set directly instead of EquipSkill so Start pushes once instead of once per slot
    void LoadCharacterSkills()
    {
        _character = AccountManager.Instance != null ? AccountManager.Instance.SelectedCharacterOf(PlayerIndex) : null;
        if (_character == null) return;
        var slots = Slots;
        for (int i = 0; i < slots.Length && i < _character.ActiveSkills.Count; i++)
        {
            string type = _character.ActiveSkills[i];
            if (slots[i] == null || string.IsNullOrEmpty(type)) continue;
            var skill = AllSkills.Find(candidate => candidate != null && candidate.Type.ToString() == type);
            if (skill == null) Debug.LogWarning($"{_character.Name}'s skill '{type}' doesn't exist anymore, so the slot is left empty.", this);
            slots[i].EquippedSkill = skill;
        }
    }

    void SaveCharacterSkills()
    {
        if (_character == null || AccountManager.Instance == null) return;
        var types = new List<string>();
        foreach (var slot in Slots) types.Add(slot != null && !slot.IsEmpty ? slot.EquippedSkill.Type.ToString() : "");
        AccountManager.Instance.SetSkills(_character, types, null);
    }

    /// <summary>
    /// Fills empty slots from AllSkills, never repeating a SkillType: the first skills the character's class reaches
    /// (SkillRequirements.InUnlockOrder), or random ones without a class (a scene played without the main menu).
    /// </summary>
    void FillEmptySlots()
    {
        var taken = new HashSet<SkillType>();
        foreach (var slot in Slots)
            if (slot != null && !slot.IsEmpty) taken.Add(slot.EquippedSkill.Type);

        var characterClass = CharacterClass();
        var pool = new List<ActiveSkill>();
        foreach (var skill in characterClass != null ? SkillRequirements.InUnlockOrder(AllSkills, characterClass) : AllSkills)
            if (skill != null && taken.Add(skill.Type)) pool.Add(skill);

        foreach (var slot in Slots)
        {
            if (slot == null || !slot.IsEmpty) continue;
            if (pool.Count == 0)
            {
                Debug.LogWarning("Not enough different skills in AllSkills to fill every skill slot.");
                return;
            }

            int index = characterClass != null ? 0 : Random.Range(0, pool.Count);
            // Set directly instead of EquipSkill so Start pushes once instead of once per slot
            slot.EquippedSkill = pool[index];
            pool.RemoveAt(index);
        }
    }

    // The played character's class asset (CharacterManager's list), or the rig's own class without a character
    Characters.CharacterClass CharacterClass()
    {
        var characters = CharacterManager.For(PlayerIndex);
        if (characters == null) return null;
        if (_character == null) return characters.Class;
        return characters.AllClasses.Find(candidate => candidate != null && candidate.Type == _character.Class);
    }

    void OnDestroy()
    {
        foreach (var slot in Slots)
            if (slot != null) slot.Changed -= OnSlotChanged;
        CharacterManager.PlayerSpawned -= OnPlayerSpawned;
        if (For(PlayerIndex) == this) _players[PlayerIndex] = null;
    }

    void OnSlotChanged(ActiveSkillSlot slot)
    {
        PushSkills();
        SaveCharacterSkills();
    }

    // New player entity (spawn/respawn): none of the skills are on it yet
    void OnPlayerSpawned(Entity player)
    {
        if (!TryGetPlayer(out _, out var own) || player != own) return;
        _applied.Clear();
        PushSkills();
    }

    void Update()
    {
        // The player entity may not exist yet (subscene still loading), so retry until it does.
        if (_dirty) TryPushSkills();
        CheckLevel();
    }

    // After the simulation has run, so the slots show this frame's state
    void LateUpdate() => MirrorState();

    /// <summary>Re-applies all slots, e.g. after editing values on a skill asset during play.</summary>
    [ContextMenu("Push skills")]
    public void PushSkills()
    {
        _dirty = true;
        TryPushSkills();
    }

    // This player's entity (CharacterManager with the same PlayerIndex), once it has its Skill buffer
    bool TryGetPlayer(out EntityManager entityManager, out Entity player)
    {
        entityManager = default;
        player = Entity.Null;
        var world = World.DefaultGameObjectInjectionWorld;
        var characters = CharacterManager.For(PlayerIndex);
        if (world == null || !world.IsCreated || characters == null) return false;

        entityManager = world.EntityManager;
        player = characters.Player;
        return player != Entity.Null && entityManager.Exists(player) && entityManager.HasBuffer<Skill>(player);
    }

    void TryPushSkills()
    {
        // In someone else's game the host runs this player: the loadout is sent to it instead (NetLoadoutSendSystem)
        if (!NetSession.IsHost)
        {
            NetLoadoutSendSystem.MarkDirty(PlayerIndex);
            _dirty = false;
            return;
        }
        if (!TryGetPlayer(out var entityManager, out var player)) return;

        var slots = Slots;
        var skills = new ActiveSkill[slots.Length];
        var variants = new int[slots.Length];
        for (int i = 0; i < slots.Length; i++)
        {
            if (slots[i] == null || slots[i].IsEmpty) continue;
            skills[i] = slots[i].EquippedSkill;
            variants[i] = slots[i].Variant;
        }
        ApplySkills(entityManager, player, skills, variants, _applied);
        _dirty = false;
    }

    /// <summary>
    /// Gives a player entity these skills (by slot, null for an empty one) and removes the ones applied before that aren't
    /// among them (applied, updated). Each slot's variant is its skill's own mode (Stances' stance), carried on from
    /// before. Used for this machine's players and, on the host, for remote ones (NetLoadoutReceiveSystem).
    /// </summary>
    public static void ApplySkills(EntityManager entityManager, Entity player, IReadOnlyList<ActiveSkill> skillsBySlot, IReadOnlyList<int> variants,
                                   List<ActiveSkill> applied)
    {
        // The same skill type in two slots only applies once
        var equipped = new List<(int SlotIndex, ActiveSkill Skill)>();
        for (int i = 0; i < skillsBySlot.Count; i++)
        {
            var skill = skillsBySlot[i];
            if (skill == null) continue;
            if (equipped.Exists(e => e.Skill.Type == skill.Type))
            {
                Debug.LogWarning($"{skill.Type} is equipped in more than one skill slot, only the first one is used.");
                continue;
            }
            equipped.Add((i, skill));
        }

        foreach (var skill in applied)
            if (!equipped.Exists(e => e.Skill == skill)) skill.Remove(entityManager, player);
        foreach (var (_, skill) in equipped)
            skill.Apply(entityManager, player);

        // Apply/Remove are structural changes, so fetch the buffer afterwards
        var buffer = entityManager.GetBuffer<Skill>(player);
        buffer.Clear();
        foreach (var (slotIndex, skill) in equipped)
        {
            var entry = ToSkill(slotIndex, skill);
            entry.State.Variant = variants[slotIndex];
            buffer.Add(entry);
        }

        // Its copy uses the skills above
        foreach (var (_, skill) in equipped)
            if (skill is MirrorImageSkill mirrorImage) mirrorImage.SetUpCopy(entityManager, player, equipped);

        applied.Clear();
        foreach (var (_, skill) in equipped) applied.Add(skill);
    }

    /// <summary>What goes in a unit's Skill buffer for this skill in this slot.</summary>
    public static Skill ToSkill(int slotIndex, ActiveSkill skill) => new Skill
    {
        Type = skill.Type,
        SlotIndex = slotIndex,
        BaseTags = skill.Tags,
        Tags = skill.Tags,
        Label = SkillDamageLabel.Of(skill.Type),
        CritShake = skill.CritShake,
        HitShake = skill.HitShake,
        Requirements = skill.Requirements,
        RequiredGear = skill.RequiredGear,
        Priority = skill.Priority,
        State = new SkillState
        {
            ResourceCost = skill.ResourceCost,
            ResourceCostPerSecond = skill.ResourceCostPerSecond,
            CooldownDuration = skill.Cooldown,
            HasEnoughResource = true
        }
    };

    void MirrorState()
    {
        var slots = Slots;
        if (_dirty || !TryGetPlayer(out var entityManager, out var player))
        {
            foreach (var slot in slots) if (slot != null) slot.ClearState();
            return;
        }

        var buffer = entityManager.GetBuffer<Skill>(player, isReadOnly: true);
        for (int i = 0; i < slots.Length; i++)
        {
            if (slots[i] == null) continue;
            bool found = false;
            foreach (var skill in buffer)
            {
                if (skill.SlotIndex != i) continue;
                slots[i].SetState(skill.State);
                // The asset's requirements or priority were edited (e.g. in Play mode): push them again. Only where
                // they're on the entity: in someone else's game only the type, slot and state arrive
                if (NetSession.IsHost && !slots[i].IsEmpty && (!skill.Requirements.Equals(slots[i].EquippedSkill.Requirements) || skill.RequiredGear != slots[i].EquippedSkill.RequiredGear || skill.Priority != slots[i].EquippedSkill.Priority)) _dirty = true;
                found = true;
                break;
            }
            if (!found) slots[i].ClearState();
        }
    }

#if UNITY_EDITOR
    void OnValidate()
    {
        var seen = new HashSet<SkillType>();
        foreach (var skill in AllSkills)
        {
            if (skill == null) { Debug.LogWarning($"{name}: AllSkills has an empty entry.", this); continue; }
            if (!seen.Add(skill.Type)) Debug.LogWarning($"{name}: {skill.Type} is in AllSkills more than once.", this);
        }
        foreach (SkillType type in System.Enum.GetValues(typeof(SkillType)))
            if (!seen.Contains(type))
                Debug.LogWarning($"{name}: AllSkills is missing {type}. Run Tools > Create missing active skill assets.", this);
    }
#endif
}
