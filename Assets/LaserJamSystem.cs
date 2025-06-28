using UnityEngine;

public class LaserJamSystem : MonoBehaviour
{
    [Header("Jam Settings")]
    public float jamDuration = 1f;
    public float recoilForce = 3f;

    private LineRenderer laserRenderer;
    private bool isJammed;
    private Vector2 lastLaserDirection;

    void Awake()
    {
        laserRenderer = GetComponent<LineRenderer>();
    }

    public void TriggerJam(Vector2 laserDirection)
    {
        if (isJammed) return;

        lastLaserDirection = laserDirection;
        isJammed = true;
        laserRenderer.enabled = false;

        // Handle jam effects (e.g., recoil, visual effects, etc.)

        Invoke(nameof(ResetJam), jamDuration);
    }

    private void ResetJam() => isJammed = false;
}
