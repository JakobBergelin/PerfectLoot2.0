using ActiveSkills;
using System;
using UnityEngine;

/// <summary>
/// One active skill slot (Slot1 = Skill1 key, ...). Its state is mirrored from the player's Skill buffer
/// every frame by ActiveSkillManager, so UI can read it without touching ECS.
/// </summary>
public class ActiveSkillSlot : MonoBehaviour
{
    public ActiveSkill EquippedSkill;

    /// <summary>Raised after the equipped skill was set or removed.</summary>
    public event Action<ActiveSkillSlot> Changed;

    [Header("State (read-only, updated every frame from the player)")]
    public bool IsPressed;
    public bool IsActive;
    public bool HasEnoughResource;
    public float CooldownRemaining;
    public float CooldownDuration;

    public bool IsEmpty => EquippedSkill == null;
    public bool IsOnCooldown => CooldownRemaining > 0f;
    /// <summary>0 when ready, 1 right after use. Handy for a cooldown fill in UI.</summary>
    public float CooldownFraction => CooldownDuration > 0f ? CooldownRemaining / CooldownDuration : 0f;

    public void EquipSkill(ActiveSkill skill)
    {
        if (skill == null) return;
        EquippedSkill = skill;
        Changed?.Invoke(this);
    }

    public void UnequipSkill()
    {
        EquippedSkill = null;
        Changed?.Invoke(this);
    }

    internal void SetState(in SkillState state)
    {
        IsPressed = state.IsPressed;
        IsActive = state.IsActive;
        HasEnoughResource = state.HasEnoughResource;
        CooldownRemaining = state.CooldownRemaining;
        CooldownDuration = state.CooldownDuration;
    }

    internal void ClearState()
    {
        IsPressed = IsActive = false;
        HasEnoughResource = true;
        CooldownRemaining = CooldownDuration = 0f;
    }

    // Lets skills be swapped in the Inspector during play mode
    void OnValidate()
    {
        if (Application.isPlaying) Changed?.Invoke(this);
    }
}
