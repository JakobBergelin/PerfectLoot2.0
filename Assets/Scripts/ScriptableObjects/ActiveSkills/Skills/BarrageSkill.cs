using Unity.Mathematics;
using UnityEngine;

namespace ActiveSkills
{
    /// <summary>
    /// A toggle. While equipped, every few resource you spend launches a homing missile up into a cone. On: missiles go
    /// for the strongest enemy; off: the weakest.
    /// </summary>
    [CreateAssetMenu(fileName = "Barrage", menuName = "Active Skills/Barrage")]
    public class BarrageSkill : ActiveSkill<Barrage>
    {
        [Tooltip("Resource spent on skills for each missile")]
        [Min(1f)] public float ResourcePerMissile = 5f;
        [Tooltip("Degrees from straight up a missile can leave at")]
        [Range(0f, 90f)] public float ConeAngle = 45f;
        public float Damage = 3f;
        public float ProjectileSpeed = 7f;
        [Tooltip("Hit radius of each projectile")]
        public float Radius = 0.25f;

        [Header("Homing")]
        public float HomingTurnSpeed = 1.5f;
        public float HomingTurnAcceleration = 1.7f;

        public override SkillType Type => SkillType.Barrage;

        public override void Describe(SkillDescription description, StatBlock? stats)
        {
            description.Damage.Add(DamageLine(DamageType.Physical, "Damage per missile", stats).Text(Num(Damage)));
            description.Effects.Add(new SkillLine().Text($"A missile for every {Num(ResourcePerMissile)} resource spent"));
            description.Effects.Add(new SkillLine().Text("On: homes on the strongest enemy. Off: the weakest"));
            description.Effects.Add(AreaLine("Hit radius", Radius, stats));
        }

        protected override Barrage Configure(Barrage component, bool isNew)
        {
            component.ResourcePerMissile = ResourcePerMissile;
            component.ConeAngle = math.radians(ConeAngle);
            component.Damage = Damage;
            component.ProjectileSpeed = ProjectileSpeed;
            component.Radius = Radius;
            component.HomingTurnSpeed = HomingTurnSpeed;
            component.HomingTurnAcceleration = HomingTurnAcceleration;
            return component;
        }
    }
}
