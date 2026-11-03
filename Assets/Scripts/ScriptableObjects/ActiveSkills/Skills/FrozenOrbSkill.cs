using UnityEngine;

namespace ActiveSkills
{
    [CreateAssetMenu(fileName = "FrozenOrb", menuName = "Active Skills/Frozen Orb")]
    public class FrozenOrbSkill : ActiveSkill<FrozenOrb>
    {
        [Tooltip("Seconds of wind-up per orb, scaled by the attack speed stat")]
        public float AttackSpeed = 1f;
        [SerializeField] float resourceCost = 25f;

        [Header("Each orb")]
        [Tooltip("Cold damage per second to enemies within Radius")]
        public float DamagePerSecond = 2f;
        [Tooltip("Seconds between damage ticks")]
        public float TickRate = 0.5f;
        [Tooltip("Damage radius around the orb")]
        public float Radius = 1.5f;
        public float Speed = 6f;
        [Tooltip("Seconds the orb flies")]
        public float Lifetime = 2.5f;
        [Tooltip("Degrees per second the orb turns")]
        public float SpinSpeed = 270f;

        [Header("Ice shards")]
        [Tooltip("Seconds between bursts: one shard out of the orb's front, one out of its back")]
        public float ShardInterval = 0.15f;
        [Tooltip("Cold damage per shard")]
        public float ShardDamage = 1f;
        public float ShardSpeed = 15f;
        [Tooltip("Hit radius of each shard")]
        public float ShardRadius = 0.25f;
        [Tooltip("Meters each shard flies at most")]
        public float ShardRange = 8f;

        public override SkillType Type => SkillType.FrozenOrb;
        public override float ResourceCost => resourceCost;

        public override void Describe(SkillDescription description, StatBlock? stats)
        {
            description.Damage.Add(DamageLine(DamageType.Cold, "Damage per second", stats).Text(Num(DamagePerSecond)));
            description.Damage.Add(DamageLine(DamageType.Cold, "Ice shard damage", stats).Text(Num(ShardDamage)));
            description.Effects.Add(ValueLine(GameIcons.Stat(StatIcon.Duration), "Lasts", Lifetime, null, " s"));
            description.Effects.Add(AreaLine("Radius", Radius, stats));
            description.Effects.Add(ValueLine(GameIcons.Stat(StatIcon.Duration), "Time between ice shards", ShardInterval, null, " s"));
            description.Effects.Add(new SkillLine().Text($"Ice shard range: {Num(ShardRange)} m"));
            description.Effects.Add(WindUpLine("Time per orb", AttackSpeed, stats));
        }

        protected override FrozenOrb Configure(FrozenOrb component, bool isNew)
        {
            component.AttackSpeed = AttackSpeed;
            component.ResourceCost = resourceCost;
            component.DamagePerSecond = DamagePerSecond;
            component.TickRate = TickRate;
            component.Radius = Radius;
            component.Speed = Speed;
            component.Lifetime = Lifetime;
            component.SpinSpeed = SpinSpeed;
            component.ShardInterval = ShardInterval;
            component.ShardDamage = ShardDamage;
            component.ShardSpeed = ShardSpeed;
            component.ShardRadius = ShardRadius;
            component.ShardRange = ShardRange;
            return component;
        }
    }
}
