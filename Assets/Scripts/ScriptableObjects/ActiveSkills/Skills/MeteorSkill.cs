using UnityEngine;

namespace ActiveSkills
{
    /// <summary>Calls a meteor down on the cursor: after a delay it hits hard in an area and leaves burning ground.</summary>
    [CreateAssetMenu(fileName = "Meteor", menuName = "Active Skills/Meteor")]
    public class MeteorSkill : ActiveSkill<Meteor>
    {
        [Tooltip("Seconds before the next cast (shared by all repeated attacks), scaled by the attack speed stat")]
        public float AttackSpeed = 1f;
        [SerializeField] float resourceCost = 55f;
        [Tooltip("Meters from you it can land; a cursor further away lands it at this distance")]
        public float Range = 15f;
        [Tooltip("Seconds from the cast to the impact")]
        [Min(0f)] public float Delay = 3f;

        [Header("Impact")]
        public float Damage = 25f;
        public float Radius = 3f;

        [Header("Burning ground (after the impact)")]
        public float HazardDamagePerSecond = 3f;
        public float HazardRadius = 3f;
        public float HazardDuration = 4f;

        public override SkillType Type => SkillType.Meteor;
        public override float ResourceCost => resourceCost;

        public override void Describe(SkillDescription description, StatBlock? stats)
        {
            description.Damage.Add(DamageLine(DamageType.Fire, "Impact damage", stats).Text(Num(Damage)));
            description.Damage.Add(DamageLine(DamageType.Fire, "Burning ground damage per second", stats).Text(Num(HazardDamagePerSecond)));
            description.Effects.Add(AreaLine("Impact radius", Radius, stats));
            description.Effects.Add(AreaLine("Burning ground radius", HazardRadius, stats));
            description.Effects.Add(ValueLine(GameIcons.Stat(StatIcon.Duration), "Impact after", Delay, null, " s"));
            description.Effects.Add(ValueLine(GameIcons.Stat(StatIcon.Duration), "Burning ground lasts", HazardDuration, null, " s"));
            description.Effects.Add(ValueLine(GameIcons.Stat(StatIcon.Distance), "Range", Range, null, " m"));
            description.Effects.Add(AttackTimeLine("Time between casts", AttackSpeed, stats));
        }

        protected override Meteor Configure(Meteor component, bool isNew)
        {
            component.AttackSpeed = AttackSpeed;
            component.ResourceCost = resourceCost;
            component.Range = Range;
            component.Delay = Delay;
            component.Damage = Damage;
            component.Radius = Radius;
            component.HazardDamagePerSecond = HazardDamagePerSecond;
            component.HazardRadius = HazardRadius;
            component.HazardDuration = HazardDuration;
            return component;
        }
    }
}
