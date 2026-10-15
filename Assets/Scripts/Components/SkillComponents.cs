using System.Linq;
using Unity.Entities;
using Unity.Mathematics;

/// <summary>An equipped active skill. Written by ActiveSkillManager, state filled in each frame for UI.</summary>
public struct Skill : IBufferElementData
{
    public SkillType Type;
    public int SlotIndex;   // Which slot/key (Skill1..Skill4) triggers it

    public SkillState State;

    public struct Triggers
    {
        public bool OnUsed;
    }

    public Triggers events;
}

/// <summary>Per-frame skill state, reported for UI (mirrored to ActiveSkillSlot).</summary>
public struct SkillState
{
    public bool IsPressed;              // Set by SkillActivationSystem
    public bool IsActive;               // Firing/channeling; defaults to "enabled", skills may override
    public float CooldownRemaining;     // Only skills with a real cooldown (e.g. Dash) report this
    public float CooldownDuration;
    public float ResourceCost;          // Static, from the ActiveSkill asset
    public float ResourceCostPerSecond;
    public bool HasEnoughResource;      // Set by SkillActivationSystem
}

public enum SkillType
{
    FistOfThunder,
    Barrage,
    Fireball,
    Dash,
    Whirlwind,
    Tornado,

}

public class SkillUtil
{
    public static bool FindSkill(DynamicBuffer<Skill> buffer,SkillType type, ref int index)
    {
        for(int i = 0; i < buffer.Length; i++)
        {
            if(buffer[i].Type == type)
            {
                index = i;
                return true;
            }
        }
        return false;
    }
}

public struct FistsOfThunder : IComponentData, IEnableableComponent
{
    public float TimeSinceLastPunch;
    public int PunchCount;
}

public struct Barrage : IComponentData, IEnableableComponent
{
    public float AttackSpeed;
    public float Elapsed;
}

public struct FireBall : IComponentData, IEnableableComponent
{
    public float AttackSpeed;
    public float ResourceCost;
    public float Elapsed;
}

public struct Dash : IComponentData, IEnableableComponent
{
    public float Cooldown;
    public float Elapsed;

    public bool Dashing;

    public float3 NextPosition;
}

public struct Whirlwind : IComponentData, IEnableableComponent
{
    public float ResourceCostPerSecond;
    public Entity WeaponEntity;
    public float Elapsed;
    public bool Spinning;
}

public struct Tornado : IComponentData, IEnableableComponent
{
    public float AttackSpeed;
    public float ResourceCost;
    public float Elapsed;
}

public struct TornadoBehavior : IComponentData
{
    public float Elapsed;
    public float3 Direction;
    public float3 Position;
    public float3 Sway;
}
