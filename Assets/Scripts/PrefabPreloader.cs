using UnityEngine;

public class PrefabPreloader : MonoBehaviour
{
    void Start()
    {
        string[] prefabPaths = { "Prefabs/Player", "Prefabs/SpaceShip", "Prefabs/Compass", "Prefabs/BoundaryVisual", "Prefabs/Planets", "Prefabs/Ore" };
        foreach (string path in prefabPaths)
        {
            GameObject prefab = Resources.Load<GameObject>(path);
            if (prefab == null)
            {
                Debug.LogError($"PrefabPreloader: Failed to load prefab at Assets/Resources/{path}.prefab");
            }
            else
            {
                Debug.Log($"PrefabPreloader: Loaded prefab at {path}");
            }
        }
    }
}