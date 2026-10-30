using UnityEngine;

namespace ActiveSkills
{
    [CreateAssetMenu(fileName = "CaptainsShield", menuName = "Active Skills/Captain's Shield")]
    public class CaptainsShieldSkill : ActiveSkill<CaptainsShield>
    {
        [SerializeField] float resourceCost = 5f;
        [Tooltip("Damage per hit, scaled by (1 + block chance)")]
        public float Damage = 3f;
        public float ProjectileSpeed = 18f;
        [Tooltip("Hit radius of the shield")]
        public float Radius = 0.5f;
        [Tooltip("Knockback speed on hit, away from the shield")]
        public float HitForce = 2f;
        [Tooltip("Targets after the first, before the Bounces stat")]
        public int Bounces = 1;
        [Tooltip("Radians per second the shield turns, at the start and after each new target")]
        public float TurnSpeed = 10f;
        [Tooltip("How fast the turn speed grows, radians per second per second")]
        public float TurnAcceleration = 20f;
        [Tooltip("How far it flies toward the cursor before returning when there's no enemy to home on")]
        public float NoTargetDistance = 10f;

        public override SkillType Type => SkillType.CaptainsShield;
        public override float ResourceCost => resourceCost;

        public override void Describe(SkillDescription description, StatBlock? stats)
        {
            // Damage × (1 + block chance), as CaptainsShieldSystem throws it
            var block = GameIcons.Affix(Equipment.Affix.BlockChance);
            var damage = DamageLine(DamageType.Physical, "Damage per hit", stats);
            if (stats is StatBlock s)
                damage.Text($"{Num(Damage * Mathf.Max(0f, 1f + s.BlockChance))} ({Num(Damage)} × (1 + {Pct(s.BlockChance)}").Icon(block).Text("))");
            else
                damage.Text($"{Num(Damage)} × (1 + ").Icon(block).Text(")");
            description.Damage.Add(damage);

            var bounces = GameIcons.Affix(Equipment.Affix.ProjectileBounces);
            description.Effects.Add(AreaLine("Hit radius", Radius, stats));
            description.Effects.Add(ValueLine(bounces, "Bounces", Bounces, stats is StatBlock b ? Bounces + b.Bounces : (float?)null));
            description.Effects.Add(ValueLine(GameIcons.Affix(Equipment.Affix.ProjectileSpeed), "Speed", ProjectileSpeed,
                                              stats is StatBlock p ? ProjectileSpeed * Mathf.Max(0f, 1f + p.ProjectileSpeed) : (float?)null, " m/s"));
        }

        protected override CaptainsShield Configure(CaptainsShield component, bool isNew)
        {
            component.ResourceCost = resourceCost;
            component.Damage = Damage;
            component.ProjectileSpeed = ProjectileSpeed;
            component.Radius = Radius;
            component.HitForce = HitForce;
            component.Bounces = Bounces;
            component.TurnSpeed = TurnSpeed;
            component.TurnAcceleration = TurnAcceleration;
            component.NoTargetDistance = NoTargetDistance;
            return component;
        }
    }
}
