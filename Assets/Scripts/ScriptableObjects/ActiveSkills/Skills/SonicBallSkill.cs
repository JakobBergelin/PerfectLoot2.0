using Equipment;
using Unity.Mathematics;
using UnityEngine;

namespace ActiveSkills
{
    [CreateAssetMenu(fileName = "SonicBall", menuName = "Active Skills/Sonic Ball")]
    public class SonicBallSkill : ActiveSkill<SonicBall>
    {
        [Tooltip("Seconds after the roll ends")]
        [SerializeField] float cooldown = 3f;
        [Tooltip("Degrees per second left/right turns the ball")]
        public float TurnSpeed = 180f;

        [Header("While rolling")]
        [Tooltip("Movement speed added (0.3 = +30%)")]
        public float MovementSpeedBonus = 0.3f;
        public int ArmorBonus = 10;
        public float FireResistanceBonus = 30f;

        [Header("Hits")]
        [Tooltip("Physical damage per hit = (Armor + Armor Bonus) * this, when the roll starts")]
        public float ArmorDamageMultiplier = 0.5f;
        [Tooltip("Hit radius around the ball, scaled by the area of effect stat")]
        public float Radius = 1.2f;
        [Tooltip("Knockback speed on hit, away from the ball")]
        public float HitForce = 12f;

        public override SkillType Type => SkillType.SonicBall;
        public override float Cooldown => cooldown;

        public override void Describe(SkillDescription description, StatBlock? stats)
        {
            // (Armor + ArmorBonus) * ArmorDamageMultiplier, as SonicBallSystem spawns the attack
            var damage = DamageLine(DamageType.Physical, "Damage per hit", stats);
            if (stats is StatBlock s) damage.Text(Num((s.Armor + ArmorBonus) * ArmorDamageMultiplier) + " (");
            damage.Text(Pct(ArmorDamageMultiplier) + " × (").Icon(GameIcons.Affix(Affix.Armor)).Text($" + {ArmorBonus})");
            if (stats != null) damage.Text(")");
            description.Damage.Add(damage);

            description.Effects.Add(AreaLine("Hit radius", Radius, stats));
            description.Effects.Add(new SkillLine().Icon(GameIcons.Affix(Affix.MovementSpeed)).Text($"+{Pct(MovementSpeedBonus)} movement speed while rolling"));
            description.Effects.Add(new SkillLine().Icon(GameIcons.Affix(Affix.Armor)).Text($"+{ArmorBonus} armor while rolling"));
            description.Effects.Add(new SkillLine().Icon(GameIcons.Affix(Affix.FireResistance)).Text($"+{Num(FireResistanceBonus)} fire resistance while rolling"));
        }

        protected override SonicBall Configure(SonicBall component, bool isNew)
        {
            component.Cooldown = cooldown;
            component.TurnSpeed = math.radians(TurnSpeed);
            component.MovementSpeedBonus = MovementSpeedBonus;
            component.ArmorBonus = ArmorBonus;
            component.FireResistanceBonus = FireResistanceBonus;
            component.ArmorDamageMultiplier = ArmorDamageMultiplier;
            component.Radius = Radius;
            component.HitForce = HitForce;
            if (isNew) component.Elapsed = cooldown;  // Ready as soon as it's equipped
            return component;
        }
    }
}
