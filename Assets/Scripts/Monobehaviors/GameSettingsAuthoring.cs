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
    public HitFlashSettings HitFlashSettings;

    public class Baker : Baker<GameSettingsAuthoring>
    {
        public override void Bake(GameSettingsAuthoring authoring)
        {
            DependsOn(authoring.MovementSettings);
            DependsOn(authoring.CombatSettings);
            DependsOn(authoring.StatusEffectSettings);
            DependsOn(authoring.HitFlashSettings);

            if (authoring.MovementSettings == null || authoring.CombatSettings == null || authoring.StatusEffectSettings == null || authoring.HitFlashSettings == null)
            {
                Debug.LogError("GameSettingsAuthoring: all settings assets must be assigned.", authoring);
                return;
            }

            Entity entity = GetEntity(TransformUsageFlags.None);
            AddComponent(entity, authoring.MovementSettings.Values);
            AddComponent(entity, authoring.CombatSettings.Values);
            AddComponent(entity, authoring.StatusEffectSettings.Values);
            AddComponent(entity, authoring.HitFlashSettings.ToConfig());
        }
    }
}
