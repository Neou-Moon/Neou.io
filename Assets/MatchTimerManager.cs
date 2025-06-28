using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using Photon.Pun;
using Photon.Realtime;
using TMPro;
using System.Collections.Generic;
using System.Linq;
using System.Collections;
using Hashtable = ExitGames.Client.Photon.Hashtable;

public class MatchTimerManager : MonoBehaviourPunCallbacks
{
    public static MatchTimerManager Instance { get; private set; }
    [SerializeField] private TextMeshProUGUI timerText;
    [SerializeField] private TextMeshProUGUI winnerText;
    [SerializeField] private TextMeshProUGUI mainMenuText;
    [SerializeField] private ScoreboardManager scoreboardManager;
    
    private PlayerController player;
    private const float MATCH_DURATION = 600f;
    private const float RESET_COUNTDOWN = 15f;
    private const float FLASH_THRESHOLD = 30f;
    private const float FLASH_INTERVAL = 0.5f;
    private const float PULSE_SPEED = 4f;
    private const float PULSE_SCALE = 0.2f;
    private double matchStartTime;
    private bool isMatchStarted;
    private bool isMatchEnded;
    public bool IsMatchEnded => isMatchEnded; // Added public property
    private float lastSyncCheckTime;
    private const float SYNC_CHECK_INTERVAL = 1f;
    private bool hasJoinedRoom;
    private int connectionAttempts;
    private const int MAX_CONNECTION_ATTEMPTS = 3;
    private float matchStartTimeout;
    private const float MATCH_START_TIMEOUT_DURATION = 5f;
    private Color originalTimerColor;
    private Vector3 originalTimerScale;
    private Coroutine timerFlashCoroutine;
    private Coroutine winnerFlashCoroutine;
    public TextMeshProUGUI TimerText => timerText;
    public bool IsMatchStarted => isMatchStarted;

    void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Debug.LogWarning("MatchTimerManager: Another instance already exists, destroying this one.");
            Destroy(gameObject);
            return;
        }
        Instance = this;
        DontDestroyOnLoad(gameObject);
        Debug.Log("MatchTimerManager: Singleton instance set and marked as DontDestroyOnLoad.");
    }

    void Start()
    {
        if (timerText == null || winnerText == null || mainMenuText == null || scoreboardManager == null || GetComponent<PhotonView>() == null)
        {
            Debug.LogError("MatchTimerManager: Missing required components or references.");
            enabled = false;
            return;
        }

        // Ensure MatchTimerManager persists across scenes
        DontDestroyOnLoad(gameObject);
        Debug.Log("MatchTimerManager: Set as DontDestroyOnLoad.");

        winnerText.gameObject.SetActive(false);
        mainMenuText.gameObject.SetActive(false);
        timerText.gameObject.SetActive(true); // Explicitly enable timerText
        timerText.text = FormatTime(MATCH_DURATION);
        originalTimerColor = timerText.color;
        originalTimerScale = timerText.transform.localScale;

        Button mainMenuButton = mainMenuText.GetComponent<Button>() ?? mainMenuText.gameObject.AddComponent<Button>();
        mainMenuButton.transition = Selectable.Transition.None;
        mainMenuButton.onClick.AddListener(GoToMainMenu);
        mainMenuButton.interactable = false;

        if (PhotonNetwork.InRoom)
        {
            OnJoinedRoom();
        }
        else
        {
            StartCoroutine(EnsurePhotonConnection());
        }

        matchStartTimeout = Time.time + MATCH_START_TIMEOUT_DURATION;
    }

    private IEnumerator EnsurePhotonConnection()
    {
        connectionAttempts = 0;
        while (connectionAttempts < MAX_CONNECTION_ATTEMPTS && !PhotonNetwork.InRoom)
        {
            connectionAttempts++;
            Debug.Log($"MatchTimerManager: Connection attempt {connectionAttempts}/{MAX_CONNECTION_ATTEMPTS}, State: {PhotonNetwork.NetworkClientState}");
            if (!PhotonNetwork.IsConnected)
            {
                PhotonNetwork.ConnectUsingSettings();
                float connectTimeout = 15f;
                float connectElapsed = 0f;
                while (!PhotonNetwork.IsConnected && connectElapsed < connectTimeout && PhotonNetwork.NetworkClientState != ClientState.Disconnected)
                {
                    connectElapsed += Time.deltaTime;
                    yield return null;
                }
                if (!PhotonNetwork.IsConnected)
                {
                    if (connectionAttempts < MAX_CONNECTION_ATTEMPTS)
                    {
                        yield return new WaitForSeconds(2f);
                        continue;
                    }
                }
            }

            float masterTimeout = 10f;
            float masterElapsed = 0f;
            while (PhotonNetwork.NetworkClientState != ClientState.ConnectedToMasterServer && masterElapsed < masterTimeout && PhotonNetwork.NetworkClientState != ClientState.Disconnected)
            {
                masterElapsed += Time.deltaTime;
                yield return null;
            }

            if (PhotonNetwork.NetworkClientState != ClientState.ConnectedToMasterServer)
            {
                if (connectionAttempts < MAX_CONNECTION_ATTEMPTS)
                {
                    yield return new WaitForSeconds(2f);
                    continue;
                }
                else
                {
                    Debug.LogWarning("MatchTimerManager: Failed to connect to Master Server, starting offline match.");
                    StartOfflineMatch();
                    yield break;
                }
            }

            if (!PhotonNetwork.InRoom)
            {
                string roomName = "MoonRan_" + Random.Range(1000, 9999);
                RoomOptions roomOptions = new RoomOptions { MaxPlayers = 20, BroadcastPropsChangeToAll = true };
                if (PhotonNetwork.NetworkClientState == ClientState.ConnectedToMasterServer)
                {
                    Debug.Log($"MatchTimerManager: Joining or creating room {roomName}, State: {PhotonNetwork.NetworkClientState}");
                    PhotonNetwork.JoinOrCreateRoom(roomName, roomOptions, TypedLobby.Default);
                }
                else
                {
                    Debug.LogWarning($"MatchTimerManager: Cannot join or create room, client not ready, State: {PhotonNetwork.NetworkClientState}");
                    continue;
                }

                float joinTimeout = 10f;
                float joinElapsed = 0f;
                while (!hasJoinedRoom && joinElapsed < joinTimeout && PhotonNetwork.NetworkClientState != ClientState.Disconnected)
                {
                    joinElapsed += Time.deltaTime;
                    yield return null;
                }

                if (!hasJoinedRoom)
                {
                    if (connectionAttempts < MAX_CONNECTION_ATTEMPTS)
                    {
                        yield return new WaitForSeconds(2f);
                        continue;
                    }
                }
            }
        }
    }

    private void StartOfflineMatch()
    {
        PhotonNetwork.OfflineMode = true;
        matchStartTime = Time.time;
        isMatchStarted = true;
        Debug.Log("MatchTimerManager: Started offline match.");
    }

    public override void OnConnectedToMaster()
    {
        base.OnConnectedToMaster();
        Debug.Log($"MatchTimerManager: Connected to Master Server, State: {PhotonNetwork.NetworkClientState}");
    }

    public override void OnDisconnected(DisconnectCause cause)
    {
        base.OnDisconnected(cause);
        if (cause != DisconnectCause.ApplicationQuit && connectionAttempts < MAX_CONNECTION_ATTEMPTS)
        {
            Debug.Log($"MatchTimerManager: Disconnected, cause: {cause}, attempting reconnection...");
            StartCoroutine(EnsurePhotonConnection());
        }
        else if (cause == DisconnectCause.ApplicationQuit)
        {
            Debug.Log("MatchTimerManager: Disconnected due to ApplicationQuit, no reconnection attempted.");
        }
        else
        {
            Debug.LogWarning("MatchTimerManager: Max connection attempts reached, starting offline match.");
            StartOfflineMatch();
        }
    }

    public override void OnJoinRoomFailed(short returnCode, string message)
    {
        base.OnJoinRoomFailed(returnCode, message);
        Debug.LogWarning($"MatchTimerManager: Failed to join room: {message} (Code: {returnCode})");
        if (connectionAttempts < MAX_CONNECTION_ATTEMPTS)
        {
            string newRoomName = "MoonRan_" + Random.Range(1000, 9999);
            RoomOptions roomOptions = new RoomOptions { MaxPlayers = 20, BroadcastPropsChangeToAll = true };
            if (PhotonNetwork.NetworkClientState == ClientState.ConnectedToMasterServer)
            {
                Debug.Log($"MatchTimerManager: Retrying with new room {newRoomName}");
                PhotonNetwork.JoinOrCreateRoom(newRoomName, roomOptions, TypedLobby.Default);
            }
        }
    }

    public override void OnJoinedRoom()
    {
        base.OnJoinedRoom();
        hasJoinedRoom = true;
        Debug.Log($"MatchTimerManager: Joined room {PhotonNetwork.CurrentRoom.Name}, IsMasterClient: {PhotonNetwork.IsMasterClient}");
        if (PhotonNetwork.IsMasterClient)
        {
            StartMatch();
        }
        else
        {
            StartCoroutine(CheckAndSyncTimer());
        }
    }

    private void StartMatch()
    {
        if (PhotonNetwork.OfflineMode || PhotonNetwork.IsMasterClient)
        {
            matchStartTime = PhotonNetwork.OfflineMode ? Time.time : PhotonNetwork.Time;
            if (!PhotonNetwork.OfflineMode && matchStartTime <= 0)
            {
                Debug.LogWarning("MatchTimerManager: PhotonNetwork.Time is not valid, delaying match start.");
                StartCoroutine(DelayedStartMatch());
                return;
            }
            Hashtable props = new Hashtable { { "MatchStartTime", matchStartTime } };
            bool setPropsSuccess = PhotonNetwork.OfflineMode || PhotonNetwork.CurrentRoom.SetCustomProperties(props);
            isMatchStarted = true;
            Debug.Log($"MatchTimerManager: Started match, MatchStartTime: {matchStartTime}, PropsSet: {setPropsSuccess}");
            if (!setPropsSuccess && !PhotonNetwork.OfflineMode)
            {
                StartCoroutine(RetrySetProperties(props));
            }
        }
    }

    private IEnumerator DelayedStartMatch()
    {
        float timeout = 5f;
        float elapsed = 0f;
        while (elapsed < timeout && PhotonNetwork.Time <= 0)
        {
            elapsed += Time.deltaTime;
            yield return null;
        }
        if (PhotonNetwork.Time > 0)
        {
            matchStartTime = PhotonNetwork.Time;
            Hashtable props = new Hashtable { { "MatchStartTime", matchStartTime } };
            bool setPropsSuccess = PhotonNetwork.CurrentRoom.SetCustomProperties(props);
            isMatchStarted = true;
            Debug.Log($"MatchTimerManager: Delayed match start successful, MatchStartTime: {matchStartTime}, PropsSet: {setPropsSuccess}");
            if (!setPropsSuccess)
            {
                StartCoroutine(RetrySetProperties(props));
            }
        }
        else
        {
            Debug.LogError("MatchTimerManager: Failed to get valid PhotonNetwork.Time, starting offline match.");
            StartOfflineMatch();
        }
    }

    private IEnumerator RetrySetProperties(Hashtable props)
    {
        yield return new WaitForSeconds(1f);
        if (PhotonNetwork.InRoom)
        {
            PhotonNetwork.CurrentRoom.SetCustomProperties(props);
            Debug.Log("MatchTimerManager: Retried setting room properties.");
        }
    }

    private IEnumerator CheckAndSyncTimer()
    {
        if (PhotonNetwork.OfflineMode)
        {
            isMatchStarted = true;
            matchStartTime = Time.time;
            Debug.Log("MatchTimerManager: Synced timer for offline mode.");
            yield break;
        }

        int retryCount = 0;
        const int maxRetries = 3;
        while (retryCount < maxRetries && !isMatchStarted)
        {
            if (PhotonNetwork.CurrentRoom?.CustomProperties.TryGetValue("MatchStartTime", out object startTimeObj) == true && startTimeObj != null)
            {
                matchStartTime = (double)startTimeObj;
                if (matchStartTime > 0) // Validate start time
                {
                    isMatchStarted = true;
                    Debug.Log($"MatchTimerManager: Synced timer, MatchStartTime: {matchStartTime}, Retry: {retryCount}");
                    yield break;
                }
            }
            retryCount++;
            Debug.LogWarning($"MatchTimerManager: Failed to sync MatchStartTime, retry {retryCount}/{maxRetries}");
            yield return new WaitForSeconds(0.5f);
        }

        if (!isMatchStarted && PhotonNetwork.IsMasterClient)
        {
            StartMatch();
            Debug.Log("MatchTimerManager: MasterClient started match after sync failure.");
        }
    }

    public override void OnRoomPropertiesUpdate(Hashtable propertiesThatChanged)
    {
        base.OnRoomPropertiesUpdate(propertiesThatChanged);
        if (propertiesThatChanged.ContainsKey("MatchStartTime") && !isMatchStarted)
        {
            StartCoroutine(CheckAndSyncTimer());
        }
    }

    void Update()
    {
        if (!PhotonNetwork.OfflineMode && !PhotonNetwork.InRoom)
        {
            Debug.Log($"MatchTimerManager: Update skipped, not in room, State: {PhotonNetwork.NetworkClientState}");
            return;
        }

        // Ensure timerText is active
        if (timerText != null && !timerText.gameObject.activeSelf)
        {
            timerText.gameObject.SetActive(true);
            Debug.LogWarning("MatchTimerManager: timerText was deactivated, reactivated.");
        }

        if (!isMatchStarted && Time.time > matchStartTimeout && !PhotonNetwork.OfflineMode)
        {
            if (PhotonNetwork.IsMasterClient)
            {
                StartMatch();
            }
            else
            {
                StartCoroutine(CheckAndSyncTimer());
            }
        }

        if (!isMatchStarted && Time.time - lastSyncCheckTime >= SYNC_CHECK_INTERVAL)
        {
            lastSyncCheckTime = Time.time;
            StartCoroutine(CheckAndSyncTimer());
        }

        if (!isMatchStarted || isMatchEnded)
        {
            if (isMatchEnded && Input.GetKeyDown(KeyCode.Q))
            {
                GoToMainMenu();
            }
            return;
        }

        float elapsed = (float)(PhotonNetwork.OfflineMode ? (Time.time - matchStartTime) : (PhotonNetwork.Time - matchStartTime));
        float remainingTime = Mathf.Clamp(MATCH_DURATION - elapsed, 0f, MATCH_DURATION);
        Debug.Log($"MatchTimerManager: Update, matchStartTime={matchStartTime:F2}, PhotonNetwork.Time={PhotonNetwork.Time:F2}, elapsed={elapsed:F2}, remainingTime={remainingTime:F2}");

        if (remainingTime <= 0)
        {
            Debug.LogWarning($"MatchTimerManager: Match ending, remainingTime={remainingTime:F2}, matchStartTime={matchStartTime:F2}");
            EndMatch();
            return;
        }

        timerText.text = FormatTime(remainingTime);

        if (remainingTime <= FLASH_THRESHOLD && timerFlashCoroutine == null)
        {
            timerFlashCoroutine = StartCoroutine(TimerFlashAndPulse());
        }
    }

    private IEnumerator TimerFlashAndPulse()
    {
        bool isRed = true;
        while (!isMatchEnded)
        {
            timerText.color = isRed ? Color.red : Color.white;
            isRed = !isRed;
            float t = (Mathf.Sin(Time.time * PULSE_SPEED) + 1) / 2;
            float scale = 1f + (t * PULSE_SCALE);
            timerText.transform.localScale = originalTimerScale * scale;
            yield return new WaitForSeconds(FLASH_INTERVAL);
        }
        timerText.color = originalTimerColor;
        timerText.transform.localScale = originalTimerScale;
        timerFlashCoroutine = null;
    }

    private void EndMatch()
    {
        if (isMatchEnded) return;
        isMatchEnded = true;

        if (timerFlashCoroutine != null)
        {
            StopCoroutine(timerFlashCoroutine);
            timerText.color = originalTimerColor;
            timerText.transform.localScale = originalTimerScale;
            timerFlashCoroutine = null;
        }

        scoreboardManager.LockScoreboard();
        Debug.Log("MatchTimerManager: Match ended, scoreboard locked, no auto-toggle");

        List<IPlayer> allPlayers = new List<IPlayer>();
        if (PhotonNetwork.OfflineMode)
        {
            allPlayers.AddRange(Object.FindObjectsByType<BotController>(FindObjectsSortMode.None));
        }
        else
        {
            allPlayers.AddRange(PhotonNetwork.PlayerList.Select(p => new BoundaryManager.RealPlayerWrapper(p) as IPlayer));
            allPlayers.AddRange(Object.FindObjectsByType<BotController>(FindObjectsSortMode.None));
        }

        string winnerMessage;
        if (SceneManager.GetActiveScene().name == "TeamMoonRan")
        {
            int redTeamScore = 0;
            int cyanTeamScore = 0;
            foreach (var player in allPlayers)
            {
                int points = player.CustomProperties != null && player.CustomProperties.ContainsKey("Points") ? (int)player.CustomProperties["Points"] : 0;
                string team = player.CustomProperties != null && player.CustomProperties.ContainsKey("Team") ? player.CustomProperties["Team"].ToString() : "None";
                if (team == "Red")
                    redTeamScore += points;
                else if (team == "Cyan")
                    cyanTeamScore += points;
            }

            if (redTeamScore > cyanTeamScore)
            {
                winnerMessage = "Red Team Wins";
            }
            else if (cyanTeamScore > redTeamScore)
            {
                winnerMessage = "Cyan Team Wins";
            }
            else
            {
                winnerMessage = "Match Tied";
            }
            Debug.Log($"MatchTimerManager: TeamMoonRan ended, Red Team: {redTeamScore}, Cyan Team: {cyanTeamScore}, Winner: {winnerMessage}");
        }
        else
        {
            int maxPoints = allPlayers
                .Where(p => p.CustomProperties != null && p.CustomProperties.ContainsKey("Points"))
                .DefaultIfEmpty()
                .Max(p => p != null && p.CustomProperties != null && p.CustomProperties.ContainsKey("Points") ? (int)p.CustomProperties["Points"] : 0);
            List<IPlayer> topPlayers = allPlayers
                .Where(p => p.CustomProperties != null && p.CustomProperties.ContainsKey("Points") && (int)p.CustomProperties["Points"] == maxPoints)
                .ToList();

            string winnerNames = topPlayers.Count > 0 ? string.Join(", ", topPlayers.Select(p => scoreboardManager.GetPlayerUsername(p))) : "No Players";
            winnerMessage = $"Winner: {winnerNames}";
        }

        mainMenuText.gameObject.SetActive(true);
        mainMenuText.text = "Press 'Q' to return to Spaceship";

        if (PhotonNetwork.OfflineMode)
        {
            StartCoroutine(WinnerAndResetCountdown(winnerMessage));
        }
        else
        {
            photonView.RPC("ShowWinnerAndCountdown", RpcTarget.All, winnerMessage, 0);
        }
    }

    [PunRPC]
    private void ShowWinnerAndCountdown(string winnerMessage, int maxPoints)
    {
        StartCoroutine(WinnerAndResetCountdown(winnerMessage));
    }

    private IEnumerator WinnerAndResetCountdown(string winnerMessage)
    {
        winnerText.gameObject.SetActive(true);
        winnerText.text = $"{winnerMessage}\nNext Match Starting in: {RESET_COUNTDOWN}";
        winnerFlashCoroutine = StartCoroutine(WinnerFlash());

        float countdown = RESET_COUNTDOWN;
        while (countdown > 0)
        {
            countdown -= Time.deltaTime;
            winnerText.text = $"{winnerMessage}\nNext Match Starting in: {Mathf.CeilToInt(countdown)}";
            yield return null;
        }

        if (winnerFlashCoroutine != null)
        {
            StopCoroutine(winnerFlashCoroutine);
            winnerText.color = Color.white;
            winnerFlashCoroutine = null;
        }

        if (PhotonNetwork.OfflineMode || PhotonNetwork.IsMasterClient)
        {
            StartCoroutine(ResetGame());
        }
    }

    private IEnumerator WinnerFlash()
    {
        bool isRed = true;
        while (winnerText.gameObject.activeSelf)
        {
            winnerText.color = isRed ? Color.red : Color.white;
            isRed = !isRed;
            yield return new WaitForSeconds(FLASH_INTERVAL);
        }
        winnerText.color = Color.white;
    }

    private IEnumerator ResetGame()
    {
        if (!PhotonNetwork.OfflineMode && PhotonNetwork.InRoom)
        {
            PhotonNetwork.LeaveRoom();
            yield return new WaitUntil(() => !PhotonNetwork.InRoom);
            Debug.Log("MatchTimerManager: Left room for reset.");
        }

        // Clean up DontDestroyOnLoad MatchTimerManager
        Destroy(gameObject);
        Debug.Log("MatchTimerManager: Destroyed MatchTimerManager before scene reload.");

        PhotonNetwork.LoadLevel("LoadingMatch");
        yield return new WaitUntil(() => SceneManager.GetActiveScene().name == "LoadingMatch");
        Debug.Log("MatchTimerManager: Loaded LoadingMatch scene for reset.");
    }

    [PunRPC]
    private void InitializeGame()
    {
        ResetMatchState();
        StartCoroutine(DelayedScoreboardReset());
        StartCoroutine(RetryScoreboardReset());
        BoundaryManager boundaryManager = Object.FindFirstObjectByType<BoundaryManager>();
        if (boundaryManager != null)
        {
            boundaryManager.ResetBoundary();
            Debug.Log("MatchTimerManager: Reset BoundaryManager state");
        }

        RandomPlanetGenerator planetGenerator = Object.FindFirstObjectByType<RandomPlanetGenerator>();
        if (planetGenerator != null)
        {
            planetGenerator.ResetPlanets();
            Debug.Log("MatchTimerManager: Reset RandomPlanetGenerator state");
        }

        CameraFollow cameraFollow = Object.FindFirstObjectByType<CameraFollow>();
        if (cameraFollow != null)
        {
            cameraFollow.ForceRetargetPlayer();
            Debug.Log("MatchTimerManager: Triggered CameraFollow to retarget player");
        }
        else
        {
            Debug.LogWarning("MatchTimerManager: CameraFollow not found in scene after reset");
        }

        StartCoroutine(EnsurePlayerAndSpaceshipSpawn());
        Debug.Log("MatchTimerManager: Game initialized after scene reload");
    }

    private IEnumerator DelayedScoreboardReset()
    {
        yield return new WaitForSeconds(0.5f);
        if (scoreboardManager != null)
        {
            scoreboardManager.ResetScoreboard();
            scoreboardManager.UnlockScoreboard();
            scoreboardManager.ForceUpdateScoreboard();
            if (scoreboardManager.IsScoreboardVisible())
            {
                scoreboardManager.ToggleScoreboard();
            }
            // Reset PlayerController points
            PlayerController[] players = Object.FindObjectsByType<PlayerController>(FindObjectsSortMode.None);
            foreach (PlayerController player in players)
            {
                if (player.photonView != null)
                {
                    player.photonView.RPC("ResetPlayerStateRPC", RpcTarget.AllBuffered);
                    Debug.Log($"MatchTimerManager: Called ResetPlayerStateRPC for player {player.NickName}");
                }
            }
            Debug.Log("MatchTimerManager: Reset and unlocked ScoreboardManager state, reset PlayerController states");
        }
        else
        {
            Debug.LogWarning("MatchTimerManager: ScoreboardManager reference is null, retrying...");
            yield return new WaitForSeconds(0.5f);
            scoreboardManager = Object.FindFirstObjectByType<ScoreboardManager>();
            if (scoreboardManager != null)
            {
                scoreboardManager.ResetScoreboard();
                scoreboardManager.UnlockScoreboard();
                scoreboardManager.ForceUpdateScoreboard();
                if (scoreboardManager.IsScoreboardVisible())
                {
                    scoreboardManager.ToggleScoreboard();
                }
                // Reset PlayerController points
                PlayerController[] players = Object.FindObjectsByType<PlayerController>(FindObjectsSortMode.None);
                foreach (PlayerController player in players)
                {
                    if (player.photonView != null)
                    {
                        player.photonView.RPC("ResetPlayerStateRPC", RpcTarget.AllBuffered);
                        Debug.Log($"MatchTimerManager: Called ResetPlayerStateRPC for player {player.NickName}");
                    }
                }
                Debug.Log("MatchTimerManager: Successfully reset ScoreboardManager and PlayerController states after retry");
            }
            else
            {
                Debug.LogError("MatchTimerManager: ScoreboardManager not found after retry, cannot reset scoreboard.");
            }
        }
    }

    private IEnumerator RetryScoreboardReset()
    {
        yield return new WaitForSeconds(1f);
        if (scoreboardManager != null && !PhotonNetwork.OfflineMode)
        {
            scoreboardManager.ResetScoreboard();
            scoreboardManager.ForceUpdateScoreboard();
            Debug.Log("MatchTimerManager: Retried ScoreboardManager reset");
        }
    }

    private IEnumerator EnsurePlayerAndSpaceshipSpawn()
    {
        yield return new WaitForSeconds(2f);
        if (player == null || !player.photonView.IsMine)
        {
            PlayerController localPlayer = FindObjectsByType<PlayerController>(FindObjectsSortMode.None).FirstOrDefault(p => p.IsLocal && p.gameObject.activeInHierarchy);
            if (localPlayer != null)
            {
                player = localPlayer;
                CustomLogger.Log($"MatchTimerManager: Found existing local player {localPlayer.NickName}, ViewID={localPlayer.photonView.ViewID}, skipping spawn.");
            }
            else
            {
                GameObject playerObj = PhotonNetwork.Instantiate("Player", Vector3.zero, Quaternion.identity);
                PhotonView playerView = playerObj.GetComponent<PhotonView>();
                if (playerView != null && playerObj.CompareTag("Player"))
                {
                    player = playerObj.GetComponent<PlayerController>();
                    PhotonNetwork.LocalPlayer.SetCustomProperties(new Hashtable { { "PlayerViewID", playerView.ViewID } });
                    CustomLogger.Log($"MatchTimerManager: Spawned new player {player.NickName}, ViewID={playerView.ViewID}, Position={playerObj.transform.position}");

                    // Set default team if in TeamMoonRan
                    if (SceneManager.GetActiveScene().name == "TeamMoonRan")
                    {
                        string team = Random.value < 0.5f ? "Red" : "Cyan";
                        PhotonNetwork.LocalPlayer.SetCustomProperties(new Hashtable { { "Team", team } });
                        Debug.Log($"MatchTimerManager: Assigned team {team} to player ViewID={playerView.ViewID}");
                    }

                    // Notify CameraFollow to retarget
                    CameraFollow cameraFollow = FindFirstObjectByType<CameraFollow>();
                    if (cameraFollow != null)
                    {
                        cameraFollow.ForceRetargetPlayer();
                        Debug.Log("MatchTimerManager: Notified CameraFollow to retarget player after spawn");
                    }
                    else
                    {
                        CustomLogger.LogWarning("MatchTimerManager: CameraFollow not found, camera may not target player correctly.");
                    }
                }
                else
                {
                    CustomLogger.LogError($"MatchTimerManager: Spawned player {playerObj.name} missing PhotonView or 'Player' tag, destroying.");
                    PhotonNetwork.Destroy(playerObj);
                }
            }
        }

        photonView.RPC("SpawnSpaceship", RpcTarget.All);
    }

    [PunRPC]
    private void SpawnSpaceship(int ownerId)
    {
        if (!PhotonNetwork.IsMasterClient)
        {
            Debug.Log($"MatchTimerManager: Ignored SpawnSpaceship for ownerId={ownerId}, not MasterClient");
            return;
        }

        // Check if a spaceship already exists for this ownerId
        GameObject[] spaceships = GameObject.FindGameObjectsWithTag("SpaceShip");
        foreach (var ship in spaceships)
        {
            SpaceshipMarker marker = ship.GetComponent<SpaceshipMarker>();
            if (marker != null && marker.ownerId == ownerId)
            {
                Debug.Log($"MatchTimerManager: Spaceship already exists for ownerId={ownerId}, ViewID={ship.GetComponent<PhotonView>().ViewID}, skipping spawn");
                // Update player's CustomProperties if needed
                PhotonNetwork.LocalPlayer.SetCustomProperties(new Hashtable { { "SpaceshipViewID", ship.GetComponent<PhotonView>().ViewID } });
                return;
            }
        }

        // Spawn new spaceship
        Vector3 spawnPos = new Vector3(Random.Range(-1000f, 1000f), Random.Range(-1000f, 1000f), 0);
        GameObject spaceship = PhotonNetwork.Instantiate("SpaceShip", spawnPos, Quaternion.identity);
        PhotonView spaceshipView = spaceship.GetComponent<PhotonView>();
        SpaceshipMarker spaceshipMarker = spaceship.GetComponent<SpaceshipMarker>();

        if (spaceshipMarker != null && spaceshipView != null)
        {
            spaceshipView.RPC("InitializeShip", RpcTarget.AllBuffered, ownerId);
            // Update player's CustomProperties
            var player = PhotonNetwork.CurrentRoom?.GetPlayer(ownerId);
            if (player != null)
            {
                player.SetCustomProperties(new Hashtable { { "SpaceshipViewID", spaceshipView.ViewID } });
                Debug.Log($"MatchTimerManager: Spawned spaceship for ownerId={ownerId}, ViewID={spaceshipView.ViewID}, Position={spawnPos}, Updated player CustomProperties");
            }
            else
            {
                // Likely a bot, update local properties if MasterClient
                if (PhotonNetwork.LocalPlayer.ActorNumber == ownerId)
                {
                    PhotonNetwork.LocalPlayer.SetCustomProperties(new Hashtable { { "SpaceshipViewID", spaceshipView.ViewID } });
                }
                Debug.Log($"MatchTimerManager: Spawned spaceship for ownerId={ownerId} (possibly bot), ViewID={spaceshipView.ViewID}, Position={spawnPos}");
            }

            // Trigger SpaceShipInteraction update
            SpaceShipInteraction interaction = Object.FindFirstObjectByType<SpaceShipInteraction>();
            if (interaction != null)
            {
                interaction.StartCoroutine(interaction.GetSpaceshipWithRetry());
                Debug.Log("MatchTimerManager: Triggered SpaceShipInteraction.GetSpaceshipWithRetry");
            }
        }
        else
        {
            Debug.LogError($"MatchTimerManager: Failed to spawn spaceship for ownerId={ownerId}, marker={spaceshipMarker != null}, photonView={spaceshipView != null}");
            if (spaceship != null) PhotonNetwork.Destroy(spaceship);
        }
    }

    private void GoToMainMenu()
    {
        StartCoroutine(LoadMainMenuScene());
    }

    private IEnumerator LoadMainMenuScene()
    {
        if (!PhotonNetwork.OfflineMode && PhotonNetwork.InRoom)
        {
            PhotonNetwork.LeaveRoom();
            float leaveTimeout = 5f;
            float elapsed = 0f;
            while (PhotonNetwork.InRoom && elapsed < leaveTimeout)
            {
                elapsed += Time.deltaTime;
                yield return null;
            }
            Debug.Log("MatchTimerManager: Left room for main menu.");
        }

        if (!PhotonNetwork.OfflineMode && PhotonNetwork.IsConnected)
        {
            PhotonNetwork.Disconnect();
            float disconnectTimeout = 5f;
            float elapsed = 0f;
            while (PhotonNetwork.IsConnected && elapsed < disconnectTimeout)
            {
                elapsed += Time.deltaTime;
                yield return null;
            }
            Debug.Log("MatchTimerManager: Disconnected for main menu.");
        }

        SceneManager.LoadScene("InsideSpaceShip");
        Debug.Log("MatchTimerManager: Loaded InsideSpaceShip scene.");
    }

    public override void OnLeftRoom()
    {
        base.OnLeftRoom();
        Debug.Log("MatchTimerManager: Left room.");
    }

    public override void OnCreatedRoom()
    {
        base.OnCreatedRoom();
        ResetMatchState();
        if (PhotonNetwork.IsMasterClient)
        {
            StartMatch();
        }
    }

    public void ResetMatchState()
    {
        isMatchStarted = false;
        isMatchEnded = false;
        matchStartTime = 0;
        timerText.text = FormatTime(MATCH_DURATION);
        timerText.color = originalTimerColor;
        timerText.transform.localScale = originalTimerScale;
        winnerText.gameObject.SetActive(false);
        mainMenuText.gameObject.SetActive(false);
        Debug.Log("MatchTimerManager: Reset match state.");
    }

    private string FormatTime(float seconds)
    {
        int minutes = Mathf.FloorToInt(seconds / 60);
        int secs = Mathf.FloorToInt(seconds % 60);
        return $"{minutes:D2}:{secs:D2}";
    }

    public void ResetTimer()
    {
        if (!PhotonNetwork.IsMasterClient)
        {
            Debug.Log("MatchTimerManager: ResetTimer ignored, not Master Client.");
            return;
        }

        ResetMatchState();
        StartMatch();
        photonView.RPC("SyncTimerReset", RpcTarget.Others);
        Debug.Log("MatchTimerManager: Timer reset and match restarted.");
    }

    [PunRPC]
    private void SyncTimerReset()
    {
        ResetMatchState();
        CheckAndSyncTimer();
        Debug.Log("MatchTimerManager: Synced timer reset on non-Master client.");
    }
    void OnDestroy()
    {
        if (Instance == this)
        {
            Instance = null;
            Debug.Log("MatchTimerManager: Singleton instance cleared on destroy.");
        }
    }
}