using UnityEngine;
using Photon.Pun;
using System.Collections;
using UnityEngine.SceneManagement;
using ExitGames.Client.Photon;

[RequireComponent(typeof(Camera))]
public class CameraFollow : MonoBehaviourPunCallbacks
{
    [Header("Camera Follow Settings")]
    public Transform target;
    public float smoothing = 5f;
    public Vector3 offset = new Vector3(0, 0, -40);

    private Camera cam;
    private Vector3 fallbackPosition = new Vector3(0, 0, -40);
    private string currentScene;
    private bool isInitialized = false;
    private const int maxSyncRetries = 5;
    private const string targetScene = "Moon Ran";
    private const float retryDelay = 1f;
    private bool isSyncingOrthoSize = false;

    void Awake()
    {
        cam = GetComponent<Camera>();
        if (cam == null)
        {
            CustomLogger.LogError($"CameraFollow: No Camera component found on {gameObject.name}, disabling.");
            enabled = false;
            return;
        }

        if (photonView == null)
        {
            CustomLogger.LogError($"CameraFollow: No PhotonView component found on {gameObject.name}, disabling.");
            enabled = false;
            return;
        }

        DontDestroyOnLoad(gameObject);
        CustomLogger.Log($"CameraFollow: Set {gameObject.name} as DontDestroyOnLoad, ViewID={photonView.ViewID}");

        cam.orthographic = true;
        CustomLogger.Log($"CameraFollow: Camera set to orthographic, photonView=ViewID={photonView.ViewID}");
    }

    void Start()
    {
        currentScene = SceneManager.GetActiveScene().name;
        CustomLogger.Log($"CameraFollow: Started in scene {currentScene}");

        InitializeCameraForScene();
        if (currentScene == targetScene)
        {
            StartCoroutine(WaitForPhoton());
        }
        else
        {
            isInitialized = true;
            CustomLogger.Log($"CameraFollow: Non-{targetScene} scene, initialization complete.");
        }
    }

    private void InitializeCameraForScene()
    {
        currentScene = SceneManager.GetActiveScene().name;
        if (currentScene == "Moon Ran" || currentScene == "TeamMoonRan")
        {
            cam.orthographicSize = 50f;
            CustomLogger.Log($"CameraFollow: {currentScene} - Orthographic Size set to 50");
            if (target == null)
            {
                transform.position = fallbackPosition;
                CustomLogger.Log($"CameraFollow: {currentScene} - No target, using fallback {fallbackPosition}");
                StartCoroutine(FindPlayerTargetWithRetry());
            }
        }
        else if (currentScene == "InsideSpaceShip")
        {
            cam.orthographicSize = 20f;
            GameObject spaceship = GameObject.FindWithTag("SpaceShip");
            if (spaceship != null)
            {
                target = spaceship.transform;
                CustomLogger.Log($"CameraFollow: InsideSpaceShip - Set target to {spaceship.name}");
            }
            else
            {
                transform.position = fallbackPosition;
                CustomLogger.LogWarning($"CameraFollow: InsideSpaceShip - No SpaceShip found, using fallback {fallbackPosition}");
            }
        }
        else if (currentScene == "StartScreen")
        {
            cam.orthographicSize = 20f;
            GameObject ui = GameObject.FindWithTag("UI");
            if (ui != null)
            {
                target = ui.transform;
                CustomLogger.Log($"CameraFollow: StartScreen - Set target to {ui.name}");
            }
            else
            {
                transform.position = fallbackPosition;
                CustomLogger.LogWarning($"CameraFollow: StartScreen - No UI found, using fallback {fallbackPosition}");
            }
        }
        else
        {
            cam.orthographicSize = 20f;
            transform.position = fallbackPosition;
            CustomLogger.Log($"CameraFollow: Unknown scene {currentScene}, using default orthographicSize=20 and fallback {fallbackPosition}");
        }
    }

    void LateUpdate()
    {
        if (!isInitialized)
        {
            CustomLogger.Log($"CameraFollow: LateUpdate skipped, not initialized.");
            return;
        }

        if (target == null)
        {
            transform.position = Vector3.Lerp(transform.position, fallbackPosition, smoothing * Time.deltaTime);
            StartCoroutine(FindPlayerTargetWithRetry());
            return;
        }

        Vector3 targetPosition = new Vector3(
            target.position.x + offset.x,
            target.position.y + offset.y,
            offset.z
        );

        transform.position = Vector3.Lerp(transform.position, targetPosition, smoothing * Time.deltaTime);

        if ((currentScene == "Moon Ran" || currentScene == "TeamMoonRan") && cam != null)
        {
            if (!Mathf.Approximately(cam.orthographicSize, 50f))
            {
                CustomLogger.LogWarning($"CameraFollow: Orthographic Size drifted to {cam.orthographicSize} in {currentScene}, resetting to 50");
                cam.orthographicSize = 50f;
                if (photonView != null && photonView.IsMine && !isSyncingOrthoSize)
                {
                    StartCoroutine(SyncOrthoSizeWithDelay(50f));
                }
            }
        }
    }

    private IEnumerator WaitForPhoton()
    {
        int photonRetries = 0;
        const int maxPhotonRetries = 10;
        while (!PhotonNetwork.IsConnectedAndReady || !PhotonNetwork.InRoom)
        {
            CustomLogger.Log($"CameraFollow: Waiting for Photon, IsConnectedAndReady={PhotonNetwork.IsConnectedAndReady}, InRoom={PhotonNetwork.InRoom}, ViewID={photonView.ViewID}, IsMine={photonView.IsMine}, Retry={photonRetries + 1}/{maxPhotonRetries}");
            yield return new WaitForSeconds(1f);
            photonRetries++;
            if (photonRetries >= maxPhotonRetries)
            {
                CustomLogger.LogWarning($"CameraFollow: Failed to connect to Photon after {maxPhotonRetries} retries. Using local size=50.");
                cam.orthographicSize = 50f;
                isInitialized = true;
                yield break;
            }
        }

        currentScene = SceneManager.GetActiveScene().name;
        if (currentScene != targetScene && currentScene != "TeamMoonRan")
        {
            CustomLogger.LogWarning($"CameraFollow: Scene changed to {currentScene}, skipping sync.");
            isInitialized = true;
            yield break;
        }

        if (photonView != null && photonView.IsMine)
        {
            cam.orthographicSize = 50f;
            StartCoroutine(SyncOrthoSizeWithDelay(50f));
            StartCoroutine(FindPlayerTargetWithRetry());
            CustomLogger.Log($"CameraFollow: Photon ready, initiating Orthographic Size sync and player targeting for {currentScene}");
        }
        else
        {
            CustomLogger.Log($"CameraFollow: Waiting for OrthoSize RPC, photonView.IsMine={photonView.IsMine}");
        }

        isInitialized = true;
    }

    private IEnumerator SyncOrthoSizeWithDelay(float size)
    {
        if (isSyncingOrthoSize)
        {
            CustomLogger.Log($"CameraFollow: SyncOrthoSize already in progress, skipping.");
            yield break;
        }

        isSyncingOrthoSize = true;
        yield return new WaitForSeconds(0.5f);

        if (photonView != null && photonView.IsMine && PhotonNetwork.IsConnectedAndReady)
        {
            photonView.RPC("SyncOrthoSize", RpcTarget.AllBuffered, size);
            CustomLogger.Log($"CameraFollow: Sent SyncOrthoSize RPC with size={size} for {targetScene}");
        }
        else
        {
            CustomLogger.LogWarning($"CameraFollow: Failed to send SyncOrthoSize RPC, photonView.IsMine={photonView.IsMine}, IsConnectedAndReady={PhotonNetwork.IsConnectedAndReady}");
        }

        isSyncingOrthoSize = false;
    }

    [PunRPC]
    void SyncOrthoSize(float size)
    {
        if (SceneManager.GetActiveScene().name != targetScene)
        {
            CustomLogger.Log($"CameraFollow: Ignored SyncOrthoSize RPC, current scene={SceneManager.GetActiveScene().name}");
            return;
        }

        if (cam == null)
        {
            CustomLogger.LogError($"CameraFollow: Camera component missing during SyncOrthoSize");
            return;
        }

        if (Mathf.Approximately(size, 50f))
        {
            cam.orthographicSize = 50f;
            CustomLogger.Log($"CameraFollow: Synced Orthographic Size to 50 for {targetScene}");
        }
        else
        {
            CustomLogger.LogWarning($"CameraFollow: Rejected invalid Orthographic Size {size} in {targetScene}, setting to 50");
            cam.orthographicSize = 50f;
        }
    }

    private IEnumerator FindPlayerTargetWithRetry()
    {
        if (!photonView.IsMine)
        {
            CustomLogger.Log($"CameraFollow: Skipping FindPlayerTargetWithRetry, photonView.IsMine={photonView.IsMine}");
            yield break;
        }

        int maxRetries = 50;
        int retries = 0;
        float retryDelay = 1.5f;
        while (retries < maxRetries)
        {
            if (PhotonNetwork.IsConnectedAndReady && PhotonNetwork.LocalPlayer != null)
            {
                if (PhotonNetwork.LocalPlayer.TagObject != null)
                {
                    GameObject playerObj = PhotonNetwork.LocalPlayer.TagObject as GameObject;
                    if (playerObj != null && playerObj.CompareTag("Player"))
                    {
                        PhotonView pv = playerObj.GetComponent<PhotonView>();
                        if (pv != null && pv.IsMine)
                        {
                            target = pv.transform;
                            PhotonNetwork.LocalPlayer.SetCustomProperties(new ExitGames.Client.Photon.Hashtable { { "PlayerViewID", pv.ViewID } });
                            CustomLogger.Log($"CameraFollow: Target set to {playerObj.name} via TagObject, ViewID={pv.ViewID}, Position={pv.transform.position}");
                            if (photonView.IsMine && (currentScene == "Moon Ran" || currentScene == "TeamMoonRan") && cam != null)
                            {
                                cam.orthographicSize = 50f;
                                StartCoroutine(SyncOrthoSizeWithDelay(50f));
                            }
                            yield break;
                        }
                    }
                }

                if (PhotonNetwork.LocalPlayer.CustomProperties.TryGetValue("PlayerViewID", out object viewID))
                {
                    PhotonView playerView = PhotonView.Find((int)viewID);
                    if (playerView != null && playerView.gameObject != null && playerView.IsMine && playerView.GetComponent<PlayerController>() != null)
                    {
                        target = playerView.transform;
                        CustomLogger.Log($"CameraFollow: Target set to {playerView.gameObject.name} (Player) at {playerView.transform.position}, ViewID={viewID}");
                        if (photonView.IsMine && (currentScene == "Moon Ran" || currentScene == "TeamMoonRan") && cam != null)
                        {
                            cam.orthographicSize = 50f;
                            StartCoroutine(SyncOrthoSizeWithDelay(50f));
                        }
                        yield break;
                    }
                    else
                    {
                        CustomLogger.LogWarning($"CameraFollow: Invalid PlayerViewID={viewID}, clearing property.");
                        PhotonNetwork.LocalPlayer.SetCustomProperties(new ExitGames.Client.Photon.Hashtable { { "PlayerViewID", null } });
                    }
                }

                foreach (PhotonView pv in FindObjectsByType<PhotonView>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                {
                    if (pv.IsMine)
                    {
                        PlayerController player = pv.GetComponent<PlayerController>();
                        if (player != null)
                        {
                            target = pv.transform;
                            PhotonNetwork.LocalPlayer.SetCustomProperties(new ExitGames.Client.Photon.Hashtable { { "PlayerViewID", pv.ViewID } });
                            CustomLogger.Log($"CameraFollow: Found local Player {pv.gameObject.name}, ViewID={pv.ViewID}, Position={pv.transform.position}");
                            if (photonView.IsMine && (currentScene == "Moon Ran" || currentScene == "TeamMoonRan") && cam != null)
                            {
                                cam.orthographicSize = 50f;
                                StartCoroutine(SyncOrthoSizeWithDelay(50f));
                            }
                            yield break;
                        }
                    }
                }

                GameObject playerByTag = GameObject.FindGameObjectWithTag("Player");
                if (playerByTag != null)
                {
                    PhotonView pv = playerByTag.GetComponent<PhotonView>();
                    if (pv != null && pv.IsMine && pv.GetComponent<PlayerController>() != null)
                    {
                        target = pv.transform;
                        PhotonNetwork.LocalPlayer.SetCustomProperties(new ExitGames.Client.Photon.Hashtable { { "PlayerViewID", pv.ViewID } });
                        CustomLogger.Log($"CameraFollow: Found Player by tag {playerByTag.name}, ViewID={pv.ViewID}, Position={pv.transform.position}");
                        if (photonView.IsMine && (currentScene == "Moon Ran" || currentScene == "TeamMoonRan") && cam != null)
                        {
                            cam.orthographicSize = 50f;
                            StartCoroutine(SyncOrthoSizeWithDelay(50f));
                        }
                        yield break;
                    }
                }

                PlayerController[] players = FindObjectsByType<PlayerController>(FindObjectsInactive.Include, FindObjectsSortMode.None);
                foreach (PlayerController player in players)
                {
                    if (player.photonView != null && player.IsLocal)
                    {
                        target = player.transform;
                        PhotonNetwork.LocalPlayer.SetCustomProperties(new ExitGames.Client.Photon.Hashtable { { "PlayerViewID", player.photonView.ViewID } });
                        CustomLogger.Log($"CameraFollow: Found local PlayerController {player.NickName}, ViewID={player.photonView.ViewID}, Position={player.transform.position}");
                        if (photonView.IsMine && (currentScene == "Moon Ran" || currentScene == "TeamMoonRan") && cam != null)
                        {
                            cam.orthographicSize = 50f;
                            StartCoroutine(SyncOrthoSizeWithDelay(50f));
                        }
                        yield break;
                    }
                }
                CustomLogger.Log($"CameraFollow: No local PlayerController found, checked {players.Length} objects.");
            }

            retries++;
            CustomLogger.Log($"CameraFollow: Retry {retries}/{maxRetries} to find player target in {currentScene}");
            yield return new WaitForSeconds(retryDelay);
        }
        CustomLogger.LogError($"CameraFollow: Failed to find player target after {maxRetries} retries in {currentScene}, using fallback position {fallbackPosition}.");
        transform.position = fallbackPosition;
    }

    public void ForceRetargetPlayer()
    {
        if (currentScene != targetScene && currentScene != "TeamMoonRan")
        {
            CustomLogger.Log($"CameraFollow: ForceRetargetPlayer ignored, current scene={currentScene}.");
            return;
        }

        target = null;
        CustomLogger.Log("CameraFollow: Cleared camera target for forced retarget.");
        StartCoroutine(FindPlayerTargetWithRetry());
    }

    public void ResetCameraTarget()
    {
        if (target != null && target.GetComponent<PhotonView>() != null && target.GetComponent<PhotonView>().IsMine)
        {
            CustomLogger.Log($"CameraFollow: ResetCameraTarget skipped, valid target {target.name} already set, ViewID={target.GetComponent<PhotonView>().ViewID}");
            return;
        }

        isInitialized = false;
        isSyncingOrthoSize = false;
        CustomLogger.Log("CameraFollow: Reset camera initialization state.");

        if (currentScene == targetScene || currentScene == "TeamMoonRan")
        {
            cam.orthographicSize = 50f;
            transform.position = fallbackPosition;
            StartCoroutine(DelayedFindPlayerTarget(2f));
            StartCoroutine(WaitForPhoton());
            CustomLogger.Log($"CameraFollow: Reset for {currentScene}, set orthographicSize=50, position={fallbackPosition}, starting delayed retarget");
        }
        else
        {
            InitializeCameraForScene();
            isInitialized = true;
            CustomLogger.Log($"CameraFollow: Reset for non-{targetScene} scene, reinitialized");
        }
    }

    public override void OnJoinedRoom()
    {
        base.OnJoinedRoom();
        if (!isInitialized && currentScene == targetScene && photonView != null && photonView.IsMine)
        {
            CustomLogger.Log($"CameraFollow: Joined room, initiating Orthographic Size sync for {targetScene}");
            StartCoroutine(SyncOrthoSizeWithDelay(50f));
            isInitialized = true;
        }
    }

    public override void OnDisconnected(Photon.Realtime.DisconnectCause cause)
    {
        base.OnDisconnected(cause);
        CustomLogger.LogWarning($"CameraFollow: Disconnected from Photon (cause={cause}), resetting initialization.");
        isInitialized = false;
        isSyncingOrthoSize = false;
        target = null;
        StartCoroutine(FindPlayerTargetWithRetry());
    }

    public override void OnPlayerLeftRoom(Photon.Realtime.Player otherPlayer)
    {
        base.OnPlayerLeftRoom(otherPlayer);
        if (target != null)
        {
            PhotonView targetView = target.GetComponent<PhotonView>();
            if (targetView != null && targetView.Owner != null && targetView.Owner.ActorNumber == otherPlayer.ActorNumber)
            {
                CustomLogger.Log($"CameraFollow: Target player {otherPlayer.NickName} left, clearing target.");
                target = null;
                StartCoroutine(FindPlayerTargetWithRetry());
            }
        }
    }

    public override void OnEnable()
    {
        base.OnEnable();
        SceneManager.sceneLoaded += OnSceneLoaded;
        CustomLogger.Log($"CameraFollow: Subscribed to sceneLoaded event.");
    }

    public override void OnDisable()
    {
        base.OnDisable();
        SceneManager.sceneLoaded -= OnSceneLoaded;
        CustomLogger.Log($"CameraFollow: Unsubscribed from sceneLoaded event.");
    }

    private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        currentScene = scene.name;
        CustomLogger.Log($"CameraFollow: Scene loaded: {currentScene}, mode={mode}");
        isInitialized = false;
        isSyncingOrthoSize = false;
        InitializeCameraForScene();
        if (currentScene == "Moon Ran" || currentScene == "TeamMoonRan")
        {
            if (photonView.IsMine)
            {
                StartCoroutine(WaitForPhoton());
                StartCoroutine(DelayedFindPlayerTarget(2f));
                CustomLogger.Log($"CameraFollow: Scene {currentScene} loaded, triggered delayed retargeting for {currentScene}");
            }
            else
            {
                CustomLogger.Log($"CameraFollow: Scene {currentScene} loaded, skipping retargeting as photonView.IsMine={photonView.IsMine}");
            }
        }
        else
        {
            isInitialized = true;
            CustomLogger.Log($"CameraFollow: Scene {currentScene} loaded, initialization complete for non-target scene");
        }
    }

    private IEnumerator DelayedFindPlayerTarget(float delay)
    {
        yield return new WaitForSeconds(delay);
        if (photonView.IsMine && target == null)
        {
            CustomLogger.Log($"CameraFollow: Starting delayed FindPlayerTargetWithRetry for {currentScene}");
            StartCoroutine(FindPlayerTargetWithRetry());
        }
    }
}