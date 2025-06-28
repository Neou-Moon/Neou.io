using UnityEngine;
using UnityEngine.SceneManagement;
using Photon.Pun;
using Photon.Realtime;
using System.Collections;
using System.Collections.Generic;
using TMPro;
using System.Linq; // Added for Count()

public class LoadingSceneManager : MonoBehaviourPunCallbacks
{
    [SerializeField] private TextMeshProUGUI loadingText;
    private const float MAX_LOADING_TIME = 10f;
    private const float FLASH_INTERVAL = 0.5f;
    private const float PULSE_SPEED = 4f;
    private const float PULSE_SCALE = 0.2f;
    private const float CONNECTION_TIMEOUT = 15f;
    private const float ROOM_TIMEOUT = 10f;
    private Vector3 originalTextScale;
    private List<RoomInfo> availableRooms = new List<RoomInfo>();

    void Start()
    {
        if (loadingText == null)
        {
            Debug.LogError("LoadingSceneManager: LoadingText reference is null. Please assign in Inspector.");
            loadingText = Object.FindFirstObjectByType<TextMeshProUGUI>();
            if (loadingText == null)
            {
                Debug.LogError("LoadingSceneManager: No TextMeshProUGUI found in scene. Creating default text.");
                GameObject textObj = new GameObject("LoadingText");
                loadingText = textObj.AddComponent<TextMeshProUGUI>();
                loadingText.text = "Loading Next Match...";
                loadingText.fontSize = 36;
                loadingText.alignment = TextAlignmentOptions.Center;
                loadingText.rectTransform.position = new Vector3(Screen.width / 2, Screen.height / 2, 0);
            }
        }

        originalTextScale = loadingText.transform.localScale;
        StartCoroutine(AnimateLoadingText());

        Debug.Log($"LoadingSceneManager: Start, Photon State: {PhotonNetwork.NetworkClientState}");
        StartCoroutine(CleanupAndLoadMoonRan());
    }

    private IEnumerator AnimateLoadingText()
    {
        bool isRed = false;
        while (true)
        {
            loadingText.color = isRed ? Color.red : Color.white;
            isRed = !isRed;
            float t = (Mathf.Sin(Time.time * PULSE_SPEED) + 1) / 2;
            float scale = 1f + (t * PULSE_SCALE);
            loadingText.transform.localScale = originalTextScale * scale;
            yield return new WaitForSeconds(FLASH_INTERVAL);
        }
    }

    private IEnumerator CleanupAndLoadMoonRan()
    {
        float startTime = Time.time;

        // Step 1: Destroy all PhotonViews except self
        PhotonView[] photonViews = Object.FindObjectsByType<PhotonView>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        foreach (PhotonView view in photonViews)
        {
            if (view != photonView)
            {
                PhotonNetwork.Destroy(view.gameObject);
                Debug.Log($"LoadingSceneManager: Destroyed PhotonView on {view.gameObject.name}, ViewID={view.ViewID}");
            }
        }
        yield return new WaitForSeconds(1f);

        // Step 2: Ensure full disconnection
        yield return StartCoroutine(EnsureFullDisconnect());

        // Step 3: Connect to Master Server
        if (!PhotonNetwork.OfflineMode)
        {
            Debug.Log("LoadingSceneManager: Connecting to Photon...");
            PhotonNetwork.ConnectUsingSettings();
            float elapsed = 0f;
            while (PhotonNetwork.NetworkClientState != ClientState.ConnectedToMasterServer && elapsed < CONNECTION_TIMEOUT)
            {
                Debug.Log($"LoadingSceneManager: Waiting for Master Server, State: {PhotonNetwork.NetworkClientState}, Elapsed: {elapsed:F2}s");
                elapsed += Time.deltaTime;
                yield return null;
            }
            if (PhotonNetwork.NetworkClientState != ClientState.ConnectedToMasterServer)
            {
                Debug.LogWarning("LoadingSceneManager: Failed to connect to Master Server, switching to offline mode.");
                PhotonNetwork.OfflineMode = true;
            }
            else
            {
                Debug.Log($"LoadingSceneManager: Connected to Master Server, Photon State: {PhotonNetwork.NetworkClientState}");
            }
        }

        // Step 4: Join lobby if not offline
        if (!PhotonNetwork.OfflineMode)
        {
            yield return StartCoroutine(JoinLobbyWithStateCheck());
        }

        // Step 5: Manage Photon room with retries
        int maxRetries = 3;
        int retryCount = 0;
        bool joinedRoom = false;
        while (!joinedRoom && retryCount < maxRetries && !PhotonNetwork.OfflineMode)
        {
            retryCount++;
            Debug.Log($"LoadingSceneManager: Attempt {retryCount}/{maxRetries} to join or create room, Photon State: {PhotonNetwork.NetworkClientState}");

            if (availableRooms.Count > 0)
            {
                foreach (RoomInfo room in availableRooms)
                {
                    if (room.Name.StartsWith("MoonRan_") && room.PlayerCount > 0 && room.CustomProperties.ContainsKey("CreationTime"))
                    {
                        double creationTime = (double)room.CustomProperties["CreationTime"];
                        double roomAge = PhotonNetwork.Time - creationTime;
                        if (roomAge < 120)
                        {
                            if (PhotonNetwork.NetworkClientState == ClientState.JoinedLobby || PhotonNetwork.NetworkClientState == ClientState.ConnectedToMasterServer)
                            {
                                Debug.Log($"LoadingSceneManager: Joining existing room {room.Name}, Age={roomAge:F2}s, Photon State: {PhotonNetwork.NetworkClientState}");
                                PhotonNetwork.JoinRoom(room.Name);
                                float elapsed = 0f;
                                while (!PhotonNetwork.InRoom && elapsed < ROOM_TIMEOUT && !PhotonNetwork.OfflineMode)
                                {
                                    elapsed += Time.deltaTime;
                                    yield return null;
                                }
                                if (PhotonNetwork.InRoom)
                                {
                                    joinedRoom = true;
                                    break;
                                }
                                else
                                {
                                    Debug.LogWarning($"LoadingSceneManager: Failed to join room {room.Name}, State: {PhotonNetwork.NetworkClientState}");
                                }
                            }
                            else
                            {
                                Debug.LogWarning($"LoadingSceneManager: Cannot join room {room.Name}, client not ready, State: {PhotonNetwork.NetworkClientState}");
                            }
                        }
                    }
                }
            }

            if (!joinedRoom)
            {
                string roomName = "MoonRan_" + Random.Range(1000, 9999);
                RoomOptions roomOptions = new RoomOptions
                {
                    MaxPlayers = 20,
                    BroadcastPropsChangeToAll = true,
                    CustomRoomProperties = new ExitGames.Client.Photon.Hashtable { { "CreationTime", PhotonNetwork.Time } },
                    CustomRoomPropertiesForLobby = new string[] { "CreationTime" }
                };
                if (PhotonNetwork.NetworkClientState == ClientState.JoinedLobby || PhotonNetwork.NetworkClientState == ClientState.ConnectedToMasterServer)
                {
                    Debug.Log($"LoadingSceneManager: Creating new room {roomName}, Photon State: {PhotonNetwork.NetworkClientState}");
                    PhotonNetwork.CreateRoom(roomName, roomOptions);
                    float elapsed = 0f;
                    while (!PhotonNetwork.InRoom && elapsed < ROOM_TIMEOUT && !PhotonNetwork.OfflineMode)
                    {
                        elapsed += Time.deltaTime;
                        yield return null;
                    }
                    if (PhotonNetwork.InRoom)
                    {
                        joinedRoom = true;
                    }
                    else
                    {
                        Debug.LogWarning($"LoadingSceneManager: Failed to create room {roomName}, State: {PhotonNetwork.NetworkClientState}");
                    }
                }
                else
                {
                    Debug.LogWarning($"LoadingSceneManager: Cannot create room {roomName}, client not ready, State: {PhotonNetwork.NetworkClientState}");
                }
            }

            if (!joinedRoom && retryCount >= maxRetries)
            {
                Debug.LogWarning($"LoadingSceneManager: Failed to join or create room after {maxRetries} attempts, switching to offline mode.");
                PhotonNetwork.OfflineMode = true;
            }
        }

        // Step 6: Load Moon Ran
        Debug.Log("LoadingSceneManager: Attempting to load Moon Ran scene");
        AsyncOperation asyncLoad = SceneManager.LoadSceneAsync("Moon Ran");
        if (asyncLoad == null)
        {
            Debug.LogError("LoadingSceneManager: Failed to start loading Moon Ran scene. Is it in Build Settings?");
            yield break;
        }

        while (!asyncLoad.isDone && Time.time - startTime < MAX_LOADING_TIME)
        {
            Debug.Log($"LoadingSceneManager: Loading progress: {asyncLoad.progress * 100:F2}%, Time elapsed: {(Time.time - startTime):F2}s");
            yield return null;
        }

        if (!asyncLoad.isDone)
        {
            Debug.LogError($"LoadingSceneManager: Moon Ran scene load timed out after {MAX_LOADING_TIME}s, Progress: {asyncLoad.progress * 100:F2}%");
            yield break;
        }

        if (SceneManager.GetActiveScene().name != "Moon Ran")
        {
            Debug.LogError($"LoadingSceneManager: Expected Moon Ran, but active scene is {SceneManager.GetActiveScene().name}");
            yield break;
        }

        // Step 7: Wait for player instantiation and clear non-essential properties
        yield return StartCoroutine(WaitForPlayerAndClearProperties());

        // Step 8: Force camera retargeting
        yield return StartCoroutine(RetargetCamera());

        // Step 9: Trigger wave timer initialization
        yield return StartCoroutine(InitializeWaveManager());
    }

    private IEnumerator InitializeWaveManager()
    {
        float timeout = 10f;
        float elapsed = 0f;
        while (!PhotonNetwork.InRoom && !PhotonNetwork.OfflineMode && elapsed < timeout)
        {
            Debug.Log($"LoadingSceneManager: Waiting for PhotonNetwork.InRoom, State={PhotonNetwork.NetworkClientState}, Elapsed={elapsed:F2}s");
            elapsed += Time.deltaTime;
            yield return null;
        }

        if (PhotonNetwork.OfflineMode || PhotonNetwork.IsMasterClient)
        {
            WaveManager waveManager = Object.FindFirstObjectByType<WaveManager>();
            if (waveManager != null)
            {
                Debug.Log($"LoadingSceneManager: Found WaveManager, photonView.ID={waveManager.photonView.ViewID}, triggering InitializeWaveTimer, frame={Time.frameCount}");
                waveManager.photonView.RPC("InitializeWaveTimer", RpcTarget.AllBuffered);
                Debug.Log("LoadingSceneManager: Triggered WaveManager.InitializeWaveTimer");
            }
            else
            {
                Debug.LogError("LoadingSceneManager: WaveManager not found after loading Moon Ran. Ensure WaveManager GameObject exists in scene.");
            }
        }
        else
        {
            Debug.Log("LoadingSceneManager: Waiting for Master Client to initialize wave timer");
        }
    }

    private IEnumerator EnsureFullDisconnect()
    {
        // Leave room if in one
        if (PhotonNetwork.InRoom)
        {
            Debug.Log($"LoadingSceneManager: Leaving current room {PhotonNetwork.CurrentRoom?.Name}, Photon State: {PhotonNetwork.NetworkClientState}");
            PhotonNetwork.LeaveRoom();
            yield return new WaitUntil(() => !PhotonNetwork.InRoom || PhotonNetwork.NetworkClientState == ClientState.Disconnected);
            Debug.Log("LoadingSceneManager: Successfully left room");
        }

        // Leave lobby if in one
        if (PhotonNetwork.InLobby)
        {
            Debug.Log($"LoadingSceneManager: Leaving lobby, Photon State: {PhotonNetwork.NetworkClientState}");
            PhotonNetwork.LeaveLobby();
            yield return new WaitUntil(() => !PhotonNetwork.InLobby || PhotonNetwork.NetworkClientState == ClientState.Disconnected);
            Debug.Log("LoadingSceneManager: Successfully left lobby");
        }

        // Disconnect if still connected
        if (PhotonNetwork.IsConnected)
        {
            Debug.Log($"LoadingSceneManager: Disconnecting from Photon, Photon State: {PhotonNetwork.NetworkClientState}");
            PhotonNetwork.Disconnect();
            yield return new WaitUntil(() => !PhotonNetwork.IsConnected);
            Debug.Log("LoadingSceneManager: Successfully disconnected");
        }

        // Ensure offline mode is off
        if (PhotonNetwork.OfflineMode)
        {
            PhotonNetwork.OfflineMode = false;
            Debug.Log("LoadingSceneManager: Disabled offline mode");
        }

        // Verify disconnected state
        float timeout = 5f;
        float elapsed = 0f;
        while (PhotonNetwork.NetworkClientState != ClientState.Disconnected && elapsed < timeout)
        {
            Debug.Log($"LoadingSceneManager: Waiting for Disconnected state, Current State: {PhotonNetwork.NetworkClientState}, Elapsed: {elapsed:F2}s");
            elapsed += Time.deltaTime;
            yield return null;
        }

        if (PhotonNetwork.NetworkClientState != ClientState.Disconnected)
        {
            Debug.LogWarning($"LoadingSceneManager: Failed to reach Disconnected state after {timeout}s, proceeding anyway, State: {PhotonNetwork.NetworkClientState}");
        }
        else
        {
            Debug.Log($"LoadingSceneManager: Confirmed Disconnected state, State: {PhotonNetwork.NetworkClientState}");
        }
    }

    private IEnumerator WaitForPlayerAndClearProperties()
    {
        int maxRetries = 5; // Reduced retries to align with faster bot spawning
        int retries = 0;
        PlayerController playerController = null;

        while (retries < maxRetries)
        {
            if (PhotonNetwork.LocalPlayer != null && PhotonNetwork.LocalPlayer.CustomProperties.ContainsKey("PlayerViewID"))
            {
                PhotonView playerView = PhotonView.Find((int)PhotonNetwork.LocalPlayer.CustomProperties["PlayerViewID"]);
                if (playerView != null)
                {
                    playerController = playerView.GetComponent<PlayerController>();
                    if (playerController != null && playerController.GetType().GetField("areActionsReady", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)?.GetValue(playerController) as bool? == true)
                    {
                        if (PhotonNetwork.IsConnectedAndReady && PhotonNetwork.LocalPlayer != null)
                        {
                            PhotonNetwork.LocalPlayer.SetCustomProperties(new ExitGames.Client.Photon.Hashtable
                            {
                                { "SpaceshipViewID", null },
                                { "CompassViewID", null }
                            });
                            Debug.Log("LoadingSceneManager: Cleared non-essential player custom properties (SpaceshipViewID, CompassViewID)");
                        }

                        // Check if this is the first player in the room
                        if (PhotonNetwork.PlayerList.Count() == 1)
                        {
                            ScoreboardManager scoreboardManager = FindFirstObjectByType<ScoreboardManager>();
                            if (scoreboardManager != null && scoreboardManager.photonView != null)
                            {
                                scoreboardManager.photonView.RPC("DisplaySpawningBotsMessage", RpcTarget.All);
                                Debug.Log($"LoadingSceneManager: Triggered Spawning Bots message for first player {playerController.NickName}, ActorNumber={PhotonNetwork.LocalPlayer.ActorNumber}");
                            }
                            else
                            {
                                CustomLogger.LogWarning("LoadingSceneManager: ScoreboardManager not found or not networked, skipping Spawning Bots message for first player");
                            }
                        }

                        Debug.Log($"LoadingSceneManager: Player {playerController.NickName}, actions ready");
                        yield return null;
                    }
                }
            }

            retries++;
            Debug.Log($"LoadingSceneManager: Retry {retries}/{maxRetries} waiting for PlayerViewID and actions to be ready");
            yield return new WaitForSeconds(0.2f);
        }

        Debug.LogWarning("LoadingSceneManager: PlayerViewID or actions not ready after retries, clearing non-essential properties");
        if (PhotonNetwork.IsConnectedAndReady && PhotonNetwork.LocalPlayer != null)
        {
            PhotonNetwork.LocalPlayer.SetCustomProperties(new ExitGames.Client.Photon.Hashtable
            {
                { "SpaceshipViewID", null },
                { "CompassViewID", null }
            });
            Debug.Log("LoadingSceneManager: Cleared non-essential player custom properties after timeout");
        }
    }

    private IEnumerator RetargetCamera()
    {
        yield return new WaitForSeconds(1f);
        CameraFollow cameraFollow = Camera.main?.GetComponent<CameraFollow>();
        if (cameraFollow != null)
        {
            cameraFollow.ForceRetargetPlayer();
            Debug.Log("LoadingSceneManager: Called CameraFollow.ForceRetargetPlayer after Moon Ran load");
        }
        else
        {
            Debug.LogError("LoadingSceneManager: CameraFollow not found after loading Moon Ran");
        }
    }

    private IEnumerator JoinLobbyWithStateCheck()
    {
        float timeout = 10f;
        float elapsed = 0f;
        while (PhotonNetwork.NetworkClientState != ClientState.ConnectedToMasterServer && elapsed < timeout)
        {
            Debug.Log($"LoadingSceneManager: Waiting for stable Master Server connection, State: {PhotonNetwork.NetworkClientState}, Elapsed: {elapsed:F2}s");
            elapsed += Time.deltaTime;
            yield return null;
        }
        if (PhotonNetwork.NetworkClientState == ClientState.ConnectedToMasterServer)
        {
            Debug.Log("LoadingSceneManager: Joining lobby...");
            PhotonNetwork.JoinLobby();
            elapsed = 0f;
            while (PhotonNetwork.NetworkClientState != ClientState.JoinedLobby && elapsed < timeout)
            {
                Debug.Log($"LoadingSceneManager: Waiting for JoinedLobby, State: {PhotonNetwork.NetworkClientState}, Elapsed: {elapsed:F2}s");
                elapsed += Time.deltaTime;
                yield return null;
            }
            if (PhotonNetwork.NetworkClientState == ClientState.JoinedLobby)
            {
                Debug.Log("LoadingSceneManager: Successfully joined lobby");
            }
            else
            {
                Debug.LogWarning($"LoadingSceneManager: Failed to join lobby after {timeout}s, State: {PhotonNetwork.NetworkClientState}");
                PhotonNetwork.OfflineMode = true;
            }
        }
        else
        {
            Debug.LogWarning($"LoadingSceneManager: Failed to reach Master Server after {timeout}s, switching to offline mode");
            PhotonNetwork.OfflineMode = true;
        }
    }

    public override void OnConnectedToMaster()
    {
        base.OnConnectedToMaster();
        Debug.Log($"LoadingSceneManager: Connected to Master Server, Photon State: {PhotonNetwork.NetworkClientState}");
    }

    public override void OnJoinedLobby()
    {
        base.OnJoinedLobby();
        Debug.Log($"LoadingSceneManager: Joined lobby, Photon State: {PhotonNetwork.NetworkClientState}");
    }

    public override void OnRoomListUpdate(List<RoomInfo> roomList)
    {
        base.OnRoomListUpdate(roomList);
        availableRooms = roomList;
        Debug.Log($"LoadingSceneManager: Received room list update with {roomList.Count} rooms");
    }

    public override void OnJoinedRoom()
    {
        base.OnJoinedRoom();
        Debug.Log($"LoadingSceneManager: Joined room {PhotonNetwork.CurrentRoom.Name}, Photon State: {PhotonNetwork.NetworkClientState}");
    }

    public override void OnCreatedRoom()
    {
        base.OnCreatedRoom();
        Debug.Log($"LoadingSceneManager: Created room {PhotonNetwork.CurrentRoom.Name}, Photon State: {PhotonNetwork.NetworkClientState}");
    }

    public override void OnJoinRoomFailed(short returnCode, string message)
    {
        base.OnJoinRoomFailed(returnCode, message);
        Debug.LogWarning($"LoadingSceneManager: Failed to join room: {message}. Creating new room, Photon State: {PhotonNetwork.NetworkClientState}");
        string roomName = "MoonRan_" + Random.Range(1000, 9999);
        RoomOptions roomOptions = new RoomOptions
        {
            MaxPlayers = 20,
            BroadcastPropsChangeToAll = true,
            CustomRoomProperties = new ExitGames.Client.Photon.Hashtable { { "CreationTime", PhotonNetwork.Time } },
            CustomRoomPropertiesForLobby = new string[] { "CreationTime" }
        };
        if (PhotonNetwork.NetworkClientState == ClientState.JoinedLobby || PhotonNetwork.NetworkClientState == ClientState.ConnectedToMasterServer)
        {
            Debug.Log($"LoadingSceneManager: Creating new room {roomName}, Photon State: {PhotonNetwork.NetworkClientState}");
            PhotonNetwork.CreateRoom(roomName, roomOptions);
        }
        else
        {
            Debug.LogWarning($"LoadingSceneManager: Cannot create room {roomName}, client not ready, State: {PhotonNetwork.NetworkClientState}, switching to offline mode");
            PhotonNetwork.OfflineMode = true;
        }
    }

    public override void OnDisconnected(DisconnectCause cause)
    {
        base.OnDisconnected(cause);
        Debug.Log($"LoadingSceneManager: Disconnected from Photon, cause: {cause}, State: {PhotonNetwork.NetworkClientState}");
    }
}