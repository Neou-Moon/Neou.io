using UnityEngine;
using Photon.Pun;
using Photon.Realtime;
using System.Collections.Generic;
using System.Collections;
using UnityEngine.SceneManagement;
using TMPro;
using ExitGames.Client.Photon;
using System.Linq;



[RequireComponent(typeof(PhotonView))]
public class BoundaryManager : MonoBehaviourPunCallbacks
{
    [SerializeField] private string playerPrefabPath = "Prefabs/Player";
    [SerializeField] private string botPrefabPath = "Prefabs/Bot";
    [SerializeField] private string spaceshipPrefabPath = "Prefabs/SpaceShip";
    [SerializeField] private float boundarySize = 3000f;
    [SerializeField] private float spawnDistance = 15f;
    [SerializeField] private float minSpaceshipSpacing = 50f;
    [SerializeField] public float minBotSpacing = 20f;
    [SerializeField] private CameraFollow cameraFollow;
    [SerializeField] private TextMeshProUGUI outOfBoundsText;
    private readonly float bufferDistance = 50f;
    private Vector2 boundaryMin;
    private Vector2 boundaryMax;
    private bool isOutOfBounds;
    private float damageTimer;
    private float warningFlashTimer;
    private bool isWarningRed = true;
    private List<Vector2> spaceshipPositions = new List<Vector2>();
    private Vector3 warningTextDefaultScale;
    private bool hasSpawnedLocalPlayer = false;
    private bool isResetting = false;

    public float BoundarySize => boundarySize;

    public class RealPlayerWrapper : IPlayer
    {
        public readonly Photon.Realtime.Player _player;

        public RealPlayerWrapper(Photon.Realtime.Player player)
        {
            _player = player;
        }

        public int ActorNumber => _player.ActorNumber;
        public string NickName
        {
            get => _player.NickName;
            set => _player.NickName = value;
        }
        public ExitGames.Client.Photon.Hashtable CustomProperties
        {
            get => _player.CustomProperties;
            set => _player.CustomProperties = value;
        }
        public bool IsLocal => _player.IsLocal;

        public void AddBrightMatter(int amount)
        {
            if (!_player.CustomProperties.TryGetValue("PlayerViewID", out object playerViewIDObj) || !(playerViewIDObj is int playerViewID))
            {
                CustomLogger.LogError($"RealPlayerWrapper: Cannot add {amount} BrightMatter for {NickName}, PlayerViewID not found");
                return;
            }

            PhotonView playerView = PhotonView.Find(playerViewID);
            if (playerView == null || playerView.gameObject == null)
            {
                CustomLogger.LogError($"RealPlayerWrapper: Cannot add {amount} BrightMatter for {NickName}, PlayerViewID={playerViewID} not found");
                return;
            }

            PlayerController playerController = playerView.gameObject.GetComponent<PlayerController>();
            if (playerController == null)
            {
                CustomLogger.LogError($"RealPlayerWrapper: Cannot add {amount} BrightMatter for {NickName}, PlayerController not found");
                return;
            }

            playerController.AddBrightMatter(amount);
            CustomLogger.Log($"RealPlayerWrapper: Added {amount} BrightMatter for {NickName}");
        }

        public bool SetCustomProperties(ExitGames.Client.Photon.Hashtable propertiesToSet)
        {
            if (_player != null)
            {
                _player.SetCustomProperties(propertiesToSet);
                CustomLogger.Log($"RealPlayerWrapper: SetCustomProperties for {NickName}, Properties={string.Join(", ", propertiesToSet.Keys.Cast<object>().Select(k => $"{k}={propertiesToSet[k]}"))}");
                return true;
            }
            CustomLogger.LogWarning($"RealPlayerWrapper: Failed to set custom properties for {NickName}, player is null");
            return false;
        }

        public void AddPoints(int points)
        {
            if (!_player.CustomProperties.TryGetValue("PlayerViewID", out object playerViewIDObj) || !(playerViewIDObj is int playerViewID))
            {
                CustomLogger.LogError($"RealPlayerWrapper: Cannot add {points} points for {NickName}, PlayerViewID not found");
                return;
            }

            PhotonView playerView = PhotonView.Find(playerViewID);
            if (playerView == null || playerView.gameObject == null)
            {
                CustomLogger.LogError($"RealPlayerWrapper: Cannot add {points} points for {NickName}, PlayerViewID={playerViewID} not found");
                return;
            }

            PlayerController playerController = playerView.gameObject.GetComponent<PlayerController>();
            if (playerController == null)
            {
                CustomLogger.LogError($"RealPlayerWrapper: Cannot add {points} points for {NickName}, PlayerController not found");
                return;
            }

            playerController.AddPoints(points);
            CustomLogger.Log($"RealPlayerWrapper: Added {points} points for {NickName}");
        }

        public void OnPlayerKilled(string killedPlayerName)
        {
            if (!_player.CustomProperties.TryGetValue("PlayerViewID", out object playerViewIDObj) || !(playerViewIDObj is int playerViewID))
            {
                CustomLogger.LogError($"RealPlayerWrapper: Cannot notify kill for {NickName}, PlayerViewID not found");
                return;
            }

            PhotonView playerView = PhotonView.Find(playerViewID);
            if (playerView == null || playerView.gameObject == null)
            {
                CustomLogger.LogError($"RealPlayerWrapper: Cannot notify kill for {NickName}, PlayerViewID={playerViewID} not found");
                return;
            }

            PlayerController playerController = playerView.gameObject.GetComponent<PlayerController>();
            if (playerController == null)
            {
                CustomLogger.LogError($"RealPlayerWrapper: Cannot notify kill for {NickName}, PlayerController not found");
                return;
            }

            playerController.OnPlayerKilled(killedPlayerName);
            CustomLogger.Log($"RealPlayerWrapper: Notified {NickName} of kill on {killedPlayerName}");
        }
    }

    void Awake()
    {
        boundaryMin = new Vector2(-boundarySize / 2, -boundarySize / 2);
        boundaryMax = new Vector2(boundarySize / 2, boundarySize / 2);
    }

    void Start()
    {
        StartCoroutine(DelayedStart());
    }

    private IEnumerator DelayedStart()
    {
        yield return new WaitForSeconds(1f);

        string currentScene = SceneManager.GetActiveScene().name;
        if (currentScene != "Moon Ran" && currentScene != "TeamMoonRan")
        {
            CustomLogger.Log($"BoundaryManager: Skipping initialization in {currentScene}. Only active in Moon Ran or TeamMoonRan.");
            enabled = false;
            yield break;
        }

        if (cameraFollow == null)
        {
            cameraFollow = Camera.main?.GetComponent<CameraFollow>();
            if (cameraFollow == null)
            {
                CustomLogger.LogError("BoundaryManager: CameraFollow not found on Main Camera");
            }
        }

        StartCoroutine(InitializeOutOfBoundsTextWithRetry());

        if (!PhotonNetwork.IsConnected)
        {
            CustomLogger.LogError("BoundaryManager: Not connected to Photon, disabling.");
            enabled = false;
            yield break;
        }

        ValidatePrefabPaths();

        if (PhotonNetwork.IsMasterClient)
        {
            try
            {
                GameObject boundaryPrefab = Resources.Load<GameObject>("Prefabs/BoundaryVisual");
                if (boundaryPrefab == null)
                {
                    CustomLogger.LogError("BoundaryManager: BoundaryVisual prefab not found at Assets/Resources/Prefabs/BoundaryVisual.prefab");
                }
                else
                {
                    PhotonNetwork.Instantiate("Prefabs/BoundaryVisual", Vector3.zero, Quaternion.identity);
                    CustomLogger.Log("BoundaryManager: Spawned BoundaryVisual for Master Client");
                }
            }
            catch (System.Exception e)
            {
                CustomLogger.LogError($"BoundaryManager: Failed to spawn BoundaryVisual: {e.Message}");
            }
        }

        StartCoroutine(SpawnLocalPlayerWithRetry());
        CustomLogger.Log($"BoundaryManager: Initialized with boundarySize={boundarySize}, boundaryMin={boundaryMin}, boundaryMax={boundaryMax} in {currentScene}");
    }

    private IEnumerator InitializeOutOfBoundsTextWithRetry()
    {
        float waitTime = 0f;
        const float maxWaitTime = 5f;
        const float retryInterval = 0.5f;

        while (waitTime < maxWaitTime && outOfBoundsText == null)
        {
            GameObject canvasObj = GameObject.Find("PlayerUI");
            if (canvasObj == null)
            {
                Canvas[] canvases = Object.FindObjectsByType<Canvas>(FindObjectsSortMode.None);
                foreach (var canvas in canvases)
                {
                    if (canvas.gameObject.name.Contains("PlayerUI"))
                    {
                        canvasObj = canvas.gameObject;
                        CustomLogger.Log($"BoundaryManager: Found PlayerUI canvas via fallback at {GetGameObjectPath(canvasObj)}");
                        break;
                    }
                }
            }

            if (canvasObj != null)
            {
                Transform existingText = canvasObj.transform.Find("OutOfBoundsText");
                if (existingText != null)
                {
                    outOfBoundsText = existingText.GetComponent<TextMeshProUGUI>();
                    CustomLogger.Log($"BoundaryManager: Found existing OutOfBoundsText in PlayerUI at {GetGameObjectPath(existingText.gameObject)}");
                }
                else
                {
                    GameObject textObj = new GameObject("OutOfBoundsText");
                    textObj.transform.SetParent(canvasObj.transform, false);
                    textObj.tag = "OutOfBoundsText";
                    outOfBoundsText = textObj.AddComponent<TextMeshProUGUI>();
                    outOfBoundsText.text = "WARNING: OUT OF BOUNDS!";
                    outOfBoundsText.fontSize = 48;
                    outOfBoundsText.color = Color.red;
                    outOfBoundsText.alignment = TextAlignmentOptions.Center;
                    outOfBoundsText.rectTransform.anchorMin = new Vector2(0.5f, 0.5f);
                    outOfBoundsText.rectTransform.anchorMax = new Vector2(0.5f, 0.5f);
                    outOfBoundsText.rectTransform.anchoredPosition = new Vector2(0, 100);
                    outOfBoundsText.rectTransform.sizeDelta = new Vector2(600, 100);
                    warningTextDefaultScale = outOfBoundsText.transform.localScale;
                    outOfBoundsText.gameObject.SetActive(false);
                    CustomLogger.Log($"BoundaryManager: Created OutOfBoundsText in PlayerUI at {GetGameObjectPath(textObj)}");
                }
                yield break;
            }

            waitTime += retryInterval;
            CustomLogger.Log($"BoundaryManager: PlayerUI not found, retrying after {waitTime:F2}/{maxWaitTime} seconds");
            yield return new WaitForSeconds(retryInterval);
        }

        CustomLogger.LogError("BoundaryManager: Failed to find or create OutOfBoundsText after retries. Ensure a canvas named 'PlayerUI' exists in the scene hierarchy.");
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

    public void OnSignUpSuccess(string data)
    {
        CustomLogger.Log($"BoundaryManager: SignUp success, data={data}");
        string[] parts = data.Split('|');
        if (parts.Length == 2)
        {
            string uid = parts[0];
            string username = parts[1];
            PlayerPrefs.SetString("PlayerUID", uid);
            PlayerPrefs.SetString("Username", username);
            PlayerPrefs.Save();
            CustomLogger.Log($"BoundaryManager: Saved PlayerUID={uid}, Username={username} to PlayerPrefs");
            if (PhotonNetwork.LocalPlayer != null)
            {
                PhotonNetwork.LocalPlayer.NickName = username;
                CustomLogger.Log($"BoundaryManager: Set Photon NickName to {username}");
            }
        }
        else
        {
            CustomLogger.LogError($"BoundaryManager: Invalid SignUp data format: {data}");
        }
    }

    public void OnSignUpFailed(string error)
    {
        CustomLogger.LogError($"BoundaryManager: SignUp failed, error={error}");
    }

    public void OnSignInSuccess(string data)
    {
        CustomLogger.Log($"BoundaryManager: SignIn success, data={data}");
        string[] parts = data.Split('|');
        if (parts.Length == 2)
        {
            string uid = parts[0];
            string username = parts[1];
            PlayerPrefs.SetString("PlayerUID", uid);
            PlayerPrefs.SetString("Username", username);
            PlayerPrefs.Save();
            CustomLogger.Log($"BoundaryManager: Saved PlayerUID={uid}, Username={username} to PlayerPrefs");
            if (PhotonNetwork.LocalPlayer != null)
            {
                PhotonNetwork.LocalPlayer.NickName = username;
                CustomLogger.Log($"BoundaryManager: Set Photon NickName to {username}");
            }
        }
        else
        {
            CustomLogger.LogError($"BoundaryManager: Invalid SignIn data format: {data}");
        }
    }

    public void OnSignInFailed(string error)
    {
        CustomLogger.LogError($"BoundaryManager: SignIn failed, error={error}");
    }

    public void OnBrightMatterLoaded(string data)
    {
        CustomLogger.Log($"BoundaryManager: Received OnBrightMatterLoaded, data={data}");
        // No direct handling in BoundaryManager; event should be processed by PlayerController
    }

    public void OnFuelLoaded(string data)
    {
        CustomLogger.Log($"BoundaryManager: Received OnFuelLoaded, data={data}");
        // No direct handling in BoundaryManager; event should be processed by PlayerController
    }

    private PlayerController FindLocalPlayerController()
    {
        if (PhotonNetwork.LocalPlayer == null || !PhotonNetwork.LocalPlayer.CustomProperties.TryGetValue("PlayerViewID", out object playerViewIDObj))
        {
            return null;
        }

        PhotonView playerView = PhotonView.Find((int)playerViewIDObj);
        return playerView != null ? playerView.GetComponent<PlayerController>() : null;
    }

    private IEnumerator SpawnLocalPlayerWithRetry()
    {
        int maxRetries = 10;
        int retries = 0;
        while (retries < maxRetries && !hasSpawnedLocalPlayer)
        {
            if (PhotonNetwork.LocalPlayer != null && PhotonNetwork.IsConnectedAndReady)
            {
                if (string.IsNullOrEmpty(PhotonNetwork.LocalPlayer.NickName))
                {
                    string username = PlayerPrefs.GetString("Username", "Player_" + Random.Range(1000, 9999));
                    PhotonNetwork.LocalPlayer.NickName = username;
                    CustomLogger.Log($"BoundaryManager: Set NickName to {username}");
                }
                SpawnPlayerAndSpaceship(new RealPlayerWrapper(PhotonNetwork.LocalPlayer));
                hasSpawnedLocalPlayer = true;
                CustomLogger.Log($"BoundaryManager: Spawned local player {PhotonNetwork.LocalPlayer.NickName}, ActorNumber={PhotonNetwork.LocalPlayer.ActorNumber}");
                yield return new WaitForSeconds(1f);
                yield break;
            }
            retries++;
            CustomLogger.Log($"BoundaryManager: Retry {retries}/{maxRetries} to spawn local player. IsConnectedAndReady={PhotonNetwork.IsConnectedAndReady}");
            yield return new WaitForSeconds(1f);
        }
        CustomLogger.LogError("BoundaryManager: Failed to spawn local player after retries.");
    }
    public void ManageBots()
    {
        if (!PhotonNetwork.IsMasterClient)
        {
            CustomLogger.Log("BoundaryManager: ManageBots ignored, not MasterClient");
            return;
        }

        string currentScene = SceneManager.GetActiveScene().name;
        int maxPlayers = currentScene == "TeamMoonRan" ? 10 : 10; // 5v5 for TeamMoonRan, 10 for MoonRan
        int humanPlayerCount = PhotonNetwork.PlayerList.Length;
        BotController[] botControllers = Object.FindObjectsByType<BotController>(FindObjectsSortMode.None);
        int botCount = botControllers.Length;
        int targetBotCount = maxPlayers - humanPlayerCount;

        CustomLogger.Log($"BoundaryManager: ManageBots in {currentScene}, humanPlayers={humanPlayerCount}, bots={botCount}, targetBots={targetBotCount}");

        // Remove excess bots
        if (botCount > targetBotCount)
        {
            List<BotController> bots = botControllers
                .OrderBy(b => b.CustomProperties != null && b.CustomProperties.ContainsKey("Points") ? (int)b.CustomProperties["Points"] : 0)
                .ToList();

            for (int i = 0; i < botCount - targetBotCount; i++)
            {
                BotController bot = bots[i];
                if (bot != null && bot.photonView != null)
                {
                    PhotonNetwork.Destroy(bot.gameObject);
                    CustomLogger.Log($"BoundaryManager: Removed bot {bot.NickName} (ActorNumber={bot.ActorNumber}, Points={(bot.CustomProperties != null && bot.CustomProperties.ContainsKey("Points") ? bot.CustomProperties["Points"] : 0)})");
                }
            }
        }

        // Spawn new bots if needed
        if (botCount < targetBotCount)
        {
            int botsToSpawn = targetBotCount - botCount;
            int redTeamCount = 0;
            int cyanTeamCount = 0;

            foreach (Player player in PhotonNetwork.PlayerList)
            {
                if (player.CustomProperties.TryGetValue("Team", out object team))
                {
                    if (team.ToString() == "Red")
                        redTeamCount++;
                    else if (team.ToString() == "Cyan")
                        cyanTeamCount++;
                }
            }

            foreach (BotController bot in botControllers)
            {
                if (bot.CustomProperties.TryGetValue("Team", out object team))
                {
                    if (team.ToString() == "Red")
                        redTeamCount++;
                    else if (team.ToString() == "Cyan")
                        cyanTeamCount++;
                }
            }

            for (int i = 0; i < botsToSpawn; i++)
            {
                int botActorNumber = -100 - i - botCount; // Negative to avoid conflict with real players
                string team = currentScene == "TeamMoonRan" ? (redTeamCount <= cyanTeamCount ? "Red" : "Cyan") : "None";
                GameObject botObject = PhotonNetwork.Instantiate(botPrefabPath, Vector3.zero, Quaternion.identity);
                BotController botController = botObject.GetComponent<BotController>();
                if (botController == null)
                {
                    CustomLogger.LogError($"BoundaryManager: Bot prefab at Assets/Resources/{botPrefabPath}.prefab is missing BotController component");
                    PhotonNetwork.Destroy(botObject);
                    continue;
                }

                botController.SetActorNumber(botActorNumber); // Use SetActorNumber instead of direct assignment
                botController.NickName = $"Bot_{(botCount + i + 1)}";
                botController.CustomProperties = new ExitGames.Client.Photon.Hashtable
            {
                { "Username", botController.NickName },
                { "Nickname", botController.NickName },
                { "Points", 0 },
                { "Team", team },
                { "PartyID", "" }
            };
                botController.photonView.RPC("UpdateCustomProperties", RpcTarget.AllBuffered, botController.CustomProperties);
                SpawnPlayerAndSpaceship(botController);
                CustomLogger.Log($"BoundaryManager: Spawned bot {botController.NickName} (ActorNumber={botActorNumber}, Team={team})");

                if (team == "Red") redTeamCount++;
                else if (team == "Cyan") cyanTeamCount++;
            }
        }

        ScoreboardManager scoreboard = Object.FindFirstObjectByType<ScoreboardManager>(FindObjectsInactive.Include);
        if (scoreboard != null)
        {
            scoreboard.UpdateScoreboard();
            CustomLogger.Log("BoundaryManager: Triggered ScoreboardManager.UpdateScoreboard after bot management");
        }
    }

    private IEnumerator InitializeSpaceshipWithRetry(PhotonView spaceshipView, IPlayer player, int ownerId, bool isBot)
    {
        int maxRetries = 10;
        int retries = 0;
        float retryDelay = 0.5f;

        while (retries < maxRetries)
        {
            if (spaceshipView != null && spaceshipView.gameObject.activeInHierarchy && PhotonNetwork.IsConnectedAndReady)
            {
                try
                {
                    spaceshipView.RPC("InitializeShip", RpcTarget.AllBuffered, ownerId);
                    CustomLogger.Log($"BoundaryManager: Sent InitializeShip RPC for {player.NickName}, ActorNumber={ownerId}, ViewID={spaceshipView.ViewID}, IsMine={spaceshipView.IsMine}");
                    ExitGames.Client.Photon.Hashtable props = new ExitGames.Client.Photon.Hashtable
                {
                    { "SpaceshipViewID", spaceshipView.ViewID }
                };
                    player.SetCustomProperties(props);
                    yield break;
                }
                catch (System.Exception e)
                {
                    CustomLogger.LogError($"BoundaryManager: Failed to call InitializeShip RPC for {player.NickName}, ViewID={spaceshipView.ViewID}: {e.Message}, StackTrace={e.StackTrace}");
                }
            }
            else
            {
                CustomLogger.LogWarning($"BoundaryManager: Cannot send InitializeShip RPC for {player.NickName}, ViewID={(spaceshipView != null ? spaceshipView.ViewID : -1)}, Active={spaceshipView?.gameObject.activeInHierarchy}, PhotonConnected={PhotonNetwork.IsConnectedAndReady}");
            }

            retries++;
            CustomLogger.LogWarning($"BoundaryManager: Retry {retries}/{maxRetries} to initialize spaceship for {player.NickName}, ViewID={(spaceshipView != null ? spaceshipView.ViewID : -1)}");
            yield return new WaitForSeconds(retryDelay);
        }

        CustomLogger.LogError($"BoundaryManager: Failed to initialize spaceship for {player.NickName} after {maxRetries} retries, ViewID={(spaceshipView != null ? spaceshipView.ViewID : -1)}");
        if (spaceshipView != null && spaceshipView.IsMine && PhotonNetwork.LocalPlayer != null)
        {
            SpaceshipMarker marker = spaceshipView.GetComponent<SpaceshipMarker>();
            if (marker != null)
            {
                marker.ownerId = PhotonNetwork.LocalPlayer.ActorNumber;
                marker.TriggerColorUpdate();
                CustomLogger.Log($"BoundaryManager: Fallback set ownerId={marker.ownerId} for local spaceship ViewID={spaceshipView.ViewID}, colored green");
            }
        }
        else if (spaceshipView != null)
        {
            CustomLogger.Log($"BoundaryManager: Keeping spaceship ViewID={spaceshipView.ViewID} for {player.NickName} despite initialization failure, likely a bot or non-local player");
        }
    }
    public void SpawnPlayerAndSpaceship(IPlayer player)
    {
        if (!PhotonNetwork.IsConnectedAndReady || player == null)
        {
            CustomLogger.LogError($"BoundaryManager: Cannot spawn, Photon not ready or player is null");
            return;
        }

        GameObject playerObject = null;
        PhotonView pv = null;
        if (player.IsLocal)
        {
            playerObject = PhotonNetwork.Instantiate(playerPrefabPath, Vector3.zero, Quaternion.identity);
            if (playerObject == null)
            {
                CustomLogger.LogError($"BoundaryManager: Failed to instantiate player at Assets/Resources/{playerPrefabPath}.prefab for {player.NickName}");
                return;
            }
            pv = playerObject.GetComponent<PhotonView>();
            if (pv == null)
            {
                CustomLogger.LogError($"BoundaryManager: Player prefab missing PhotonView for {player.NickName}");
                PhotonNetwork.Destroy(playerObject);
                return;
            }
            PhotonNetwork.LocalPlayer.TagObject = playerObject;
            playerObject.GetComponent<PlayerController>().CustomProperties["PlayerViewID"] = pv.ViewID;
            player.CustomProperties["PlayerViewID"] = pv.ViewID;
            CustomLogger.Log($"BoundaryManager: Set PlayerViewID={pv.ViewID} and TagObject for local player {player.NickName}");
        }
        else
        {
            playerObject = GameObject.FindGameObjectsWithTag(player is BotController ? "Bot" : "Player")
                .FirstOrDefault(go => go.GetComponent<IPlayer>()?.ActorNumber == player.ActorNumber);
            if (playerObject == null || !playerObject.activeInHierarchy)
            {
                CustomLogger.LogError($"BoundaryManager: Player/Bot not found or inactive for ActorNumber={player.ActorNumber}, NickName={player.NickName}");
                return;
            }
            pv = playerObject.GetComponent<PhotonView>();
            if (pv == null)
            {
                CustomLogger.LogError($"BoundaryManager: Player/Bot object missing PhotonView for {player.NickName}");
                return;
            }
        }

        PlayerHealth.Team assignedTeam = PlayerHealth.Team.None;
        string partyId = player.CustomProperties.ContainsKey("PartyID") ? player.CustomProperties["PartyID"].ToString() : "";
        if (SceneManager.GetActiveScene().name == "TeamMoonRan")
        {
            int redTeamCount = 0;
            int cyanTeamCount = 0;
            foreach (Player p in PhotonNetwork.PlayerList)
            {
                if (p.CustomProperties.TryGetValue("Team", out object team))
                {
                    if (team.ToString() == "Red")
                        redTeamCount++;
                    else if (team.ToString() == "Cyan")
                        cyanTeamCount++;
                }
            }
            foreach (BotController bot in Object.FindObjectsByType<BotController>(FindObjectsSortMode.None))
            {
                if (bot.CustomProperties.TryGetValue("Team", out object team))
                {
                    if (team.ToString() == "Red")
                        redTeamCount++;
                    else if (team.ToString() == "Cyan")
                        cyanTeamCount++;
                }
            }

            if (!string.IsNullOrEmpty(partyId))
            {
                var partyLeader = PhotonNetwork.PlayerList.FirstOrDefault(p => p.CustomProperties.ContainsKey("PartyID") &&
                                                                              p.CustomProperties["PartyID"].ToString() == partyId &&
                                                                              p.CustomProperties.ContainsKey("IsPartyLeader") &&
                                                                              (bool)p.CustomProperties["IsPartyLeader"]);
                assignedTeam = partyLeader != null ? (partyLeader.ActorNumber % 2 == 0 ? PlayerHealth.Team.Red : PlayerHealth.Team.Cyan) :
                                                    (redTeamCount <= cyanTeamCount ? PlayerHealth.Team.Red : PlayerHealth.Team.Cyan);
            }
            else
            {
                assignedTeam = redTeamCount <= cyanTeamCount ? PlayerHealth.Team.Red : PlayerHealth.Team.Cyan;
            }
            player.CustomProperties["Team"] = assignedTeam.ToString();
            player.CustomProperties["PartyID"] = partyId;
            CustomLogger.Log($"BoundaryManager: Assigned {player.NickName} to team {assignedTeam}, RedCount={redTeamCount}, CyanCount={cyanTeamCount}, PartyID={partyId}");
        }

        Vector3 spaceshipPosition = Vector3.zero;
        const int maxAttempts = 20;
        int attempts = 0;
        bool validPosition = false;
        float safeBoundary = boundarySize / 2 - bufferDistance;
        float gridSize = minSpaceshipSpacing * 2f;
        List<Vector2> candidatePositions = new List<Vector2>();

        for (float x = -safeBoundary; x <= safeBoundary; x += gridSize)
        {
            for (float y = -safeBoundary; y <= safeBoundary; y += gridSize)
            {
                candidatePositions.Add(new Vector2(x, y));
            }
        }
        candidatePositions = candidatePositions.OrderBy(pos => Random.value).ToList();

        while (attempts < maxAttempts && !validPosition && candidatePositions.Count > 0)
        {
            spaceshipPosition = candidatePositions[0];
            candidatePositions.RemoveAt(0);
            spaceshipPosition += new Vector3(Random.Range(-gridSize / 2, gridSize / 2), Random.Range(-gridSize / 2, gridSize / 2), 0f);
            spaceshipPosition.x = Mathf.Clamp(spaceshipPosition.x, -safeBoundary, safeBoundary);
            spaceshipPosition.y = Mathf.Clamp(spaceshipPosition.y, -safeBoundary, safeBoundary);
            validPosition = IsValidSpaceshipPosition(spaceshipPosition, player.ActorNumber);
            attempts++;
            CustomLogger.Log($"BoundaryManager: Spaceship spawn attempt {attempts}/{maxAttempts} for {player.NickName}, position={spaceshipPosition}, valid={validPosition}");
        }

        if (!validPosition)
        {
            CustomLogger.LogWarning($"BoundaryManager: Could not find valid spaceship position for {player.NickName} after {maxAttempts} attempts, trying fallback");
            spaceshipPosition = new Vector3(Random.Range(-safeBoundary, safeBoundary), Random.Range(-safeBoundary, safeBoundary), 0f);
            int fallbackAttempts = 5;
            attempts = 0;
            while (attempts < fallbackAttempts && !validPosition)
            {
                spaceshipPosition += new Vector3(Random.Range(-minSpaceshipSpacing, minSpaceshipSpacing), Random.Range(-minSpaceshipSpacing, minSpaceshipSpacing), 0f);
                spaceshipPosition.x = Mathf.Clamp(spaceshipPosition.x, -safeBoundary, safeBoundary);
                spaceshipPosition.y = Mathf.Clamp(spaceshipPosition.y, -safeBoundary, safeBoundary);
                validPosition = IsValidSpaceshipPosition(spaceshipPosition, player.ActorNumber);
                attempts++;
                CustomLogger.Log($"BoundaryManager: Fallback attempt {attempts}/{fallbackAttempts} for {player.NickName}, position={spaceshipPosition}, valid={validPosition}");
            }
            if (!validPosition)
            {
                spaceshipPosition = new Vector3(Random.Range(-safeBoundary / 2, safeBoundary / 2), Random.Range(-safeBoundary / 2, safeBoundary / 2), 0f);
                CustomLogger.LogWarning($"BoundaryManager: All fallback attempts failed for {player.NickName}, using central position={spaceshipPosition}");
            }
        }

        GameObject spaceship = PhotonNetwork.Instantiate(spaceshipPrefabPath, spaceshipPosition, Quaternion.identity);
        if (spaceship == null)
        {
            CustomLogger.LogError($"BoundaryManager: Failed to instantiate spaceship at Assets/Resources/{spaceshipPrefabPath}.prefab for {player.NickName}");
            if (player.IsLocal) PhotonNetwork.Destroy(playerObject);
            return;
        }

        PhotonView spaceshipView = spaceship.GetComponent<PhotonView>();
        SpaceshipMarker spaceshipMarker = spaceship.GetComponent<SpaceshipMarker>();
        if (spaceshipView == null || spaceshipMarker == null)
        {
            CustomLogger.LogError($"BoundaryManager: Spaceship missing PhotonView or SpaceshipMarker for {player.NickName}");
            if (spaceship != null) PhotonNetwork.Destroy(spaceship);
            if (player.IsLocal) PhotonNetwork.Destroy(playerObject);
            return;
        }

        bool isBot = player is BotController;
        if (isBot && player is not BotController)
        {
            CustomLogger.LogError($"BoundaryManager: Invalid bot player with ActorNumber={player.ActorNumber}, NickName={player.NickName}. Aborting spawn.");
            PhotonNetwork.Destroy(spaceship);
            if (player.IsLocal) PhotonNetwork.Destroy(playerObject);
            return;
        }
        int ownerId = player.ActorNumber;
        StartCoroutine(InitializeSpaceshipWithRetry(spaceshipView, player, ownerId, isBot));

        spaceshipPositions.Add(spaceshipPosition);
        CustomLogger.Log($"BoundaryManager: Added spaceship position {spaceshipPosition} to spaceshipPositions list, total={spaceshipPositions.Count}");

        Vector3 playerPosition = Vector3.zero;
        attempts = 0;
        validPosition = false;
        float spawnDist = player.IsLocal ? spawnDistance : 25f;
        float minSpacing = player.IsLocal ? minBotSpacing : 10f;

        while (attempts < maxAttempts && !validPosition)
        {
            Vector2 offset = Random.insideUnitCircle.normalized * Random.Range(5f, spawnDist);
            playerPosition = spaceshipPosition + new Vector3(offset.x, offset.y, 0f);
            playerPosition.x = Mathf.Clamp(playerPosition.x, -safeBoundary, safeBoundary);
            playerPosition.y = Mathf.Clamp(playerPosition.y, -safeBoundary, safeBoundary);
            validPosition = player.IsLocal ? IsValidPlayerPosition(playerPosition, player.ActorNumber) : IsValidBotPosition(playerPosition, player.ActorNumber, minSpacing);
            attempts++;
            CustomLogger.Log($"BoundaryManager: {(player.IsLocal ? "Player" : "Bot")} spawn attempt {attempts}/{maxAttempts} for {player.NickName}, position={playerPosition}, valid={validPosition}");
        }

        if (!validPosition)
        {
            CustomLogger.LogWarning($"BoundaryManager: Could not find valid player/bot position for {player.NickName} after {maxAttempts} attempts, trying fallback");
            float fallbackDistance = spawnDistance;
            for (int i = 0; i < 5 && !validPosition; i++)
            {
                Vector2 offset = Random.insideUnitCircle.normalized * fallbackDistance;
                playerPosition = spaceshipPosition + new Vector3(offset.x, offset.y, 0f);
                playerPosition.x = Mathf.Clamp(playerPosition.x, -safeBoundary, safeBoundary);
                playerPosition.y = Mathf.Clamp(playerPosition.y, -safeBoundary, safeBoundary);
                validPosition = IsValidBotPosition(playerPosition, player.ActorNumber, minSpacing);
                CustomLogger.Log($"BoundaryManager: Fallback attempt {i + 1}/5 for {player.NickName}, position={playerPosition}, valid={validPosition}, fallbackDistance={fallbackDistance}");
                fallbackDistance += 5f;
            }
            if (!validPosition)
            {
                playerPosition = spaceshipPosition + new Vector3(5f, 5f, 0f);
                playerPosition.x = Mathf.Clamp(playerPosition.x, -safeBoundary, safeBoundary);
                playerPosition.y = Mathf.Clamp(playerPosition.y, -safeBoundary, safeBoundary);
                CustomLogger.LogWarning($"BoundaryManager: All fallback attempts failed for {player.NickName}, using minimal offset position={playerPosition}");
            }
        }

        playerObject.transform.position = playerPosition;
        CustomLogger.Log($"BoundaryManager: Set {player.NickName} position to {playerPosition}");

        if (player.IsLocal)
        {
            PlayerController playerController = playerObject.GetComponent<PlayerController>();
            if (playerController == null)
            {
                CustomLogger.LogError($"BoundaryManager: Player prefab at Assets/Resources/{playerPrefabPath}.prefab is missing PlayerController component");
                PhotonNetwork.Destroy(playerObject);
                PhotonNetwork.Destroy(spaceship);
                return;
            }
            playerController.NickName = player.NickName;
            playerController.CustomProperties["Username"] = player.NickName;
            playerController.CustomProperties["Points"] = player.CustomProperties.ContainsKey("Points") ? player.CustomProperties["Points"] : 0;
            playerController.CustomProperties["SpaceshipViewID"] = spaceshipView.ViewID;
            playerController.CustomProperties["PartyID"] = partyId;
            if (assignedTeam != PlayerHealth.Team.None)
            {
                playerController.SetTeam(assignedTeam);
                playerController.CustomProperties["Team"] = assignedTeam.ToString();
            }
            player.SetCustomProperties(new ExitGames.Client.Photon.Hashtable
        {
            { "SpaceshipViewID", spaceshipView.ViewID },
            { "Team", assignedTeam.ToString() },
            { "PartyID", partyId }
        });
            CustomLogger.Log($"BoundaryManager: Set SpaceshipViewID={spaceshipView.ViewID}, PlayerViewID={pv.ViewID}, Team={assignedTeam}, PartyID={partyId}, Points={playerController.CustomProperties["Points"]} for local player {player.NickName}");

            if (cameraFollow != null)
            {
                cameraFollow.ForceRetargetPlayer();
                CustomLogger.Log($"BoundaryManager: Triggered CameraFollow to retarget for {player.NickName}");
            }
            else
            {
                CustomLogger.LogWarning($"BoundaryManager: CameraFollow not assigned, camera may not target player {player.NickName} correctly.");
            }
        }
        else
        {
            BotController botController = playerObject.GetComponent<BotController>();
            if (botController != null && assignedTeam != PlayerHealth.Team.None)
            {
                botController.SetTeam(assignedTeam);
                botController.CustomProperties["Team"] = assignedTeam.ToString();
                botController.CustomProperties["Points"] = player.CustomProperties.ContainsKey("Points") ? player.CustomProperties["Points"] : 0;
                player.SetCustomProperties(new ExitGames.Client.Photon.Hashtable
            {
                { "Team", assignedTeam.ToString() },
                { "PartyID", "" },
                { "Points", player.CustomProperties.ContainsKey("Points") ? player.CustomProperties["Points"] : 0 }
            });
                CustomLogger.Log($"BoundaryManager: Set Team={assignedTeam}, PartyID=, Points={player.CustomProperties["Points"]} for bot {player.NickName}");
            }
        }

        RandomPlanetGenerator generator = Object.FindFirstObjectByType<RandomPlanetGenerator>(FindObjectsInactive.Include);
        if (generator != null)
        {
            generator.AddPlayer(player.ActorNumber, playerObject);
            CustomLogger.Log($"BoundaryManager: Notified RandomPlanetGenerator for {player.NickName}, ActorNumber={player.ActorNumber}");
        }
        else
        {
            CustomLogger.LogWarning($"BoundaryManager: RandomPlanetGenerator not found for {player.NickName}, planets may not spawn");
        }

        ScoreboardManager scoreboard = Object.FindFirstObjectByType<ScoreboardManager>(FindObjectsInactive.Include);
        if (scoreboard != null)
        {
            scoreboard.UpdateScoreboard();
            CustomLogger.Log($"BoundaryManager: Triggered ScoreboardManager.UpdateScoreboard for {player.NickName}");
        }
    }
    private bool IsValidSpaceshipPosition(Vector3 position, int actorNumber)
    {
        foreach (Vector2 existingPos in spaceshipPositions)
        {
            if (Vector2.Distance(position, existingPos) < minSpaceshipSpacing)
            {
                CustomLogger.Log($"BoundaryManager: Spaceship position {position} too close to existing spaceship at {existingPos}, distance={Vector2.Distance(position, existingPos):F2}");
                return false;
            }
        }

        GameObject[] bots = GameObject.FindGameObjectsWithTag("Bot");
        foreach (GameObject bot in bots)
        {
            if (bot.activeInHierarchy)
            {
                BotController botController = bot.GetComponent<BotController>();
                if (botController != null && botController.ActorNumber != actorNumber)
                {
                    float distance = Vector3.Distance(position, bot.transform.position);
                    if (distance < minSpaceshipSpacing)
                    {
                        CustomLogger.Log($"BoundaryManager: Spaceship position {position} too close to bot {botController.NickName} at {bot.transform.position}, distance={distance:F2}");
                        return false;
                    }
                }
            }
        }

        GameObject[] spaceships = GameObject.FindGameObjectsWithTag("SpaceShip");
        foreach (GameObject ship in spaceships)
        {
            if (ship.activeInHierarchy)
            {
                SpaceshipMarker marker = ship.GetComponent<SpaceshipMarker>();
                if (marker != null && marker.ownerId != actorNumber)
                {
                    float distance = Vector3.Distance(position, ship.transform.position);
                    if (distance < minSpaceshipSpacing)
                    {
                        CustomLogger.Log($"BoundaryManager: Spaceship position {position} too close to spaceship (ownerId={marker.ownerId}) at {ship.transform.position}, distance={distance:F2}");
                        return false;
                    }
                }
            }
        }

        return true;
    }

    public bool IsValidBotPosition(Vector3 position, int actorNumber)
    {
        GameObject[] bots = GameObject.FindGameObjectsWithTag("Bot");
        foreach (GameObject bot in bots)
        {
            if (bot.activeInHierarchy)
            {
                BotController botController = bot.GetComponent<BotController>();
                if (botController != null && botController.ActorNumber != actorNumber)
                {
                    float distance = Vector3.Distance(position, bot.transform.position);
                    if (distance < minBotSpacing)
                    {
                        CustomLogger.Log($"BoundaryManager: Bot position {position} too close to bot {botController.NickName} at {bot.transform.position}, distance={distance:F2}, minBotSpacing={minBotSpacing}");
                        return false;
                    }
                }
            }
        }

        GameObject[] spaceships = GameObject.FindGameObjectsWithTag("SpaceShip");
        foreach (GameObject ship in spaceships)
        {
            if (ship.activeInHierarchy)
            {
                SpaceshipMarker marker = ship.GetComponent<SpaceshipMarker>();
                if (marker != null && marker.ownerId != actorNumber)
                {
                    float distance = Vector3.Distance(position, ship.transform.position);
                    if (distance < minBotSpacing)
                    {
                        CustomLogger.Log($"BoundaryManager: Bot position {position} too close to spaceship (ownerId={marker.ownerId}) at {ship.transform.position}, distance={distance:F2}, minBotSpacing={minBotSpacing}");
                        return false;
                    }
                }
            }
        }

        return true;
    }

    public bool IsValidBotPosition(Vector3 position, int actorNumber, float customMinSpacing)
    {
        GameObject[] bots = GameObject.FindGameObjectsWithTag("Bot");
        foreach (GameObject bot in bots)
        {
            if (bot.activeInHierarchy)
            {
                BotController botController = bot.GetComponent<BotController>();
                if (botController != null && botController.ActorNumber != actorNumber)
                {
                    float distance = Vector3.Distance(position, bot.transform.position);
                    if (distance < customMinSpacing)
                    {
                        CustomLogger.Log($"BoundaryManager: Bot position {position} too close to bot {botController.NickName} at {bot.transform.position}, distance={distance:F2}, customMinSpacing={customMinSpacing}");
                        return false;
                    }
                }
            }
        }

        GameObject[] spaceships = GameObject.FindGameObjectsWithTag("SpaceShip");
        foreach (GameObject ship in spaceships)
        {
            if (ship.activeInHierarchy)
            {
                SpaceshipMarker marker = ship.GetComponent<SpaceshipMarker>();
                if (marker != null && marker.ownerId != actorNumber)
                {
                    float distance = Vector3.Distance(position, ship.transform.position);
                    if (distance < customMinSpacing)
                    {
                        CustomLogger.Log($"BoundaryManager: Bot position {position} too close to spaceship (ownerId={marker.ownerId}) at {ship.transform.position}, distance={distance:F2}, customMinSpacing={customMinSpacing}");
                        return false;
                    }
                }
            }
        }

        return true;
    }

    public bool IsValidPlayerPosition(Vector3 position, int actorNumber)
    {
        GameObject[] players = GameObject.FindGameObjectsWithTag("Player");
        foreach (GameObject player in players)
        {
            if (player.activeInHierarchy)
            {
                PlayerController playerController = player.GetComponent<PlayerController>();
                if (playerController != null && playerController.ActorNumber != actorNumber)
                {
                    float distance = Vector3.Distance(position, playerController.transform.position);
                    if (distance < minSpaceshipSpacing)
                    {
                        CustomLogger.Log($"BoundaryManager: Player position {position} too close to player {playerController.NickName} at {player.transform.position}, distance={distance:F2}");
                        return false;
                    }
                }
            }
        }

        GameObject[] bots = GameObject.FindGameObjectsWithTag("Bot");
        foreach (GameObject bot in bots)
        {
            if (bot.activeInHierarchy)
            {
                BotController botController = bot.GetComponent<BotController>();
                if (botController != null && botController.ActorNumber != actorNumber)
                {
                    float distance = Vector3.Distance(position, bot.transform.position);
                    if (distance < minSpaceshipSpacing)
                    {
                        CustomLogger.Log($"BoundaryManager: Player position {position} too close to bot {botController.NickName} at {bot.transform.position}, distance={distance:F2}");
                        return false;
                    }
                }
            }
        }

        GameObject[] spaceships = GameObject.FindGameObjectsWithTag("SpaceShip");
        foreach (GameObject ship in spaceships)
        {
            if (ship.activeInHierarchy)
            {
                SpaceshipMarker marker = ship.GetComponent<SpaceshipMarker>();
                if (marker != null && marker.ownerId != actorNumber)
                {
                    float distance = Vector3.Distance(position, ship.transform.position);
                    if (distance < minSpaceshipSpacing)
                    {
                        CustomLogger.Log($"BoundaryManager: Player position {position} too close to spaceship (ownerId={marker.ownerId}) at {ship.transform.position}, distance={distance:F2}");
                        return false;
                    }
                }
            }
        }

        return true;
    }

    void Update()
    {
        if (!PhotonNetwork.IsConnected)
        {
            CustomLogger.LogWarning("BoundaryManager: Photon disconnected, attempting to reconnect.");
            PhotonNetwork.ConnectUsingSettings();
            return;
        }

        if (!hasSpawnedLocalPlayer || cameraFollow == null || cameraFollow.target == null)
            return;

        Vector3 playerPosition = cameraFollow.target.position;
        bool wasOutOfBounds = isOutOfBounds;
        isOutOfBounds = playerPosition.x < boundaryMin.x || playerPosition.x > boundaryMax.x ||
                        playerPosition.y < boundaryMin.y || playerPosition.y > boundaryMax.y;

        if (outOfBoundsText != null)
        {
            if (isOutOfBounds)
            {
                if (!wasOutOfBounds)
                {
                    CustomLogger.Log($"BoundaryManager: Player {PhotonNetwork.LocalPlayer.NickName} out of bounds at {playerPosition}");
                    outOfBoundsText.gameObject.SetActive(true);
                }

                warningFlashTimer += Time.deltaTime;
                if (warningFlashTimer >= 0.5f)
                {
                    isWarningRed = !isWarningRed;
                    outOfBoundsText.color = isWarningRed ? Color.red : Color.white;
                    float scale = isWarningRed ? 1.2f : 1.0f;
                    outOfBoundsText.transform.localScale = warningTextDefaultScale * scale;
                    warningFlashTimer = 0f;
                }

                damageTimer += Time.deltaTime;
                if (damageTimer >= 1f)
                {
                    if (PhotonNetwork.LocalPlayer != null && PhotonNetwork.LocalPlayer.CustomProperties.TryGetValue("PlayerViewID", out object playerViewIDObj))
                    {
                        PhotonView playerView = PhotonView.Find((int)playerViewIDObj);
                        if (playerView != null)
                        {
                            PlayerHealth playerHealth = playerView.GetComponent<PlayerHealth>();
                            if (playerHealth != null)
                            {
                                playerHealth.TakeDamage(6, true, -1, PlayerHealth.DeathCause.OutOfBounds);
                                CustomLogger.Log($"BoundaryManager: Applied 6 damage to {PhotonNetwork.LocalPlayer.NickName} for being out of bounds");
                            }
                        }
                    }
                    damageTimer = 0f;
                }
            }
            else
            {
                if (wasOutOfBounds)
                {
                    CustomLogger.Log($"BoundaryManager: Player {PhotonNetwork.LocalPlayer.NickName} returned to bounds at {playerPosition}");
                    outOfBoundsText.gameObject.SetActive(false);
                    outOfBoundsText.color = Color.red;
                    outOfBoundsText.transform.localScale = warningTextDefaultScale;
                }
                damageTimer = 0f;
                warningFlashTimer = 0f;
                isWarningRed = true;
            }
        }
        else if (isOutOfBounds)
        {
            CustomLogger.LogWarning($"BoundaryManager: Player {PhotonNetwork.LocalPlayer?.NickName} at {playerPosition}, but OutOfBoundsText not instantiated");
        }
    }

    public override void OnPlayerLeftRoom(Player otherPlayer)
    {
        base.OnPlayerLeftRoom(otherPlayer);
        if (PhotonNetwork.IsMasterClient)
        {
            CustomLogger.Log($"BoundaryManager: Player {otherPlayer.NickName} (ActorNumber={otherPlayer.ActorNumber}) left room {PhotonNetwork.CurrentRoom.Name}");
            RandomPlanetGenerator generator = Object.FindFirstObjectByType<RandomPlanetGenerator>(FindObjectsInactive.Include);
            if (generator != null)
            {
                generator.RemovePlayer(otherPlayer.ActorNumber);
                CustomLogger.Log($"BoundaryManager: Removed player {otherPlayer.NickName} from RandomPlanetGenerator");
            }

            GameObject[] spaceships = GameObject.FindGameObjectsWithTag("SpaceShip");
            foreach (GameObject spaceship in spaceships)
            {
                SpaceshipMarker marker = spaceship.GetComponent<SpaceshipMarker>();
                if (marker != null && marker.ownerId == otherPlayer.ActorNumber)
                {
                    spaceshipPositions.Remove(spaceship.transform.position);
                    PhotonNetwork.Destroy(spaceship);
                    CustomLogger.Log($"BoundaryManager: Destroyed spaceship for {otherPlayer.NickName}, ownerId={marker.ownerId}");
                }
            }
        }
    }

    public override void OnDisconnected(DisconnectCause cause)
    {
        base.OnDisconnected(cause);
        CustomLogger.LogError($"BoundaryManager: Disconnected from PhotonNetwork, cause={cause}. Attempting to reconnect.");
        PhotonNetwork.Reconnect();
    }

    private void ValidatePrefabPaths()
    {
        if (!Resources.Load<GameObject>(playerPrefabPath))
            CustomLogger.LogError($"BoundaryManager: Player prefab not found at Assets/Resources/{playerPrefabPath}.prefab");
        if (!Resources.Load<GameObject>(botPrefabPath))
            CustomLogger.LogError($"BoundaryManager: Bot prefab not found at Assets/Resources/{botPrefabPath}.prefab");
        if (!Resources.Load<GameObject>(spaceshipPrefabPath))
            CustomLogger.LogError($"BoundaryManager: SpaceShip prefab not found at Assets/Resources/{spaceshipPrefabPath}.prefab");
        if (!Resources.Load<GameObject>("Prefabs/BoundaryVisual"))
            CustomLogger.LogError($"BoundaryManager: Boundary prefab not found at Assets/Resources/Prefabs/BoundaryVisual.prefab");
    }


    public override void OnPlayerEnteredRoom(Player newPlayer)
    {
        base.OnPlayerEnteredRoom(newPlayer);
        CustomLogger.Log($"BoundaryManager: Player {newPlayer.NickName} (ActorNumber={newPlayer.ActorNumber}) entered room.");

        if (newPlayer.IsLocal)
        {
            CustomLogger.Log("BoundaryManager: Local player detected, spawn handled by Start()");
        }
        else if (PhotonNetwork.IsMasterClient)
        {
            string currentScene = SceneManager.GetActiveScene().name;
            List<IPlayer> allPlayers = PhotonNetwork.PlayerList
                .Select(p => new RealPlayerWrapper(p) as IPlayer)
                .Concat(Object.FindObjectsByType<BotController>(FindObjectsSortMode.None))
                .ToList();

            int humanPlayerCount = PhotonNetwork.PlayerList.Length;
            if (humanPlayerCount > 10)
            {
                CustomLogger.LogWarning($"BoundaryManager: Room has {humanPlayerCount} players, exceeding max of 10, cannot add {newPlayer.NickName}");
                return;
            }

            // Find and replace lowest-scoring bot
            BotController botToReplace = null;
            int lowestPoints = int.MaxValue;
            string team = currentScene == "TeamMoonRan" ? (newPlayer.CustomProperties.ContainsKey("PartyID") && !string.IsNullOrEmpty(newPlayer.CustomProperties["PartyID"]?.ToString()) ?
                (PhotonNetwork.PlayerList.FirstOrDefault(p => p.CustomProperties.ContainsKey("PartyID") && p.CustomProperties["PartyID"].ToString() == newPlayer.CustomProperties["PartyID"].ToString() && p.CustomProperties.ContainsKey("IsPartyLeader") && (bool)p.CustomProperties["IsPartyLeader"])?.ActorNumber % 2 == 0 ? "Red" : "Cyan") :
                (humanPlayerCount % 2 == 0 ? "Red" : "Cyan")) : "None";

            foreach (BotController bot in Object.FindObjectsByType<BotController>(FindObjectsSortMode.None))
            {
                if (currentScene == "TeamMoonRan" && bot.CustomProperties["Team"]?.ToString() != team)
                    continue;

                int points = bot.CustomProperties != null && bot.CustomProperties.ContainsKey("Points") ? (int)bot.CustomProperties["Points"] : 0;
                if (points < lowestPoints)
                {
                    lowestPoints = points;
                    botToReplace = bot;
                }
            }

            if (botToReplace != null)
            {
                int botPoints = botToReplace.CustomProperties != null && botToReplace.CustomProperties.ContainsKey("Points") ? (int)botToReplace.CustomProperties["Points"] : 0;
                string botTeam = botToReplace.CustomProperties != null && botToReplace.CustomProperties.ContainsKey("Team") ? botToReplace.CustomProperties["Team"].ToString() : "None";
                Vector3 botPosition = botToReplace.transform.position;

                // Destroy bot's spaceship
                GameObject[] spaceships = GameObject.FindGameObjectsWithTag("SpaceShip");
                foreach (GameObject spaceship in spaceships)
                {
                    SpaceshipMarker marker = spaceship.GetComponent<SpaceshipMarker>();
                    if (marker != null && marker.ownerId == botToReplace.ActorNumber)
                    {
                        spaceshipPositions.Remove(spaceship.transform.position);
                        PhotonNetwork.Destroy(spaceship);
                        CustomLogger.Log($"BoundaryManager: Destroyed spaceship for bot {botToReplace.NickName} (ActorNumber={botToReplace.ActorNumber})");
                    }
                }

                // Destroy bot
                PhotonNetwork.Destroy(botToReplace.gameObject);
                CustomLogger.Log($"BoundaryManager: Replaced bot {botToReplace.NickName} (ActorNumber={botToReplace.ActorNumber}, Points={botPoints}, Team={botTeam}) with player {newPlayer.NickName}");

                // Assign bot's points to new player in TeamMoonRan
                if (currentScene == "TeamMoonRan")
                {
                    newPlayer.SetCustomProperties(new ExitGames.Client.Photon.Hashtable
                {
                    { "Points", botPoints },
                    { "Team", botTeam },
                    { "PartyID", newPlayer.CustomProperties.ContainsKey("PartyID") ? newPlayer.CustomProperties["PartyID"] : "" }
                });
                }
                else
                {
                    newPlayer.SetCustomProperties(new ExitGames.Client.Photon.Hashtable
                {
                    { "Points", 0 },
                    { "Team", "None" },
                    { "PartyID", newPlayer.CustomProperties.ContainsKey("PartyID") ? newPlayer.CustomProperties["PartyID"] : "" }
                });
                }

                // Spawn new player at bot's position
                SpawnPlayerAndSpaceship(new RealPlayerWrapper(newPlayer));
                newPlayer.SetCustomProperties(new ExitGames.Client.Photon.Hashtable
            {
                { "Username", newPlayer.NickName },
                { "Nickname", newPlayer.NickName }
            });
                CustomLogger.Log($"BoundaryManager: Spawned {newPlayer.NickName} at position {botPosition}, Points={(currentScene == "TeamMoonRan" ? botPoints : 0)}, Team={botTeam}");
            }
            else
            {
                CustomLogger.Log($"BoundaryManager: No bot available to replace, spawning {newPlayer.NickName} normally");
                SpawnPlayerAndSpaceship(new RealPlayerWrapper(newPlayer));
            }

            ScoreboardManager scoreboard = Object.FindFirstObjectByType<ScoreboardManager>(FindObjectsInactive.Include);
            if (scoreboard != null)
            {
                scoreboard.UpdateScoreboard();
                CustomLogger.Log($"BoundaryManager: Triggered ScoreboardManager.UpdateScoreboard for {newPlayer.NickName}");
            }
        }
    }

    public void ResetBoundary()
    {
        if (!PhotonNetwork.IsMasterClient)
        {
            CustomLogger.Log("BoundaryManager: ResetBoundary ignored, not MasterClient");
            return;
        }

        if (photonView == null)
        {
            CustomLogger.LogError("BoundaryManager: Cannot send PerformBoundaryReset RPC, PhotonView component is missing on BoundaryManager GameObject.");
            return;
        }

        if (isResetting)
        {
            CustomLogger.Log("BoundaryManager: ResetBoundary already in progress, ignoring.");
            return;
        }

        isResetting = true;
        photonView.RPC("PerformBoundaryReset", RpcTarget.AllBuffered);
        CustomLogger.Log("BoundaryManager: Sent PerformBoundaryReset RPC to all clients.");
        StartCoroutine(ResetFlagAfterDelay());
    }

    private IEnumerator ResetFlagAfterDelay()
    {
        yield return new WaitForSeconds(1f);
        isResetting = false;
        CustomLogger.Log("BoundaryManager: Reset isResetting flag set to false.");
    }

    [PunRPC]
    private void PerformBoundaryReset()
    {
        CustomLogger.Log($"BoundaryManager: Starting PerformBoundaryReset for client, IsMasterClient={PhotonNetwork.IsMasterClient}");

        spaceshipPositions.Clear();
        CustomLogger.Log("BoundaryManager: Cleared spaceship positions list.");

        try
        {
            GameObject[] boundaryVisuals = GameObject.FindGameObjectsWithTag("BoundaryVisual");
            foreach (GameObject visual in boundaryVisuals)
            {
                if (visual != null && visual.GetComponent<PhotonView>() != null)
                {
                    PhotonNetwork.Destroy(visual);
                    CustomLogger.Log("BoundaryManager: Destroyed existing BoundaryVisual.");
                }
            }
        }
        catch (System.Exception e)  // Fully qualify the Exception type
        {
            CustomLogger.LogError($"BoundaryManager: Failed to destroy BoundaryVisuals: {e.Message}");
        }

        if (PhotonNetwork.IsMasterClient)
        {
            try
            {
                GameObject boundaryPrefab = Resources.Load<GameObject>("Prefabs/BoundaryVisual");
                if (boundaryPrefab == null)
                {
                    CustomLogger.LogError("BoundaryManager: BoundaryVisual prefab not found at Assets/Resources/Prefabs/BoundaryVisual.prefab");
                }
                else
                {
                    PhotonNetwork.Instantiate("Prefabs/BoundaryVisual", Vector3.zero, Quaternion.identity);
                    CustomLogger.Log("BoundaryManager: Respawned new BoundaryVisual.");
                }
            }
            catch (System.Exception e)  // Fully qualify the Exception type
            {
                CustomLogger.LogError($"BoundaryManager: Failed to respawn BoundaryVisual: {e.Message}");
            }

            GameObject[] spaceships = GameObject.FindGameObjectsWithTag("SpaceShip");
            CustomLogger.Log($"BoundaryManager: Found {spaceships.Length} spaceships to relocate.");
            float safeBoundary = boundarySize / 2f - bufferDistance;
            const int maxAttempts = 10;

            foreach (GameObject spaceship in spaceships)
            {
                if (spaceship == null || !spaceship.activeInHierarchy)
                {
                    CustomLogger.LogWarning("BoundaryManager: Skipping null or inactive spaceship during teleport.");
                    continue;
                }

                SpaceshipMarker marker = spaceship.GetComponent<SpaceshipMarker>();
                if (marker == null)
                {
                    CustomLogger.LogWarning($"BoundaryManager: Spaceship {spaceship.name} missing SpaceshipMarker, skipping teleport.");
                    continue;
                }

                PhotonView spaceshipView = spaceship.GetComponent<PhotonView>();
                if (spaceshipView == null)
                {
                    CustomLogger.LogWarning($"BoundaryManager: Spaceship {spaceship.name} missing PhotonView, skipping teleport.");
                    continue;
                }

                Vector3 newPosition = Vector3.zero;
                bool validPosition = false;
                int attempts = 0;

                while (attempts < maxAttempts && !validPosition)
                {
                    newPosition = new Vector3(
                        Random.Range(-safeBoundary, safeBoundary),
                        Random.Range(-safeBoundary, safeBoundary),
                        0f
                    );
                    validPosition = IsValidSpaceshipPosition(newPosition, marker.ownerId);
                    attempts++;
                    CustomLogger.Log($"BoundaryManager: Spaceship teleport attempt {attempts}/{maxAttempts} for ownerId={marker.ownerId}, position={newPosition}, valid={validPosition}");
                }

                if (!validPosition)
                {
                    CustomLogger.LogWarning($"BoundaryManager: Could not find valid position for spaceship ownerId={marker.ownerId} after {maxAttempts} attempts, using default position.");
                    newPosition = new Vector3(
                        Random.Range(-safeBoundary, safeBoundary),
                        Random.Range(-safeBoundary, safeBoundary),
                        0f
                    );
                }

                spaceshipPositions.Add(newPosition);
                spaceshipView.RPC("SyncSpaceshipPosition", RpcTarget.AllBuffered, newPosition);
                CustomLogger.Log($"BoundaryManager: Sent SyncSpaceshipPosition RPC for spaceship ownerId={marker.ownerId} to {newPosition}, ViewID={spaceshipView.ViewID}");

                Photon.Realtime.Player owner = PhotonNetwork.CurrentRoom?.GetPlayer(marker.ownerId);
                if (owner != null)
                {
                    ExitGames.Client.Photon.Hashtable props = new ExitGames.Client.Photon.Hashtable
                    {
                        { "SpaceshipViewID", spaceshipView.ViewID }
                    };
                    owner.SetCustomProperties(props);
                    CustomLogger.Log($"BoundaryManager: Updated SpaceshipViewID={spaceshipView.ViewID} for player {owner.NickName}");
                }
                else
                {
                    CustomLogger.Log($"BoundaryManager: Player with ownerId={marker.ownerId} not found in room for ViewID={spaceshipView.ViewID}, likely a bot.");
                }
            }

            CustomLogger.Log($"BoundaryManager: Completed teleport of {spaceships.Length} spaceships.");
        }

        GameObject[] finalSpaceships = GameObject.FindGameObjectsWithTag("SpaceShip");
        foreach (GameObject spaceship in finalSpaceships)
        {
            if (spaceship != null && spaceship.activeInHierarchy)
            {
                SpaceshipMarker marker = spaceship.GetComponent<SpaceshipMarker>();
                if (marker != null)
                {
                    CustomLogger.Log($"BoundaryManager: Spaceship ownerId={marker.ownerId} final position={spaceship.transform.position}, ViewID={spaceship.GetComponent<PhotonView>().ViewID}");
                }
            }
        }
    }
}