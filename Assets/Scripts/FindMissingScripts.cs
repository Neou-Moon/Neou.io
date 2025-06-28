using UnityEngine;

public class FindMissingScripts : MonoBehaviour
{
    void Start()
    {
        foreach (GameObject go in Object.FindObjectsByType<GameObject>(FindObjectsSortMode.None))
        {
            foreach (Component comp in go.GetComponents<Component>())
            {
                if (comp == null)
                {
                    Debug.LogError($"Missing script on {go.name} at path {GetGameObjectPath(go)}");
                }
            }
        }
    }

    private string GetGameObjectPath(GameObject obj)
    {
        string path = "/" + obj.name;
        while (obj.transform.parent != null)
        {
            obj = obj.transform.parent.gameObject;
            path = "/" + obj.name + path;
        }
        return path;
    }
}