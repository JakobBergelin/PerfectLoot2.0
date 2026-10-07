using System;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;

public class CombatTextManager : MonoBehaviour
{
    public GameObject CombatTextPrefab;

    public void Start()
    {
        var world = World.DefaultGameObjectInjectionWorld;

        var damagingSystem = world.GetExistingSystemManaged<DamagingSystem>();
        var calculateDamageSystem = world.GetExistingSystemManaged<CalculateDamageSystem>();
        var bleedingUISystem = world.GetExistingSystemManaged<BleedingUISystem>();
        var burningUISystem = world.GetExistingSystemManaged<BurningUISystem>();
        var poisonUISystem = world.GetExistingSystemManaged<PoisonedUISystem>();
        var healingUISystem = world.GetExistingSystemManaged<ApplyHealingSystem>();

        if(damagingSystem != null)
        {
            damagingSystem.OnDamageTaken += DisplayCombatText;
            calculateDamageSystem.OnDamageBlocked += DisplayBlockedText;
            bleedingUISystem.OnBleeding += DisplayBleedingText;
            burningUISystem.OnBurning += DisplayBurningText;
            poisonUISystem.OnPoisoned += DisplayPoisonText;
            healingUISystem.OnHealingReceived += DisplayHealingText;
        }
    }

    private GameObject CreateCombatText(float3 position)
    {
        UnityEngine.Random.InitState(System.DateTime.Now.Millisecond);
        var direction = new float3(UnityEngine.Random.Range(-0.5f,0.5f),1.5f,0.0f);
        position += direction;
        var combatTextGameObject = Instantiate(CombatTextPrefab);
        combatTextGameObject.transform.position = position;
        var combatText = combatTextGameObject.GetComponent<CombatTextMonoBehaviour>();
        combatText.Direction = ((Vector3)direction).normalized;
        return combatTextGameObject;
    }

    private string ValueToString(float value)
    {
        if(value >= 1.0f)
        {
            return value.ToString("F0");
        }
        if(value > 0.2f)
        {
            return "<1";
        }
        return "~0";
    }

    public void DisplayCombatText(float damage, float3 position, bool isCrit)
    {
        var combatTextGameObject = CreateCombatText(position);
        var combatText = combatTextGameObject.GetComponent<CombatTextMonoBehaviour>();
        combatText.Text = ValueToString(damage);
        combatText.Color = Color.white;
        if(isCrit)
        {
            combatText.Color = Color.yellow;
        }
    }

    public void DisplayBlockedText(float amount, float3 position)
    {
        var combatTextGameObject = CreateCombatText(position);
        var combatText = combatTextGameObject.GetComponent<CombatTextMonoBehaviour>();
        combatText.Text = ValueToString(amount);
        combatText.Color = Color.darkBlue;
    }

    public void DisplayBleedingText(float damage, float3 position)
    {
        var combatTextGameObject = CreateCombatText(position);
        var combatText = combatTextGameObject.GetComponent<CombatTextMonoBehaviour>();
        combatText.Text = ValueToString(damage);
        combatText.Color = Color.pink;
    }

    public void DisplayBurningText(float damage, float3 position)
    {
        var combatTextGameObject = CreateCombatText(position);
        var combatText = combatTextGameObject.GetComponent<CombatTextMonoBehaviour>();
        combatText.Text = ValueToString(damage);
        combatText.Color = Color.red;
    }

    public void DisplayPoisonText(float damage, float3 position)
    {
        var combatTextGameObject = CreateCombatText(position);
        var combatText = combatTextGameObject.GetComponent<CombatTextMonoBehaviour>();
        combatText.Text = ValueToString(damage);
        combatText.Color = Color.forestGreen;
    }

    public void DisplayHealingText(float damage, float3 position)
    {
        var combatTextGameObject = CreateCombatText(position);
        var combatText = combatTextGameObject.GetComponent<CombatTextMonoBehaviour>();
        combatText.Text = ValueToString(damage);
        combatText.Color = Color.lawnGreen;
    }
}
