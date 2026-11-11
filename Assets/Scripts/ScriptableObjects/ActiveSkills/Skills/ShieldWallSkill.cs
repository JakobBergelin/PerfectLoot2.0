using Equipment;
using UnityEngine;

namespace ActiveSkills
{
    /// <summary>Hold to raise your shield: you're slowed, and enemy projectiles in front of you are destroyed, each building resource.</summary>
    [CreateAssetMenu(fileName = "ShieldWall", menuName = "Active Skills/Shield Wall")]
    public class ShieldWallSkill : ActiveSkill<ShieldWall>
    {
        [Tooltip("Meters in front of you projectiles are blocked, scaled by area of effect")]
        [Min(0.1f)] public float Reach = 2.5f;
        [Tooltip("Degrees to each side of where you face")]
        [Range(0f, 180f)] public float HalfAngle = 60f;
        [Tooltip("Movement speed taken off while raised: 0.5 = 50%")]
        public float Slow = 0.5f;
        [Tooltip("Resource gained for each projectile blocked")]
        public float ResourcePerBlock = 2f;

        public override SkillType Type => SkillType.ShieldWall;

        public override void Describe(SkillDescription description, StatBlock? stats)
        {
            description.Effects.Add(new SkillLine().Icon(GameIcons.Affix(Affix.BlockChance)).Text($"Blocks enemy projectiles within {Num(HalfAngle)}° to each side"));
            description.Effects.Add(AreaLine("Reach", Reach, stats));
            description.Effects.Add(new SkillLine().Icon(GameIcons.Affix(Affix.ResourceRegeneration)).Text($"Resource per projectile blocked: +{Num(ResourcePerBlock)}"));
            description.Effects.Add(new SkillLine().Icon(GameIcons.Affix(Affix.MovementSpeed)).Text($"{Pct(Slow)} less movement speed while raised"));
        }

        protected override ShieldWall Configure(ShieldWall component, bool isNew)
        {
            component.Reach = Reach;
            component.HalfAngle = HalfAngle;
            component.Slow = Slow;
            component.ResourcePerBlock = ResourcePerBlock;
            return component;
        }
    }
}
