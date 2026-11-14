using Unity.Entities;
using Unity.NetCode;
using UnityEngine;

/// <summary>
/// A stand-in ghost for an effect that follows something only the host has (a projectile, a tornado, a soul): the host's
/// VfxSystem makes one per such effect, moves it along and destroys it with its entity. Other machines' VfxSystem plays the
/// same effect on it, and its impact where it goes away.
/// </summary>
public struct NetVfx : IComponentData
{
    [GhostField] public VfxType Type;
    [GhostField] public VfxType Impact;
    /// <summary>Above 0: the effect is sized sideways to it (a field's radius), or every way when Uniform.</summary>
    [GhostField(Quantization = 100)] public float Scale;
    /// <summary>Sized every way to the stand-in's own LocalTransform.Scale, which the host keeps at its entity's size (a growing flame's radius, a wall's scale).</summary>
    [GhostField] public bool Uniform;
    /// <summary>Turned like the stand-in's LocalTransform (a wall), instead of facing where it moves.</summary>
    [GhostField] public bool Oriented;
    [GhostField] public bool Tinted;
    /// <summary>Tinted: its colour is this damage type's (VfxSystem.StrikeColor).</summary>
    [GhostField] public DamageType Tint;
}

/// <summary>On the NetVfx ghost prefab (Prefabs/Net/NetVfx).</summary>
public class NetVfxAuthoring : MonoBehaviour
{
    class Baker : Baker<NetVfxAuthoring>
    {
        public override void Bake(NetVfxAuthoring authoring) => AddComponent(GetEntity(TransformUsageFlags.Dynamic), new NetVfx());
    }
}
