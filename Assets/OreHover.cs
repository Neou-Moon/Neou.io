using UnityEngine;

public class OreHover : MonoBehaviour
{
    public float hoverSpeed = 2f;  // Speed of hovering
    public float hoverAmount = 0.5f; // How much it moves up/down

    private Vector3 startPosition;

    void Start()
    {
        startPosition = transform.position;
    }

    void Update()
    {
        // Smooth hovering effect
        transform.position = startPosition + Vector3.up * Mathf.Sin(Time.time * hoverSpeed) * hoverAmount;
    }
}
