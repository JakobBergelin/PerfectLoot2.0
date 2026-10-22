using UnityEngine;

namespace Effects
{
    /// <summary>
    /// How a buff or debuff is shown in the HUD. The effect itself is an enableable component with its own systems
    /// (see EffectComponents.cs); its tuning is in StatusEffectSettings or on whatever applies it.
    /// </summary>
    [CreateAssetMenu(fileName = "Effect", menuName = "Effects/Effect Definition")]
    public class EffectDefinition : ScriptableObject
    {
        public EffectType Type;
        public string Name;
        public Sprite Icon;
        [Tooltip("Filled from the live effect: {Stacks}, {TimeRemaining}, {Duration}, {Magnitude} (effect-specific, " +
                 "e.g. damage per second or slow), with an optional format like {Magnitude:P0}")]
        [TextArea] public string Description;
        public EffectKind Kind;

        public string DisplayName => string.IsNullOrEmpty(Name) ? name : Name;

        /// <summary>The description filled in from the effect as it is right now.</summary>
        public string Describe(EffectDisplay effect) => DescriptionFormatter.Format(effect, Description);
    }
}
