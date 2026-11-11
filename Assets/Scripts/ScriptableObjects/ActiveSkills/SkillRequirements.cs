using System.Collections.Generic;
using Characters;
using Unity.Mathematics;

namespace ActiveSkills
{
    /// <summary>
    /// The Strength, Agility and Intelligence active skills need (ActiveSkill.Requirements). An equipped skill whose
    /// requirements the character's current attributes (class levels, items, buffs) don't meet is greyed out and can't be
    /// used in a dungeon (SkillActivationSystem); the Hub ignores them. A new character starts with the first four skills
    /// its class reaches on its own (StartingSkills).
    /// </summary>
    public static class SkillRequirements
    {
        public const int StartingSkillCount = 4;
        public static readonly string[] Names = { "Strength", "Agility", "Intelligence" };
        public static readonly string[] Short = { "Str", "Agi", "Int" };

        /// <summary>True when these attributes (x Strength, y Agility, z Intelligence) cover the requirements.</summary>
        public static bool Meets(int3 requirements, float3 attributes) => math.all(attributes >= requirements);

        public static int Total(int3 requirements) => requirements.x + requirements.y + requirements.z;

        /// <summary>
        /// The level the class reaches the requirements at on its own (level × its attributes per level), at least 1;
        /// int.MaxValue when it never does (an attribute the class doesn't grow).
        /// </summary>
        public static int UnlockLevel(int3 requirements, CharacterClass characterClass)
        {
            if (characterClass == null) return int.MaxValue;
            var perLevel = new int3(characterClass.StrengthPerLevel, characterClass.AgilityPerLevel, characterClass.IntelligencePerLevel);
            int level = 1;
            for (int i = 0; i < 3; i++)
            {
                if (requirements[i] <= 0) continue;
                if (perLevel[i] <= 0) return int.MaxValue;
                level = math.max(level, (requirements[i] + perLevel[i] - 1) / perLevel[i]);
            }
            return level;
        }

        /// <summary>
        /// The skills in the order the class reaches them: by unlock level, then the lower total requirement, then the
        /// order of SkillType. Skills it never reaches on its own come last, the smallest first.
        /// </summary>
        public static List<ActiveSkill> InUnlockOrder(IEnumerable<ActiveSkill> skills, CharacterClass characterClass)
        {
            var ordered = new List<ActiveSkill>();
            foreach (var skill in skills)
                if (skill != null) ordered.Add(skill);
            ordered.Sort((a, b) =>
            {
                int byLevel = UnlockLevel(a.Requirements, characterClass).CompareTo(UnlockLevel(b.Requirements, characterClass));
                if (byLevel != 0) return byLevel;
                int byTotal = Total(a.Requirements).CompareTo(Total(b.Requirements));
                return byTotal != 0 ? byTotal : ((int)a.Type).CompareTo((int)b.Type);
            });
            return ordered;
        }

        /// <summary>A new character's skills: the first StartingSkillCount its class reaches.</summary>
        public static List<ActiveSkill> StartingSkills(IEnumerable<ActiveSkill> skills, CharacterClass characterClass)
        {
            var ordered = InUnlockOrder(skills, characterClass);
            if (ordered.Count > StartingSkillCount) ordered.RemoveRange(StartingSkillCount, ordered.Count - StartingSkillCount);
            return ordered;
        }

        /// <summary>"Str 3 · Agi 1", or "None".</summary>
        public static string Text(int3 requirements)
        {
            var parts = new List<string>();
            for (int i = 0; i < 3; i++)
                if (requirements[i] > 0) parts.Add($"{Short[i]} {requirements[i]}");
            return parts.Count > 0 ? string.Join(" · ", parts) : "None";
        }
    }
}
