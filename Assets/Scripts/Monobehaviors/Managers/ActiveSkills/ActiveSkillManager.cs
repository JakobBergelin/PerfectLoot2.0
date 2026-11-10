using ActiveSkills;
using System.Collections.Generic;
using Unity.Collections;
using Unity.Entities;
using UnityEngine;

/// <summary>
/// Pushes the skills in Slot1..Slot4 to the player (Skill buffer + skill components) and mirrors each
/// skill's SkillState back into its slot every frame for UI. Activation itself is SkillActivationSystem.
/// The slots start with the selected character's saved skills (random ones where it has none) and every change is saved
/// with the character (AccountManager.SetSkills). PassiveSkillManager does the same for passives.
/// </summary>
public class ActiveSkillManager : MonoBehaviour
{
    static readonly ActiveSkillManager[] _players = new ActiveSkillManager[GameInput.MaxPlayers];

    /// <summary>Player 1's.</summary>
    public static ActiveSkillManager Instance => _players[0];

    /// <summary>A local player's (0 is player 1), or null when that player isn't in the game.</summary>
    public static ActiveSkillManager For(int playerIndex) =>
        playerIndex >= 0 && playerIndex < _players.Length ? _players[playerIndex] : null;

    [Tooltip("Which local player's skills these are (CharacterManager.PlayerIndex)")]
    public int PlayerIndex;
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

    // The character's saved skills, then random ones in the slots still empty (saved too, so they stay)
    void Start()
    {
        LoadCharacterSkills();
        RandomizeEmptySlots();
        SaveCharacterSkills();
        PushSkills();
    }

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
            if (skill == null) Debug.LogWarning($"{_character.Name}'s skill '{type}' doesn't exist anymore, so the slot is filled at random.", this);
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

    /// <summary>Fills empty slots with random skills from AllSkills, never repeating a SkillType.</summary>
    void RandomizeEmptySlots()
    {
        var taken = new HashSet<SkillType>();
        foreach (var slot in Slots)
            if (slot != null && !slot.IsEmpty) taken.Add(slot.EquippedSkill.Type);

        var pool = new List<ActiveSkill>();
        foreach (var skill in AllSkills)
            if (skill != null && taken.Add(skill.Type)) pool.Add(skill);

        foreach (var slot in Slots)
        {
            if (slot == null || !slot.IsEmpty) continue;
            if (pool.Count == 0)
            {
                Debug.LogWarning("Not enough different skills in AllSkills to fill every skill slot.");
                return;
            }

            int index = Random.Range(0, pool.Count);
            // Set directly instead of EquipSkill so Start pushes once instead of once per slot
            slot.EquippedSkill = pool[index];
            pool.RemoveAt(index);
        }
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
        if (!TryGetPlayer(out var entityManager, out var player)) return;

        // Equipped skills by slot; the same skill type in two slots only applies once
        var equipped = new List<(int SlotIndex, ActiveSkill Skill)>();
        var slots = Slots;
        for (int i = 0; i < slots.Length; i++)
        {
            if (slots[i] == null || slots[i].IsEmpty) continue;
            var skill = slots[i].EquippedSkill;
            if (equipped.Exists(e => e.Skill.Type == skill.Type))
            {
                Debug.LogWarning($"{skill.Type} is equipped in more than one skill slot, only the first one is used.");
                continue;
            }
            equipped.Add((i, skill));
        }

        foreach (var skill in _applied)
            if (!equipped.Exists(e => e.Skill == skill)) skill.Remove(entityManager, player);
        foreach (var (_, skill) in equipped)
            skill.Apply(entityManager, player);

        // Apply/Remove are structural changes, so fetch the buffer afterwards
        var buffer = entityManager.GetBuffer<Skill>(player);
        buffer.Clear();
        foreach (var (slotIndex, skill) in equipped)
            buffer.Add(ToSkill(slotIndex, skill));

        // Its copy uses the skills above
        foreach (var (_, skill) in equipped)
            if (skill is MirrorImageSkill mirrorImage) mirrorImage.SetUpCopy(entityManager, player, equipped);

        _applied.Clear();
        foreach (var (_, skill) in equipped) _applied.Add(skill);
        _dirty = false;
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
