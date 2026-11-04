using System;
using System.Collections.Generic;
using Equipment;
using UnityEngine;

/// <summary>
/// The 3D look of one base item: a part prefab per socket bone (a helmet on head, pauldrons on both arms, ...), each
/// modelled around its bone's rest position. Uniques use their visual base's (RollableItem.VisualBase).
/// Registered in EquipmentVisualsAuthoring.
/// </summary>
[CreateAssetMenu(fileName = "EquipmentVisual", menuName = "Items/Equipment Visual")]
public class EquipmentVisual : ScriptableObject
{
    [Serializable]
    public class Part
    {
        [Tooltip("Bone name on the unit: head, chest, hips, arm_L, arm_R, leg_L, leg_R, hand_L, hand_R")]
        public string Socket;
        public GameObject Prefab;
    }

    public RollableItem Item;
    public List<Part> Parts = new List<Part>();
}
