using UnityEngine;

public class FlagIconPositioner : MonoBehaviour
{
    [SerializeField] private Transform target; // LaserBeamGun transform
    [SerializeField] private Vector3 offset; // Optional offset for fine-tuning

    void Update()
    {
        if (target != null)
        {
            transform.position = target.position + offset;
            transform.rotation = Quaternion.identity; // Keep flag upright, no rotation
        }
        else
        {
            Debug.LogWarning($"FlagIconPositioner: Target is null on {gameObject.name}.");
        }
    }
}