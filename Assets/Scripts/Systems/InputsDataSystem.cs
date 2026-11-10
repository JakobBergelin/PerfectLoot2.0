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
/// The aim point (MousePosition) and Hovered come from the mouse, or from the Aim stick when a controller was used
/// last: the stick's direction (else the movement direction, else the last one), pushed further the more it's tilted,
/// and pulled onto the enemy closest to that line inside a narrow cone (aim assist). Skills read the point the same way
/// for both devices.
/// </summary>
[UpdateInGroup(typeof(InputSystemGroup))]
public partial class InputsDataSystem : SystemBase
{
    const float AimDeadzone = 0.2f;         // Stick tilt below this counts as not aiming (the movement stick too)
    const float MinAimDistance = 1.5f;      // Meters out at the lightest tilt past the deadzone
    const float MaxAimDistance = 12f;       // Meters out at full tilt, shared by every skill (each still caps at its own range)
    const float RestAimDistance = 5f;       // Meters out while the stick rests and the aim follows the movement or last direction
    const float AssistHalfAngle = 20f;      // Degrees each side of the aim line an enemy can be pulled in from
    const float AssistRange = 15f;          // Meters from the player an enemy can be pulled in from
    const float AimHeight = 0.5f;           // Above the ground, as the mouse's point (TODO shared with the mouse's hardcoded 0.5)

    protected override void OnUpdate()
    {
        var physicsWorldSingleton = SystemAPI.GetSingleton<PhysicsWorldSingleton>();
        var camera = Camera.main;

        // Each player entity reads its own player's input (copies like Mirror Image get their owner's afterwards)
        foreach (var (data, playerIndex, entity) in SystemAPI.Query<RefRW<InputsData>, RefRO<PlayerIndex>>().WithEntityAccess())
        {
            var controls = GameInput.For(playerIndex.ValueRO.Value);
            if (controls == null)
            {
                data.ValueRW = default;
                continue;
            }
            var inputs = controls.Actions;
            bool gamepad = controls.UsingGamepad;
            var menus = MenuPanels.For(controls.Index);
            var nav = UiNav.For(controls.Index);

            // While typing in a UI text field the movement keys are letters for the text, and while a menu has the
            // controller its stick moves through the menu
            data.ValueRW.Move = menus.IsTyping || nav.HasGamepadInput ? Vector2.zero : inputs.Player.Move.ReadValue<Vector2>();

            // While one of their menus is open it has their buttons (or the mouse), so skills are ignored (movement still works)
            bool menuOpen = menus.IsAnyOpen;
            for(int i = 0; i < 4; i++)
            {
                var skill = controls.Skills[i];
                data.ValueRW.SkillHeld[i] = !menuOpen && skill.IsPressed();
                data.ValueRW.SkillPressedThisFrame[i] = !menuOpen && skill.WasPressedThisFrame();
            }

            if (gamepad)
            {
                if (SystemAPI.HasComponent<LocalTransform>(entity) && SystemAPI.HasComponent<DesiredDirection>(entity))
                    AimWithStick(ref data.ValueRW, SystemAPI.GetComponent<LocalTransform>(entity).Position,
                                 SystemAPI.GetComponent<DesiredDirection>(entity).Direction, physicsWorldSingleton, inputs.Player.Aim.ReadValue<Vector2>(), menuOpen);
                continue;
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

    // The controller's aim point: stick up is +Z like the movement stick (InputsData.Move)
    void AimWithStick(ref InputsData data, float3 position, float3 lastDirection, in PhysicsWorldSingleton world, float2 stick, bool menuOpen)
    {
        float2 direction;
        float distance;
        float tilt = math.length(stick);
        if (tilt > AimDeadzone)
        {
            direction = stick / tilt;
            distance = math.lerp(MinAimDistance, MaxAimDistance, math.saturate((tilt - AimDeadzone) / (1f - AimDeadzone)));
        }
        else
        {
            direction = math.length(data.Move) > AimDeadzone ? math.normalize(data.Move) : math.normalizesafe(lastDirection.xz, new float2(0f, 1f));
            distance = RestAimDistance;
        }

        data.Hovered = Entity.Null;
        if (TryAssist(position, direction, out var target, out var targetPosition))
        {
            data.MousePosition = targetPosition;
            if (!menuOpen && SystemAPI.HasComponent<UnitInfo>(target)) data.Hovered = target;
            return;
        }

        // On the ground under the point, as the mouse's (the player's height where there's none, over a gap)
        var point = position;
        point.xz += direction * distance;
        var down = new RaycastInput
        {
            Start = point + new float3(0f, 10f, 0f),
            End = point - new float3(0f, 20f, 0f),
            Filter = new CollisionFilter { BelongsTo = ~0u, CollidesWith = UnitMovement.GroundLayer },
        };
        if (world.CastRay(down, out var hit)) point.y = hit.Position.y + AimHeight;
        data.MousePosition = point;
    }

    // The living enemy closest to the aim line, within AssistHalfAngle of it and AssistRange of the player
    bool TryAssist(float3 position, float2 direction, out Entity target, out float3 targetPosition)
    {
        target = Entity.Null;
        targetPosition = default;
        float minCos = math.cos(math.radians(AssistHalfAngle));
        float best = float.MaxValue;
        foreach (var (transform, entity) in SystemAPI.Query<RefRO<LocalTransform>>().WithAll<Enemy>().WithNone<RigidDeath>().WithEntityAccess())
        {
            float2 offset = transform.ValueRO.Position.xz - position.xz;
            float length = math.length(offset);
            if (length > AssistRange || length < 1e-3f || math.dot(offset / length, direction) < minCos) continue;
            float fromLine = math.abs(direction.x * offset.y - direction.y * offset.x);
            if (fromLine >= best) continue;
            best = fromLine;
            target = entity;
            targetPosition = transform.ValueRO.Position;
        }
        return target != Entity.Null;
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
