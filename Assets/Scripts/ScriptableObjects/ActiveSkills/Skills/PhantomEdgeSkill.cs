using Unity.Entities;
using UnityEngine;

namespace ActiveSkills
{
    /// <summary>
    /// Marks every enemy around you, then blinks unseen and invulnerable from one to the next, striking each once. The
    /// cooldown starts with the flurry.
    /// </summary>
    [CreateAssetMenu(fileName = "PhantomEdge", menuName = "Active Skills/Phantom Edge")]
    public class PhantomEdgeSkill : ActiveSkill<PhantomEdge>
    {
        [Tooltip("Meters around you within which enemies are marked")]
        public float Radius = 15f;
        [Tooltip("Seconds between strikes at attack speed 1 (your attack speed, weapon cooldown included, divides it)")]
        public float StrikeInterval = 1.8f;
        [Tooltip("Each strike deals weapon damage * this")]
        public float WeaponDamageMultiplier = 1.3f;
        [Tooltip("Radius of each strike's hit around its target")]
        public float StrikeRadius = 0.6f;
        [Tooltip("Seconds, starting when the flurry starts")]
        [SerializeField] float cooldown = 10f;

        public override SkillType Type => SkillType.PhantomEdge;
        public override float Cooldown => cooldown;

        public override void Describe(SkillDescription description, StatBlock? stats)
        {
            description.Damage.Add(WeaponDamageLine(DamageType.Physical, "Damage per strike", 0f, 0f, WeaponDamageMultiplier, stats));
            description.Effects.Add(new SkillLine().Text($"Marks every enemy within {Num(Radius)} m and strikes each once"));
            description.Effects.Add(AttackTimeLine("Time between strikes", StrikeInterval, stats));
            description.Effects.Add(new SkillLine().Text("Unseen and invulnerable until the last strike"));
        }

        protected override PhantomEdge Configure(PhantomEdge component, bool isNew)
        {
            component.Radius = Radius;
            component.StrikeInterval = StrikeInterval;
            component.WeaponDamageMultiplier = WeaponDamageMultiplier;
            component.StrikeRadius = StrikeRadius;
            component.Cooldown = cooldown;
            if (isNew) component.Elapsed = cooldown;    // Ready at once
            return component;
        }

        // The flurry's state and marks stay on the unit, so a flurry still finishes if the skill is unequipped mid-way
        public override void Apply(EntityManager entityManager, Entity entity)
        {
            base.Apply(entityManager, entity);
            if (!entityManager.HasComponent<PhantomEdgeFlurry>(entity)) entityManager.AddComponentData(entity, new PhantomEdgeFlurry());
            if (!entityManager.HasBuffer<PhantomEdgeMark>(entity)) entityManager.AddBuffer<PhantomEdgeMark>(entity);
        }
    }
}
