using UnityEngine;
using UnityEngine.UI;
using TMPro;
using Photon.Pun;
using System.Collections;

public class Compass : MonoBehaviour
{
    [Header("Compass Settings")]
    [SerializeField] private Image compassNeedle;
    [SerializeField] private TextMeshProUGUI distanceText;
    private Transform playerTransform;
    private Transform spaceshipTransform;
    private bool isInitialized;
    private bool isVisible = true;

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
                    CustomLogger.LogError("Compass: Image component not found on Compass or its children.");
                    enabled = false;
                    return;
                }
            }
        }
        CustomLogger.Log($"Compass: Found Compass Image on {compassNeedle.gameObject.name}.");

        // Validate distanceText
        if (distanceText == null)
        {
            CustomLogger.LogError($"Compass: DistanceText not assigned in Inspector. Please assign a TextMeshProUGUI component in the Compass Inspector.");
            enabled = false;
            return;
        }
        CustomLogger.Log($"Compass: Found DistanceText on {distanceText.gameObject.name}.");

        // Ensure RectTransform is set up (should be configured in prefab)
        RectTransform rect = GetComponent<RectTransform>();
        if (rect == null)
        {
            rect = gameObject.AddComponent<RectTransform>();
            rect.anchoredPosition = new Vector2(0, 100);
            rect.sizeDelta = new Vector2(100, 100);
            CustomLogger.Log("Compass: Added RectTransform with default settings.");
        }
        else
        {
            CustomLogger.Log($"Compass: RectTransform found, anchoredPosition={rect.anchoredPosition}, sizeDelta={rect.sizeDelta}");
        }
    }

    void Start()
    {
        if (!PhotonNetwork.IsConnected)
        {
            CustomLogger.LogError("Compass: Not connected to Photon, disabling.");
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
            CustomLogger.Log($"Compass: Set playerTransform to {playerTransform.gameObject.name}");
        }
        else
        {
            CustomLogger.LogError("Compass: Could not find parent Player GameObject with tag 'Player'.");
            enabled = false;
            return;
        }

        StartCoroutine(InitializeSpaceshipTransformWithRetry());
    }

    private IEnumerator InitializeSpaceshipTransformWithRetry()
    {
        yield return new WaitForSeconds(4f);

        int maxRetries = 30;
        int retries = 0;
        float retryDelay = 0.5f;

        while (retries < maxRetries)
        {
            if (isInitialized)
            {
                CustomLogger.Log("Compass: Already initialized, exiting retry loop.");
                yield break;
            }

            if (PhotonNetwork.LocalPlayer == null || !PhotonNetwork.IsConnectedAndReady)
            {
                CustomLogger.LogWarning($"Compass: LocalPlayer null or Photon not ready, retry {retries + 1}/{maxRetries}.");
                retries++;
                yield return new WaitForSeconds(retryDelay);
                continue;
            }

            if (spaceshipTransform == null && PhotonNetwork.LocalPlayer.CustomProperties.TryGetValue("SpaceshipViewID", out object spaceshipViewID))
            {
                PhotonView spaceshipView = PhotonView.Find((int)spaceshipViewID);
                if (spaceshipView != null && spaceshipView.gameObject != null && spaceshipView.gameObject.CompareTag("SpaceShip") && spaceshipView.gameObject.activeInHierarchy)
                {
                    SpaceshipMarker marker = spaceshipView.gameObject.GetComponent<SpaceshipMarker>();
                    if (marker != null && marker.ownerId == PhotonNetwork.LocalPlayer.ActorNumber && marker.ownerId > 0)
                    {
                        spaceshipTransform = spaceshipView.transform;
                        CustomLogger.Log($"Compass: Found SpaceshipViewID={spaceshipViewID}, spaceship set to {spaceshipView.gameObject.name}, ownerId={marker.ownerId}, active={spaceshipView.gameObject.activeInHierarchy}");
                    }
                }
            }

            if (playerTransform != null && spaceshipTransform != null)
            {
                isInitialized = true;
                CustomLogger.Log($"Compass: Initialization complete. Player={playerTransform.gameObject.name}, Spaceship={spaceshipTransform.gameObject.name}");
                yield break;
            }

            retries++;
            CustomLogger.Log($"Compass: Retry {retries}/{maxRetries} to initialize spaceshipTransform. Spaceship={(spaceshipTransform != null ? spaceshipTransform.gameObject.name : "null")}");
            yield return new WaitForSeconds(retryDelay);
        }

        CustomLogger.LogError($"Compass: Failed to initialize after {maxRetries} retries: spaceshipTransform={(spaceshipTransform != null ? spaceshipTransform.gameObject.name : "null")}. Disabling.");
        enabled = false;
    }

    public void SetSpaceshipTransform(Transform spaceship)
    {
        if (spaceship != null && spaceship.gameObject.CompareTag("SpaceShip"))
        {
            var marker = spaceship.GetComponent<SpaceshipMarker>();
            if (marker != null && marker.ownerId == PhotonNetwork.LocalPlayer.ActorNumber && marker.ownerId > 0)
            {
                spaceshipTransform = spaceship;
                isInitialized = playerTransform != null;
                CustomLogger.Log($"Compass: Set spaceshipTransform to {spaceship.gameObject.name}, ownerId={marker.ownerId}");
            }
            else
            {
                CustomLogger.LogWarning($"Compass: Cannot set spaceshipTransform, invalid ownerId={(marker != null ? marker.ownerId : -1)} or marker missing.");
            }
        }
        else
        {
            CustomLogger.LogWarning($"Compass: Invalid spaceship transform, tag={(spaceship != null ? spaceship.gameObject.tag : "null")}");
        }
    }

    public void ForceUpdateCompass()
    {
        if (!isInitialized || playerTransform == null || spaceshipTransform == null || compassNeedle == null || distanceText == null)
        {
            CustomLogger.LogWarning("Compass: ForceUpdateCompass skipped, not fully initialized.");
            return;
        }
        UpdateCompass();
    }

    public void SetVisibility(bool visible)
    {
        if (isVisible == visible)
        {
            return;
        }
        isVisible = visible;
        gameObject.SetActive(visible);
        CustomLogger.Log($"Compass: Set visibility to {visible} for {playerTransform?.gameObject.name}");
    }

    void Update()
    {
        if (!isInitialized || playerTransform == null || spaceshipTransform == null || compassNeedle == null || distanceText == null || !isVisible)
        {
            return;
        }
        UpdateCompass();
    }

    private void UpdateCompass()
    {
        Vector3 direction = spaceshipTransform.position - playerTransform.position;
        float angle = Mathf.Atan2(direction.y, direction.x) * Mathf.Rad2Deg;
        compassNeedle.transform.rotation = Quaternion.Euler(0f, 0f, angle);
        distanceText.text = $"Distance from Ship: {Mathf.FloorToInt(direction.magnitude)}m";
        CustomLogger.Log($"Compass: Direction={direction}, Angle={angle}, NeedleRotation={compassNeedle.transform.rotation.eulerAngles}, Distance={Mathf.FloorToInt(direction.magnitude)}m");
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