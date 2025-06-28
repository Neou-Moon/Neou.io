using UnityEngine;
using Photon.Pun;
using System.Collections;
using TMPro;
using UnityEngine.SceneManagement;
using PhotonHashtable = ExitGames.Client.Photon.Hashtable;

public class SpaceShipInteraction : MonoBehaviourPunCallbacks
{
    [Header("SpaceShip Interaction Settings")]
    private GameObject spaceshipObject;
    private PhotonView spaceshipPhotonView;
    private const float interactionDistance = 30f;

    private TextMeshProUGUI exitText;
    private TextMeshProUGUI healText;
    private bool wasInRange;
    private Canvas playerCanvas;
    private int textPositionUpdateCount = 0;
    private Vector2 exitTextFinalPos;
    private Vector2 healTextPos;
    private Coroutine textFlashCoroutine;
    private bool isTextPositionLocked = false;
    private Coroutine textPositionCoroutine;


    void Start()
    {
        if (SceneManager.GetActiveScene().name != "Moon Ran")
        {
            CustomLogger.Log("SpaceShipInteraction: Disabled outside Moon Ran scene.");
            enabled = false;
            return;
        }

        if (!photonView.IsMine)
        {
            CustomLogger.Log("SpaceShipInteraction: Disabled for non-local player.");
            enabled = false;
            return;
        }

        StartCoroutine(InitializeUIWithRetry());
        if (!PhotonNetwork.IsConnected)
        {
            CustomLogger.LogWarning("SpaceShipInteraction: Not connected to Photon, delaying initialization.");
            StartCoroutine(DelayedStart());
            return;
        }
        StartCoroutine(GetSpaceshipWithRetry());
    }

    private IEnumerator InitializeUIWithRetry()
    {
        int maxRetries = 20;
        int retries = 0;
        GameObject localPlayer = null;

        while (retries < maxRetries && playerCanvas == null)
        {
            if (PhotonNetwork.LocalPlayer != null &&
                PhotonNetwork.LocalPlayer.CustomProperties.TryGetValue("PlayerViewID", out object playerViewIDObj) &&
                playerViewIDObj is int playerViewID)
            {
                PhotonView playerView = PhotonView.Find(playerViewID);
                if (playerView != null && playerView.gameObject != null && playerView.IsMine)
                {
                    localPlayer = playerView.gameObject;
                    Canvas[] canvases = localPlayer.GetComponentsInChildren<Canvas>(true);
                    foreach (var canvas in canvases)
                    {
                        if (canvas.name == "Player Canvas")
                        {
                            playerCanvas = canvas;
                            CustomLogger.Log($"SpaceShipInteraction: Found Player Canvas at {GetGameObjectPath(playerCanvas.gameObject)}.");
                            break;
                        }
                    }
                    if (playerCanvas == null)
                    {
                        CustomLogger.LogWarning($"SpaceShipInteraction: Player Canvas not found in local player hierarchy. LocalPlayer: {GetGameObjectPath(localPlayer)}");
                        foreach (var canvas in canvases)
                        {
                            CustomLogger.Log($"SpaceShipInteraction: Found Canvas: {canvas.name} at {GetGameObjectPath(canvas.gameObject)}");
                        }
                    }
                }
            }
            if (playerCanvas != null) break;

            retries++;
            CustomLogger.Log($"SpaceShipInteraction: Retry {retries}/{maxRetries} to find Player Canvas.");
            yield return new WaitForSeconds(0.5f);
        }

        if (playerCanvas == null)
        {
            CustomLogger.LogError("SpaceShipInteraction: Player Canvas not found on local player after retries.");
            yield break;
        }

        TextMeshProUGUI[] texts = playerCanvas.GetComponentsInChildren<TextMeshProUGUI>(true);
        foreach (var text in texts)
        {
            if (text.name == "ExitInteractionText")
            {
                exitText = text;
                exitText.text = "PRESS 'ENTER' OR TAP SPACESHIP TO EXIT MATCH";
                exitText.color = Color.white;
                exitText.fontSize = 22;
                exitText.alignment = TextAlignmentOptions.Center;
                exitText.rectTransform.sizeDelta = new Vector2(600, 50);
                exitText.textWrappingMode = TextWrappingModes.NoWrap;
                exitText.autoSizeTextContainer = true;
                exitText.rectTransform.anchorMin = new Vector2(0.5f, 0.5f);
                exitText.rectTransform.anchorMax = new Vector2(0.5f, 0.5f);
                exitText.rectTransform.pivot = new Vector2(0.5f, 0.5f);
                exitText.gameObject.SetActive(false);
                CustomLogger.Log($"SpaceShipInteraction: Found ExitInteractionText at {GetGameObjectPath(text.gameObject)}.");
            }
            if (text.name == "HealInteractionText")
            {
                healText = text;
                healText.text = "PRESS 'H' OR TAP HEALTHBAR TO HEAL";
                healText.color = Color.white;
                healText.fontSize = 22;
                healText.alignment = TextAlignmentOptions.Center;
                healText.rectTransform.sizeDelta = new Vector2(600, 50);
                healText.textWrappingMode = TextWrappingModes.NoWrap;
                healText.autoSizeTextContainer = true;
                healText.rectTransform.anchorMin = new Vector2(0.5f, 0.5f);
                healText.rectTransform.anchorMax = new Vector2(0.5f, 0.5f);
                healText.rectTransform.pivot = new Vector2(0.5f, 0.5f);
                healText.gameObject.SetActive(false);
                CustomLogger.Log($"SpaceShipInteraction: Found HealInteractionText at {GetGameObjectPath(text.gameObject)}.");
            }
            if (exitText != null && healText != null) break;
        }

        if (exitText == null)
        {
            GameObject exitTextObj = new GameObject("ExitInteractionText");
            exitTextObj.transform.SetParent(playerCanvas.transform, false);
            exitText = exitTextObj.AddComponent<TextMeshProUGUI>();
            exitText.text = "PRESS 'ENTER' OR TAP SPACESHIP TO EXIT MATCH";
            exitText.color = Color.white;
            exitText.fontSize = 24;
            exitText.alignment = TextAlignmentOptions.Center;
            exitText.rectTransform.sizeDelta = new Vector2(600, 50);
            exitText.textWrappingMode = TextWrappingModes.NoWrap;
            exitText.autoSizeTextContainer = true;
            exitText.rectTransform.anchorMin = new Vector2(0.5f, 0.5f);
            exitText.rectTransform.anchorMax = new Vector2(0.5f, 0.5f);
            exitText.rectTransform.pivot = new Vector2(0.5f, 0.5f);
            exitText.rectTransform.anchoredPosition = Vector2.zero;
            exitText.gameObject.SetActive(false);
            CustomLogger.Log($"SpaceShipInteraction: Created ExitInteractionText at {GetGameObjectPath(exitTextObj)}.");
        }

        if (healText == null)
        {
            GameObject healTextObj = new GameObject("HealInteractionText");
            healTextObj.transform.SetParent(playerCanvas.transform, false);
            healText = healTextObj.AddComponent<TextMeshProUGUI>();
            healText.text = "PRESS 'H' OR TAP HEALTHBAR TO HEAL";
            healText.color = Color.white;
            healText.fontSize = 24;
            healText.alignment = TextAlignmentOptions.Center;
            healText.rectTransform.sizeDelta = new Vector2(600, 50);
            healText.textWrappingMode = TextWrappingModes.NoWrap;
            healText.autoSizeTextContainer = true;
            healText.rectTransform.anchorMin = new Vector2(0.5f, 0.5f);
            healText.rectTransform.anchorMax = new Vector2(0.5f, 0.5f);
            healText.rectTransform.pivot = new Vector2(0.5f, 0.5f);
            healText.rectTransform.anchoredPosition = new Vector2(0, -100);
            healText.gameObject.SetActive(false);
            CustomLogger.Log($"SpaceShipInteraction: Created HealInteractionText at {GetGameObjectPath(healTextObj)}.");
        }
    }

    public override void OnDisable()
    {
        base.OnDisable();
        if (exitText != null && exitText.gameObject != null)
        {
            exitText.gameObject.SetActive(false);
            CustomLogger.Log("SpaceShipInteraction: Hid exitText due to script disable.");
        }
        if (healText != null && healText.gameObject != null)
        {
            healText.gameObject.SetActive(false);
            CustomLogger.Log("SpaceShipInteraction: Hid healText due to script disable.");
        }
        if (textPositionCoroutine != null)
        {
            StopCoroutine(textPositionCoroutine);
            textPositionCoroutine = null;
        }
        if (textFlashCoroutine != null)
        {
            StopCoroutine(textFlashCoroutine);
            textFlashCoroutine = null;
            CustomLogger.Log("SpaceShipInteraction: Stopped text flash coroutine due to script disable.");
        }
        CustomLogger.Log("SpaceShipInteraction: Script disabled, hid interaction texts and stopped coroutines.");
    }

    private IEnumerator DelayedStart()
    {
        yield return new WaitForSeconds(5f);
        if (PhotonNetwork.IsConnected)
        {
            StartCoroutine(GetSpaceshipWithRetry());
        }
        else
        {
            CustomLogger.LogError("SpaceShipInteraction: Failed to connect to Photon after delay.");
            enabled = false;
        }
    }

    public IEnumerator GetSpaceshipWithRetry()
    {
        float retryDelay = 5f;
        int maxRetries = 10;

        for (int retries = 0; retries < maxRetries && SceneManager.GetActiveScene().name == "Moon Ran" && enabled; retries++)
        {
            if (PhotonNetwork.IsConnectedAndReady && PhotonNetwork.LocalPlayer != null)
            {
                if (PhotonNetwork.LocalPlayer.CustomProperties.TryGetValue("SpaceshipViewID", out object viewID))
                {
                    spaceshipPhotonView = PhotonView.Find((int)viewID);
                    if (spaceshipPhotonView != null && spaceshipPhotonView.gameObject != null && spaceshipPhotonView.gameObject.CompareTag("SpaceShip"))
                    {
                        SpaceshipMarker marker = spaceshipPhotonView.gameObject.GetComponent<SpaceshipMarker>();
                        if (marker != null && marker.ownerId == PhotonNetwork.LocalPlayer.ActorNumber)
                        {
                            spaceshipObject = spaceshipPhotonView.gameObject;
                            CustomLogger.Log($"SpaceShipInteraction: Found spaceship {spaceshipObject.name} with ViewID {viewID}, ownerId={marker.ownerId}");
                            yield break;
                        }
                        else
                        {
                            CustomLogger.LogWarning($"SpaceShipInteraction: SpaceshipViewID={viewID} invalid or ownerId mismatch. Marker={(marker != null ? "found" : "null")}, OwnerId={(marker != null ? marker.ownerId : -1)}");
                            PhotonNetwork.LocalPlayer.SetCustomProperties(new PhotonHashtable { { "SpaceshipViewID", null } });
                        }
                    }
                    else
                    {
                        CustomLogger.LogWarning($"SpaceShipInteraction: SpaceshipViewID={viewID} invalid or not tagged SpaceShip.");
                        PhotonNetwork.LocalPlayer.SetCustomProperties(new PhotonHashtable { { "SpaceshipViewID", null } });
                    }
                }

                GameObject[] spaceships = GameObject.FindGameObjectsWithTag("SpaceShip");
                foreach (GameObject spaceship in spaceships)
                {
                    SpaceshipMarker marker = spaceship.GetComponent<SpaceshipMarker>();
                    if (marker != null && marker.ownerId == PhotonNetwork.LocalPlayer.ActorNumber)
                    {
                        spaceshipPhotonView = spaceship.GetComponent<PhotonView>();
                        if (spaceshipPhotonView != null)
                        {
                            spaceshipObject = spaceship;
                            PhotonNetwork.LocalPlayer.SetCustomProperties(new PhotonHashtable { { "SpaceshipViewID", spaceshipPhotonView.ViewID } });
                            CustomLogger.Log($"SpaceShipInteraction: Fallback found spaceship {spaceship.name} with ViewID {spaceshipPhotonView.ViewID}, ownerId={marker.ownerId}");
                            yield break;
                        }
                    }
                }

                CustomLogger.Log($"SpaceShipInteraction: Retry {retries + 1}/{maxRetries} to find spaceship...");
                yield return new WaitForSeconds(retryDelay);
            }
            else
            {
                CustomLogger.LogWarning("SpaceShipInteraction: Photon not ready or LocalPlayer null.");
                yield return new WaitForSeconds(retryDelay);
            }
        }
        CustomLogger.LogError("SpaceShipInteraction: Failed to find spaceship after max retries.");
    }

    void Update()
    {
        if (!photonView.IsMine)
            return;

        if (Input.GetKeyDown(KeyCode.H))
        {
            ConvertBrightMatterToHealth();
        }

        if (Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.KeypadEnter))
        {
            InteractWithShip();
        }

        if (Input.GetMouseButtonDown(0))
        {
            Vector2 mousePos = Camera.main.ScreenToWorldPoint(Input.mousePosition);
            RaycastHit2D hit = Physics2D.Raycast(mousePos, Vector2.zero);
            if (hit.collider != null && hit.collider.gameObject.CompareTag("SpaceShip"))
            {
                SpaceshipMarker marker = hit.collider.gameObject.GetComponent<SpaceshipMarker>();
                if (marker != null && marker.ownerId == PhotonNetwork.LocalPlayer.ActorNumber)
                {
                    float distance = Vector3.Distance(transform.position, hit.collider.gameObject.transform.position);
                    if (distance <= interactionDistance)
                    {
                        InteractWithShip();
                        CustomLogger.Log($"SpaceShipInteraction: Clicked SpaceShip {hit.collider.gameObject.name}, triggered InteractWithShip, distance={distance:F1}");
                    }
                    else
                    {
                        CustomLogger.Log($"SpaceShipInteraction: Clicked SpaceShip {hit.collider.gameObject.name}, but too far (distance={distance:F1}, max={interactionDistance})");
                    }
                }
            }
        }

        bool isInRange = false;
        if (spaceshipObject != null && spaceshipPhotonView != null)
        {
            float distance = Vector3.Distance(transform.position, spaceshipObject.transform.position);
            isInRange = distance <= interactionDistance;
        }

        if (isInRange && !wasInRange)
        {
            if (playerCanvas == null)
            {
                StartCoroutine(InitializeUIWithRetry());
            }
            if (playerCanvas != null)
            {
                if (exitText != null && exitText.gameObject != null)
                {
                    exitText.gameObject.SetActive(true);
                }
                if (healText != null && healText.gameObject != null)
                {
                    healText.gameObject.SetActive(true);
                }
                textPositionUpdateCount = 0;
                isTextPositionLocked = false;
                if (textPositionCoroutine != null) StopCoroutine(textPositionCoroutine);
                textPositionCoroutine = StartCoroutine(UpdateTextPositionsCoroutine());
                if (textFlashCoroutine != null) StopCoroutine(textFlashCoroutine);
                textFlashCoroutine = StartCoroutine(FlashTextColor());
                CustomLogger.Log("SpaceShipInteraction: Player entered range, showing interaction texts, resetting position update count, starting text position and flash coroutines.");
            }
            else
            {
                CustomLogger.LogWarning("SpaceShipInteraction: Player Canvas not found, cannot show interaction texts.");
            }
        }
        else if (!isInRange && wasInRange)
        {
            if (exitText != null && exitText.gameObject != null)
            {
                exitText.gameObject.SetActive(false);
                CustomLogger.Log("SpaceShipInteraction: Hid exitText due to player leaving range.");
            }
            if (healText != null && healText.gameObject != null)
            {
                healText.gameObject.SetActive(false);
                CustomLogger.Log("SpaceShipInteraction: Hid healText due to player leaving range.");
            }
            textPositionUpdateCount = 0;
            isTextPositionLocked = false;
            if (textPositionCoroutine != null)
            {
                StopCoroutine(textPositionCoroutine);
                textPositionCoroutine = null;
            }
            if (textFlashCoroutine != null)
            {
                StopCoroutine(textFlashCoroutine);
                textFlashCoroutine = null;
                CustomLogger.Log("SpaceShipInteraction: Stopped text flash coroutine due to player leaving range.");
            }
            CustomLogger.Log("SpaceShipInteraction: Player left range, hid interaction texts, reset position update count, and stopped coroutines.");
        }

        wasInRange = isInRange;
    }

    private IEnumerator UpdateTextPositionsCoroutine()
    {
        while (true)
        {
            if (spaceshipObject == null || playerCanvas == null || exitText == null || healText == null || exitText.gameObject == null || healText.gameObject == null)
            {
                CustomLogger.LogWarning("SpaceShipInteraction: UpdateTextPositionsCoroutine skipped, missing required objects.");
                yield return new WaitForSeconds(1f);
                continue;
            }

            float distance = Vector3.Distance(transform.position, spaceshipObject.transform.position);
            if (distance > interactionDistance)
            {
                if (exitText.gameObject.activeSelf) CustomLogger.LogWarning("SpaceShipInteraction: ExitText was active before range exit.");
                if (healText.gameObject.activeSelf) CustomLogger.LogWarning("SpaceShipInteraction: HealText was active before range exit.");
                exitText.gameObject.SetActive(false);
                healText.gameObject.SetActive(false);
                CustomLogger.Log("SpaceShipInteraction: UpdateTextPositionsCoroutine stopped, player out of range, texts deactivated.");
                yield break;
            }

            // Ensure texts are active while in range
            if (!exitText.gameObject.activeSelf)
            {
                exitText.gameObject.SetActive(true);
                CustomLogger.Log("SpaceShipInteraction: Re-enabled exitText in coroutine as it was unexpectedly inactive.");
            }
            if (!healText.gameObject.activeSelf)
            {
                healText.gameObject.SetActive(true);
                CustomLogger.Log("SpaceShipInteraction: Re-enabled healText in coroutine as it was unexpectedly inactive.");
            }

            if (isTextPositionLocked)
            {
                exitText.rectTransform.anchoredPosition = exitTextFinalPos;
                healText.rectTransform.anchoredPosition = healTextPos;
                CustomLogger.Log($"SpaceShipInteraction: Text positions locked, maintaining exitText={exitTextFinalPos}, healText={healTextPos}");
                yield return new WaitForSeconds(1f);
                continue;
            }

            if (textPositionUpdateCount >= 10)
            {
                isTextPositionLocked = true;
                exitText.rectTransform.anchoredPosition = exitTextFinalPos;
                healText.rectTransform.anchoredPosition = healTextPos;
                CustomLogger.Log($"SpaceShipInteraction: Text positions locked after {textPositionUpdateCount} updates, set to exitText={exitTextFinalPos}, healText={healTextPos}");
                yield return new WaitForSeconds(1f);
                continue;
            }

            Collider2D collider = spaceshipObject.GetComponent<Collider2D>();
            Vector3 textWorldPos = spaceshipObject.transform.position;
            if (collider != null)
            {
                textWorldPos.y = collider.bounds.min.y - 10f;
            }
            else
            {
                CustomLogger.LogWarning("SpaceShipInteraction: No Collider2D on spaceship, using transform.position for text positioning.");
                textWorldPos.y -= 10f;
            }

            Vector2 screenPos = Camera.main.WorldToScreenPoint(textWorldPos);
            Vector2 canvasPos;
            Camera canvasCamera = (playerCanvas.renderMode == RenderMode.ScreenSpaceOverlay) ? null : Camera.main;
            bool success = RectTransformUtility.ScreenPointToLocalPointInRectangle(
                playerCanvas.GetComponent<RectTransform>(),
                screenPos,
                canvasCamera,
                out canvasPos
            );

            if (success)
            {
                exitText.rectTransform.anchoredPosition = canvasPos;
                exitTextFinalPos = canvasPos;

                Vector3 healTextWorldPos = textWorldPos;
                healTextWorldPos.y -= 4f;
                screenPos = Camera.main.WorldToScreenPoint(healTextWorldPos);
                success = RectTransformUtility.ScreenPointToLocalPointInRectangle(
                    playerCanvas.GetComponent<RectTransform>(),
                    screenPos,
                    canvasCamera,
                    out canvasPos
                );

                if (success)
                {
                    healText.rectTransform.anchoredPosition = canvasPos;
                    healTextPos = canvasPos;
                    textPositionUpdateCount++;
                    CustomLogger.Log($"SpaceShipInteraction: Updated text positions (update {textPositionUpdateCount}/10), exitText={exitText.rectTransform.anchoredPosition}, healText={healText.rectTransform.anchoredPosition}");
                }
                else
                {
                    CustomLogger.LogWarning("SpaceShipInteraction: Failed to convert healText screen position to canvas position.");
                }
            }
            else
            {
                CustomLogger.LogWarning("SpaceShipInteraction: Failed to convert exitText screen position to canvas position.");
            }

            yield return new WaitForSeconds(1f);
        }
    }

    private IEnumerator FlashTextColor()
    {
        while (true)
        {
            if (exitText == null || healText == null || !exitText.gameObject.activeSelf || !healText.gameObject.activeSelf)
            {
                CustomLogger.Log("SpaceShipInteraction: FlashTextColor stopped, texts are null or inactive.");
                yield break;
            }

            exitText.color = Color.red;
            healText.color = Color.red;
            CustomLogger.Log("SpaceShipInteraction: Set interaction texts to red.");
            yield return new WaitForSeconds(1f);

            if (exitText == null || healText == null || !exitText.gameObject.activeSelf || !healText.gameObject.activeSelf)
            {
                CustomLogger.Log("SpaceShipInteraction: FlashTextColor stopped during cycle, texts are null or inactive.");
                yield break;
            }

            exitText.color = Color.white;
            healText.color = Color.white;
            CustomLogger.Log("SpaceShipInteraction: Set interaction texts to white.");
            yield return new WaitForSeconds(1f);
        }
    }

    public void ConvertBrightMatterToHealth()
    {
        if (spaceshipObject == null || spaceshipPhotonView == null)
        {
            GameObject[] spaceships = GameObject.FindGameObjectsWithTag("SpaceShip");
            foreach (GameObject spaceship in spaceships)
            {
                SpaceshipMarker marker = spaceship.GetComponent<SpaceshipMarker>();
                if (marker != null && marker.ownerId == PhotonNetwork.LocalPlayer.ActorNumber)
                {
                    spaceshipObject = spaceship;
                    spaceshipPhotonView = spaceship.GetComponent<PhotonView>();
                    CustomLogger.Log($"SpaceShipInteraction: Found spaceship via fallback, name={spaceshipObject.name}, ownerId={marker.ownerId}");
                    break;
                }
            }
        }

        if (spaceshipObject == null || spaceshipPhotonView == null)
        {
            CustomLogger.LogWarning("SpaceShipInteraction: Cannot convert BrightMatter to health, spaceship not found.");
            return;
        }

        float distance = Vector3.Distance(transform.position, spaceshipObject.transform.position);
        if (distance > interactionDistance)
        {
            CustomLogger.Log($"SpaceShipInteraction: Too far from spaceship to convert BrightMatter (distance={distance:F1}, max={interactionDistance})");
            return;
        }

        PlayerHealth playerHealth = GetComponent<PlayerHealth>();
        if (playerHealth == null)
        {
            CustomLogger.LogError("SpaceShipInteraction: PlayerHealth component not found.");
            return;
        }

        PlayerController playerController = GetComponent<PlayerController>();
        if (playerController == null)
        {
            CustomLogger.LogError("SpaceShipInteraction: PlayerController component not found.");
            return;
        }

        int currentHealth = playerHealth.GetCurrentHealth();
        int maxHealth = playerHealth.maxHealth;
        int currentBrightMatter = playerController.GetBrightMatter();

        int healthNeeded = maxHealth - currentHealth;
        if (healthNeeded <= 0)
        {
            CustomLogger.Log($"SpaceShipInteraction: Health already at maximum ({currentHealth}/{maxHealth}). No conversion needed.");
            return;
        }

        int healthToAdd = Mathf.Min(healthNeeded, currentBrightMatter);
        int brightMatterToConsume = healthToAdd;

        if (healthToAdd <= 0)
        {
            CustomLogger.Log($"SpaceShipInteraction: Insufficient BrightMatter ({currentBrightMatter}) to convert to health.");
            return;
        }

        playerHealth.Heal(healthToAdd);
        playerController.SyncBrightMatter(currentBrightMatter - brightMatterToConsume);

        CustomLogger.Log($"SpaceShipInteraction: Converted {brightMatterToConsume} BrightMatter to {healthToAdd} Health. New Health={playerHealth.GetCurrentHealth()}/{maxHealth}, New BrightMatter={playerController.GetBrightMatter()}");
    }

    public void InteractWithShip()
    {
        if (!photonView.IsMine)
        {
            CustomLogger.LogWarning("SpaceShipInteraction: Cannot interact, photonView not mine.");
            return;
        }

        if (spaceshipObject == null || spaceshipPhotonView == null)
        {
            GameObject[] spaceships = GameObject.FindGameObjectsWithTag("SpaceShip");
            foreach (GameObject spaceship in spaceships)
            {
                SpaceshipMarker marker = spaceship.GetComponent<SpaceshipMarker>();
                if (marker != null && marker.ownerId == PhotonNetwork.LocalPlayer.ActorNumber)
                {
                    spaceshipObject = spaceship;
                    spaceshipPhotonView = spaceship.GetComponent<PhotonView>();
                    CustomLogger.Log($"SpaceShipInteraction: Found spaceship via fallback, name={spaceshipObject.name}, ownerId={marker.ownerId}");
                    break;
                }
            }
        }

        if (spaceshipObject == null || spaceshipPhotonView == null)
        {
            CustomLogger.LogWarning("SpaceShipInteraction: Cannot interact, spaceship not found.");
            return;
        }

        float distance = Vector3.Distance(transform.position, spaceshipObject.transform.position);
        if (distance > interactionDistance)
        {
            CustomLogger.Log($"SpaceShipInteraction: Too far from spaceship (distance={distance:F1}, max={interactionDistance})");
            return;
        }

        PlayerController playerController = GetComponent<PlayerController>();
        if (playerController != null)
        {
            playerController.SyncBrightMatter(0);
            CustomLogger.Log("SpaceShipInteraction: Reset BrightMatter to 0.");
        }
        else
        {
            CustomLogger.LogError("SpaceShipInteraction: PlayerController component not found.");
        }

        PlayerPrefs.SetInt("InsideSpaceShip", 1);
        PlayerPrefs.Save();

        if (PhotonNetwork.LocalPlayer.CustomProperties.TryGetValue("PlayerViewID", out object playerViewIDObj) && playerViewIDObj is int playerViewID)
        {
            PhotonView playerView = PhotonView.Find(playerViewID);
            if (playerView != null) PhotonNetwork.Destroy(playerView.gameObject);
            else CustomLogger.LogWarning($"PlayerViewID={playerViewID} not found for destruction.");
        }
        if (PhotonNetwork.LocalPlayer.CustomProperties.TryGetValue("SpaceshipViewID", out object spaceshipViewIDObj) && spaceshipViewIDObj is int spaceshipViewID)
        {
            PhotonView spaceshipView = PhotonView.Find(spaceshipViewID);
            if (spaceshipView != null) PhotonNetwork.Destroy(spaceshipView.gameObject);
            else CustomLogger.LogWarning($"SpaceshipViewID={spaceshipViewID} not found for destruction.");
        }
        if (PhotonNetwork.LocalPlayer.CustomProperties.TryGetValue("CompassViewID", out object compassViewIDObj) && compassViewIDObj is int compassViewID)
        {
            PhotonView compassView = PhotonView.Find(compassViewID);
            if (compassView != null) PhotonNetwork.Destroy(compassView.gameObject);
            else CustomLogger.LogWarning($"CompassViewID={compassViewID} not found for destruction.");
        }

        PhotonNetwork.LocalPlayer.SetCustomProperties(new PhotonHashtable
        {
            { "PlayerViewID", null },
            { "SpaceshipViewID", null },
            { "CompassViewID", null }
        });

        if (PhotonNetwork.InRoom)
        {
            CustomLogger.Log($"SpaceShipInteraction: Leaving room {PhotonNetwork.CurrentRoom?.Name}");
            PhotonNetwork.LeaveRoom();
        }

        CustomLogger.Log("SpaceShipInteraction: Loading InsideSpaceShip scene.");
        PhotonNetwork.LoadLevel("InsideSpaceShip");
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