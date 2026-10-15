using UnityEngine;
using Unity.Entities;
using UnityEngine.InputSystem;
using Unity.Collections;
using Unity.Physics;
using Unity.Transforms;
using Unity.Mathematics;

/// <summary>
/// Reads the Input System and writes it to InputsData. Polls the actions every frame instead of using
/// callbacks, so there is no state to keep in sync and nothing to unsubscribe.
/// </summary>
[UpdateInGroup(typeof(InputSystemGroup))]
public partial class InputsDataSystem : SystemBase
{
    private InputSystem_Actions inputs;
    private InputAction[] skillActions;

    protected override void OnCreate()
    {
        inputs = new InputSystem_Actions();
        inputs.Enable();
        skillActions = new[] { inputs.Player.Skill1, inputs.Player.Skill2, inputs.Player.Skill3, inputs.Player.Skill4 };
    }

    protected override void OnDestroy()
    {
        inputs.Disable();
        inputs.Dispose();
    }

    protected override void OnUpdate()
    {
        var physicsWorldSingleton = SystemAPI.GetSingleton<PhysicsWorldSingleton>();
        var camera = Camera.main;

        foreach (RefRW<InputsData> data in SystemAPI.Query<RefRW<InputsData>>())
        {
            data.ValueRW.Move = inputs.Player.Move.ReadValue<Vector2>();

            for(int i = 0; i < skillActions.Length; i++)
            {
                data.ValueRW.SkillHeld[i] = skillActions[i].IsPressed();
                data.ValueRW.SkillPressedThisFrame[i] = skillActions[i].WasPressedThisFrame();
            }

            if(camera == null)
            {
                continue;
            }

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
            desiredDirection.ValueRW.Direction = Vector3.Normalize(inputsData.ValueRO.MousePosition - transform.ValueRO.Position);
            desiredDirection.ValueRW.Quaternion = Quaternion.LookRotation(desiredDirection.ValueRO.Direction, Vector3.up);
        }
    }
}
