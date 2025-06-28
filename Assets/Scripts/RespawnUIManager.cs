using UnityEngine;
using UnityEngine.UI;
using TMPro;
using Photon.Pun;
using Photon.Realtime;
using Hashtable = ExitGames.Client.Photon.Hashtable;
using System.Collections;
using UnityEngine.SceneManagement;
using UnityEngine.EventSystems; // Added for EventTrigger

public class RespawnUIManager : MonoBehaviour
{
    private static RespawnUIManager instance;
    public static RespawnUIManager Instance
    {
        get
        {
            if (instance == null)
            {
                GameObject obj = new GameObject("RespawnUIManager");
                instance = obj.AddComponent<RespawnUIManager>();
                CustomLogger.Log("RespawnUIManager: Created singleton instance dynamically.");
            }
            return instance;
        }
    }

    [SerializeField] private Canvas playerCanvas; // Assign in Inspector if possible
    private TextMeshProUGUI respawnText;
    private bool isWaitingForRespawn;
    private PlayerController playerController;
    public bool IsReady => playerCanvas != null && respawnText != null && respawnText.transform.parent == playerCanvas.transform;

    void Awake()
    {
        if (instance != null && instance != this)
        {
            CustomLogger.LogWarning("RespawnUIManager: Another instance already exists, destroying this one.");
            Destroy(gameObject);
            return;
        }
        instance = this;
        DontDestroyOnLoad(gameObject);
        CustomLogger.Log("RespawnUIManager: Awake, setting up singleton instance.");

        SceneManager.sceneLoaded += OnSceneLoaded;
        StartCoroutine(InitializeUIWithRetry());
    }

    private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        CustomLogger.Log($"RespawnUIManager: Scene loaded - {scene.name}");
        if (scene.name == "Moon Ran" || scene.name == "TeamMoonRan")
        {
            // Always reinitialize to ensure UI is ready
            playerCanvas = null;
            respawnText = null;
            StartCoroutine(InitializeUIWithRetry());
        }
    }

    public IEnumerator InitializeUIWithRetry()
    {
        int retryCount = 0;
        const int maxRetries = 20; // Increased to 10 seconds (20 * 0.5s)
        const float checkInterval = 0.5f;

        // Reset UI state before initialization
        playerCanvas = null;
        respawnText = null;

        while (retryCount < maxRetries)
        {
            if (PhotonNetwork.LocalPlayer?.TagObject != null)
            {
                GameObject localPlayer = PhotonNetwork.LocalPlayer.TagObject as GameObject;
                if (localPlayer != null && localPlayer.activeInHierarchy && localPlayer.CompareTag("Player"))
                {
                    playerCanvas = FindLocalPlayerCanvas();
                    if (playerCanvas != null)
                    {
                        EnsureCanvasComponents(playerCanvas);
                        // Check if RespawnText exists and is properly parented
                        if (respawnText == null || respawnText.transform.parent != playerCanvas.transform)
                        {
                            CreateRespawnText(playerCanvas);
                        }
                        if (IsReady)
                        {
                            CustomLogger.Log($"RespawnUIManager: Initialized UI after {retryCount * checkInterval:F2} seconds at {GetGameObjectPath(playerCanvas.gameObject)}");
                            yield break;
                        }
                    }
                }
            }
            retryCount++;
            CustomLogger.Log($"RespawnUIManager: Waiting for Player Canvas, retry {retryCount}/{maxRetries}");
            yield return new WaitForSeconds(checkInterval);
        }

        CustomLogger.LogError("RespawnUIManager: Failed to initialize UI after 10 seconds. Ensure Player Canvas exists in Player prefab.");
        // Fallback: Create a new canvas if all else fails
        CreateFallbackCanvas();
    }      

    private void CreateFallbackCanvas()
    {
        GameObject canvasObj = new GameObject("Fallback Player Canvas");
        playerCanvas = canvasObj.AddComponent<Canvas>();
        playerCanvas.renderMode = RenderMode.ScreenSpaceOverlay;
        EnsureCanvasComponents(playerCanvas);
        CreateRespawnText(playerCanvas);
        CustomLogger.Log($"RespawnUIManager: Created fallback canvas at {GetGameObjectPath(canvasObj)}");
    }

    private Canvas FindLocalPlayerCanvas()
    {
        // Priority 1: Find via TagObject
        if (PhotonNetwork.LocalPlayer?.TagObject != null)
        {
            GameObject localPlayer = PhotonNetwork.LocalPlayer.TagObject as GameObject;
            if (localPlayer != null && localPlayer.activeInHierarchy && localPlayer.CompareTag("Player"))
            {
                PhotonView photonView = localPlayer.GetComponent<PhotonView>();
                if (photonView != null && photonView.IsMine)
                {
                    Canvas[] canvases = localPlayer.GetComponentsInChildren<Canvas>(true);
                    foreach (Canvas canvas in canvases)
                    {
                        if (canvas.gameObject.name == "Player Canvas" || canvas.gameObject.name.Contains("Canvas"))
                        {
                            if (!canvas.gameObject.activeInHierarchy)
                            {
                                canvas.gameObject.SetActive(true);
                                CustomLogger.Log($"RespawnUIManager: Reactivated inactive canvas at {GetGameObjectPath(canvas.gameObject)}");
                            }
                            CustomLogger.Log($"RespawnUIManager: Found canvas at {GetGameObjectPath(canvas.gameObject)} via TagObject");
                            return canvas;
                        }
                    }
                }
            }
        }

        // Priority 2: Search all players by tag
        foreach (GameObject player in GameObject.FindGameObjectsWithTag("Player"))
        {
            PhotonView photonView = player.GetComponent<PhotonView>();
            if (photonView != null && photonView.IsMine)
            {
                Canvas[] canvases = player.GetComponentsInChildren<Canvas>(true);
                foreach (Canvas canvas in canvases)
                {
                    if (canvas.gameObject.name == "Player Canvas" || canvas.gameObject.name.Contains("Canvas"))
                    {
                        if (!canvas.gameObject.activeInHierarchy)
                        {
                            canvas.gameObject.SetActive(true);
                            CustomLogger.Log($"RespawnUIManager: Reactivated inactive canvas at {GetGameObjectPath(canvas.gameObject)}");
                        }
                        CustomLogger.Log($"RespawnUIManager: Found canvas at {GetGameObjectPath(canvas.gameObject)} via tag search");
                        return canvas;
                    }
                }
            }
        }

        CustomLogger.LogError("RespawnUIManager: Could not find Player Canvas for local player.");
        return null;
    }

    private void EnsureCanvasComponents(Canvas canvas)
    {
        CanvasScaler scaler = canvas.GetComponent<CanvasScaler>();
        if (scaler == null)
        {
            scaler = canvas.gameObject.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);
            CustomLogger.Log($"RespawnUIManager: Added CanvasScaler to {GetGameObjectPath(canvas.gameObject)}.");
        }

        if (!canvas.GetComponent<GraphicRaycaster>())
        {
            canvas.gameObject.AddComponent<GraphicRaycaster>();
            CustomLogger.Log($"RespawnUIManager: Added GraphicRaycaster to {GetGameObjectPath(canvas.gameObject)}.");
        }
    }

    private string GetGameObjectPath(GameObject obj)
    {
        string path = obj.name;
        Transform current = obj.transform.parent;
        while (current != null)
        {
            path = current.name + "/" + path;
            current = current.parent;
        }
        return path;
    }

    private void CreateRespawnText(Canvas parentCanvas)
    {
        CustomLogger.Log("RespawnUIManager: Creating RespawnText.");
        GameObject textObj = new GameObject("RespawnText");
        textObj.transform.SetParent(parentCanvas.transform, false);

        respawnText = textObj.AddComponent<TextMeshProUGUI>();
        respawnText.fontSize = 48;
        respawnText.alignment = TextAlignmentOptions.Center;
        respawnText.rectTransform.sizeDelta = new Vector2(600, 100);
        respawnText.rectTransform.anchorMin = new Vector2(0.5f, 0.5f);
        respawnText.rectTransform.anchorMax = new Vector2(0.5f, 0.5f);
        respawnText.rectTransform.anchoredPosition = Vector2.zero;
        respawnText.rectTransform.localScale = Vector3.one; // Ensure scale is 1
        respawnText.text = "";
        respawnText.color = Color.white;
        respawnText.raycastTarget = true; // Ensure clickable
        respawnText.gameObject.SetActive(false);

        // Add EventTrigger for handling taps
        EventTrigger trigger = textObj.AddComponent<EventTrigger>();
        EventTrigger.Entry clickEntry = new EventTrigger.Entry
        {
            eventID = EventTriggerType.PointerClick
        };
        clickEntry.callback.AddListener((data) => { OnRespawnTextClick(); });
        trigger.triggers.Add(clickEntry);

        CustomLogger.Log($"RespawnUIManager: Created RespawnText at {GetGameObjectPath(textObj)}, parent={GetGameObjectPath(parentCanvas.gameObject)}, position={respawnText.rectTransform.anchoredPosition}, scale={respawnText.rectTransform.localScale}");
    }

    // New method to handle text tap
    private void OnRespawnTextClick()
    {
        if (isWaitingForRespawn && playerController != null)
        {
            CustomLogger.Log($"RespawnUIManager: RespawnText tapped, triggering respawn for {playerController.NickName}");
            isWaitingForRespawn = false;
        }
    }

    public IEnumerator StartRespawnCountdown(PlayerController player)
    {
        if (player == null || !IsReady)
        {
            CustomLogger.LogError($"RespawnUIManager: Cannot start countdown. Player={(player == null ? "null" : player.NickName)}, IsReady={IsReady}, Canvas={(playerCanvas != null ? GetGameObjectPath(playerCanvas.gameObject) : "null")}, RespawnText={(respawnText != null ? "present" : "null")}");
            yield break;
        }

        playerController = player;
        isWaitingForRespawn = false;
        if (!respawnText.gameObject.activeInHierarchy)
        {
            respawnText.gameObject.SetActive(true);
            CustomLogger.Log($"RespawnUIManager: Activated RespawnText for {player.NickName} at {GetGameObjectPath(respawnText.gameObject)}");
        }
        float countdown = 5f;
        bool isRed = true;

        CustomLogger.Log($"RespawnUIManager: Started countdown for {player.NickName}, ViewID={player.photonView.ViewID}");

        while (countdown > 0)
        {
            respawnText.text = Mathf.CeilToInt(countdown).ToString();
            respawnText.color = isRed ? Color.red : Color.white;
            isRed = !isRed;
            countdown -= Time.deltaTime;
            yield return null;
        }

        respawnText.text = "Tap Here/Press 'P' to Respawn";
        respawnText.color = Color.white;
        isWaitingForRespawn = true;
        CustomLogger.Log($"RespawnUIManager: Awaiting respawn input for {player.NickName}, RespawnText active={respawnText.gameObject.activeInHierarchy}");

        while (isWaitingForRespawn)
        {
            yield return null;
        }

        CustomLogger.Log($"RespawnUIManager: Respawn input received, performing respawn for {player.NickName}");
        yield return StartCoroutine(PerformRespawn(player));
    }

    void Update()
    {
        if (isWaitingForRespawn && Input.GetKeyDown(KeyCode.P))
        {
            if (playerController != null)
            {
                CustomLogger.Log($"RespawnUIManager: Player pressed P, signaling respawn for {playerController.NickName}");
                isWaitingForRespawn = false;
            }
        }
    }

    public void HandleRespawn(PlayerController player)
    {
        StartCoroutine(PerformRespawn(player));
    }

    private IEnumerator PerformRespawn(PlayerController player)
    {
        if (player == null || player.photonView == null)
        {
            CustomLogger.LogError("RespawnUIManager: PlayerController or PhotonView is null, cannot perform respawn.");
            yield break;
        }

        CustomLogger.Log($"RespawnUIManager: Respawn started for {player.NickName}, ViewID={player.photonView.ViewID}");

        Vector3 respawnPosition = Vector3.zero;
        GameObject spaceship = null;
        BoundaryManager boundaryManager = FindFirstObjectByType<BoundaryManager>();
        float boundarySize = boundaryManager != null ? boundaryManager.BoundarySize : 3000f;
        float safeBoundary = boundarySize / 2 - 50f;

        // Find spaceship by ownerId
        GameObject[] spaceships = GameObject.FindGameObjectsWithTag("SpaceShip");
        foreach (GameObject ship in spaceships)
        {
            SpaceshipMarker marker = ship.GetComponent<SpaceshipMarker>();
            if (marker != null && marker.ownerId == player.ActorNumber)
            {
                spaceship = ship;
                PhotonView shipView = ship.GetComponent<PhotonView>();
                if (shipView != null)
                {
                    int newViewID = shipView.ViewID;
                    PhotonNetwork.LocalPlayer.SetCustomProperties(new Hashtable { { "SpaceshipViewID", newViewID } });
                    CustomLogger.Log($"RespawnUIManager: Found spaceship via ownerId={player.ActorNumber}, Name={ship.name}, ViewID={newViewID}, Position={ship.transform.position}");
                }
                break;
            }
        }

        // Fallback to SpaceshipViewID
        if (spaceship == null && PhotonNetwork.LocalPlayer.CustomProperties.TryGetValue("SpaceshipViewID", out object viewID))
        {
            PhotonView spaceshipView = PhotonView.Find((int)viewID);
            if (spaceshipView != null && spaceshipView.gameObject != null && spaceshipView.gameObject.CompareTag("SpaceShip"))
            {
                spaceship = spaceshipView.gameObject;
                CustomLogger.Log($"RespawnUIManager: Found spaceship via SpaceshipViewID={viewID}, Name={spaceship.name}, Position={spaceship.transform.position}");
            }
            else
            {
                CustomLogger.LogWarning($"RespawnUIManager: SpaceshipViewID={viewID} invalid or not tagged 'SpaceShip'");
            }
        }

        // Calculate respawn position near spaceship
        if (spaceship != null)
        {
            Vector3 spaceshipPos = spaceship.transform.position;
            float spawnDistance = 15f;
            Vector2 offset = Random.insideUnitCircle.normalized * spawnDistance;
            respawnPosition = spaceshipPos + new Vector3(offset.x, offset.y, 0f);
            respawnPosition.x = Mathf.Clamp(respawnPosition.x, -safeBoundary, safeBoundary);
            respawnPosition.y = Mathf.Clamp(respawnPosition.y, -safeBoundary, safeBoundary);

            if (boundaryManager != null && !boundaryManager.IsValidPlayerPosition(respawnPosition, player.ActorNumber))
            {
                respawnPosition = spaceshipPos + new Vector3(spawnDistance, 0f, 0f); // Fallback to a fixed offset
                respawnPosition.x = Mathf.Clamp(respawnPosition.x, -safeBoundary, safeBoundary);
                respawnPosition.y = Mathf.Clamp(respawnPosition.y, -safeBoundary, safeBoundary);
                CustomLogger.LogWarning($"RespawnUIManager: Invalid respawn position adjusted to {respawnPosition} for {player.NickName}");
            }
            CustomLogger.Log($"RespawnUIManager: Set respawn position to {respawnPosition} near spaceship {spaceship.name}");
        }
        else
        {
            // Request spaceship spawn if none exists
            if (PhotonNetwork.IsMasterClient)
            {
                MatchTimerManager timerManager = FindFirstObjectByType<MatchTimerManager>();
                if (timerManager != null)
                {
                    timerManager.photonView.RPC("SpawnSpaceship", RpcTarget.All, player.ActorNumber);
                    CustomLogger.Log($"RespawnUIManager: Triggered spaceship spawn for {player.NickName}, ActorNumber={player.ActorNumber}");
                }
            }
            else
            {
                player.photonView.RPC("RequestSpaceshipSpawn", RpcTarget.MasterClient, player.ActorNumber);
                CustomLogger.Log($"RespawnUIManager: Requested spaceship spawn from MasterClient for {player.NickName}");
            }

            // Wait for spaceship to spawn
            float waitTime = 0f;
            const float maxWaitTime = 5f;
            while (waitTime < maxWaitTime && spaceship == null)
            {
                foreach (GameObject ship in GameObject.FindGameObjectsWithTag("SpaceShip"))
                {
                    SpaceshipMarker marker = ship.GetComponent<SpaceshipMarker>();
                    if (marker != null && marker.ownerId == player.ActorNumber)
                    {
                        spaceship = ship;
                        break;
                    }
                }
                waitTime += 0.5f;
                yield return new WaitForSeconds(0.5f);
            }

            if (spaceship != null)
            {
                Vector3 spaceshipPos = spaceship.transform.position;
                float spawnDistance = 15f;
                Vector2 offset = Random.insideUnitCircle.normalized * spawnDistance;
                respawnPosition = spaceshipPos + new Vector3(offset.x, offset.y, 0f);
                respawnPosition.x = Mathf.Clamp(respawnPosition.x, -safeBoundary, safeBoundary);
                respawnPosition.y = Mathf.Clamp(respawnPosition.y, -safeBoundary, safeBoundary);
                CustomLogger.Log($"RespawnUIManager: Set respawn position to {respawnPosition} near newly spawned spaceship {spaceship.name}");
            }
            else
            {
                respawnPosition = new Vector3(Random.Range(-safeBoundary, safeBoundary), Random.Range(-safeBoundary, safeBoundary), 0f);
                CustomLogger.LogError($"RespawnUIManager: No spaceship found for {player.NickName} after waiting, using random position {respawnPosition}");
            }
        }

        // Reset points
        player.LoadPoints();
        player.CustomProperties["Points"] = player.Points;
        PhotonNetwork.LocalPlayer.SetCustomProperties(new Hashtable { { "Points", player.Points } });
        CustomLogger.Log($"RespawnUIManager: Restored points for {player.NickName}, Points={player.Points}");

        // Reassign camera with retry
        if (Camera.main != null)
        {
            CameraFollow cameraFollow = Camera.main.GetComponent<CameraFollow>();
            if (cameraFollow != null)
            {
                cameraFollow.target = player.transform;
                cameraFollow.ForceRetargetPlayer(); // Force immediate retargeting
                CustomLogger.Log($"RespawnUIManager: Re-assigned CameraFollow.target to {player.gameObject.name} and called ForceRetargetPlayer for {player.NickName}");
            }
            else
            {
                CustomLogger.LogWarning($"RespawnUIManager: CameraFollow component not found on main camera for {player.NickName}, retrying");
                yield return StartCoroutine(RetryAssignCamera(player));
            }
        }
        else
        {
            CustomLogger.LogWarning($"RespawnUIManager: Camera.main not found for {player.NickName}, retrying");
            yield return StartCoroutine(RetryAssignCamera(player));
        }

        // Notify RandomPlanetGenerator with retry
        yield return StartCoroutine(NotifyRandomPlanetGeneratorWithRetry(player));

        // Update scoreboard
        ScoreboardManager scoreboardManager = FindFirstObjectByType<ScoreboardManager>();
        if (scoreboardManager != null)
        {
            scoreboardManager.UpdateScoreboard();
            CustomLogger.Log($"RespawnUIManager: Triggered scoreboard update after respawn for {player.NickName}, Points={player.Points}");
        }
        else
        {
            CustomLogger.LogWarning($"RespawnUIManager: ScoreboardManager not found for {player.NickName} during respawn");
        }

        // Reset fuel for local player
        if (player.photonView.IsMine)
        {
            PlayerFuel fuel = player.GetComponent<PlayerFuel>();
            if (fuel != null)
            {
                fuel.SetFuel(100f);
                CustomLogger.Log($"RespawnUIManager: Restored full fuel for {player.NickName}");
            }
            else
            {
                CustomLogger.LogWarning($"RespawnUIManager: PlayerFuel component missing for {player.NickName}");
            }
        }

        // Sync respawn state across network
        PlayerHealth health = player.GetComponent<PlayerHealth>();
        if (health != null && player.photonView != null)
        {
            CustomLogger.Log($"RespawnUIManager: Calling SyncRespawnState RPC for ViewID={player.photonView.ViewID}, position={respawnPosition}, health={health.GetCurrentHealth()}");
            player.photonView.RPC("SyncRespawnState", RpcTarget.AllBuffered, respawnPosition, health.GetCurrentHealth());
        }
        else
        {
            CustomLogger.LogError($"RespawnUIManager: PlayerHealth or PhotonView component missing for {player.NickName}, cannot sync respawn state");
        }

        OnRespawnComplete();
        yield return null;
    }

    private IEnumerator NotifyRandomPlanetGeneratorWithRetry(PlayerController player)
    {
        int maxRetries = 5;
        int retryCount = 0;
        while (retryCount < maxRetries)
        {
            RandomPlanetGenerator generator = RandomPlanetGenerator.Instance ?? FindFirstObjectByType<RandomPlanetGenerator>();
            if (generator != null)
            {
                generator.ReAddPlayer(player.ActorNumber, player.gameObject);
                CustomLogger.Log($"RespawnUIManager: Notified RandomPlanetGenerator to re-add ActorNumber={player.ActorNumber} after respawn");
                yield break;
            }
            retryCount++;
            CustomLogger.LogWarning($"RespawnUIManager: RandomPlanetGenerator not found for {player.NickName}, retry {retryCount}/{maxRetries}");
            yield return new WaitForSeconds(1f);
        }
        CustomLogger.LogError($"RespawnUIManager: Failed to find RandomPlanetGenerator for {player.NickName} after {maxRetries} retries");
    }

    private IEnumerator RetryAssignCamera(PlayerController player)
    {
        int maxRetries = 5;
        int retryCount = 0;
        while (retryCount < maxRetries)
        {
            if (Camera.main != null)
            {
                CameraFollow cameraFollow = Camera.main.GetComponent<CameraFollow>();
                if (cameraFollow != null)
                {
                    cameraFollow.target = player.transform;
                    CustomLogger.Log($"RespawnUIManager: Successfully re-assigned CameraFollow.target to {player.gameObject.name} for {player.NickName} on retry {retryCount + 1}");
                    yield break;
                }
            }
            retryCount++;
            CustomLogger.LogWarning($"Respawn player.NickName {player.NickName}, retry {retryCount}/{maxRetries}");
            yield return new WaitForSeconds(1f);
        }
        CustomLogger.LogError($"RespawnUIManager: Failed to assign camera for {player.NickName} after {maxRetries} retries");
    }

    public void OnRespawnComplete()
    {
        if (respawnText != null)
        {
            respawnText.text = "";
            respawnText.gameObject.SetActive(false);
            CustomLogger.Log("RespawnUIManager: Cleared and hid RespawnText.");
        }
        isWaitingForRespawn = false;
        playerController = null;
        // Force reinitialization to ensure UI is ready for next death
        if (!IsReady || playerCanvas == null || !playerCanvas.gameObject.activeInHierarchy)
        {
            CustomLogger.LogWarning("RespawnUIManager: UI not ready or canvas inactive after respawn, reinitializing.");
            StartCoroutine(InitializeUIWithRetry());
        }
        else
        {
            CustomLogger.Log("RespawnUIManager: UI ready for next respawn.");
        }
    }

    void OnDestroy()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded;
        if (instance == this)
        {
            instance = null;
            CustomLogger.Log("RespawnUIManager: Cleared singleton instance on destroy.");
        }
    }
}