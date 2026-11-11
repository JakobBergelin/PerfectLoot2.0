using Equipment;
using UnityEngine;

namespace ActiveSkills
{
    /// <summary>
    /// A short physical hit in front of you that knocks enemies far back, then a moment where enemy projectiles in front
    /// of you are turned into yours and sent the way you aim.
    /// </summary>
    [CreateAssetMenu(fileName = "Riposte", menuName = "Active Skills/Riposte")]
    public class RiposteSkill : ActiveSkill<Riposte>
    {
        [SerializeField] float cooldown = 3f;
        [Tooltip("Physical damage of the hit: weapon damage * this")]
        public float WeaponDamageMultiplier = 0.3f;
        [Tooltip("Degrees to each side of your aim, for the hit and the deflecting")]
        [Range(0f, 180f)] public float HalfAngle = 60f;
        [Tooltip("Knockback of the hit")]
        public float HitForce = 12f;
        [Tooltip("Seconds after the hit that projectiles are deflected")]
        [Min(0f)] public float Window = 0.25f;

        public override SkillType Type => SkillType.Riposte;
        public override float Cooldown => cooldown;

        public override void Describe(SkillDescription description, StatBlock? stats)
        {
            description.Damage.Add(WeaponDamageLine(DamageType.Physical, "Damage", 0f, 0f, WeaponDamageMultiplier, stats));
            description.Effects.Add(new SkillLine().Icon(GameIcons.Affix(Affix.WeaponReach)).Text($"Reaches as far as your weapon, {Num(HalfAngle)}° to each side"));
            description.Effects.Add(ValueLine(GameIcons.Stat(StatIcon.Duration), "Deflects enemy projectiles the way you aim for", Window, null, " s"));
        }

        protected override Riposte Configure(Riposte component, bool isNew)
        {
            component.Cooldown = cooldown;
            component.WeaponDamageMultiplier = WeaponDamageMultiplier;
            component.HalfAngle = HalfAngle;
            component.HitForce = HitForce;
            component.Window = Window;
            if (isNew) component.Elapsed = cooldown;  // Ready as soon as it's equipped
            return component;
        }
    }
}
