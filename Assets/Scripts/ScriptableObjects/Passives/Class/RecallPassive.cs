using Unity.Mathematics;
using UnityEngine;

namespace Passives
{
    /// <summary>Mage's class passive: using every equipped skill in quick succession restores resource and finishes a cooldown.</summary>
    [CreateAssetMenu(fileName = "Recall", menuName = "Passives/Class/Recall")]
    public class RecallPassive : PassiveSkill<Recall>
    {
        [Tooltip("Seconds in which every equipped skill must be used")]
        [Min(0.5f)] public float Window = 6f;
        [Tooltip("Share of the maximum resource restored, 0.25 = 25%")]
        [Range(0f, 1f)] public float ResourceFraction = 0.25f;

        public override PassiveType Type => PassiveType.Recall;

        // No skill has been used yet
        protected override Recall CreateComponent() => new Recall { Window = Window, ResourceFraction = ResourceFraction, LastUsed = new double4(-1e9) };
    }
}
