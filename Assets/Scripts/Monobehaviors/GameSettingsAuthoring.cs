using Unity.Entities;
using UnityEngine;

/// <summary>
/// Bakes the settings assets into singleton components so Burst systems can read them.
/// Must be in the Entities subscene (Bakers only run there). Editing an asset rebakes, also in Play mode.
/// </summary>
public class GameSettingsAuthoring : MonoBehaviour
{
    public MovementSettings MovementSettings;
    public CombatSettings CombatSettings;
    public StatusEffectSettings StatusEffectSettings;

    public class Baker : Baker<GameSettingsAuthoring>
    {
        public override void Bake(GameSettingsAuthoring authoring)
        {
            DependsOn(authoring.MovementSettings);
            DependsOn(authoring.CombatSettings);
            DependsOn(authoring.StatusEffectSettings);

            if (authoring.MovementSettings == null || authoring.CombatSettings == null || authoring.StatusEffectSettings == null)
            {
                Debug.LogError("GameSettingsAuthoring: all three settings assets must be assigned.", authoring);
                return;
            }

            Entity entity = GetEntity(TransformUsageFlags.None);
            AddComponent(entity, authoring.MovementSettings.Values);
            AddComponent(entity, authoring.CombatSettings.Values);
            AddComponent(entity, authoring.StatusEffectSettings.Values);
        }
    }
}
