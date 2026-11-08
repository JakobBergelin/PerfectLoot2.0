using Unity.Entities;
using UnityEngine;

public class AssetStorage : MonoBehaviour
{
    public static AssetStorage Instance;

    public void Start()
    {
        if(Instance == null)
        {
            Instance = this;
        }
    }
}
