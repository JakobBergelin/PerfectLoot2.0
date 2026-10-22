using System.Collections.Generic;
using UnityEngine;

namespace Effects
{
    /// <summary>Every EffectDefinition: names, icons and descriptions for the HUD.</summary>
    [CreateAssetMenu(fileName = "EffectLibrary", menuName = "Effects/Effect Library")]
    public class EffectLibrary : ScriptableObject
    {
        public List<EffectDefinition> Effects = new List<EffectDefinition>();

        public EffectDefinition Get(EffectType type)
        {
            foreach (var effect in Effects)
                if (effect != null && effect.Type == type) return effect;
            return null;
        }

#if UNITY_EDITOR
        void OnValidate()
        {
            var seen = new HashSet<EffectType>();
            foreach (var effect in Effects)
            {
                if (effect == null) continue;
                if (effect.Type == EffectType.None) Debug.LogWarning($"{name}: {effect.name} has no EffectType.", this);
                else if (!seen.Add(effect.Type)) Debug.LogWarning($"{name}: more than one effect is {effect.Type}.", this);
            }
        }
#endif
    }
}
