using System.Collections.Generic;
using UnityEngine;

namespace ActiveSkills
{
    /// <summary>
    /// A skill's numbers for its tooltip (ActiveSkill.Describe): how its damage is made up, and its other effects.
    /// SkillTooltip turns it into UI.
    /// </summary>
    public class SkillDescription
    {
        public readonly List<SkillLine> Damage = new List<SkillLine>();
        public readonly List<SkillLine> Effects = new List<SkillLine>();
    }

    /// <summary>One tooltip line: text and icons in a row, e.g. "[poison] Damage per second: 14 (5 + 20% [sword])".</summary>
    public class SkillLine
    {
        public struct Piece
        {
            public string Text;     // Null for an icon
            public Sprite Icon;
            public bool Muted;      // Greyed, e.g. a base value next to the current one
        }

        public readonly List<Piece> Pieces = new List<Piece>();

        public SkillLine Text(string text)
        {
            if (!string.IsNullOrEmpty(text)) Pieces.Add(new Piece { Text = text });
            return this;
        }

        public SkillLine Muted(string text)
        {
            if (!string.IsNullOrEmpty(text)) Pieces.Add(new Piece { Text = text, Muted = true });
            return this;
        }

        public SkillLine Icon(Sprite icon)
        {
            if (icon != null) Pieces.Add(new Piece { Icon = icon });
            return this;
        }
    }
}
