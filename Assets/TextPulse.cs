using UnityEngine;
using TMPro;
using UnityEngine.EventSystems;

public class TextPulse : MonoBehaviour, IPointerClickHandler
{
    [SerializeField] private float minScale = 0.9f; // Minimum scale (90%)
    [SerializeField] private float maxScale = 1.1f; // Maximum scale (110%)
    [SerializeField] private float pulseSpeed = 0.2f; // Pulses per second (1 pulse every 5 seconds)

    private TextMeshProUGUI text;
    private Vector3 originalScale;
    private UpgradeManager upgradeManager;

    void Awake()
    {
        // Get TextMeshProUGUI component
        text = GetComponent<TextMeshProUGUI>();
        if (text == null)
        {
            Debug.LogError($"TextPulse: No TextMeshProUGUI found on {gameObject.name}");
            enabled = false;
            return;
        }

        // Store original scale
        originalScale = transform.localScale;

        // Enable raycast target for click detection
        text.raycastTarget = true;

        // Find UpgradeManager
        upgradeManager = Object.FindFirstObjectByType<UpgradeManager>();
        if (upgradeManager == null)
        {
            Debug.LogError($"TextPulse: No UpgradeManager found in scene");
        }
    }

    void Update()
    {
        // Use serialized pulseSpeed for pulsing effect
        float t = (Mathf.Sin(Time.time * pulseSpeed * 2f * Mathf.PI) + 1f) / 2f;
        float scale = Mathf.Lerp(minScale, maxScale, t);
        transform.localScale = originalScale * scale;
    }

    public void OnPointerClick(PointerEventData eventData)
    {
        if (upgradeManager != null)
        {
            upgradeManager.ToggleUpgradePanel();
            Debug.Log($"TextPulse: Text clicked, toggling upgrade panel");
        }
        else
        {
            Debug.LogWarning($"TextPulse: Cannot toggle panel, UpgradeManager not found");
        }
    }
}