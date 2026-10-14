using Passives;
using System.Collections.Generic;
using Unity.Collections;
using Unity.Entities;
using UnityEngine;

public class PassiveSkillManager : MonoBehaviour
{
    public static PassiveSkillManager Instance;
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
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;

        foreach (var slot in Slots)
            if (slot != null) slot.Changed += OnSlotChanged;
    }

    void Start()
    {
        RandomizeEmptySlots();
        RebuildPassives();
    }

    /// <summary>Fills empty slots with random passives from AllPassives, never repeating a PassiveType.</summary>
    void RandomizeEmptySlots()
    {
        var taken = new HashSet<PassiveType>();
        foreach (var slot in Slots)
            if (slot != null && !slot.IsEmpty) taken.Add(slot.EquippedPassive.Type);

        var pool = new List<PassiveSkill>();
        foreach (var passive in AllPassives)
            if (passive != null && taken.Add(passive.Type)) pool.Add(passive);

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
        if (Instance == this) Instance = null;
    }

    void OnSlotChanged(PassiveSkillSlot slot) => RebuildPassives();

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
        using var query = entityManager.CreateEntityQuery(ComponentType.ReadOnly<Player>(), ComponentType.ReadWrite<Passive>());
        if (query.IsEmpty) return;

        using var players = query.ToEntityArray(Allocator.Temp);
        foreach (var player in players)
        {
            foreach (var passive in _applied)
                if (!Active.Contains(passive)) passive.Remove(entityManager, player);

            foreach (var passive in Active)
                passive.Apply(entityManager, player);

            // Apply/Remove are structural changes, so fetch the buffer afterwards
            var buffer = entityManager.GetBuffer<Passive>(player);
            buffer.Clear();
            foreach (var passive in Active)
                buffer.Add(new Passive { Type = passive.Type });
        }

        _applied.Clear();
        _applied.AddRange(Active);
        _dirty = false;
    }
}
