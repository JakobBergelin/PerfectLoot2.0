using UnityEngine;

namespace ActiveSkills
{
    /// <summary>
    /// A toggle. While it's on the caster has lower critical chance, and enemies they hit get Thrall Mark. A marked enemy that
    /// dies rises as their companion, with its model, skills, stats and passives. Thralls stay when it's switched off.
    /// </summary>
    [CreateAssetMenu(fileName = "RaiseThrall", menuName = "Active Skills/Raise Thrall")]
    public class RaiseThrallSkill : ActiveSkill<RaiseThrall>
    {
        [Tooltip("Taken off critical chance while it's on, 0.2 = -20%")]
        public float CriticalChancePenalty = 0.2f;
        [Tooltip("Seconds a mark lasts; each hit starts it over")]
        public float MarkDuration = 5f;
        [Tooltip("With this many, a new Thrall kills the oldest one")]
        public int MaxThralls = 5;
        [Tooltip("Enemies choosing a target treat a Thrall as this many meters closer")]
        public float Threat = 3f;
        [Tooltip("How far a Thrall sees enemies, in meters, all around it")]
        public float SightRadius = 12f;
        [Tooltip("How Thralls move. Attack Distance comes from the enemy they were.")]
        public BoidSettings Movement = new BoidSettings
        {
            Position = BoidPosition.Around, Distance = 3f, Spread = 3f, Leash = 10f, CatchUpMultiplier = 1.5f, TeleportDistance = 20f,
        };

        public override SkillType Type => SkillType.RaiseThrall;

        public override void Describe(SkillDescription description, StatBlock? stats)
        {
            description.Effects.Add(new SkillLine().Text($"While on: -{Pct(CriticalChancePenalty)} critical chance"));
            description.Effects.Add(new SkillLine().Text($"Mark lasts {Num(MarkDuration)} s"));
            description.Effects.Add(new SkillLine().Text($"Up to {MaxThralls} Thralls"));
        }

        protected override RaiseThrall Configure(RaiseThrall component, bool isNew)
        {
            component.CriticalChancePenalty = CriticalChancePenalty;
            component.MarkDuration = MarkDuration;
            component.MaxThralls = MaxThralls;
            component.Threat = Threat;
            component.SightRadius = SightRadius;
            component.Movement = Movement;
            return component;
        }
    }
}
