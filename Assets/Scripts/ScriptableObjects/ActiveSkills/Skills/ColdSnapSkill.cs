using UnityEngine;

namespace ActiveSkills
{
    [CreateAssetMenu(fileName = "ColdSnap", menuName = "Active Skills/Cold Snap")]
    public class ColdSnapSkill : ActiveSkill<ColdSnap>
    {
        [SerializeField] float cooldown = 12f;
        [Tooltip("Cold damage to each enemy around the caster")]
        public float Damage = 5f;
        [Tooltip("Meters around the caster that are hit, scaled by its area of effect")]
        public float Radius = 5f;
        [Tooltip("Chill stacks one cast leaves on an enemy, the hit's own chill included")]
        public int ChillStacks = 90;

        public override SkillType Type => SkillType.ColdSnap;
        public override float Cooldown => cooldown;

        public override void Describe(SkillDescription description, StatBlock? stats)
        {
            description.Damage.Add(DamageLine(DamageType.Cold, "Damage", stats).Text(Num(Damage)));
            description.Effects.Add(AreaLine("Radius", Radius, stats));
            description.Effects.Add(new SkillLine().Text($"Chill: {ChillStacks} stacks"));
        }

        protected override ColdSnap Configure(ColdSnap component, bool isNew)
        {
            component.Cooldown = cooldown;
            component.Damage = Damage;
            component.Radius = Radius;
            component.ChillStacks = ChillStacks;
            if (isNew) component.Elapsed = cooldown;  // Ready as soon as it's equipped
            return component;
        }
    }
}
