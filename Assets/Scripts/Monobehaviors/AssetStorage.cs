using Unity.Entities;
using UnityEngine;

public class AssetStorage : MonoBehaviour
{
    public static AssetStorage Instance;
    public Material ManabarMaterial;
    public Material RagebarMaterial;
    public Material FocusbarMaterial;
    public Material EnergybarMaterial;
    public Material ArcanebarMaterial;

    public void Start()
    {
        if(Instance == null)
        {
            Instance = this;
        }
    }
}
