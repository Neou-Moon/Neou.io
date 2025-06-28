using UnityEngine;
using UnityEngine.UI;
using Photon.Pun;
using System.Linq;
using TMPro;
using System.Collections;

public class EnemyCompass : MonoBehaviour
{
    [Header("Enemy Compass Settings")]
    [SerializeField] private Image compassNeedle;
    [SerializeField] private TextMeshProUGUI warningText;
    private Transform playerTransform;
    private Transform targetTransform;
    private bool isInitialized;
    private bool isVisible = true;
    private Coroutine flashCoroutine;
    private const float SEARCH_RADIUS = 500f;
    private const float HIDE_DISTANCE = 50f; // Distance to hide compass and text

    void Awake()
    {
        // Validate compassNeedle
        if (compassNeedle == null)
        {
            compassNeedle = GetComponent<Image>();
            if (compassNeedle == null)
            {
                compassNeedle = GetComponentInChildren<Image>();
                if (compassNeedle == null)
                {
                    CustomLogger.LogError("EnemyCompass: Image component not found on EnemyCompass or its children.");
                    enabled = false;
                    return;
                }
            }
        }
        CustomLogger.Log($"EnemyCompass: Found Compass Image on {compassNeedle.gameObject.name}.");

        // Validate warningText
        if (warningText == null)
        {
            GameObject textObj = new GameObject("WarningText");
            textObj.transform.SetParent(transform, false);
            warningText = textObj.AddComponent<TextMeshProUGUI>();
            warningText.text = "Enemy Nearby!";
            warningText.fontSize = 16;
            warningText.color = Color.white;
            warningText.alignment = TextAlignmentOptions.Center;
            RectTransform textRect = warningText.GetComponent<RectTransform>();
            textRect.anchoredPosition = new Vector2(0, -50); // Position below compass
            textRect.sizeDelta = new Vector2(150, 30);
            warningText.gameObject.SetActive(false);
            CustomLogger.Log($"EnemyCompass: Created WarningText for EnemyCompass on {gameObject.name}");
        }
        else
        {
            CustomLogger.Log($"EnemyCompass: Found WarningText on {warningText.gameObject.name}");
        }

        // Ensure RectTransform is set up
        RectTransform rect = GetComponent<RectTransform>();
        if (rect == null)
        {
            rect = gameObject.AddComponent<RectTransform>();
            rect.anchoredPosition = new Vector2(0, 50);
            rect.sizeDelta = new Vector2(80, 80);
            CustomLogger.Log("EnemyCompass: Added RectTransform with default settings.");
        }
        else
        {
            CustomLogger.Log($"EnemyCompass: RectTransform found, anchoredPosition={rect.anchoredPosition}, sizeDelta={rect.sizeDelta}");
        }
    }

    void Start()
    {
        if (!PhotonNetwork.IsConnected)
        {
            CustomLogger.LogError("EnemyCompass: Not connected to Photon, disabling.");
            enabled = false;
            return;
        }

        // Set playerTransform to the parent player's transform
        Transform parentPlayer = transform;
        while (parentPlayer != null && !parentPlayer.CompareTag("Player"))
        {
            parentPlayer = parentPlayer.parent;
        }
        if (parentPlayer != null)
        {
            playerTransform = parentPlayer;
            CustomLogger.Log($"EnemyCompass: Set playerTransform to {playerTransform.gameObject.name}");
            isInitialized = true;
        }
        else
        {
            CustomLogger.LogError("EnemyCompass: Could not find parent Player GameObject with tag 'Player'.");
            enabled = false;
            return;
        }
    }

    public void SetVisibility(bool visible)
    {
        if (isVisible == visible)
        {
            return;
        }
        isVisible = visible;
        gameObject.SetActive(visible);
        if (!visible && flashCoroutine != null)
        {
            StopCoroutine(flashCoroutine);
            flashCoroutine = null;
            warningText.gameObject.SetActive(false);
        }
        CustomLogger.Log($"EnemyCompass: Set visibility to {visible} for {playerTransform?.gameObject.name}");
    }

    void Update()
    {
        if (!isInitialized || playerTransform == null || compassNeedle == null || warningText == null || !isVisible)
        {
            return;
        }
        UpdateCompass();
    }

    private void UpdateCompass()
    {
        // Find the nearest Player or Bot within 500 units
        var (nearestTarget, distance) = FindNearestTarget();

        if (nearestTarget == null || distance < HIDE_DISTANCE)
        {
            // Hide needle and text if no target or too close
            compassNeedle.enabled = false;
            warningText.gameObject.SetActive(false);
            if (flashCoroutine != null)
            {
                StopCoroutine(flashCoroutine);
                flashCoroutine = null;
            }
            if (distance < HIDE_DISTANCE)
            {
                CustomLogger.Log($"EnemyCompass: Target {nearestTarget?.gameObject.name} too close ({distance:F1} units), hiding compass and text.");
            }
            return;
        }

        // Show needle and text, start flashing
        targetTransform = nearestTarget;
        compassNeedle.enabled = true;
        warningText.gameObject.SetActive(true);
        if (flashCoroutine == null)
        {
            flashCoroutine = StartCoroutine(FlashWarningText());
        }

        Vector3 direction = targetTransform.position - playerTransform.position;
        float angle = Mathf.Atan2(direction.y, direction.x) * Mathf.Rad2Deg;
        compassNeedle.transform.rotation = Quaternion.Euler(0f, 0f, angle);
        CustomLogger.Log($"EnemyCompass: Pointing to {targetTransform.gameObject.name}, Distance={distance:F1}, Direction={direction}, Angle={angle}, NeedleRotation={compassNeedle.transform.rotation.eulerAngles}");
    }

    private IEnumerator FlashWarningText()
    {
        while (true)
        {
            warningText.color = Color.red;
            yield return new WaitForSeconds(0.5f);
            warningText.color = Color.white;
            yield return new WaitForSeconds(0.5f);
        }
    }

    private (Transform, float) FindNearestTarget()
    {
        Transform nearestTarget = null;
        float minDistance = SEARCH_RADIUS;

        // Search for Players and Bots
        GameObject[] players = GameObject.FindGameObjectsWithTag("Player");
        GameObject[] bots = GameObject.FindGameObjectsWithTag("Bot");

        foreach (var target in players.Concat(bots))
        {
            // Skip the local player
            if (target.transform == playerTransform)
            {
                continue;
            }

            // Check if target is active and has a PhotonView
            PhotonView photonView = target.GetComponent<PhotonView>();
            if (photonView == null || !target.activeInHierarchy)
            {
                continue;
            }

            float distance = Vector3.Distance(playerTransform.position, target.transform.position);
            if (distance < minDistance)
            {
                minDistance = distance;
                nearestTarget = target.transform;
            }
        }

        return (nearestTarget, minDistance);
    }

    private string GetGameObjectPath(GameObject obj)
    {
        if (obj == null) return "null";
        string path = "/" + obj.name;
        while (obj.transform.parent != null)
        {
            obj = obj.transform.parent.gameObject;
            path = "/" + obj.name + path;
        }
        return path;
    }
}