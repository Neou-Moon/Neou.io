using UnityEngine;
using UnityEngine.SceneManagement;
using Photon.Pun;
using Photon.Realtime;
using System.Collections;
using System.Collections.Generic;
using TMPro;
using System.Linq;

public class TeamLoadingSceneManager : MonoBehaviourPunCallbacks
{
    [SerializeField] private TextMeshProUGUI loadingText;
    [SerializeField] private TextMeshProUGUI teamText;
    private const float MAX_LOADING_TIME = 10f;
    private const float FLASH_INTERVAL = 0.5f;
    private const float PULSE_SPEED = 4f;
    private const float PULSE_SCALE = 0.2f;
    private const float CONNECTION_TIMEOUT = 15f;
    private const float ROOM_TIMEOUT = 10f;
    private const float DOT_ANIMATION_INTERVAL = 0.3f;
    private Vector3 originalTextScale;
    private List<RoomInfo> availableRooms = new List<RoomInfo>();

    void Start()
    {
        // Initialize loadingText
        if (loadingText == null)
        {
            Debug.LogError("TeamLoadingSceneManager: LoadingText reference is null. Please assign in Inspector.");
            loadingText = Object.FindFirstObjectByType<TextMeshProUGUI>();
            if (loadingText == null)
            {
                Debug.LogError("TeamLoadingSceneManager: No TextMeshProUGUI found in scene for loadingText. Creating default text.");
                GameObject textObj = new GameObject("LoadingText");
                loadingText = textObj.AddComponent<TextMeshProUGUI>();
                loadingText.text = "Loading Match";
                loadingText.fontSize = 36;
                loadingText.alignment = TextAlignmentOptions.Center;
                loadingText.rectTransform.position = new Vector3(Screen.width / 2, Screen.height / 2, 0);
            }
        }

        // Initialize teamText
        if (teamText == null)
        {
            Debug.LogError("TeamLoadingSceneManager: TeamText reference is null. Please assign in Inspector.");
            GameObject textObj = new GameObject("TeamText");
            teamText = textObj.AddComponent<TextMeshProUGUI>();
            teamText.text = "Assigning team...";
            teamText.fontSize = 28;
            teamText.alignment = TextAlignmentOptions.Center;
            teamText.rectTransform.position = new Vector3(Screen.width / 2, Screen.height / 2 - 50, 0);
        }

        originalTextScale = loadingText.transform.localScale;
        StartCoroutine(AnimateLoadingText());
        StartCoroutine(AnimateTeamTextDots());

        Debug.Log($"TeamLoadingSceneManager: Start, Photon State: {PhotonNetwork.NetworkClientState}");
        StartCoroutine(CleanupAndLoadTeamMoonRan());
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

    private IEnumerator AnimateTeamTextDots()
    {
        string baseText = "Assigning team";
        while (true)
        {
            teamText.text = baseText;
            yield return new WaitForSeconds(DOT_ANIMATION_INTERVAL);
            teamText.text = baseText + ".";
            yield return new WaitForSeconds(DOT_ANIMATION_INTERVAL);
            teamText.text = baseText + "..";
            yield return new WaitForSeconds(DOT_ANIMATION_INTERVAL);
            teamText.text = baseText + "...";
            yield return new WaitForSeconds(DOT_ANIMATION_INTERVAL);
        }
    }

    private IEnumerator CleanupAndLoadTeamMoonRan()
    {
        float startTime = Time.time;

        // Step 1: Destroy all PhotonViews except self
        PhotonView[] photonViews = Object.FindObjectsByType<PhotonView>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        foreach (PhotonView view in photonViews)
        {
            if (view != photonView)
            {
                PhotonNetwork.Destroy(view.gameObject);
                Debug.Log($"TeamLoadingSceneManager: Destroyed PhotonView on {view.gameObject.name}, ViewID={view.ViewID}");
            }
        }
        yield return new WaitForSeconds(1f);

        // Step 2: Ensure full disconnection
        yield return StartCoroutine(EnsureFullDisconnect());

        // Step 3: Connect to Master Server
        if (!PhotonNetwork.OfflineMode)
        {
            Debug.Log("TeamLoadingSceneManager: Connecting to Photon...");
            PhotonNetwork.ConnectUsingSettings();
            float elapsed = 0f;
            while (PhotonNetwork.NetworkClientState != ClientState.ConnectedToMasterServer && elapsed < CONNECTION_TIMEOUT)
            {
                Debug.Log($"TeamLoadingSceneManager: Waiting for Master Server, State: {PhotonNetwork.NetworkClientState}, Elapsed: {elapsed:F2}s");
                elapsed += Time.deltaTime;
                yield return null;
            }
            if (PhotonNetwork.NetworkClientState != ClientState.ConnectedToMasterServer)
            {
                Debug.LogWarning("TeamLoadingSceneManager: Failed to connect to Master Server, switching to offline mode.");
                PhotonNetwork.OfflineMode = true;
            }
            else
                Debug.Log($"TeamLoadingSceneManager: Connected to Master Server, Photon State: {PhotonNetwork.NetworkClientState}");
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
        string partyId = PlayerPrefs.GetString("PartyID", "");
        bool isPartyLeader = PlayerPrefs.GetInt("IsPartyLeader", 0) == 1;

        while (!joinedRoom && retryCount < maxRetries && !PhotonNetwork.OfflineMode)
        {
            retryCount++;
            Debug.Log($"TeamLoadingSceneManager: Attempt {retryCount}/{maxRetries} to join or create room, Photon State: {PhotonNetwork.NetworkClientState}, PartyID: {partyId}");

            if (!string.IsNullOrEmpty(partyId) && availableRooms.Count > 0)
            {
                foreach (RoomInfo room in availableRooms)
                {
                    if (room.Name.StartsWith("TeamMoonRan_") && room.PlayerCount > 0 && room.CustomProperties.ContainsKey("CreationTime") &&
                        room.CustomProperties.ContainsKey("PartyID") && room.CustomProperties["PartyID"].ToString() == partyId)
                    {
                        double creationTime = (double)room.CustomProperties["CreationTime"];
                        double roomAge = PhotonNetwork.Time - creationTime;
                        if (roomAge < 120 && room.PlayerCount < 10)
                        {
                            if (PhotonNetwork.NetworkClientState == ClientState.JoinedLobby || PhotonNetwork.NetworkClientState == ClientState.ConnectedToMasterServer)
                            {
                                Debug.Log($"TeamLoadingSceneManager: Joining party room {room.Name}, Age={roomAge:F2}s, PartyID={partyId}, Photon State: {PhotonNetwork.NetworkClientState}");
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
                                    Debug.LogWarning($"TeamLoadingSceneManager: Failed to join room {room.Name}, State: {PhotonNetwork.NetworkClientState}");
                            }
                            else
                                Debug.LogWarning($"TeamLoadingSceneManager: Cannot join room {room.Name}, client not ready, State: {PhotonNetwork.NetworkClientState}");
                        }
                    }
                }
            }

            if (!joinedRoom && isPartyLeader)
            {
                string roomName = "TeamMoonRan_" + Random.Range(1000, 9999);
                RoomOptions roomOptions = new RoomOptions
                {
                    MaxPlayers = 10, // Max 10 players (5v5)
                    BroadcastPropsChangeToAll = true,
                    CustomRoomProperties = new ExitGames.Client.Photon.Hashtable
                {
                    { "CreationTime", PhotonNetwork.Time },
                    { "PartyID", string.IsNullOrEmpty(partyId) ? System.Guid.NewGuid().ToString() : partyId },
                    { "GameMode", "TeamMoonRan" }
                },
                    CustomRoomPropertiesForLobby = new string[] { "CreationTime", "PartyID", "GameMode" }
                };
                if (PhotonNetwork.NetworkClientState == ClientState.JoinedLobby || PhotonNetwork.NetworkClientState == ClientState.ConnectedToMasterServer)
                {
                    Debug.Log($"TeamLoadingSceneManager: Creating new room {roomName}, PartyID={roomOptions.CustomRoomProperties["PartyID"]}, Photon State: {PhotonNetwork.NetworkClientState}");
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
                        if (!string.IsNullOrEmpty(partyId))
                        {
                            PlayerPrefs.SetString("PartyID", roomOptions.CustomRoomProperties["PartyID"].ToString());
                            PlayerPrefs.Save();
                        }
                    }
                    else
                        Debug.LogWarning($"TeamLoadingSceneManager: Failed to create room {roomName}, State: {PhotonNetwork.NetworkClientState}");
                }
                else
                    Debug.LogWarning($"TeamLoadingSceneManager: Cannot create room {roomName}, client not ready, State: {PhotonNetwork.NetworkClientState}");
            }
            else if (!joinedRoom && !isPartyLeader && !string.IsNullOrEmpty(partyId))
            {
                Debug.Log($"TeamLoadingSceneManager: Waiting for party leader to create room for PartyID={partyId}, retrying...");
                yield return new WaitForSeconds(2f);
            }
            else if (!joinedRoom)
            {
                string roomName = "TeamMoonRan_" + Random.Range(1000, 9999);
                RoomOptions roomOptions = new RoomOptions
                {
                    MaxPlayers = 10,
                    BroadcastPropsChangeToAll = true,
                    CustomRoomProperties = new ExitGames.Client.Photon.Hashtable
                {
                    { "CreationTime", PhotonNetwork.Time },
                    { "GameMode", "TeamMoonRan" }
                },
                    CustomRoomPropertiesForLobby = new string[] { "CreationTime", "GameMode" }
                };
                if (PhotonNetwork.NetworkClientState == ClientState.JoinedLobby || PhotonNetwork.NetworkClientState == ClientState.ConnectedToMasterServer)
                {
                    Debug.Log($"TeamLoadingSceneManager: Creating new room {roomName}, Photon State: {PhotonNetwork.NetworkClientState}");
                    PhotonNetwork.CreateRoom(roomName, roomOptions);
                    float elapsed = 0f;
                    while (!PhotonNetwork.InRoom && elapsed < ROOM_TIMEOUT && !PhotonNetwork.OfflineMode)
                    {
                        elapsed += Time.deltaTime;
                        yield return null;
                    }
                    if (PhotonNetwork.InRoom)
                        joinedRoom = true;
                    else
                        Debug.LogWarning($"TeamLoadingSceneManager: Failed to create room {roomName}, State: {PhotonNetwork.NetworkClientState}");
                }
                else
                    Debug.LogWarning($"TeamLoadingSceneManager: Cannot create room {roomName}, client not ready, State: {PhotonNetwork.NetworkClientState}");
            }

            if (!joinedRoom && retryCount >= maxRetries)
            {
                Debug.LogWarning($"TeamLoadingSceneManager: Failed to join or create room after {maxRetries} attempts, switching to offline mode.");
                PhotonNetwork.OfflineMode = true;
            }
        }

        // Step 6: Load TeamMoonRan
        Debug.Log("TeamLoadingSceneManager: Attempting to load TeamMoonRan scene");
        AsyncOperation asyncLoad = SceneManager.LoadSceneAsync("TeamMoonRan");
        if (asyncLoad == null)
        {
            Debug.LogError("TeamLoadingSceneManager: Failed to start loading TeamMoonRan scene. Is it in Build Settings?");
            yield break;
        }

        while (!asyncLoad.isDone && Time.time - startTime < MAX_LOADING_TIME)
        {
            Debug.Log($"TeamLoadingSceneManager: Loading progress: {asyncLoad.progress * 100:F2}%, Time elapsed: {(Time.time - startTime):F2}s");
            yield return null;
        }

        if (!asyncLoad.isDone)
        {
            Debug.LogError($"TeamLoadingSceneManager: TeamMoonRan scene load timed out after {MAX_LOADING_TIME}s, Progress: {asyncLoad.progress * 100:F2}%");
            yield break;
        }

        if (SceneManager.GetActiveScene().name != "TeamMoonRan")
        {
            Debug.LogError($"TeamLoadingSceneManager: Expected TeamMoonRan, but active scene is {SceneManager.GetActiveScene().name}");
            yield break;
        }

        // Step 7: Wait for player instantiation and clear non-essential properties
        yield return StartCoroutine(WaitForPlayerAndClearProperties());

        // Step 8: Force camera retargeting
        yield return StartCoroutine(RetargetCamera());

        // Step 9: Trigger wave timer initialization and bot management
        yield return StartCoroutine(InitializeWaveManager());
    }

    private IEnumerator InitializeWaveManager()
    {
        float timeout = 10f;
        float elapsed = 0f;
        while (!PhotonNetwork.InRoom && !PhotonNetwork.OfflineMode && elapsed < timeout)
        {
            Debug.Log($"TeamLoadingSceneManager: Waiting for PhotonNetwork.InRoom, State={PhotonNetwork.NetworkClientState}, Elapsed={elapsed:F2}s");
            elapsed += Time.deltaTime;
            yield return null;
        }

        if (PhotonNetwork.OfflineMode || PhotonNetwork.IsMasterClient)
        {
            WaveManager waveManager = Object.FindFirstObjectByType<WaveManager>();
            if (waveManager != null)
            {
                Debug.Log($"TeamLoadingSceneManager: Found WaveManager, photonView.ID={waveManager.photonView.ViewID}, triggering InitializeWaveTimer, frame={Time.frameCount}");
                waveManager.photonView.RPC("InitializeWaveTimer", RpcTarget.AllBuffered);
                Debug.Log("TeamLoadingSceneManager: Triggered WaveManager.InitializeWaveTimer");
            }
            else
                Debug.LogError("TeamLoadingSceneManager: WaveManager not found after loading TeamMoonRan. Ensure WaveManager GameObject exists in scene.");

            // Trigger bot management after scene load
            BoundaryManager boundaryManager = Object.FindFirstObjectByType<BoundaryManager>();
            if (boundaryManager != null)
            {
                boundaryManager.ManageBots();
                Debug.Log("TeamLoadingSceneManager: Triggered BoundaryManager.ManageBots");
            }
            else
                Debug.LogError("TeamLoadingSceneManager: BoundaryManager not found, cannot manage bots.");
        }
        else
            Debug.Log("TeamLoadingSceneManager: Waiting for Master Client to initialize wave timer and manage bots");
    }

    private IEnumerator EnsureFullDisconnect()
    {
        if (PhotonNetwork.InRoom)
        {
            Debug.Log($"TeamLoadingSceneManager: Leaving current room {PhotonNetwork.CurrentRoom?.Name}, Photon State: {PhotonNetwork.NetworkClientState}");
            PhotonNetwork.LeaveRoom();
            yield return new WaitUntil(() => !PhotonNetwork.InRoom || PhotonNetwork.NetworkClientState == ClientState.Disconnected);
            Debug.Log("TeamLoadingSceneManager: Successfully left room");
        }

        if (PhotonNetwork.InLobby)
        {
            Debug.Log($"TeamLoadingSceneManager: Leaving lobby, Photon State: {PhotonNetwork.NetworkClientState}");
            PhotonNetwork.LeaveLobby();
            yield return new WaitUntil(() => !PhotonNetwork.InLobby || PhotonNetwork.NetworkClientState == ClientState.Disconnected);
            Debug.Log("TeamLoadingSceneManager: Successfully left lobby");
        }

        if (PhotonNetwork.IsConnected)
        {
            Debug.Log($"TeamLoadingSceneManager: Disconnecting from Photon, Photon State: {PhotonNetwork.NetworkClientState}");
            PhotonNetwork.Disconnect();
            yield return new WaitUntil(() => !PhotonNetwork.IsConnected);
            Debug.Log("TeamLoadingSceneManager: Successfully disconnected");
        }

        if (PhotonNetwork.OfflineMode)
        {
            PhotonNetwork.OfflineMode = false;
            Debug.Log("TeamLoadingSceneManager: Disabled offline mode");
        }

        float timeout = 5f;
        float elapsed = 0f;
        while (PhotonNetwork.NetworkClientState != ClientState.Disconnected && elapsed < timeout)
        {
            Debug.Log($"TeamLoadingSceneManager: Waiting for Disconnected state, Current State: {PhotonNetwork.NetworkClientState}, Elapsed: {elapsed:F2}s");
            elapsed += Time.deltaTime;
            yield return null;
        }

        if (PhotonNetwork.NetworkClientState != ClientState.Disconnected)
            Debug.LogWarning($"TeamLoadingSceneManager: Failed to reach Disconnected state after {timeout}s, proceeding anyway, State: {PhotonNetwork.NetworkClientState}");
        else
            Debug.Log($"TeamLoadingSceneManager: Confirmed Disconnected state, State: {PhotonNetwork.NetworkClientState}");
    }

    private IEnumerator WaitForPlayerAndClearProperties()
    {
        int maxRetries = 5;
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
                            Debug.Log("TeamLoadingSceneManager: Cleared non-essential player custom properties (SpaceshipViewID, CompassViewID)");
                        }

                        if (PhotonNetwork.PlayerList.Count() == 1)
                        {
                            ScoreboardManager scoreboardManager = FindFirstObjectByType<ScoreboardManager>();
                            if (scoreboardManager != null && scoreboardManager.photonView != null)
                            {
                                scoreboardManager.photonView.RPC("DisplaySpawningBotsMessage", RpcTarget.All);
                                Debug.Log($"TeamLoadingSceneManager: Triggered Spawning Bots message for first player {playerController.NickName}, ActorNumber={PhotonNetwork.LocalPlayer.ActorNumber}");
                            }
                            else
                                CustomLogger.LogWarning("TeamLoadingSceneManager: ScoreboardManager not found or not networked, skipping Spawning Bots message for first player");
                        }

                        Debug.Log($"TeamLoadingSceneManager: Player {playerController.NickName}, actions ready");
                        yield return null;
                    }
                }
            }

            retries++;
            Debug.Log($"TeamLoadingSceneManager: Retry {retries}/{maxRetries} waiting for PlayerViewID and actions to be ready");
            yield return new WaitForSeconds(0.2f);
        }

        Debug.LogWarning("TeamLoadingSceneManager: PlayerViewID or actions not ready after retries, clearing non-essential properties");
        if (PhotonNetwork.IsConnectedAndReady && PhotonNetwork.LocalPlayer != null)
        {
            PhotonNetwork.LocalPlayer.SetCustomProperties(new ExitGames.Client.Photon.Hashtable
            {
                { "SpaceshipViewID", null },
                { "CompassViewID", null }
            });
            Debug.Log("TeamLoadingSceneManager: Cleared non-essential player custom properties after timeout");
        }
    }

    private IEnumerator RetargetCamera()
    {
        yield return new WaitForSeconds(1f);
        CameraFollow cameraFollow = Camera.main?.GetComponent<CameraFollow>();
        if (cameraFollow != null)
        {
            cameraFollow.ForceRetargetPlayer();
            Debug.Log("TeamLoadingSceneManager: Called CameraFollow.ForceRetargetPlayer after TeamMoonRan load");
        }
        else
            Debug.LogError("TeamLoadingSceneManager: CameraFollow not found after loading TeamMoonRan");
    }

    private IEnumerator JoinLobbyWithStateCheck()
    {
        float timeout = 10f;
        float elapsed = 0f;
        while (PhotonNetwork.NetworkClientState != ClientState.ConnectedToMasterServer && elapsed < timeout)
        {
            Debug.Log($"TeamLoadingSceneManager: Waiting for stable Master Server connection, State: {PhotonNetwork.NetworkClientState}, Elapsed: {elapsed:F2}s");
            elapsed += Time.deltaTime;
            yield return null;
        }
        if (PhotonNetwork.NetworkClientState == ClientState.ConnectedToMasterServer)
        {
            Debug.Log("TeamLoadingSceneManager: Joining lobby...");
            PhotonNetwork.JoinLobby();
            elapsed = 0f;
            while (PhotonNetwork.NetworkClientState != ClientState.JoinedLobby && elapsed < timeout)
            {
                Debug.Log($"TeamLoadingSceneManager: Waiting for JoinedLobby, State: {PhotonNetwork.NetworkClientState}, Elapsed: {elapsed:F2}s");
                elapsed += Time.deltaTime;
                yield return null;
            }
            if (PhotonNetwork.NetworkClientState == ClientState.JoinedLobby)
                Debug.Log("TeamLoadingSceneManager: Successfully joined lobby");
            else
            {
                Debug.LogWarning($"TeamLoadingSceneManager: Failed to join lobby after {timeout}s, State: {PhotonNetwork.NetworkClientState}");
                PhotonNetwork.OfflineMode = true;
            }
        }
        else
        {
            Debug.LogWarning($"TeamLoadingSceneManager: Failed to reach Master Server after {timeout}s, switching to offline mode");
            PhotonNetwork.OfflineMode = true;
        }
    }

    public override void OnConnectedToMaster()
    {
        base.OnConnectedToMaster();
        Debug.Log($"TeamLoadingSceneManager: Connected to Master Server, Photon State: {PhotonNetwork.NetworkClientState}");
    }

    public override void OnJoinedLobby()
    {
        base.OnJoinedLobby();
        Debug.Log($"TeamLoadingSceneManager: Joined lobby, Photon State: {PhotonNetwork.NetworkClientState}");
    }

    public override void OnRoomListUpdate(List<RoomInfo> roomList)
    {
        base.OnRoomListUpdate(roomList);
        availableRooms = roomList;
        Debug.Log($"TeamLoadingSceneManager: Received room list update with {roomList.Count} rooms");
    }

    public override void OnJoinedRoom()
    {
        base.OnJoinedRoom();
        Debug.Log($"TeamLoadingSceneManager: Joined room {PhotonNetwork.CurrentRoom.Name}, Photon State: {PhotonNetwork.NetworkClientState}");
    }

    public override void OnCreatedRoom()
    {
        base.OnCreatedRoom();
        Debug.Log($"TeamLoadingSceneManager: Created room {PhotonNetwork.CurrentRoom.Name}, PhotonNetwork State: {PhotonNetwork.NetworkClientState}");
    }

    public override void OnJoinRoomFailed(short returnCode, string message)
    {
        base.OnJoinRoomFailed(returnCode, message);
        Debug.LogWarning($"TeamLoadingSceneManager: Failed to join room: {message}");
        string roomName = "TeamMoonRan_" + Random.Range(1000, 9999);
        RoomOptions roomOptions = new RoomOptions
        {
            MaxPlayers = 20,
            BroadcastPropsChangeToAll = true,
            CustomRoomProperties = new ExitGames.Client.Photon.Hashtable { { "CreationTime", PhotonNetwork.Time } },
            CustomRoomPropertiesForLobby = new string[] { "CreationTime" }
        };
        if (PhotonNetwork.NetworkClientState == ClientState.JoinedLobby || PhotonNetwork.NetworkClientState == ClientState.ConnectedToMasterServer)
        {
            Debug.Log($"Creating new room {roomName}, Photon State: {PhotonNetwork.NetworkClientState}");
            PhotonNetwork.CreateRoom(roomName, roomOptions);
        }
        else
        {
            Debug.LogWarning($"TeamLoadingSceneManager: Cannot create room {roomName}, client not ready, State: {PhotonNetwork.NetworkClientState}, switching to offline mode");
            PhotonNetwork.OfflineMode = true;
        }
    }

    public override void OnDisconnected(DisconnectCause cause)
    {
        base.OnDisconnected(cause);
        Debug.Log($"TeamLoadingSceneManager: Disconnected from Photon, cause: {cause}, State: {PhotonNetwork.NetworkClientState}");
    }
}