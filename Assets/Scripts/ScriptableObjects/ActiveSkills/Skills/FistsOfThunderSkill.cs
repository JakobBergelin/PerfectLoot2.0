using System;
using System.Collections.Generic;
using UnityEngine;

namespace ActiveSkills
{
    [CreateAssetMenu(fileName = "FistsOfThunder", menuName = "Active Skills/Fists Of Thunder")]
    public class FistsOfThunderSkill : ActiveSkill<FistsOfThunder>
    {
        [Serializable]
        public class Hit
        {
            [Tooltip("Time before this punch can land, scaled by attack speed")]
            public float Cooldown;
            [Tooltip("Flat damage, plus weapon damage * WeaponDamageMultiplier")]
            public float Damage;
        }

        // Punches land WeaponReach in front of the player
        [Tooltip("Resource gained per punch")]
        public float ResourceGeneration = 1f;
        public float WeaponDamageMultiplier = 0.1f;
        [Tooltip("Hit radius of each punch")]
        public float Radius = 0.5f;
        [Tooltip("Knockback speed of each punch, in the punch direction")]
        public float HitForce = 2f;

        [Tooltip("The combo, in order. Max 15 punches.")]
        public List<Hit> Combo = new List<Hit>
        {
            new Hit { Cooldown = 0.30f, Damage = 0.5f },
            new Hit { Cooldown = 0.24f, Damage = 0.5f },
            new Hit { Cooldown = 0.24f, Damage = 0.6f },
            new Hit { Cooldown = 0.52f, Damage = 0.7f },
            new Hit { Cooldown = 0.26f, Damage = 0.4f },
            new Hit { Cooldown = 0.24f, Damage = 0.6f },
            new Hit { Cooldown = 0.45f, Damage = 0.9f },
        };

        public override SkillType Type => SkillType.FistOfThunder;

        protected override FistsOfThunder Configure(FistsOfThunder component, bool isNew)
        {
            component.ResourceGeneration = ResourceGeneration;
            component.WeaponDamageMultiplier = WeaponDamageMultiplier;
            component.Radius = Radius;
            component.HitForce = HitForce;

            component.Hits.Clear();
            foreach (var hit in Combo)
            {
                if (component.Hits.Length == component.Hits.Capacity)
                {
                    Debug.LogWarning($"{name}: the combo has more than {component.Hits.Capacity} punches, the rest are ignored.", this);
                    break;
                }
                component.Hits.Add(new FistsOfThunderHit { Cooldown = hit.Cooldown, Damage = hit.Damage });
            }
            return component;
        }
    }
}
