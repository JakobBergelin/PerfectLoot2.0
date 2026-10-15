using ActiveSkills;
using System.Collections.Generic;
using Unity.Collections;
using Unity.Entities;
using UnityEngine;

/// <summary>
/// Pushes the skills in Slot1..Slot4 to the player (Skill buffer + skill components) and mirrors each
/// skill's SkillState back into its slot every frame for UI. Activation itself is SkillActivationSystem.
/// </summary>
public class ActiveSkillManager : MonoBehaviour
{
    public static ActiveSkillManager Instance;
    public ActiveSkillSlot Slot1, Slot2, Slot3, Slot4;

    [Header("Every skill that can be equipped")]
    public List<ActiveSkill> AllSkills = new List<ActiveSkill>();

    // Skills currently applied to the player entity, so unequipped ones can be removed
    readonly List<ActiveSkill> _applied = new List<ActiveSkill>();
    bool _dirty;

    ActiveSkillSlot[] Slots => new[] { Slot1, Slot2, Slot3, Slot4 };

    void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;

        foreach (var slot in Slots)
            if (slot != null) slot.Changed += OnSlotChanged;
    }

    void Start() => PushSkills();

    void OnDestroy()
    {
        foreach (var slot in Slots)
            if (slot != null) slot.Changed -= OnSlotChanged;
        if (Instance == this) Instance = null;
    }

    void OnSlotChanged(ActiveSkillSlot slot) => PushSkills();

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

    // Cached because MirrorState runs every frame
    World _queryWorld;
    EntityQuery _playerQuery;

    bool TryGetPlayer(out EntityManager entityManager, out Entity player)
    {
        entityManager = default;
        player = Entity.Null;
        var world = World.DefaultGameObjectInjectionWorld;
        if (world == null || !world.IsCreated) return false;

        entityManager = world.EntityManager;
        if (_queryWorld != world)
        {
            _playerQuery = entityManager.CreateEntityQuery(ComponentType.ReadOnly<Player>(), ComponentType.ReadWrite<Skill>());
            _queryWorld = world;
        }
        if (_playerQuery.IsEmpty) return false;

        using var players = _playerQuery.ToEntityArray(Allocator.Temp);
        player = players[0];
        return true;
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
        {
            buffer.Add(new Skill
            {
                Type = skill.Type,
                SlotIndex = slotIndex,
                State = new SkillState
                {
                    ResourceCost = skill.ResourceCost,
                    ResourceCostPerSecond = skill.ResourceCostPerSecond,
                    CooldownDuration = skill.Cooldown,
                    HasEnoughResource = true
                }
            });
        }

        _applied.Clear();
        foreach (var (_, skill) in equipped) _applied.Add(skill);
        _dirty = false;
    }

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
