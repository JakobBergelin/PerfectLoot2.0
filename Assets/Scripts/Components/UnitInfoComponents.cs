using System.Collections.Generic;
using EnemySkills;
using Passives;
using Unity.Entities;
using UnityEngine;

/// <summary>What the unit panel says about a kind of unit. Enemies and companions are made from one of these.</summary>
public interface IUnitDescription
{
    string DisplayName { get; }
    List<PassiveSkill> Passives { get; }
    List<EnemySkill> Skills { get; }
}

/// <summary>On enemies and companions: the asset the unit was made from, an IUnitDescription.</summary>
public struct UnitInfo : IComponentData
{
    public UnityObjectRef<ScriptableObject> Source;
}
