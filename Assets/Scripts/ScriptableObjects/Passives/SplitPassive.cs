using Enemies;
using UnityEngine;

namespace Passives
{
    /// <summary>An enemy passive: when the wearer dies it splits into Count of another enemy (SplitSystem).</summary>
    [CreateAssetMenu(fileName = "Split", menuName = "Passives/Split")]
    public class SplitPassive : PassiveSkill<Split>
    {
        [Tooltip("The enemy it splits into")]
        public EnemyDefinition Into;
        [Min(0)] public int Count = 3;
        [Tooltip("Meters from where it died that the new enemies land")]
        public float Spread = 1f;

        public override PassiveType Type => PassiveType.Split;

        protected override Split CreateComponent() => new Split { Into = Into, Count = Count, Spread = Spread };

        /// <summary>Kills one enemy of this definition is worth at a difficulty: itself plus everything it splits into.</summary>
        public static int KillsPer(EnemyDefinition definition, int difficulty, int depth = 0)
        {
            int kills = 1;
            if (definition == null || depth > 4) return kills;
            foreach (var passive in definition.PassivesAt(difficulty))
                if (passive is SplitPassive split && split.Into != null)
                    kills += split.Count * KillsPer(split.Into, difficulty, depth + 1);
            return kills;
        }
    }
}
