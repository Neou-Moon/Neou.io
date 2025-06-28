using UnityEngine;
using Photon.Pun;
using Photon.Realtime;
using TMPro;
using UnityEngine.UI;
using System.Collections.Generic;
using System.Linq;
using System.Collections;
using UnityEngine.SceneManagement;

using Hashtable = ExitGames.Client.Photon.Hashtable;

public class ScoreboardManager : MonoBehaviourPunCallbacks
{
    [SerializeField] private GameObject scoreboardPanel;
    [SerializeField] private TextMeshProUGUI scoreboardButtonText;
    [SerializeField] private Transform scoreboardEntryContainer;
    [SerializeField] private GameObject scoreboardEntryPrefab;
    [SerializeField] private TextMeshProUGUI timerText;
    [SerializeField] private TextMeshProUGUI redTeamScoreText;
    [SerializeField] private TextMeshProUGUI cyanTeamScoreText;
    [SerializeField] private Sprite crownSprite; // Drag your crown PNG here in Inspector
    private bool isScoreboardVisible = false;
    private Vector2 hiddenPosition;
    private Vector2 visiblePosition = Vector2.zero;
    private float slideDuration = 0.5f;
    private List<GameObject> entryObjects = new List<GameObject>();
    private Canvas canvas;
    private RectTransform panelRect;
    private GameObject headerEntry;
    private bool isMatchEnded = false;
    private Coroutine autoHideCoroutine;
    private readonly Color redTeamColor = new Color(1f, 0f, 0f); // Bright Red
    private readonly Color cyanTeamColor = new Color(0f, 1f, 1f); // Cyan Blue

    void Start()
    {
        if (scoreboardEntryPrefab == null)
        {
            CustomLogger.LogWarning("ScoreboardManager: ScoreboardEntryPrefab not assigned in Inspector. Attempting to load from Resources.");
            string prefabPath = "Prefabs/ScoreboardEntry";
            scoreboardEntryPrefab = Resources.Load<GameObject>(prefabPath);
            if (scoreboardEntryPrefab == null)
            {
                CustomLogger.LogError($"ScoreboardManager: Failed to load ScoreboardEntry prefab from Assets/Resources/{prefabPath}.prefab. Ensure prefab exists, is named exactly 'ScoreboardEntry', and is included in Build Settings. Check Assets/Resources/Prefabs folder.");
                scoreboardEntryPrefab = CreateFallbackEntryPrefab();
                if (scoreboardEntryPrefab == null)
                {
                    CustomLogger.LogError("ScoreboardManager: Failed to create fallback prefab. Disabling ScoreboardManager.");
                    enabled = false;
                    return;
                }
                CustomLogger.LogWarning("ScoreboardManager: Using fallback ScoreboardEntry prefab due to missing prefab.");
            }
            else
            {
                CustomLogger.Log($"ScoreboardManager: Successfully loaded ScoreboardEntry prefab from Assets/Resources/{prefabPath}");
            }
        }

        if (crownSprite == null)
        {
            CustomLogger.LogWarning("ScoreboardManager: CrownSprite not assigned in Inspector. Crown feature will not display.");
        }

        if (scoreboardPanel == null || scoreboardButtonText == null || scoreboardEntryContainer == null)
        {
            CustomLogger.LogError("ScoreboardManager: Missing references. Assign scoreboardPanel, scoreboardButtonText, and scoreboardEntryContainer in Inspector.");
            enabled = false;
            return;
        }

        if (timerText == null)
        {
            CustomLogger.LogWarning("ScoreboardManager: TimerText not assigned in Inspector. Timer display will not function.");
        }

        if (SceneManager.GetActiveScene().name == "TeamMoonRan" && (redTeamScoreText == null || cyanTeamScoreText == null))
        {
            CustomLogger.LogWarning("ScoreboardManager: RedTeamScoreText or CyanTeamScoreText not assigned in Inspector for TeamMoonRan. Team scores will not display.");
        }

        canvas = scoreboardPanel.GetComponentInParent<Canvas>();
        panelRect = scoreboardPanel.GetComponent<RectTransform>();
        if (canvas == null)
        {
            CustomLogger.LogError("ScoreboardManager: ScoreboardPanel must be a child of a Canvas.");
            enabled = false;
            return;
        }

        Rect canvasRect = canvas.GetComponent<RectTransform>().rect;
        CustomLogger.Log($"ScoreboardManager: Canvas size={canvasRect.size}, ScaleFactor={canvas.scaleFactor}, Panel AnchorMin={panelRect.anchorMin}, AnchorMax={panelRect.anchorMax}, Pivot={panelRect.pivot}, SizeDelta={panelRect.sizeDelta}");

        hiddenPosition = new Vector2(canvasRect.width * 0.5f + panelRect.rect.width * 0.5f + 10, 0);
        panelRect.anchoredPosition = hiddenPosition;
        scoreboardPanel.SetActive(false);
        CustomLogger.Log($"ScoreboardManager: Initial panel position={panelRect.anchoredPosition}");

        Button button = scoreboardButtonText.GetComponentInParent<Button>();
        if (button != null)
        {
            button.onClick.AddListener(ToggleScoreboard);
            CustomLogger.Log("ScoreboardManager: Added click listener to scoreboard button.");
        }
        else
        {
            CustomLogger.LogWarning("ScoreboardManager: Button component not found for scoreboardButtonText. Button toggle won't work.");
        }

        CreateHeader();
        StartCoroutine(InitializePlayerProperties());
        StartCoroutine(DelayedUpdateScoreboard());
        CustomLogger.Log("ScoreboardManager: Initialization complete");
    }
    private IEnumerator InitializePlayerProperties()
    {
        yield return new WaitForSeconds(1f); // Increased delay for Photon stabilization
        if (!PhotonNetwork.IsConnectedAndReady)
        {
            CustomLogger.LogWarning("ScoreboardManager: Not connected to Photon, skipping InitializePlayerProperties.");
            yield break;
        }

        string currentScene = SceneManager.GetActiveScene().name;
        string partyId = PlayerPrefs.GetString("PartyID", "");
        int retryCount = 3;

        while (retryCount > 0)
        {
            bool allPropertiesSet = true;
            foreach (Player player in PhotonNetwork.PlayerList)
            {
                string username = PlayerPrefs.GetString("PlayerUsername", $"Player_{player.ActorNumber}");
                string nickname = PlayerPrefs.GetString("PlayerNickname", username);
                if (player == PhotonNetwork.LocalPlayer)
                {
                    username = PlayerPrefs.GetString("PlayerUsername", $"Player_{player.ActorNumber}");
                    nickname = PlayerPrefs.GetString("PlayerNickname", username);
                }

                string team = currentScene == "TeamMoonRan" ?
                    (!string.IsNullOrEmpty(partyId) && player.CustomProperties.ContainsKey("PartyID") && player.CustomProperties["PartyID"].ToString() == partyId ?
                        (player.ActorNumber % 2 == 0 ? "Red" : "Cyan") :
                        (player.ActorNumber % 2 == 0 ? "Red" : "Cyan")) : "None";

                Hashtable props = new Hashtable
            {
                { "Username", username },
                { "Nickname", nickname },
                { "Points", 0 },
                { "Team", team },
                { "PartyID", partyId }
            };

                if (player.CustomProperties == null || !player.CustomProperties.ContainsKey("Nickname") || player.CustomProperties["Nickname"] == null)
                {
                    player.SetCustomProperties(props);
                    CustomLogger.Log($"ScoreboardManager: Initialized properties for player {player.NickName}, ActorNumber={player.ActorNumber}, Username={username}, Nickname={nickname}, Team={team}, PartyID={partyId}");
                    allPropertiesSet = false;
                }
            }

            BotController[] bots = Object.FindObjectsByType<BotController>(FindObjectsSortMode.None);
            int botIndex = 1;
            foreach (BotController bot in bots.OrderBy(b => b.ActorNumber))
            {
                if (bot.photonView != null)
                {
                    string botName = $"Bot_{botIndex++}";
                    string team = currentScene == "TeamMoonRan" ? (bot.ActorNumber % 2 == 0 ? "Red" : "Cyan") : "None";
                    Hashtable props = new Hashtable
                {
                    { "Nickname", botName },
                    { "Username", botName },
                    { "Points", 0 },
                    { "Team", team },
                    { "PartyID", "" }
                };
                    if (bot.CustomProperties == null || !bot.CustomProperties.ContainsKey("Nickname") || bot.CustomProperties["Nickname"] == null)
                    {
                        bot.photonView.RPC("UpdateCustomProperties", RpcTarget.AllBuffered, props);
                        CustomLogger.Log($"ScoreboardManager: Initialized properties for bot {bot.gameObject.name}, ActorNumber={bot.ActorNumber}, Nickname={botName}, Username={botName}, Team={team}");
                        allPropertiesSet = false;
                    }
                }
            }

            if (allPropertiesSet)
            {
                UpdateScoreboard();
                CustomLogger.Log("ScoreboardManager: All properties initialized, updated scoreboard");
                yield break;
            }

            retryCount--;
            CustomLogger.Log($"ScoreboardManager: Retrying property initialization, attempts left: {retryCount}");
            yield return new WaitForSeconds(0.5f);
        }

        CustomLogger.LogWarning("ScoreboardManager: Failed to initialize all properties after retries, forcing scoreboard update");
        UpdateScoreboard();
    }

    private GameObject CreateFallbackEntryPrefab()
    {
        GameObject fallback = new GameObject("FallbackScoreboardEntry");
        RectTransform rect = fallback.AddComponent<RectTransform>();
        rect.sizeDelta = new Vector2(300, 30);

        GameObject rankObj = new GameObject("RankText");
        rankObj.transform.SetParent(fallback.transform, false);
        TextMeshProUGUI rankText = rankObj.AddComponent<TextMeshProUGUI>();
        rankText.text = "Rank";
        rankText.alignment = TextAlignmentOptions.Left;
        rankText.rectTransform.anchoredPosition = new Vector2(-100, 0);
        rankText.rectTransform.sizeDelta = new Vector2(50, 30);

        GameObject usernameObj = new GameObject("UsernameText");
        usernameObj.transform.SetParent(fallback.transform, false);
        TextMeshProUGUI usernameText = usernameObj.AddComponent<TextMeshProUGUI>();
        usernameText.text = "Name";
        usernameText.alignment = TextAlignmentOptions.Left;
        usernameText.rectTransform.anchoredPosition = new Vector2(0, 0);
        usernameText.rectTransform.sizeDelta = new Vector2(150, 30);

        GameObject pointsObj = new GameObject("PointsText");
        pointsObj.transform.SetParent(fallback.transform, false);
        TextMeshProUGUI pointsText = pointsObj.AddComponent<TextMeshProUGUI>();
        pointsText.text = "Score";
        pointsText.alignment = TextAlignmentOptions.Right;
        pointsText.rectTransform.anchoredPosition = new Vector2(100, 0);
        pointsText.rectTransform.sizeDelta = new Vector2(50, 30);

        CustomLogger.Log("ScoreboardManager: Created fallback ScoreboardEntry prefab programmatically.");
        return fallback;
    }

    private IEnumerator DelayedUpdateScoreboard()
    {
        yield return new WaitForSeconds(10f);
        UpdateScoreboard();
        CustomLogger.Log("ScoreboardManager: Triggered DelayedUpdateScoreboard after 10 seconds");
    }

    private void CreateHeader()
    {
        if (scoreboardEntryPrefab == null)
        {
            CustomLogger.LogError("ScoreboardManager: Cannot create header, scoreboardEntryPrefab is null at runtime.");
            return;
        }

        headerEntry = Instantiate(scoreboardEntryPrefab, scoreboardEntryContainer);
        headerEntry.name = "ScoreboardHeader";

        Transform rankTransform = headerEntry.transform.Find("RankText");
        Transform usernameTransform = headerEntry.transform.Find("UsernameText");
        Transform pointsTransform = headerEntry.transform.Find("PointsText");
        Transform causeTransform = headerEntry.transform.Find("CauseText");
        TextMeshProUGUI rankText = rankTransform != null ? rankTransform.GetComponent<TextMeshProUGUI>() : null;
        TextMeshProUGUI usernameText = usernameTransform != null ? usernameTransform.GetComponent<TextMeshProUGUI>() : null;
        TextMeshProUGUI pointsText = pointsTransform != null ? pointsTransform.GetComponent<TextMeshProUGUI>() : null;
        TextMeshProUGUI causeText = causeTransform != null ? causeTransform.GetComponent<TextMeshProUGUI>() : null;

        if (rankText == null) rankText = FindTextMeshProUGUIByName(headerEntry, "RankText");
        if (usernameText == null) usernameText = FindTextMeshProUGUIByName(headerEntry, "UsernameText");
        if (pointsText == null) pointsText = FindTextMeshProUGUIByName(headerEntry, "PointsText");

        if (rankText == null || usernameText == null || pointsText == null)
        {
            CustomLogger.LogError($"ScoreboardManager: Failed to set up header. RankText={(rankTransform == null && rankText == null ? "not found" : rankText == null ? "no TextMeshProUGUI" : "found")}, UsernameText={(usernameTransform == null && usernameText == null ? "not found" : usernameText == null ? "no TextMeshProUGUI" : "found")}, PointsText={(pointsTransform == null && pointsText == null ? "not found" : pointsText == null ? "no TextMeshProUGUI" : "found")}");
            Destroy(headerEntry);
            headerEntry = null;
            return;
        }

        rankText.text = "Rank";
        usernameText.text = "Name";
        pointsText.text = "Score";
        if (causeText != null) causeText.gameObject.SetActive(false);
        rankText.fontStyle = FontStyles.Bold;
        usernameText.fontStyle = FontStyles.Bold;
        pointsText.fontStyle = FontStyles.Bold;
        rankText.color = Color.white;
        usernameText.color = Color.white;
        pointsText.color = Color.white;

        RectTransform headerRect = headerEntry.GetComponent<RectTransform>();
        headerRect.anchorMin = new Vector2(0.5f, 1f);
        headerRect.anchorMax = new Vector2(0.5f, 1f);
        headerRect.pivot = new Vector2(0.5f, 1f);
        headerRect.anchoredPosition = new Vector2(0, -5);
        CustomLogger.Log($"ScoreboardManager: Created header with Rank | Name | Score, position={headerRect.anchoredPosition}");
    }

    void Update()
    {
        if (Input.GetKeyDown(KeyCode.K))
        {
            ToggleScoreboard();
        }
    }

    public void ToggleScoreboard()
    {
        isScoreboardVisible = !isScoreboardVisible;
        if (isScoreboardVisible)
        {
            scoreboardPanel.SetActive(true);
            if (timerText != null)
            {
                timerText.gameObject.SetActive(true);
            }
            StartCoroutine(SlidePanel(hiddenPosition, visiblePosition));
            if (autoHideCoroutine != null)
            {
                StopCoroutine(autoHideCoroutine);
            }
            autoHideCoroutine = StartCoroutine(AutoHideScoreboard(10f));
            CustomLogger.Log("ScoreboardManager: Scoreboard activated, starting 10-second auto-hide timer");
        }
        else
        {
            if (autoHideCoroutine != null)
            {
                StopCoroutine(autoHideCoroutine);
                autoHideCoroutine = null;
            }
            if (timerText != null)
            {
                timerText.gameObject.SetActive(false);
            }
            StartCoroutine(SlidePanel(visiblePosition, hiddenPosition, true));
            CustomLogger.Log("ScoreboardManager: Scoreboard deactivated manually");
        }
    }

    private IEnumerator AutoHideScoreboard(float duration)
    {
        float timeLeft = duration;
        while (timeLeft > 0)
        {
            if (timerText != null)
            {
                timerText.text = $"Hiding in {Mathf.CeilToInt(timeLeft)}s";
            }
            timeLeft -= Time.deltaTime;
            yield return null;
        }
        if (isScoreboardVisible)
        {
            isScoreboardVisible = false;
            if (timerText != null)
            {
                timerText.gameObject.SetActive(false);
            }
            StartCoroutine(SlidePanel(visiblePosition, hiddenPosition, true));
            CustomLogger.Log($"ScoreboardManager: Scoreboard auto-hidden after {duration} seconds");
        }
        autoHideCoroutine = null;
    }

    private IEnumerator SlidePanel(Vector2 startPos, Vector2 endPos, bool deactivateAfter = false)
    {
        float elapsed = 0f;
        while (elapsed < slideDuration)
        {
            elapsed += Time.deltaTime;
            panelRect.anchoredPosition = Vector2.Lerp(startPos, endPos, elapsed / slideDuration);
            CustomLogger.Log($"ScoreboardManager: Sliding panel, position={panelRect.anchoredPosition}, progress={elapsed / slideDuration}");
            yield return null;
        }
        panelRect.anchoredPosition = endPos;
        CustomLogger.Log($"ScoreboardManager: Slide complete, final position={panelRect.anchoredPosition}");
        if (deactivateAfter)
        {
            scoreboardPanel.SetActive(false);
        }
    }

    public override void OnPlayerPropertiesUpdate(Player targetPlayer, Hashtable changedProps)
    {
        if (changedProps.ContainsKey("Points") || changedProps.ContainsKey("Username"))
        {
            int newPoints = changedProps.ContainsKey("Points") && changedProps["Points"] != null ? (int)changedProps["Points"] : 0;
            string newUsername = changedProps.ContainsKey("Username") && changedProps["Username"] != null ? changedProps["Username"].ToString() : targetPlayer.NickName;
            CustomLogger.Log($"ScoreboardManager: OnPlayerPropertiesUpdate for player {targetPlayer.NickName}, ActorNumber={targetPlayer.ActorNumber}, Points={newPoints}, Username={newUsername}");
            UpdateScoreboard();
            UpdateCrowns();
        }
    }

    private void UpdateCrowns()
    {
        if (crownSprite == null)
        {
            CustomLogger.LogWarning("ScoreboardManager: CrownSprite not assigned, skipping crown update.");
            return;
        }

        List<IPlayer> allPlayers = PhotonNetwork.PlayerList
            .Select(p => new BoundaryManager.RealPlayerWrapper(p) as IPlayer)
            .Concat(Object.FindObjectsByType<BotController>(FindObjectsSortMode.None))
            .ToList();

        // Clear all crowns first
        photonView.RPC("ClearAllCrownsRPC", RpcTarget.All);

        if (SceneManager.GetActiveScene().name == "TeamMoonRan")
        {
            // Replicate UpdateScoreboard sorting for TeamMoonRan
            List<IPlayer> redTeamPlayers = allPlayers
                .Where(p => p.CustomProperties != null && p.CustomProperties.ContainsKey("Team") && p.CustomProperties["Team"].ToString() == "Red")
                .OrderByDescending(p => p.CustomProperties != null && p.CustomProperties.ContainsKey("Points") ? (int)p.CustomProperties["Points"] : 0)
                .Take(5)
                .ToList();

            List<IPlayer> cyanTeamPlayers = allPlayers
                .Where(p => p.CustomProperties != null && p.CustomProperties.ContainsKey("Team") && p.CustomProperties["Team"].ToString() == "Cyan")
                .OrderByDescending(p => p.CustomProperties != null && p.CustomProperties.ContainsKey("Points") ? (int)p.CustomProperties["Points"] : 0)
                .Take(15)
                .ToList();

            List<IPlayer> sortedPlayers = redTeamPlayers.Concat(cyanTeamPlayers).Take(20).ToList();

            // Assign crowns to 1st (Red leader) and 6th (Cyan leader) if points > 0
            if (sortedPlayers.Count >= 1 && sortedPlayers[0] != null)
            {
                IPlayer redLeader = sortedPlayers[0];
                if (redLeader.CustomProperties != null && redLeader.CustomProperties.ContainsKey("Points") && (int)redLeader.CustomProperties["Points"] > 0)
                {
                    StartCoroutine(AssignCrownWithRetry(redLeader, "Red team leader (Rank 1)"));
                }
                else
                {
                    CustomLogger.Log($"ScoreboardManager: No crown for Red team leader, points={(redLeader.CustomProperties != null && redLeader.CustomProperties.ContainsKey("Points") ? (int)redLeader.CustomProperties["Points"] : 0)}");
                }
            }

            if (sortedPlayers.Count >= 6 && sortedPlayers[5] != null)
            {
                IPlayer cyanLeader = sortedPlayers[5];
                if (cyanLeader.CustomProperties != null && cyanLeader.CustomProperties.ContainsKey("Points") && (int)cyanLeader.CustomProperties["Points"] > 0)
                {
                    StartCoroutine(AssignCrownWithRetry(cyanLeader, "Cyan team leader (Rank 6)"));
                }
                else
                {
                    CustomLogger.Log($"ScoreboardManager: No crown for Cyan team leader, points={(cyanLeader.CustomProperties != null && cyanLeader.CustomProperties.ContainsKey("Points") ? (int)cyanLeader.CustomProperties["Points"] : 0)}");
                }
            }
        }
        else // Moon Ran
        {
            // Find 1st place player
            IPlayer topPlayer = allPlayers
                .OrderByDescending(p => p.CustomProperties != null && p.CustomProperties.ContainsKey("Points") ? (int)p.CustomProperties["Points"] : 0)
                .FirstOrDefault();

            if (topPlayer != null && topPlayer.CustomProperties != null && topPlayer.CustomProperties.ContainsKey("Points") && (int)topPlayer.CustomProperties["Points"] > 0)
            {
                StartCoroutine(AssignCrownWithRetry(topPlayer, "1st place player"));
            }
            else
            {
                CustomLogger.Log("ScoreboardManager: No crown assigned in Moon Ran, top player is null or has 0 points");
            }
        }
    }

    private IEnumerator AssignCrownWithRetry(IPlayer player, string role)
    {
        int retries = 3;
        float delay = 0.5f;
        int viewID = GetPlayerViewID(player);

        while (retries > 0 && viewID == -1)
        {
            CustomLogger.LogWarning($"ScoreboardManager: Failed to find PhotonView for {role} {player.NickName} (ActorNumber={player.ActorNumber}), retry {4 - retries}/3");
            yield return new WaitForSeconds(delay);
            viewID = GetPlayerViewID(player);
            retries--;
        }

        if (viewID != -1)
        {
            photonView.RPC("ShowCrown", RpcTarget.All, viewID);
            CustomLogger.Log($"ScoreboardManager: Assigned crown to {role} {player.NickName} (ActorNumber={player.ActorNumber}, ViewID={viewID})");
        }
        else
        {
            CustomLogger.LogError($"ScoreboardManager: Could not assign crown to {role} {player.NickName} (ActorNumber={player.ActorNumber}) after retries, no valid PhotonView found");
        }
    }

    [PunRPC]
    private void ShowCrown(int viewID)
    {
        PhotonView targetView = PhotonView.Find(viewID);
        if (targetView != null)
        {
            NameTag nameTag = targetView.GetComponentInChildren<NameTag>();
            if (nameTag != null)
            {
                nameTag.ShowCrown();
                CustomLogger.Log($"ScoreboardManager: ShowCrown RPC: Enabled crown for ViewID={viewID}, NameTag found on {GetGameObjectPath(nameTag.gameObject)}");
            }
            else
            {
                CustomLogger.LogWarning($"ScoreboardManager: ShowCrown RPC: No NameTag found for ViewID={viewID} on {GetGameObjectPath(targetView.gameObject)} or its children");
            }
        }
        else
        {
            CustomLogger.LogWarning($"ScoreboardManager: ShowCrown RPC: PhotonView not found for ViewID={viewID}");
        }
    }


    private int GetPlayerViewID(IPlayer player)
    {
        if (player == null)
        {
            CustomLogger.LogWarning($"ScoreboardManager: GetPlayerViewID called with null player, ActorNumber={player?.ActorNumber}");
            return -1;
        }

        if (player is BoundaryManager.RealPlayerWrapper realPlayer)
        {
            var photonViews = Object.FindObjectsByType<PhotonView>(FindObjectsSortMode.None);
            foreach (var view in photonViews)
            {
                // Only consider PhotonViews on GameObjects with PlayerController
                if (view.GetComponent<PlayerController>() != null && view.Owner == realPlayer._player)
                {
                    CustomLogger.Log($"ScoreboardManager: Found PhotonView for real player {realPlayer._player.NickName}, ActorNumber={realPlayer._player.ActorNumber}, ViewID={view.ViewID}, GameObject={GetGameObjectPath(view.gameObject)}");
                    return view.ViewID;
                }
            }
            CustomLogger.LogWarning($"ScoreboardManager: No PhotonView found for real player {realPlayer._player.NickName}, ActorNumber={realPlayer._player.ActorNumber} with PlayerController");
            return -1;
        }
        else if (player is BotController bot)
        {
            if (bot.photonView != null)
            {
                CustomLogger.Log($"ScoreboardManager: Found PhotonView for bot {bot.NickName}, ActorNumber={bot.ActorNumber}, ViewID={bot.photonView.ViewID}, GameObject={GetGameObjectPath(bot.gameObject)}");
                return bot.photonView.ViewID;
            }
            CustomLogger.LogWarning($"ScoreboardManager: No PhotonView found for bot {bot.NickName}, ActorNumber={bot.ActorNumber}");
            return -1;
        }

        CustomLogger.LogWarning($"ScoreboardManager: Could not find PhotonView for player {player.NickName}, ActorNumber={player.ActorNumber}, type={player.GetType().Name}");
        return -1;
    }


    [PunRPC]
    private void ClearAllCrownsRPC()
    {
        NameTag[] nameTags = Object.FindObjectsByType<NameTag>(FindObjectsSortMode.None);
        foreach (NameTag nameTag in nameTags)
        {
            nameTag.HideCrown();
        }
        CustomLogger.Log("ScoreboardManager: Cleared all crowns via RPC");
    }
    private string GetGameObjectPath(GameObject obj)
    {
        if (obj == null) return "null";
        string path = obj.name;
        Transform current = obj.transform.parent;
        while (current != null)
        {
            path = $"{current.name}/{path}";
            current = current.parent;
        }
        return path;
    }

    public void LockScoreboard()
    {
        isMatchEnded = true;
        if (isScoreboardVisible)
        {
            ToggleScoreboard();
        }
        photonView.RPC("ClearAllCrownsRPC", RpcTarget.All);
        Debug.Log("ScoreboardManager: Scoreboard locked, no further updates or auto-display, crowns cleared.");
    }

    public void UnlockScoreboard()
    {
        isMatchEnded = false;
        UpdateScoreboard();
        UpdateCrowns();
        Debug.Log("ScoreboardManager: Scoreboard unlocked, updates enabled, crowns updated.");
    }

    public bool IsScoreboardVisible()
    {
        return isScoreboardVisible;
    }

    public string GetPlayerUsername(IPlayer player)
    {
        if (player == null)
        {
            CustomLogger.LogWarning("ScoreboardManager: GetPlayerUsername called with null player.");
            return "Unknown";
        }

        string currentScene = SceneManager.GetActiveScene().name;
        string defaultUsername = player is BotController ? $"Bot_{player.ActorNumber}" : $"Player_{player.ActorNumber}";
        string displayName = defaultUsername;

        if (player is BotController bot)
        {
            BotController[] bots = Object.FindObjectsByType<BotController>(FindObjectsSortMode.None).OrderBy(b => b.ActorNumber).ToArray();
            int botIndex = System.Array.IndexOf(bots, bot) + 1;
            displayName = $"Bot_{botIndex}";
            if (player.CustomProperties == null || !player.CustomProperties.ContainsKey("Nickname"))
            {
                Hashtable props = new Hashtable { { "Nickname", displayName }, { "Username", displayName } };
                bot.photonView.RPC("UpdateCustomProperties", RpcTarget.AllBuffered, props);
                CustomLogger.Log($"ScoreboardManager: Set bot Nickname and Username to {displayName} for ActorNumber={player.ActorNumber}");
            }
        }
        else if (player is BoundaryManager.RealPlayerWrapper realPlayer)
        {
            if (realPlayer._player == PhotonNetwork.LocalPlayer && (currentScene == "MoonRan" || currentScene == "TeamMoonRan"))
            {
                string nickname = PlayerPrefs.GetString("PlayerNickname", "");
                string username = PlayerPrefs.GetString("PlayerUsername", defaultUsername);
                if (!string.IsNullOrEmpty(nickname))
                {
                    displayName = nickname;
                    Hashtable props = new Hashtable { { "Nickname", nickname }, { "Username", username } };
                    realPlayer._player.SetCustomProperties(props);
                    CustomLogger.Log($"ScoreboardManager: Used PlayerNickname {nickname} for local player, Username={username}, ActorNumber={player.ActorNumber}");
                }
                else
                {
                    displayName = username;
                    Hashtable props = new Hashtable { { "Nickname", username }, { "Username", username } };
                    realPlayer._player.SetCustomProperties(props);
                    CustomLogger.Log($"ScoreboardManager: Used PlayerUsername {username} for local player, ActorNumber={player.ActorNumber}");
                }
            }
            else if (player.CustomProperties != null)
            {
                if (player.CustomProperties.TryGetValue("Nickname", out object nicknameObj) && !string.IsNullOrEmpty(nicknameObj?.ToString()))
                {
                    displayName = nicknameObj.ToString();
                    CustomLogger.Log($"ScoreboardManager: Used Nickname {displayName} from CustomProperties for player, ActorNumber={player.ActorNumber}");
                }
                else if (player.CustomProperties.TryGetValue("Username", out object usernameObj) && !string.IsNullOrEmpty(usernameObj?.ToString()))
                {
                    displayName = usernameObj.ToString();
                    CustomLogger.Log($"ScoreboardManager: Used Username {displayName} from CustomProperties for player, ActorNumber={player.ActorNumber}");
                }
                else
                {
                    displayName = defaultUsername;
                    Hashtable props = new Hashtable { { "Nickname", displayName }, { "Username", displayName } };
                    realPlayer._player.SetCustomProperties(props);
                    CustomLogger.Log($"ScoreboardManager: Fell back to defaultUsername {displayName} for player, ActorNumber={player.ActorNumber}");
                }
            }
            else
            {
                displayName = defaultUsername;
                Hashtable props = new Hashtable { { "Nickname", displayName }, { "Username", displayName } };
                realPlayer._player.SetCustomProperties(props);
                CustomLogger.Log($"ScoreboardManager: CustomProperties null, fell back to defaultUsername {displayName} for player, ActorNumber={player.ActorNumber}");
            }
        }

        return displayName;
    }

    public void UpdateScoreboard()
    {
        if (isMatchEnded)
        {
            CustomLogger.Log("ScoreboardManager: Skipping UpdateScoreboard, match is ended and scoreboard is locked.");
            return;
        }

        if (scoreboardEntryPrefab == null)
        {
            CustomLogger.LogError("ScoreboardManager: Cannot update scoreboard, scoreboardEntryPrefab is null at runtime.");
            return;
        }

        foreach (GameObject entry in entryObjects)
        {
            Destroy(entry);
        }
        entryObjects.Clear();

        List<IPlayer> allPlayers = new List<IPlayer>();
        allPlayers.AddRange(PhotonNetwork.PlayerList.Select(p => new BoundaryManager.RealPlayerWrapper(p) as IPlayer));
        allPlayers.AddRange(Object.FindObjectsByType<BotController>(FindObjectsSortMode.None));

        int retries = 3;
        float delay = 0.5f;
        bool allPropertiesSet = false;

        while (retries > 0 && !allPropertiesSet)
        {
            allPropertiesSet = true;
            foreach (var player in allPlayers)
            {
                if (player.CustomProperties == null || !player.CustomProperties.ContainsKey("Username") || player.CustomProperties["Username"] == null ||
                    !player.CustomProperties.ContainsKey("Points") || player.CustomProperties["Points"] == null ||
                    !player.CustomProperties.ContainsKey("Team") || player.CustomProperties["Team"] == null)
                {
                    allPropertiesSet = false;
                    string username = player is BotController ? $"Bot_{player.ActorNumber}" : PlayerPrefs.GetString("PlayerUsername", $"Player_{player.ActorNumber}");
                    string team = SceneManager.GetActiveScene().name == "TeamMoonRan" ? (player.ActorNumber % 2 == 0 ? "Red" : "Cyan") : "None";
                    Hashtable props = new Hashtable
                {
                    { "Username", username },
                    { "Nickname", username },
                    { "Points", player.CustomProperties != null && player.CustomProperties.ContainsKey("Points") ? player.CustomProperties["Points"] : 0 },
                    { "Team", team }
                };

                    if (player is BoundaryManager.RealPlayerWrapper realPlayer)
                    {
                        realPlayer._player.SetCustomProperties(props);
                        CustomLogger.Log($"ScoreboardManager: Retrying to set properties for real player {realPlayer._player.NickName}, ActorNumber={realPlayer._player.ActorNumber}");
                    }
                    else if (player is BotController botController && botController.photonView != null)
                    {
                        botController.photonView.RPC("UpdateCustomProperties", RpcTarget.AllBuffered, props);
                        CustomLogger.Log($"ScoreboardManager: Retrying to set properties for bot {botController.gameObject.name}, ActorNumber={botController.ActorNumber}");
                    }
                }
            }
            if (!allPropertiesSet)
            {
                retries--;
                CustomLogger.Log($"ScoreboardManager: Retry {4 - retries}/3 to ensure properties for all players");
                System.Threading.Thread.Sleep((int)(delay * 1000));
            }
        }

        int redTeamScore = 0;
        int cyanTeamScore = 0;
        if (SceneManager.GetActiveScene().name == "TeamMoonRan")
        {
            foreach (var player in allPlayers)
            {
                int points = player.CustomProperties != null && player.CustomProperties.ContainsKey("Points") ? (int)player.CustomProperties["Points"] : 0;
                string team = player.CustomProperties != null && player.CustomProperties.ContainsKey("Team") ? player.CustomProperties["Team"].ToString() : "None";
                if (team == "Red") redTeamScore += points;
                else if (team == "Cyan") cyanTeamScore += points;
            }
            if (redTeamScoreText != null)
            {
                redTeamScoreText.text = $"Red Team: {redTeamScore}";
                redTeamScoreText.color = redTeamColor;
            }
            if (cyanTeamScoreText != null)
            {
                cyanTeamScoreText.text = $"Cyan Team: {cyanTeamScore}";
                cyanTeamScoreText.color = cyanTeamColor;
            }
            CustomLogger.Log($"ScoreboardManager: Team Scores - Red: {redTeamScore}, Cyan: {cyanTeamScore}");
        }

        List<IPlayer> sortedPlayers = SceneManager.GetActiveScene().name == "TeamMoonRan" ?
            allPlayers
                .Where(p => p.CustomProperties != null && p.CustomProperties.ContainsKey("Team"))
                .GroupBy(p => p.CustomProperties["Team"].ToString())
                .SelectMany(g => g.OrderByDescending(p => p.CustomProperties != null && p.CustomProperties.ContainsKey("Points") ? (int)p.CustomProperties["Points"] : 0))
                .Take(20)
                .ToList() :
            allPlayers
                .OrderByDescending(p => p.CustomProperties != null && p.CustomProperties.ContainsKey("Points") ? (int)p.CustomProperties["Points"] : 0)
                .Take(20)
                .ToList();

        for (int i = 0; i < sortedPlayers.Count; i++)
        {
            IPlayer player = sortedPlayers[i];
            GameObject entry = Instantiate(scoreboardEntryPrefab, scoreboardEntryContainer);
            entryObjects.Add(entry);

            RectTransform entryRect = entry.GetComponent<RectTransform>();
            entryRect.anchorMin = new Vector2(0.5f, 1f);
            entryRect.anchorMax = new Vector2(0.5f, 1f);
            entryRect.pivot = new Vector2(0.5f, 1f);
            entryRect.anchoredPosition = new Vector2(0, -30 - i * 30);

            TextMeshProUGUI rankText = entry.transform.Find("RankText")?.GetComponent<TextMeshProUGUI>();
            TextMeshProUGUI usernameText = entry.transform.Find("UsernameText")?.GetComponent<TextMeshProUGUI>();
            TextMeshProUGUI pointsText = entry.transform.Find("PointsText")?.GetComponent<TextMeshProUGUI>();
            TextMeshProUGUI causeText = entry.transform.Find("CauseText")?.GetComponent<TextMeshProUGUI>();

            if (rankText == null || usernameText == null || pointsText == null)
            {
                CustomLogger.LogError($"ScoreboardManager: Invalid entry prefab for {player.NickName}, destroying entry");
                Destroy(entry);
                entryObjects.Remove(entry);
                continue;
            }

            rankText.text = (i + 1).ToString();
            usernameText.text = GetPlayerUsername(player);
            pointsText.text = player.CustomProperties != null && player.CustomProperties.ContainsKey("Points") ? ((int)player.CustomProperties["Points"]).ToString() : "0";
            if (causeText != null) causeText.gameObject.SetActive(false);

            if (SceneManager.GetActiveScene().name == "TeamMoonRan")
            {
                string team = player.CustomProperties != null && player.CustomProperties.ContainsKey("Team") ? player.CustomProperties["Team"].ToString() : "None";
                Color teamColor = team == "Red" ? redTeamColor : team == "Cyan" ? cyanTeamColor : Color.white;
                usernameText.color = teamColor;
                pointsText.color = teamColor;
                rankText.color = teamColor;
            }
        }

        UpdateCrowns();
    }

    private IEnumerator EnsurePropertiesThenUpdate(List<IPlayer> allPlayers)
    {
        int retries = 3;
        float delay = 0.5f;

        while (retries > 0)
        {
            bool allPropertiesSet = true;
            foreach (var player in allPlayers)
            {
                if (player.CustomProperties == null || !player.CustomProperties.ContainsKey("Username") || player.CustomProperties["Username"] == null ||
                    !player.CustomProperties.ContainsKey("Points") || player.CustomProperties["Points"] == null ||
                    !player.CustomProperties.ContainsKey("Team") || player.CustomProperties["Team"] == null)
                {
                    allPropertiesSet = false;
                    string username = player is BotController ? $"Bot_{player.ActorNumber}" : PlayerPrefs.GetString("PlayerUsername", $"Player_{player.ActorNumber}");
                    string team = SceneManager.GetActiveScene().name == "TeamMoonRan" ? (player.ActorNumber % 2 == 0 ? "Red" : "Cyan") : "None";
                    Hashtable props = new Hashtable
                {
                    { "Username", username },
                    { "Points", player.CustomProperties != null && player.CustomProperties.ContainsKey("Points") ? player.CustomProperties["Points"] : 0 },
                    { "Team", player.CustomProperties != null && player.CustomProperties.ContainsKey("Team") ? player.CustomProperties["Team"] : team }
                };

                    if (player is BoundaryManager.RealPlayerWrapper realPlayer)
                    {
                        realPlayer._player.SetCustomProperties(props);
                        CustomLogger.Log($"ScoreboardManager: Retrying to set properties for real player {realPlayer._player.NickName}, ActorNumber={realPlayer._player.ActorNumber}, Username={username}, Team={team}");
                    }
                    else if (player is BotController botController && botController.photonView != null)
                    {
                        botController.photonView.RPC("UpdateCustomProperties", RpcTarget.AllBuffered, props);
                        CustomLogger.Log($"ScoreboardManager: Retrying to set properties for bot {botController.gameObject.name}, ActorNumber={botController.ActorNumber}, Username={username}, Team={team}");
                    }
                }
            }
            if (allPropertiesSet) break;
            yield return new WaitForSeconds(delay);
            retries--;
            CustomLogger.Log($"ScoreboardManager: Retry {4 - retries}/3 to ensure properties for all players");
        }

        int redTeamScore = 0;
        int cyanTeamScore = 0;
        if (SceneManager.GetActiveScene().name == "TeamMoonRan")
        {
            foreach (var player in allPlayers)
            {
                int points = player.CustomProperties != null && player.CustomProperties.ContainsKey("Points") ? (int)player.CustomProperties["Points"] : 0;
                string team = player.CustomProperties != null && player.CustomProperties.ContainsKey("Team") ? player.CustomProperties["Team"].ToString() : "None";
                if (team == "Red") redTeamScore += points;
                else if (team == "Cyan") cyanTeamScore += points;
            }
            if (redTeamScoreText != null)
            {
                redTeamScoreText.text = $"Red Team: {redTeamScore}";
                redTeamScoreText.color = redTeamColor;
                CustomLogger.Log($"ScoreboardManager: Updated RedTeamScoreText to 'Red Team: {redTeamScore}'");
            }
            if (cyanTeamScoreText != null)
            {
                cyanTeamScoreText.text = $"Cyan Team: {cyanTeamScore}";
                cyanTeamScoreText.color = cyanTeamColor;
                CustomLogger.Log($"ScoreboardManager: Updated CyanTeamScoreText to 'Cyan Team: {cyanTeamScore}'");
            }
            CustomLogger.Log($"ScoreboardManager: Team Scores - Red: {redTeamScore}, Cyan: {cyanTeamScore}");
        }

        List<IPlayer> sortedPlayers;
        if (SceneManager.GetActiveScene().name == "TeamMoonRan")
        {
            List<IPlayer> redTeamPlayers = allPlayers
                .Where(p => p.CustomProperties != null && p.CustomProperties.ContainsKey("Team") && p.CustomProperties["Team"].ToString() == "Red")
                .OrderByDescending(p => p.CustomProperties != null && p.CustomProperties.ContainsKey("Points") ? (int)p.CustomProperties["Points"] : 0)
                .Take(5)
                .ToList();

            List<IPlayer> cyanTeamPlayers = allPlayers
                .Where(p => p.CustomProperties != null && p.CustomProperties.ContainsKey("Team") && p.CustomProperties["Team"].ToString() == "Cyan")
                .OrderByDescending(p => p.CustomProperties != null && p.CustomProperties.ContainsKey("Points") ? (int)p.CustomProperties["Points"] : 0)
                .Take(15)
                .ToList();

            sortedPlayers = redTeamPlayers.Concat(cyanTeamPlayers).Take(20).ToList();
        }
        else
        {
            sortedPlayers = allPlayers
                .OrderByDescending(p =>
                {
                    if (p.CustomProperties == null)
                    {
                        p.CustomProperties = new Hashtable();
                        p.CustomProperties["Points"] = 0;
                        p.CustomProperties["Team"] = "None";
                        CustomLogger.LogWarning($"ScoreboardManager: Initialized null CustomProperties with Points=0, Team=None for player {p.NickName} (ActorNumber={p.ActorNumber})");
                    }
                    if (!p.CustomProperties.ContainsKey("Points") || !p.CustomProperties.ContainsKey("Team"))
                    {
                        Hashtable props = new Hashtable
                        {
                        { "Points", 0 },
                        { "Team", "None" }
                        };
                        if (p is BoundaryManager.RealPlayerWrapper realPlayer)
                        {
                            realPlayer._player.SetCustomProperties(props);
                        }
                        else if (p is BotController botControllerSort && botControllerSort.photonView != null)
                        {
                            botControllerSort.photonView.RPC("UpdateCustomProperties", RpcTarget.AllBuffered, props);
                        }
                        p.CustomProperties["Points"] = 0;
                        p.CustomProperties["Team"] = "None";
                        CustomLogger.Log($"ScoreboardManager: Initialized Points=0, Team=None for player {p.NickName} (ActorNumber={p.ActorNumber})");
                    }
                    return (int)p.CustomProperties["Points"];
                })
                .Take(20)
                .ToList();
        }

        string playerPointsLog = "ScoreboardManager: Player Points - ";
        foreach (var player in sortedPlayers)
        {
            int points = player.CustomProperties != null && player.CustomProperties.ContainsKey("Points") ? (int)player.CustomProperties["Points"] : 0;
            string team = player.CustomProperties != null && player.CustomProperties.ContainsKey("Team") ? player.CustomProperties["Team"].ToString() : "None";
            playerPointsLog += $"[{player.NickName}: {points}, Team={team}], ";
        }
        CustomLogger.Log(playerPointsLog.TrimEnd(',', ' '));

        for (int i = 0; i < sortedPlayers.Count; i++)
        {
            IPlayer player = sortedPlayers[i];
            if (scoreboardEntryPrefab == null)
            {
                CustomLogger.LogError("ScoreboardManager: ScoreboardEntryPrefab is null during UpdateScoreboard. Skipping entry creation.");
                continue;
            }
            GameObject entry = Instantiate(scoreboardEntryPrefab, scoreboardEntryContainer);
            entryObjects.Add(entry);

            RectTransform entryRect = entry.GetComponent<RectTransform>();
            entryRect.anchorMin = new Vector2(0.5f, 1f);
            entryRect.anchorMax = new Vector2(0.5f, 1f);
            entryRect.pivot = new Vector2(0.5f, 1f);
            float yOffset = -30 - i * 30;
            entryRect.anchoredPosition = new Vector2(0, yOffset);

            Transform rankTransform = entry.transform.Find("RankText");
            Transform usernameTransform = entry.transform.Find("UsernameText");
            Transform pointsTransform = entry.transform.Find("PointsText");
            Transform causeTransform = entry.transform.Find("CauseText");
            TextMeshProUGUI rankText = rankTransform != null ? rankTransform.GetComponent<TextMeshProUGUI>() : null;
            TextMeshProUGUI usernameText = usernameTransform != null ? usernameTransform.GetComponent<TextMeshProUGUI>() : null;
            TextMeshProUGUI pointsText = pointsTransform != null ? pointsTransform.GetComponent<TextMeshProUGUI>() : null;
            TextMeshProUGUI causeText = causeTransform != null ? causeTransform.GetComponent<TextMeshProUGUI>() : null;

            if (rankText == null) rankText = FindTextMeshProUGUIByName(entry, "RankText");
            if (usernameText == null) usernameText = FindTextMeshProUGUIByName(entry, "UsernameText");
            if (pointsText == null) pointsText = FindTextMeshProUGUIByName(entry, "PointsText");

            if (rankText == null || usernameText == null || pointsText == null)
            {
                CustomLogger.LogError($"ScoreboardManager: Scoreboard entry prefab issue for player {player.NickName}. RankText={(rankTransform == null && rankText == null ? "not found" : rankText == null ? "no TextMeshProUGUI" : "found")}, UsernameText={(usernameTransform == null && usernameText == null ? "not found" : usernameText == null ? "no TextMeshProUGUI" : "found")}, PointsText={(pointsTransform == null && pointsText == null ? "not found" : pointsText == null ? "no TextMeshProUGUI" : "found")}");
                Destroy(entry);
                entryObjects.Remove(entry);
                continue;
            }

            rankText.text = (i + 1).ToString();
            usernameText.text = GetPlayerUsername(player);
            pointsText.text = player.CustomProperties != null && player.CustomProperties.ContainsKey("Points") ? ((int)player.CustomProperties["Points"]).ToString() : "0";
            if (causeText != null) causeText.gameObject.SetActive(false);

            if (SceneManager.GetActiveScene().name == "TeamMoonRan")
            {
                string team = player.CustomProperties != null && player.CustomProperties.ContainsKey("Team") ? player.CustomProperties["Team"].ToString() : "None";
                Color teamColor = team == "Red" ? redTeamColor : team == "Cyan" ? cyanTeamColor : Color.white;
                usernameText.color = teamColor;
                pointsText.color = teamColor;
                rankText.color = teamColor;
                CustomLogger.Log($"ScoreboardManager: Applied team color {teamColor} for player {player.NickName}, Team={team}");
            }

            CustomLogger.Log($"ScoreboardManager: Added entry for {player.NickName}, Rank={rankText.text}, Name={usernameText.text}, Points={pointsText.text}, Position={entryRect.anchoredPosition}");
        }

        UpdateCrowns();
    }

    private TextMeshProUGUI FindTextMeshProUGUIByName(GameObject go, string name)
    {
        foreach (Transform child in go.GetComponentsInChildren<Transform>(true))
        {
            if (child.gameObject.name == name)
            {
                TextMeshProUGUI component = child.GetComponent<TextMeshProUGUI>();
                if (component != null)
                {
                    return component;
                }
            }
        }
        return null;
    }

    public bool IsTopPlayer(int actorNumber)
    {
        List<IPlayer> allPlayers = PhotonNetwork.PlayerList
            .Select(p => new BoundaryManager.RealPlayerWrapper(p) as IPlayer)
            .Concat(Object.FindObjectsByType<BotController>(FindObjectsSortMode.None))
            .ToList();

        if (SceneManager.GetActiveScene().name == "TeamMoonRan")
        {
            IPlayer redTopPlayer = allPlayers
                .Where(p => p.CustomProperties != null && p.CustomProperties.ContainsKey("Team") && p.CustomProperties["Team"].ToString() == "Red")
                .OrderByDescending(p =>
                {
                    if (p.CustomProperties != null && p.CustomProperties.ContainsKey("Points"))
                    {
                        return (int)p.CustomProperties["Points"];
                    }
                    CustomLogger.LogWarning($"ScoreboardManager: Player {p.NickName} (ActorNumber={p.ActorNumber}) has no Points in CustomProperties for Red team IsTopPlayer, defaulting to 0");
                    return 0;
                })
                .FirstOrDefault();

            IPlayer cyanTopPlayer = allPlayers
                .Where(p => p.CustomProperties != null && p.CustomProperties.ContainsKey("Team") && p.CustomProperties["Team"].ToString() == "Cyan")
                .OrderByDescending(p =>
                {
                    if (p.CustomProperties != null && p.CustomProperties.ContainsKey("Points"))
                    {
                        return (int)p.CustomProperties["Points"];
                    }
                    CustomLogger.LogWarning($"ScoreboardManager: Player {p.NickName} (ActorNumber={p.ActorNumber}) has no Points in CustomProperties for Cyan team IsTopPlayer, defaulting to 0");
                    return 0;
                })
                .FirstOrDefault();

            bool isTop = (redTopPlayer != null && redTopPlayer.ActorNumber == actorNumber) ||
                         (cyanTopPlayer != null && cyanTopPlayer.ActorNumber == actorNumber);
            CustomLogger.Log($"ScoreboardManager: IsTopPlayer check for ActorNumber={actorNumber}, RedTopPlayer={(redTopPlayer != null ? redTopPlayer.NickName : "none")}, CyanTopPlayer={(cyanTopPlayer != null ? cyanTopPlayer.NickName : "none")}, isTop={isTop}");
            return isTop;
        }
        else
        {
            IPlayer topPlayer = allPlayers
                .OrderByDescending(p =>
                {
                    if (p.CustomProperties != null && p.CustomProperties.ContainsKey("Points"))
                    {
                        return (int)p.CustomProperties["Points"];
                    }
                    CustomLogger.LogWarning($"ScoreboardManager: Player {p.NickName} (ActorNumber={p.ActorNumber}) has no Points in CustomProperties for IsTopPlayer, defaulting to 0");
                    return 0;
                })
                .FirstOrDefault();
            bool isTop = topPlayer != null && topPlayer.ActorNumber == actorNumber;
            CustomLogger.Log($"ScoreboardManager: IsTopPlayer check for ActorNumber={actorNumber}, topPlayer={(topPlayer != null ? topPlayer.NickName : "none")}, isTop={isTop}");
            return isTop;
        }
    }

    public override void OnPlayerEnteredRoom(Player newPlayer)
    {
        UpdateScoreboard();
        CustomLogger.Log($"ScoreboardManager: Updated scoreboard, new player {newPlayer.NickName} entered, ActorNumber={newPlayer.ActorNumber}");
    }

    public override void OnPlayerLeftRoom(Player otherPlayer)
    {
        UpdateScoreboard();
        CustomLogger.Log($"ScoreboardManager: Updated scoreboard, player {otherPlayer.NickName} left, ActorNumber={otherPlayer.ActorNumber}");
    }

    public void ResetScoreboard()
    {
        if (!PhotonNetwork.IsMasterClient)
        {
            CustomLogger.Log("ScoreboardManager: ResetScoreboard ignored, not Master Client.");
            return;
        }

        ClearScoreboardUI();
        photonView.RPC("ResetScoreboardRPC", RpcTarget.AllBuffered);
        UnlockScoreboard();
        ForceUpdateScoreboard();
        CustomLogger.Log("ScoreboardManager: Initiated scoreboard reset");
    }

    [PunRPC]
    private void ResetScoreboardRPC()
    {
        foreach (Player player in PhotonNetwork.PlayerList)
        {
            string username = player.CustomProperties.TryGetValue("Username", out object usernameObj) && usernameObj != null ? usernameObj.ToString() : player.NickName;
            player.SetCustomProperties(new Hashtable());
            Hashtable resetProps = new Hashtable
            {
                { "Points", 0 },
                { "Username", username }
            };
            player.SetCustomProperties(resetProps);
            CustomLogger.Log($"ScoreboardManager: Cleared properties and reset Points=0, Username={username} for player {player.NickName}, ActorNumber={player.ActorNumber}");
        }

        BotController[] bots = Object.FindObjectsByType<BotController>(FindObjectsSortMode.None);
        if (bots.Length == 0)
        {
            CustomLogger.LogWarning("ScoreboardManager: No BotControllers found during ResetScoreboardRPC. Ensure bots are spawned.");
        }
        foreach (BotController bot in bots)
        {
            if (bot.photonView != null)
            {
                string username = bot.CustomProperties.TryGetValue("Username", out object usernameObj) && usernameObj != null ? usernameObj.ToString() : $"Bot_{bot.ActorNumber}";
                bot.photonView.RPC("ClearCustomProperties", RpcTarget.AllBuffered);
                Hashtable resetProps = new Hashtable
                {
                    { "Points", 0 },
                    { "Username", username }
                };
                bot.photonView.RPC("UpdateCustomProperties", RpcTarget.AllBuffered, resetProps);
                CustomLogger.Log($"ScoreboardManager: Cleared properties and sent reset props for bot {bot.gameObject.name}, Points=0, Username={username}");
                StartCoroutine(RetryBotPropertyUpdate(bot, resetProps));
            }
        }

        ClearScoreboardUI();
        UnlockScoreboard();
        StartCoroutine(ForceScoreboardUpdate());
        CustomLogger.Log("ScoreboardManager: Completed scoreboard reset via RPC");
    }

    [PunRPC]
    private void ClearAllPlayerPropertiesRPC()
    {
        foreach (Player player in PhotonNetwork.PlayerList)
        {
            string username = player.CustomProperties.TryGetValue("Username", out object usernameObj) && usernameObj != null ? usernameObj.ToString() : player.NickName;
            player.SetCustomProperties(new Hashtable());
            player.SetCustomProperties(new Hashtable { { "Username", username } });
            CustomLogger.Log($"ScoreboardManager: Cleared all CustomProperties for player {player.NickName}, ActorNumber={player.ActorNumber}, restored Username={username}");
        }

        BotController[] bots = Object.FindObjectsByType<BotController>(FindObjectsSortMode.None);
        foreach (BotController bot in bots)
        {
            if (bot.photonView != null)
            {
                string username = bot.CustomProperties.TryGetValue("Username", out object usernameObj) && usernameObj != null ? usernameObj.ToString() : $"Bot_{bot.ActorNumber}";
                bot.photonView.RPC("ClearCustomProperties", RpcTarget.AllBuffered);
                Hashtable props = new Hashtable { { "Username", username } };
                bot.photonView.RPC("UpdateCustomProperties", RpcTarget.AllBuffered, props);
                CustomLogger.Log($"ScoreboardManager: Cleared properties for bot {bot.gameObject.name}, restored Username={username}");
            }
        }

        ClearScoreboardUI();
        UpdateScoreboard();
        CustomLogger.Log("ScoreboardManager: Cleared all player and bot properties via RPC");
    }

    private IEnumerator RetryBotPropertyUpdate(BotController bot, Hashtable props)
    {
        yield return new WaitForSeconds(1f);
        if (bot != null && bot.photonView != null)
        {
            bot.photonView.RPC("UpdateCustomProperties", RpcTarget.AllBuffered, props);
            CustomLogger.Log($"ScoreboardManager: Retried reset props for bot {bot.gameObject.name}");
        }
    }

    private IEnumerator ForceScoreboardUpdate()
    {
        yield return new WaitForSeconds(0.5f);
        ForceUpdateScoreboard();
        CustomLogger.Log("ScoreboardManager: Forced scoreboard UI update after reset");
    }

    public void ForceUpdateScoreboard()
    {
        bool wasMatchEnded = isMatchEnded;
        isMatchEnded = false;
        UpdateScoreboard();
        isMatchEnded = wasMatchEnded;
        CustomLogger.Log("ScoreboardManager: Forced scoreboard update, ignoring match ended state");
    }

    private void ClearScoreboardUI()
    {
        foreach (GameObject entry in entryObjects)
        {
            if (entry != null)
            {
                Destroy(entry);
            }
        }
        entryObjects.Clear();

        if (headerEntry != null)
        {
            Destroy(headerEntry);
            headerEntry = null;
            CreateHeader();
            CustomLogger.Log("ScoreboardManager: Recreated scorecard header after reset");
        }

        if (redTeamScoreText != null)
        {
            redTeamScoreText.text = "Red Team: 0";
            CustomLogger.Log("ScoreboardManager: Reset RedTeamScoreText to 'Red Team: 0'");
        }
        if (cyanTeamScoreText != null)
        {
            cyanTeamScoreText.text = "Cyan Team: 0";
            CustomLogger.Log("ScoreboardManager: Reset CyanTeamScoreText to 'Cyan Team: 0'");
        }

        photonView.RPC("ClearAllCrownsRPC", RpcTarget.All);
        CustomLogger.Log("ScoreboardManager: Cleared all crowns during scoreboard UI clear");
    }

    public Sprite GetCrownSprite()
    {
        return crownSprite;
    }
}