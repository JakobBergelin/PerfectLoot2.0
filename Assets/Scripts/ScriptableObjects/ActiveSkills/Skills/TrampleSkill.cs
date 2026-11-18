using Equipment;
using UnityEngine;

namespace ActiveSkills
{
    [CreateAssetMenu(fileName = "Trample", menuName = "Active Skills/Trample")]
    public class TrampleSkill : ActiveSkill<Trample>
    {
        [Tooltip("Seconds after it's released")]
        [SerializeField] float cooldown = 4f;
        [Tooltip("Movement speed while stomping (0.5 = half speed), on top of other slows")]
        public float SpeedMultiplier = 0.5f;
        [Tooltip("Meters walked between stomps")]
        public float Stride = 1.5f;

        [Header("Stomps")]
        [Tooltip("Physical damage per stomp as a share of the caster's current health (0.05 = 5%)")]
        public float DamagePerCurrentHealth = 0.05f;
        [Tooltip("Hit radius around the caster, scaled by the area of effect stat")]
        public float Radius = 2f;
        [Tooltip("Knockback speed on hit, away from the caster")]
        public float HitForce = 2f;

        public override SkillType Type => SkillType.Trample;
        public override float Cooldown => cooldown;

        public override void Describe(SkillDescription description, StatBlock? stats)
        {
            // Current health * DamagePerCurrentHealth, as TrampleSystem spawns each stomp (current health isn't in the stats)
            description.Damage.Add(DamageLine(DamageType.Physical, "Damage per stomp", stats).Text($"{Pct(DamagePerCurrentHealth)} of your current health"));
            description.Effects.Add(AreaLine("Hit radius", Radius, stats));
            description.Effects.Add(new SkillLine().Icon(GameIcons.Affix(Affix.MovementSpeed)).Text($"Stomps every {Num(Stride)} m walked"));
            description.Effects.Add(new SkillLine().Icon(GameIcons.Affix(Affix.MovementSpeed)).Text($"{Pct(1f - SpeedMultiplier)} slower while trampling"));
        }

        protected override Trample Configure(Trample component, bool isNew)
        {
            component.Cooldown = cooldown;
            component.SpeedMultiplier = SpeedMultiplier;
            component.Stride = Stride;
            component.DamagePerCurrentHealth = DamagePerCurrentHealth;
            component.Radius = Radius;
            component.HitForce = HitForce;
            if (isNew) component.Elapsed = cooldown;  // Ready as soon as it's equipped
            return component;
        }
    }
}
