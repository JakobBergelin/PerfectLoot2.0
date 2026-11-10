using UnityEngine;
using Unity.Entities;
using UnityEngine.InputSystem;
using Unity.Collections;
using Unity.Physics;
using Unity.Transforms;
using Unity.Mathematics;

/// <summary>
/// Reads the Input System (GameInput's shared actions, with the player's bindings) and writes it to InputsData. Polls
/// the actions every frame instead of using callbacks, so there is no state to keep in sync and nothing to unsubscribe.
/// </summary>
[UpdateInGroup(typeof(InputSystemGroup))]
public partial class InputsDataSystem : SystemBase
{
    protected override void OnUpdate()
    {
        var physicsWorldSingleton = SystemAPI.GetSingleton<PhysicsWorldSingleton>();
        var camera = Camera.main;
        var inputs = GameInput.Actions;

        foreach (RefRW<InputsData> data in SystemAPI.Query<RefRW<InputsData>>())
        {
            // While typing in a UI text field the movement keys are letters for the text
            data.ValueRW.Move = MenuPanels.IsTyping ? Vector2.zero : inputs.Player.Move.ReadValue<Vector2>();

            // While a menu panel is open the mouse is used for the menu, so skills are ignored (movement still works)
            bool menuOpen = MenuPanels.IsAnyOpen;
            for(int i = 0; i < 4; i++)
            {
                var skill = GameInput.Skill(i);
                data.ValueRW.SkillHeld[i] = !menuOpen && skill.IsPressed();
                data.ValueRW.SkillPressedThisFrame[i] = !menuOpen && skill.WasPressedThisFrame();
            }

            if(camera == null)
            {
                continue;
            }

            data.ValueRW.Hovered = Entity.Null;
            var screenPoint = inputs.Player.Look.ReadValue<Vector2>();
            var cameraRay = camera.ScreenPointToRay(screenPoint);
            var raycastInput = new RaycastInput
            {
                Start = cameraRay.GetPoint(0f),
                End = cameraRay.GetPoint(9999f),
                Filter = CollisionFilter.Default
            };

            var allResults = new NativeList<Unity.Physics.RaycastHit>(Allocator.Temp);
            if (physicsWorldSingleton.CastRay(raycastInput, ref allResults))
            {
                if (!menuOpen) data.ValueRW.Hovered = ClosestUnit(allResults);

                foreach(var result in allResults)
                {
                    if(SystemAPI.HasComponent<Enemy>(result.Entity))
                    {
                        data.ValueRW.MousePosition = SystemAPI.GetComponentRO<LocalTransform>(result.Entity).ValueRO.Position;
                        break;
                    }
                    if(SystemAPI.HasComponent<Ground>(result.Entity))
                    {
                        // TODO remove hardcoded 0.5 over ground
                        data.ValueRW.MousePosition = result.Position + new float3(0.0f, 0.5f, 0.0f);
                    }
                }
            }
        }

        foreach (var (desiredDirection, transform, inputsData) in SystemAPI.Query<RefRW<DesiredDirection>, RefRO<LocalTransform>, RefRO<InputsData>>())
        {
            // Horizontal only, so turning toward the mouse never tilts the body (the mouse point and the body center
            // are at different heights); keep the last direction when the mouse is right on the player
            var toMouse = inputsData.ValueRO.MousePosition - transform.ValueRO.Position;
            toMouse.y = 0f;
            if (math.lengthsq(toMouse) < 1e-6f) continue;
            desiredDirection.ValueRW.Direction = math.normalize(toMouse);
            desiredDirection.ValueRW.Quaternion = Quaternion.LookRotation(desiredDirection.ValueRO.Direction, Vector3.up);
        }
    }

    // The hits come in no particular order
    Entity ClosestUnit(NativeList<Unity.Physics.RaycastHit> hits)
    {
        var closest = Entity.Null;
        float closestFraction = float.MaxValue;
        foreach (var hit in hits)
        {
            if (hit.Fraction >= closestFraction || !SystemAPI.HasComponent<UnitInfo>(hit.Entity)) continue;
            closestFraction = hit.Fraction;
            closest = hit.Entity;
        }
        return closest;
    }
}
