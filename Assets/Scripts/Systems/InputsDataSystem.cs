using UnityEngine;
using Unity.Entities;
using UnityEngine.InputSystem;
using Unity.Collections;
using Unity.Physics;
using Unity.Transforms;
using Unity.Mathematics;
using Unity.VisualScripting;
using System.Collections.Generic;

public partial class InputsDataSystem : SystemBase
{
    private InputSystem_Actions inputs = null;

    private List<bool> skillActive;

    protected override void OnCreate()
    {
        skillActive = new List<bool>{false,false,false,false};
        inputs = new InputSystem_Actions();
        inputs.Enable();

        inputs.Player.Skill1.performed += (InputAction.CallbackContext context) => skillActive[0] = true;
        inputs.Player.Skill1.canceled += (InputAction.CallbackContext context) => skillActive[0] = false;
        inputs.Player.Skill2.performed += (InputAction.CallbackContext context) => skillActive[1] = true;
        inputs.Player.Skill2.canceled += (InputAction.CallbackContext context) => skillActive[1] = false;
        inputs.Player.Skill3.performed += (InputAction.CallbackContext context) => skillActive[2] = true;
        inputs.Player.Skill3.canceled += (InputAction.CallbackContext context) => skillActive[2] = false;
        inputs.Player.Skill4.performed += (InputAction.CallbackContext context) => skillActive[3] = true;
        inputs.Player.Skill4.canceled += (InputAction.CallbackContext context) => skillActive[3] = false;
    }

    protected override void OnUpdate()
    {
        var physicsWorldSingleton = SystemAPI.GetSingleton<PhysicsWorldSingleton>();

        foreach (RefRW<InputsData> data in SystemAPI.Query<RefRW<InputsData>>())
        {
            data.ValueRW.Move = inputs.Player.Move.ReadValue<Vector2>();

            for(int i = 0; i < skillActive.Count; i++)
            {
                data.ValueRW.Skill[i] = skillActive[i];
            }

            var screenPoint = inputs.Player.Look.ReadValue<Vector2>();
            var cameraRay = Camera.main.ScreenPointToRay(screenPoint);
            var raycastInput = new RaycastInput
            {
                Start = cameraRay.GetPoint(0f),
                End = cameraRay.GetPoint(9999f),
                Filter = CollisionFilter.Default
            };

            // var dir = raycastInput.End - (raycastInput.Start + new float3(0,-1f,0));

            // Debug.DrawRay(raycastInput.Start + new float3(0,-1f,0), dir, Color.blue);

            var allResults = new NativeList<Unity.Physics.RaycastHit>(Allocator.Temp);
            if (physicsWorldSingleton.CastRay(raycastInput, ref allResults))
            {
                foreach(var result in allResults)
                {
                    if(SystemAPI.HasComponent<Enemy>(result.Entity))
                    {
                        data.ValueRW.MousePosition = SystemAPI.GetComponentRO<LocalTransform>(result.Entity).ValueRO.Position;
                        //Debug.DrawRay(data.ValueRW.MousePosition, new float3(0f,5.0f,0f), Color.red);
                        break;
                    }
                    if(SystemAPI.HasComponent<Ground>(result.Entity))
                    {
                        // TODO remove hardcoded 0.5 over ground
                        data.ValueRW.MousePosition = result.Position + new float3(0.0f, 0.5f, 0.0f);
                        //Debug.DrawRay(data.ValueRW.MousePosition, new float3(0f,5.0f,0f), Color.green);
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
