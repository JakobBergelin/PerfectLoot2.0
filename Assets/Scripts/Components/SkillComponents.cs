using System.Linq;
using Unity.Entities;
using Unity.Mathematics;

public struct Skill : IBufferElementData
{
    public SkillType Type;
    public struct UIInfo
    {
        public float MaxCooldown;
        public float CurrentCooldown;
        public float ResourceCost;
        public float ResourceCostPerSecond;
    }

    public UIInfo UIInformation;

    public struct Triggers
    {
        public bool OnUsed;
        public bool OnPressed;
    }

    public Triggers events;
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
    public float Elapsed;
}

public struct Dash : IComponentData, IEnableableComponent
{
    public float Elapsed;

    public bool Dashing;

    public float3 NextPosition;
}

public struct Whirlwind : IComponentData, IEnableableComponent
{
    public Entity WeaponEntity;
    public float Elapsed;
    public bool Spinning;
}

public struct Tornado : IComponentData, IEnableableComponent
{
    public float Elapsed;
}

public struct TornadoBehavior : IComponentData
{
    public float Elapsed;
    public float3 Direction;
    public float3 Position;
    public float3 Sway;
}
