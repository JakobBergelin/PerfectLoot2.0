using Passives;
using System;
using UnityEngine;

public class PassiveSkillSlot : MonoBehaviour
{
    public PassiveSkill EquippedPassive;

    /// <summary>Raised after the equipped passive was set or removed.</summary>
    public event Action<PassiveSkillSlot> Changed;

    public bool IsEmpty => EquippedPassive == null;

    public void EquipPassive(PassiveSkill passive)
    {
        if (passive == null) return;
        EquippedPassive = passive;
        Changed?.Invoke(this);
    }

    public void UnequipPassive()
    {
        EquippedPassive = null;
        Changed?.Invoke(this);
    }

    // Lets passives be swapped in the Inspector during play mode
    void OnValidate()
    {
        if (Application.isPlaying) Changed?.Invoke(this);
    }
}
