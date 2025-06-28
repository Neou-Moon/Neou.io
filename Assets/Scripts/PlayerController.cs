using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using Photon.Pun;
using Photon.Realtime;
using System.Runtime.InteropServices;
using System.Collections;
using System; // Added for Exception
using System.Linq; // Added for Cast<object>()
using Hashtable = ExitGames.Client.Photon.Hashtable;
using TMPro;
using System.Collections.Generic; // For List<T>

public class PlayerController : MonoBehaviourPunCallbacks, IPlayer, IPunObservable
{
    public PlayerMovement playerMovement;
    public PlayerFuel playerFuel;
    public GameObject shockShield;
    public GameObject bombPrefab;
    public Slider fuelBar;
    private SpaceShipInteraction shipInteraction;
    private UpgradeManager upgradeManager;
    private TwinTurretManager twinTurretManager;
    private PhasingTeleportation phasingTeleportation;
    private LaserBeam laserBeam;
    public bool isShieldActive;

    private int brightMatterCollected = 0;
    private int points = 0;
    private int crowns = 0; // Persistent across sessions
    public int Points => points;
    private bool isAddingBrightMatter = false;
    private bool hasLoadedBrightMatter = false;
    private static int addBrightMatterCount = 0;
    private bool areActionsReady = false;
    private PlayerCanvasController canvasController;
    private Compass compass;
    public bool HasDied { get; private set; } = false;

    [SerializeField] private Joystick movementJoystick;
    [SerializeField] private Joystick shootingJoystick;
    public string NickName { get; set; }
    public Hashtable CustomProperties { get; set; }
    public int ActorNumber { get; private set; }
    public bool IsLocal => photonView.IsMine;
    [SerializeField] private TextMeshProUGUI killMessageText;
    private Coroutine flashMessageCoroutine;
    private Coroutine monitorDistanceCoroutine;
    [SerializeField] private Image toggleImage;
    [SerializeField] private Sprite toggleSprite;
    private bool isImageVisible = false;
    [SerializeField] private TextMeshProUGUI toggleText;
    [SerializeField] private AudioSource musicPlayer;
    [SerializeField] private Button playPauseButton;
    [SerializeField] private Button skipButton;
    [SerializeField] private Button previousButton;
    [SerializeField] private Button muteButton;
    [SerializeField] private TextMeshProUGUI currentSongText;
    private List<AudioClip> playlist = new List<AudioClip>();
    private int currentSongIndex = -1;
    private bool isMuted = false;
    private float previousVolume = 1f;
    [SerializeField] private SongDatabase songDatabase; // Add reference to SongDatabase
    [SerializeField] private CanvasGroup musicControlPanel;
    private bool isSongPaused = false;
    [SerializeField] private Button musicToggleButton; // Button for music toggle, assigned in Inspector
    private bool isMusicPanelTimerRunning = false;
    private float musicPanelTimer = 0f;
    [SerializeField] private TextMeshProUGUI musicPanelTimerText;

    [DllImport("__Internal")]
    private static extern void FirebaseSaveBrightMatter(string uid, int brightMatter);

    [DllImport("__Internal")]
    private static extern void FirebaseLoadBrightMatter(string uid);

    [DllImport("__Internal")]
    private static extern void FirebaseLoadFuel(string uid);

    [DllImport("__Internal")]
    private static extern void FirebaseLoadCrowns(string uid, string gameMode);

    [DllImport("__Internal")]
    private static extern void FirebaseSaveCrowns(string uid, int crowns, string gameMode);

    void Awake()
    {
        if (photonView.IsMine)
        {
            ActorNumber = PhotonNetwork.LocalPlayer.ActorNumber;
            NickName = PlayerPrefs.GetString("PlayerNickname", PlayerPrefs.GetString("PlayerUsername", $"Player_{ActorNumber}"));
            PhotonNetwork.NickName = NickName;
            CustomProperties = new Hashtable();
            CustomProperties["Points"] = points;
            CustomProperties["Username"] = NickName;
            CustomProperties["Nickname"] = NickName;
            CustomProperties["Crowns"] = crowns;
            PhotonNetwork.LocalPlayer.TagObject = gameObject;
            SetPlayerViewID();
            isShieldActive = false;
            if (shockShield != null)
            {
                shockShield.SetActive(false);
                Debug.Log($"PlayerController: Initialized shockShield, set active=false, ViewID={photonView.ViewID}");
            }
            StartCoroutine(SyncInitialProperties());
            CustomLogger.Log($"PlayerController: Initialized player {NickName}, ActorNumber={ActorNumber}, ViewID={photonView.ViewID}, Points={points}, Crowns={crowns}");
            StartCoroutine(NotifyCameraFollow());
            LoadCrowns();
        }
    }
    private IEnumerator SyncInitialProperties()
    {
        yield return new WaitForSeconds(0.5f); // Wait for Photon to stabilize
        int retries = 5;
        float delay = 0.2f;
        while (retries > 0)
        {
            if (PhotonNetwork.IsConnectedAndReady && PhotonNetwork.LocalPlayer != null)
            {
                SetCustomProperties(CustomProperties);
                CustomLogger.Log($"PlayerController: Synced initial properties for {NickName}, retry {6 - retries}/5");
                yield break;
            }
            retries--;
            CustomLogger.Log($"PlayerController: Waiting for Photon to sync initial properties, retry {6 - retries}/5");
            yield return new WaitForSeconds(delay);
        }
        CustomLogger.LogError($"PlayerController: Failed to sync initial properties for {NickName} after retries");
    }

    private void SetPlayerViewID()
    {
        if (!PhotonNetwork.IsConnectedAndReady || PhotonNetwork.LocalPlayer == null || photonView == null)
        {
            CustomLogger.LogWarning("PlayerController: Photon not ready, scheduling retry for SetPlayerViewID.");
            StartCoroutine(RetrySetPlayerViewID());
            return;
        }
        Hashtable props = new Hashtable
        {
            { "PlayerViewID", photonView.ViewID },
            { "Username", PhotonNetwork.LocalPlayer.NickName },
            { "Points", points },
            { "Crowns", crowns }
        };
        CustomProperties = props;
        PhotonNetwork.LocalPlayer.SetCustomProperties(props);
        CustomLogger.Log($"PlayerController: Set PlayerViewID={photonView.ViewID}, Username={NickName}, Points={points}, Crowns={crowns} in CustomProperties for ActorNumber={ActorNumber}");
    }

    private IEnumerator RetrySetPlayerViewID()
    {
        int maxRetries = 10;
        int retries = 0;
        float delay = 0.5f;
        while (retries < maxRetries)
        {
            if (PhotonNetwork.IsConnectedAndReady && PhotonNetwork.LocalPlayer != null && photonView != null)
            {
                SetPlayerViewID();
                yield break;
            }
            retries++;
            CustomLogger.Log($"PlayerController: Retry {retries}/{maxRetries} for SetPlayerViewID, IsConnected={PhotonNetwork.IsConnectedAndReady}, LocalPlayer={(PhotonNetwork.LocalPlayer != null)}, PhotonView={(photonView != null)}");
            yield return new WaitForSeconds(delay);
            delay = Mathf.Min(delay * 1.5f, 2f);
        }
        CustomLogger.LogError($"PlayerController: Failed to set PlayerViewID after {maxRetries} retries, switching to offline mode fallback.");
        PhotonNetwork.OfflineMode = true;
        SetPlayerViewID();
    }

    public bool SetCustomProperties(Hashtable propertiesToSet)
    {
        if (!photonView.IsMine || !PhotonNetwork.IsConnectedAndReady)
        {
            CustomLogger.LogWarning($"PlayerController: Failed to set custom properties for {NickName}, IsMine={photonView.IsMine}, IsConnected={PhotonNetwork.IsConnectedAndReady}");
            return false;
        }

        int retries = 3;
        float delay = 0.2f;
        bool success = false;

        while (retries > 0)
        {
            try
            {
                // Create a new Hashtable and copy existing properties
                Hashtable newProperties = new Hashtable();
                if (CustomProperties != null)
                {
                    foreach (DictionaryEntry entry in CustomProperties)
                    {
                        newProperties[entry.Key] = entry.Value;
                    }
                }
                // Merge new properties
                foreach (DictionaryEntry entry in propertiesToSet)
                {
                    newProperties[entry.Key] = entry.Value;
                }
                CustomProperties = newProperties;
                PhotonNetwork.LocalPlayer.SetCustomProperties(CustomProperties);
                success = true;
                CustomLogger.Log($"PlayerController: SetCustomProperties for {NickName}, ViewID={photonView.ViewID}, Properties={string.Join(", ", CustomProperties.Keys.Cast<object>().Select(k => $"{k}={CustomProperties[k]}"))}");
                break;
            }
            catch (System.Exception e)
            {
                CustomLogger.LogError($"PlayerController: Error setting properties for {NickName}, retry {4 - retries}/3: {e.Message}");
            }
            retries--;
            System.Threading.Thread.Sleep((int)(delay * 1000));
        }

        if (!success)
        {
            CustomLogger.LogError($"PlayerController: Failed to set custom properties for {NickName} after retries");
        }
        return success;
    }

    void Start()
    {
        string sceneName = SceneManager.GetActiveScene().name;
        if (sceneName != "Moon Ran" && sceneName != "TeamMoonRan")
        {
            CustomLogger.LogWarning($"PlayerController: Disabled in {sceneName}. Only active in Moon Ran or TeamMoonRan.");
            enabled = false;
            return;
        }

        if (photonView.IsMine)
        {
            CustomLogger.Log($"PlayerController: Instantiated local player {NickName}, ViewID={photonView.ViewID}, IsMine={photonView.IsMine}");
            StartCoroutine(InitializePlayerSetup()); // Changed from InitializeComponents to InitializePlayerSetup
        }
        else
        {
            CustomLogger.Log($"PlayerController: Non-local player detected, ViewID={photonView.ViewID}, Owner={photonView.Owner?.NickName}");
            if (upgradeManager != null)
            {
                upgradeManager.enabled = false;
                if (upgradeManager.upgradePanel != null)
                    upgradeManager.upgradePanel.SetActive(false);
                CustomLogger.Log($"PlayerController: Disabled UpgradeManager for non-local player {NickName}");
            }
            if (killMessageText != null)
            {
                killMessageText.gameObject.SetActive(false);
                CustomLogger.Log($"PlayerController: Disabled killMessageText for non-local player {NickName}");
            }
            if (musicPlayer != null)
            {
                musicPlayer.gameObject.SetActive(false);
                CustomLogger.Log($"PlayerController: Disabled music player for non-local player {NickName}");
            }
        }
    }
    private IEnumerator InitializePlayerSetup()
    {
        int retryCount = 0;
        const int maxRetries = 10;
        const float retryDelay = 0.5f;

        while (retryCount < maxRetries)
        {
            playerFuel = GetComponent<PlayerFuel>();
            shipInteraction = GetComponent<SpaceShipInteraction>();
            upgradeManager = GetComponent<UpgradeManager>();
            twinTurretManager = GetComponent<TwinTurretManager>();
            phasingTeleportation = GetComponent<PhasingTeleportation>();
            laserBeam = GetComponentInChildren<LaserBeam>();
            musicPlayer = GetComponent<AudioSource>();

            if (playerFuel == null)
            {
                Debug.LogWarning($"PlayerController: PlayerFuel not found on {gameObject.name}. Adding one.");
                playerFuel = gameObject.AddComponent<PlayerFuel>();
            }
            if (phasingTeleportation == null)
            {
                Debug.LogWarning($"PlayerController: PhasingTeleportation not found on {gameObject.name}. Adding one.");
                phasingTeleportation = gameObject.AddComponent<PhasingTeleportation>();
            }
            if (twinTurretManager == null)
            {
                Debug.LogWarning($"PlayerController: TwinTurretManager not found on {gameObject.name}. Adding one.");
                twinTurretManager = gameObject.AddComponent<TwinTurretManager>();
            }
            if (GetComponent<ShockShield>() == null)
            {
                Debug.LogWarning($"PlayerController: ShockShield not found on {gameObject.name}. Adding one.");
                gameObject.AddComponent<ShockShield>();
            }
            if (laserBeam == null)
            {
                Debug.LogWarning($"PlayerController: LaserBeam not found on {gameObject.name} or its children. Searching again after delay.");
                yield return new WaitForSeconds(0.5f);
                laserBeam = GetComponentInChildren<LaserBeam>();
            }
            if (musicPlayer == null)
            {
                Debug.LogWarning($"PlayerController: AudioSource not found on {gameObject.name}. Adding one.");
                musicPlayer = gameObject.AddComponent<AudioSource>();
                musicPlayer.playOnAwake = false;
                musicPlayer.loop = false;
                musicPlayer.volume = 1f; // Ensure default volume
                CustomLogger.Log($"PlayerController: Initialized AudioSource for {NickName}");
            }

            while (playerFuel == null || playerFuel.CurrentFuel < 0)
            {
                Debug.Log($"PlayerController: Waiting for PlayerFuel initialization on {gameObject.name}");
                yield return new WaitForSeconds(0.1f);
                playerFuel = GetComponent<PlayerFuel>();
            }

            ShockShield shockShield = GetComponent<ShockShield>();
            while (shockShield == null || shockShield.GetEnergy() < 2f)
            {
                Debug.Log($"PlayerController: Waiting for Shield initialization on {gameObject.name}, energy={shockShield?.GetEnergy() ?? -1}");
                yield return new WaitForSeconds(0.1f);
                shockShield = GetComponent<ShockShield>();
            }

            Canvas playerCanvas = GetComponentInChildren<Canvas>(true);
            if (playerCanvas == null)
            {
                CustomLogger.LogWarning($"PlayerController: No Player Canvas found in {gameObject.name}, creating one. Retry {retryCount + 1}/{maxRetries}");
                GameObject canvasObj = new GameObject("Player Canvas");
                canvasObj.transform.SetParent(transform, false);
                playerCanvas = canvasObj.AddComponent<Canvas>();
                playerCanvas.renderMode = RenderMode.ScreenSpaceOverlay;
                CanvasScaler scaler = canvasObj.AddComponent<CanvasScaler>();
                scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
                scaler.referenceResolution = new Vector2(1920, 1080);
                canvasObj.AddComponent<GraphicRaycaster>();
                CustomLogger.Log($"PlayerController: Created new Player Canvas for {NickName}");
            }
            else if (playerCanvas.name != "Player Canvas")
            {
                playerCanvas.name = "Player Canvas";
                CustomLogger.Log($"PlayerController: Renamed canvas to 'Player Canvas' for {NickName}");
            }
            if (!playerCanvas.gameObject.activeInHierarchy)
            {
                playerCanvas.gameObject.SetActive(true);
                CustomLogger.Log($"PlayerController: Activated Player Canvas for {NickName}");
            }

            canvasController = playerCanvas.GetComponent<PlayerCanvasController>();
            if (canvasController == null)
            {
                CustomLogger.LogError($"PlayerController: PlayerCanvasController not found on Player Canvas. Adding one for {gameObject.name}");
                canvasController = playerCanvas.gameObject.AddComponent<PlayerCanvasController>();
            }
            CustomLogger.Log($"PlayerController: Found PlayerCanvasController on Player Canvas at {GetGameObjectPath(playerCanvas.gameObject)}");

            if (toggleImage == null)
            {
                GameObject imageObj = new GameObject("ToggleImage");
                imageObj.transform.SetParent(playerCanvas.transform, false);
                toggleImage = imageObj.AddComponent<Image>();
                toggleImage.rectTransform.sizeDelta = new Vector2(1440, 1080);
                toggleImage.rectTransform.anchoredPosition = Vector2.zero;
                toggleImage.raycastTarget = false;
                if (toggleSprite != null)
                {
                    toggleImage.sprite = toggleSprite;
                    toggleImage.preserveAspect = true;
                    toggleImage.color = new Color(1f, 1f, 1f, 0.8f);
                }
                else
                {
                    toggleImage.color = new Color(1f, 1f, 1f, 0.8f);
                    CustomLogger.LogWarning($"PlayerController: No toggleSprite assigned for {NickName}, using default white image");
                }
                toggleImage.gameObject.SetActive(false);
                CustomLogger.Log($"PlayerController: Created ToggleImage for {NickName} on Player Canvas, size=1440x1080, opacity=80%");
            }
            else
            {
                toggleImage.rectTransform.sizeDelta = new Vector2(1440, 1080);
                toggleImage.preserveAspect = true;
                toggleImage.color = new Color(toggleImage.color.r, toggleImage.color.g, toggleImage.color.b, 0.8f);
                toggleImage.raycastTarget = false;
                toggleImage.gameObject.SetActive(false);
                CustomLogger.Log($"PlayerController: Found assigned ToggleImage for {NickName}, set size=1440x1080, opacity=80%");
            }

            if (killMessageText == null)
            {
                GameObject textObj = new GameObject("KillMessageText");
                textObj.transform.SetParent(playerCanvas.transform, false);
                killMessageText = textObj.AddComponent<TextMeshProUGUI>();
                killMessageText.text = "";
                killMessageText.fontSize = 24;
                killMessageText.color = Color.white;
                killMessageText.alignment = TextAlignmentOptions.Center;
                killMessageText.rectTransform.anchorMin = new Vector2(0.5f, 0.8f);
                killMessageText.rectTransform.anchorMax = new Vector2(0.5f, 0.8f);
                killMessageText.rectTransform.anchoredPosition = Vector2.zero;
                killMessageText.rectTransform.sizeDelta = new Vector2(300, 50);
                killMessageText.gameObject.SetActive(false);
                CustomLogger.Log($"PlayerController: Created KillMessageText for {NickName} on Player Canvas");
            }
            else
            {
                killMessageText.gameObject.SetActive(false);
                CustomLogger.Log($"PlayerController: Validated KillMessageText for {NickName}");
            }

            // Initialize Music Control Panel
            GameObject musicPanel = musicControlPanel != null ? musicControlPanel.gameObject : playerCanvas.transform.Find("MusicControlPanel")?.gameObject;
            if (musicPanel == null)
            {
                musicPanel = new GameObject("MusicControlPanel");
                musicPanel.transform.SetParent(playerCanvas.transform, false);
                RectTransform panelRect = musicPanel.AddComponent<RectTransform>();
                panelRect.anchorMin = new Vector2(0.5f, 0);
                panelRect.anchorMax = new Vector2(0.5f, 0);
                panelRect.anchoredPosition = new Vector2(0, -50);
                panelRect.sizeDelta = new Vector2(300, 100);
                Image panelImage = musicPanel.AddComponent<Image>();
                panelImage.color = new Color(0, 0, 0, 0.7f);
                musicControlPanel = musicPanel.AddComponent<CanvasGroup>();
                musicControlPanel.alpha = 0f;
                musicControlPanel.interactable = false;
                musicControlPanel.blocksRaycasts = false;
                CustomLogger.Log($"PlayerController: Created MusicControlPanel for {NickName} on Player Canvas");
            }
            else if (musicControlPanel == null)
            {
                musicControlPanel = musicPanel.GetComponent<CanvasGroup>();
                if (musicControlPanel == null)
                {
                    musicControlPanel = musicPanel.AddComponent<CanvasGroup>();
                    musicControlPanel.alpha = 0f;
                    musicControlPanel.interactable = false;
                    musicControlPanel.blocksRaycasts = false;
                    CustomLogger.Log($"PlayerController: Added CanvasGroup to existing MusicControlPanel for {NickName}");
                }
            }

            if (playPauseButton == null)
            {
                GameObject buttonObj = new GameObject("PlayPauseButton");
                buttonObj.transform.SetParent(musicPanel.transform, false);
                playPauseButton = buttonObj.AddComponent<Button>();
                playPauseButton.GetComponent<RectTransform>().sizeDelta = new Vector2(40, 40);
                playPauseButton.GetComponent<RectTransform>().anchoredPosition = new Vector2(-90, 0);
                Image img = buttonObj.AddComponent<Image>();
                img.color = Color.white;
                playPauseButton.interactable = true;
                playPauseButton.onClick.RemoveAllListeners();
                playPauseButton.onClick.AddListener(() => { TogglePlayPause(); CustomLogger.Log($"PlayerController: PlayPauseButton clicked for {NickName}"); });
                CustomLogger.Log($"PlayerController: Created PlayPauseButton for {NickName} on MusicControlPanel");
            }
            else
            {
                playPauseButton.interactable = true;
                playPauseButton.onClick.RemoveAllListeners();
                playPauseButton.onClick.AddListener(() => { TogglePlayPause(); CustomLogger.Log($"PlayerController: PlayPauseButton clicked for {NickName}"); });
                CustomLogger.Log($"PlayerController: Reassigned PlayPauseButton listener for {NickName}");
            }

            if (skipButton == null)
            {
                GameObject buttonObj = new GameObject("SkipButton");
                buttonObj.transform.SetParent(musicPanel.transform, false);
                skipButton = buttonObj.AddComponent<Button>();
                skipButton.GetComponent<RectTransform>().sizeDelta = new Vector2(40, 40);
                skipButton.GetComponent<RectTransform>().anchoredPosition = new Vector2(-30, 0);
                Image img = buttonObj.AddComponent<Image>();
                img.color = Color.white;
                skipButton.interactable = true;
                skipButton.onClick.RemoveAllListeners();
                skipButton.onClick.AddListener(() => { PlayNextSong(); CustomLogger.Log($"PlayerController: SkipButton clicked for {NickName}"); });
                CustomLogger.Log($"PlayerController: Created SkipButton for {NickName} on MusicControlPanel");
            }
            else
            {
                skipButton.interactable = true;
                skipButton.onClick.RemoveAllListeners();
                skipButton.onClick.AddListener(() => { PlayNextSong(); CustomLogger.Log($"PlayerController: SkipButton clicked for {NickName}"); });
                CustomLogger.Log($"PlayerController: Reassigned SkipButton listener for {NickName}");
            }

            if (previousButton == null)
            {
                GameObject buttonObj = new GameObject("PreviousButton");
                buttonObj.transform.SetParent(musicPanel.transform, false);
                previousButton = buttonObj.AddComponent<Button>();
                previousButton.GetComponent<RectTransform>().sizeDelta = new Vector2(40, 40);
                previousButton.GetComponent<RectTransform>().anchoredPosition = new Vector2(-150, 0);
                Image img = buttonObj.AddComponent<Image>();
                img.color = Color.white;
                previousButton.interactable = true;
                previousButton.onClick.RemoveAllListeners();
                previousButton.onClick.AddListener(() => { PlayPreviousSong(); CustomLogger.Log($"PlayerController: PreviousButton clicked for {NickName}"); });
                CustomLogger.Log($"PlayerController: Created PreviousButton for {NickName} on MusicControlPanel");
            }
            else
            {
                previousButton.interactable = true;
                previousButton.onClick.RemoveAllListeners();
                previousButton.onClick.AddListener(() => { PlayPreviousSong(); CustomLogger.Log($"PlayerController: PreviousButton clicked for {NickName}"); });
                CustomLogger.Log($"PlayerController: Reassigned PreviousButton listener for {NickName}");
            }

            if (muteButton == null)
            {
                GameObject buttonObj = new GameObject("MuteButton");
                buttonObj.transform.SetParent(musicPanel.transform, false);
                muteButton = buttonObj.AddComponent<Button>();
                muteButton.GetComponent<RectTransform>().sizeDelta = new Vector2(40, 40);
                muteButton.GetComponent<RectTransform>().anchoredPosition = new Vector2(30, 0);
                Image img = buttonObj.AddComponent<Image>();
                img.color = Color.white;
                muteButton.interactable = true;
                muteButton.onClick.RemoveAllListeners();
                muteButton.onClick.AddListener(() => { ToggleMute(); CustomLogger.Log($"PlayerController: MuteButton clicked for {NickName}"); });
                CustomLogger.Log($"PlayerController: Created MuteButton for {NickName} on MusicControlPanel");
            }
            else
            {
                muteButton.interactable = true;
                muteButton.onClick.RemoveAllListeners();
                muteButton.onClick.AddListener(() => { ToggleMute(); CustomLogger.Log($"PlayerController: MuteButton clicked for {NickName}"); });
                CustomLogger.Log($"PlayerController: Reassigned MuteButton listener for {NickName}");
            }

            if (currentSongText == null)
            {
                GameObject textObj = new GameObject("CurrentSongText");
                textObj.transform.SetParent(musicPanel.transform, false);
                currentSongText = textObj.AddComponent<TextMeshProUGUI>();
                currentSongText.text = "No Song Playing";
                currentSongText.fontSize = 20;
                currentSongText.color = Color.white;
                currentSongText.alignment = TextAlignmentOptions.Center;
                currentSongText.rectTransform.anchorMin = new Vector2(0.5f, 1);
                currentSongText.rectTransform.anchorMax = new Vector2(0.5f, 1);
                currentSongText.rectTransform.anchoredPosition = new Vector2(0, -10);
                currentSongText.rectTransform.sizeDelta = new Vector2(260, 30);
                CustomLogger.Log($"PlayerController: Created CurrentSongText for {NickName} on MusicControlPanel");
            }
            if (musicPanelTimerText == null)
            {
                GameObject timerTextObj = musicPanel.transform.Find("MusicPanelTimerText")?.gameObject;
                if (timerTextObj != null)
                {
                    musicPanelTimerText = timerTextObj.GetComponent<TextMeshProUGUI>();
                    if (musicPanelTimerText != null)
                    {
                        musicPanelTimerText.text = "";
                        CustomLogger.Log($"PlayerController: Found existing MusicPanelTimerText for {NickName}");
                    }
                }
            }
            if (musicPanelTimerText == null)
            {
                GameObject timerTextObj = new GameObject("MusicPanelTimerText");
                timerTextObj.transform.SetParent(musicPanel.transform, false);
                musicPanelTimerText = timerTextObj.AddComponent<TextMeshProUGUI>();
                musicPanelTimerText.text = "";
                musicPanelTimerText.fontSize = 16;
                musicPanelTimerText.color = Color.white;
                musicPanelTimerText.alignment = TextAlignmentOptions.Center;
                musicPanelTimerText.rectTransform.anchorMin = new Vector2(0.5f, 0);
                musicPanelTimerText.rectTransform.anchorMax = new Vector2(0.5f, 0);
                musicPanelTimerText.rectTransform.anchoredPosition = new Vector2(0, 10);
                musicPanelTimerText.rectTransform.sizeDelta = new Vector2(200, 30);
                CustomLogger.Log($"PlayerController: Created MusicPanelTimerText for {NickName} on MusicControlPanel");
            }
            else
            {
                musicPanelTimerText.text = "";
                CustomLogger.Log($"PlayerController: Validated MusicPanelTimerText for {NickName}");
            }
            if (musicToggleButton != null)
            {
                musicToggleButton.interactable = true;
                musicToggleButton.onClick.RemoveAllListeners();
                musicToggleButton.onClick.AddListener(() =>
                {
                    ToggleMusicPanel();
                    CustomLogger.Log($"PlayerController: MusicToggleButton clicked for {NickName}");
                });
                CustomLogger.Log($"PlayerController: Configured MusicToggleButton for {NickName}");
            }
            else
            {
                CustomLogger.LogWarning($"PlayerController: MusicToggleButton not assigned in Inspector for {NickName}, music toggle button not initialized");
            }
            if (toggleText == null)
            {
                GameObject textObj = GameObject.FindGameObjectWithTag("HowToPlayText");
                if (textObj != null)
                {
                    toggleText = textObj.GetComponent<TextMeshProUGUI>();
                }
                else
                {
                    GameObject uiCanvas = GameObject.Find("PlayerUI");
                    if (uiCanvas != null)
                    {
                        textObj = new GameObject("HowToPlayText");
                        textObj.tag = "HowToPlayText";
                        textObj.transform.SetParent(uiCanvas.transform, false);
                        toggleText = textObj.AddComponent<TextMeshProUGUI>();
                        toggleText.text = "How to Play";
                        toggleText.fontSize = 20;
                        toggleText.color = Color.white;
                        toggleText.alignment = TextAlignmentOptions.Center;
                        toggleText.rectTransform.anchorMin = new Vector2(0.9f, 0.9f);
                        toggleText.rectTransform.anchorMax = new Vector2(0.9f, 0.9f);
                        toggleText.rectTransform.anchoredPosition = Vector2.zero;
                        toggleText.rectTransform.sizeDelta = new Vector2(150, 40);
                        CustomLogger.Log($"PlayerController: Created HowToPlayText on PlayerUI for {NickName}");
                    }
                }
            }
            if (toggleText != null)
            {
                toggleText.raycastTarget = true;
                TextClickHandler clickHandler = toggleText.GetComponent<TextClickHandler>();
                if (clickHandler == null)
                {
                    clickHandler = toggleText.gameObject.AddComponent<TextClickHandler>();
                }
                clickHandler.OnClick = null;
                clickHandler.OnClick += ToggleImage;
                CustomLogger.Log($"PlayerController: Initialized toggleText for {NickName}, added click handler");
            }
            else
            {
                CustomLogger.LogWarning($"PlayerController: Failed to initialize toggleText for {NickName}, PlayerUI canvas not found");
            }

            compass = GameObject.FindGameObjectWithTag("Compass")?.GetComponent<Compass>();
            if (compass == null)
            {
                CustomLogger.LogWarning($"PlayerController: Compass not found via tag, retry {retryCount + 1}/{maxRetries}.");
                retryCount++;
                yield return new WaitForSeconds(retryDelay);
                continue;
            }
            CustomLogger.Log($"PlayerController: Found Compass at {GetGameObjectPath(compass.gameObject)}.");

            Slider[] sliders = playerCanvas.GetComponentsInChildren<Slider>(true);
            foreach (var slider in sliders)
            {
                if (slider.name == "FuelBar")
                {
                    fuelBar = slider;
                    CustomLogger.Log("PlayerController: Found FuelBar Slider in Player Canvas.");
                    break;
                }
            }
            if (fuelBar == null)
            {
                CustomLogger.LogError("PlayerController: Could not find Slider named 'FuelBar' in Player Canvas.");
            }
            else
            {
                fuelBar.maxValue = 1f;
                CustomLogger.Log("PlayerController: Set fuelBar.maxValue to 1.");
            }

            if (movementJoystick == null || shootingJoystick == null)
            {
                Debug.LogWarning($"PlayerController: {(movementJoystick == null ? "MovementJoystick" : "")} {(shootingJoystick == null ? "ShootingJoystick" : "")} not assigned in Inspector. Retry {retryCount + 1}/{maxRetries}");
                retryCount++;
                yield return new WaitForSeconds(retryDelay);
                continue;
            }
            CustomLogger.Log($"PlayerController: Validated movementJoystick={movementJoystick.name}, shootingJoystick={shootingJoystick.name} from Inspector for {NickName}");
            DroidShooting droidShooting = GetComponentInChildren<DroidShooting>();
            if (droidShooting == null)
            {
                CustomLogger.LogError($"PlayerController: DroidShooting component not found for retry {retryCount + 1}/{maxRetries}");
                retryCount++;
                yield return new WaitForSeconds(retryDelay);
                continue;
            }
            CustomLogger.Log($"PlayerController: Found DroidShooting component on {droidShooting.gameObject.name}.");

            string currentScene = SceneManager.GetActiveScene().name;
            if (currentScene == "Moon Ran" || currentScene == "TeamMoonRan")
            {
                brightMatterCollected = 50;
                SavePlayerData();
                if (upgradeManager != null)
                    upgradeManager.SyncBrightMatter(brightMatterCollected);
                CustomLogger.Log($"PlayerController: Set brightMatterCollected=50 for fresh {currentScene} join.");
                LoadPlaylist();
                if (playlist.Count > 0)
                {
                    PlayNextSong();
                }
                else
                {
                    CustomLogger.LogWarning($"PlayerController: No songs loaded in playlist for {NickName}, music controls may not function");
                }
                // Show music panel at game start with timer
                if (musicControlPanel != null)
                {
                    StartCoroutine(FadePanel(true));
                    if (!isMusicPanelTimerRunning)
                    {
                        StartCoroutine(MusicPanelTimerCoroutine());
                        CustomLogger.Log($"PlayerController: Started MusicPanelTimerCoroutine for {NickName} at game start");
                    }
                }
            }
            else
            {
                LoadPlayerData();
            }

            if (bombPrefab == null)
                CustomLogger.LogError("PlayerController: bombPrefab is not assigned in Inspector.");

            StartCoroutine(NotifyRandomPlanetGenerator());
            StartCoroutine(EnsureSpaceshipAssignment());
            StartCoroutine(AssignCameraWithRetry());

            areActionsReady = true;
            CustomLogger.Log($"PlayerController: Actions ready for {NickName}, all components initialized.");
            yield break;
        }
    }

    private void ToggleMusicPanel()
    {
        if (musicControlPanel == null)
        {
            GameObject musicPanel = GameObject.Find("MusicControlPanel");
            if (musicPanel != null) musicControlPanel = musicPanel.GetComponent<CanvasGroup>();
            if (musicControlPanel == null)
            {
                CustomLogger.LogError($"PlayerController: MusicControlPanel not found for {NickName}, cannot toggle panel");
                return;
            }
        }
        bool isVisible = musicControlPanel.alpha > 0f;
        CustomLogger.Log($"PlayerController: Toggling MusicControlPanel for {NickName}, isVisible={isVisible}");

        if (isVisible && isMusicPanelTimerRunning)
        {
            StopCoroutine(MusicPanelTimerCoroutine());
            isMusicPanelTimerRunning = false;
            if (musicPanelTimerText != null)
            {
                musicPanelTimerText.text = "";
            }
            CustomLogger.Log($"PlayerController: Stopped MusicPanelTimerCoroutine for {NickName} on hide");
        }

        StartCoroutine(FadePanel(!isVisible));

        if (!isVisible && !isMusicPanelTimerRunning)
        {
            StartCoroutine(MusicPanelTimerCoroutine());
            CustomLogger.Log($"PlayerController: Started MusicPanelTimerCoroutine for {NickName} on show");
        }
    }

    void CheckCompassDistance(PlayerHealth playerHealth)
    {
        if (compass == null)
        {
            CustomLogger.LogWarning($"PlayerController: No Compass found for {NickName} to check distance");
            return;
        }

        // Check for nearby Bots or Players
        bool isNearEntity = false;
        Collider2D[] hits = Physics2D.OverlapCircleAll(transform.position, 100f, LayerMask.GetMask("Player", "Bot"));
        foreach (var hit in hits)
        {
            if (hit.gameObject != gameObject && (hit.CompareTag("Player") || hit.CompareTag("Bot")))
            {
                isNearEntity = true;
                break;
            }
        }

        // Update compass visibility based on proximity
        bool compassVisible = !isNearEntity;
        compass.SetVisibility(compassVisible);
        CustomLogger.Log($"PlayerController: Compass visibility set to {compassVisible} for {NickName}, isNearEntity={isNearEntity}");

        // Existing spaceship distance check
        Transform spaceshipTransform = null;
        if (CustomProperties.TryGetValue("SpaceshipViewID", out object viewID))
        {
            PhotonView spaceshipView = PhotonView.Find((int)viewID);
            if (spaceshipView != null && spaceshipView.gameObject != null && spaceshipView.gameObject.CompareTag("SpaceShip"))
            {
                SpaceshipMarker marker = spaceshipView.gameObject.GetComponent<SpaceshipMarker>();
                if (marker != null && marker.ownerId == ActorNumber)
                {
                    spaceshipTransform = spaceshipView.transform;
                }
            }
        }

        if (spaceshipTransform == null)
        {
            GameObject[] spaceships = GameObject.FindGameObjectsWithTag("SpaceShip");
            foreach (var ship in spaceships)
            {
                SpaceshipMarker marker = ship.GetComponent<SpaceshipMarker>();
                if (marker != null && marker.ownerId == ActorNumber)
                {
                    spaceshipTransform = ship.transform;
                    CustomProperties["SpaceshipViewID"] = ship.GetComponent<PhotonView>().ViewID;
                    PhotonNetwork.LocalPlayer.SetCustomProperties(new Hashtable { { "SpaceshipViewID", ship.GetComponent<PhotonView>().ViewID } });
                    CustomLogger.Log($"PlayerController: Found spaceship via fallback for {NickName}, ViewID={ship.GetComponent<PhotonView>().ViewID}");
                    break;
                }
            }
        }

        if (spaceshipTransform != null)
        {
            float distance = Vector3.Distance(transform.position, spaceshipTransform.position);
            if (distance > 5000f)
            {
                TeleportToBoundaryCenter();
                CustomLogger.Log($"PlayerController: {NickName} teleported to boundary center (0,0,0), compass distance was {distance:F2} units");
            }
            else if (distance > 12000f)
            {
                CustomLogger.Log($"PlayerController: {NickName} is {distance:F2} units from spaceship, triggering death due to out-of-range");
                playerHealth.TakeDamage(playerHealth.GetCurrentHealth(), true, -1, PlayerHealth.DeathCause.OutOfRange);
            }
        }
        else
        {
            CustomLogger.LogWarning($"PlayerController: No spaceship found for {NickName} to check compass distance, retrying assignment");
            StartCoroutine(EnsureSpaceshipAssignment());
        }
    }

    [PunRPC]
    private void RequestSpaceshipSpawn(int actorNumber)
    {
        if (!PhotonNetwork.IsMasterClient) return;
        MatchTimerManager timerManager = UnityEngine.Object.FindFirstObjectByType<MatchTimerManager>();
        if (timerManager != null)
        {
            timerManager.photonView.RPC("SpawnSpaceship", RpcTarget.All, actorNumber);
            CustomLogger.Log($"PlayerController: MasterClient triggered SpawnSpaceship for ActorNumber={actorNumber}");
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
    private void LoadPlaylist()
    {
        string uid = PlayerPrefs.GetString("PlayerUID", "");
        if (string.IsNullOrEmpty(uid))
        {
            CustomLogger.LogWarning($"PlayerController: No PlayerUID found for {NickName}, cannot load playlist.");
            currentSongText.text = "No Playlist";
            return;
        }

        if (songDatabase == null)
        {
            songDatabase = Resources.Load<SongDatabase>("Data/SongDatabase");
            if (songDatabase == null)
            {
                CustomLogger.LogError($"PlayerController: SongDatabase not found at Resources/Data/SongDatabase for {NickName}.");
                currentSongText.text = "No Songs Available";
                return;
            }
        }

        playlist.Clear();
        int count = PlayerPrefs.GetInt($"PlaylistCount_{uid}", 0);
        for (int i = 0; i < count; i++)
        {
            string songName = PlayerPrefs.GetString($"PlaylistSong_{uid}_{i}", "");
            if (!string.IsNullOrEmpty(songName))
            {
                AudioClip clip = songDatabase.GetSongByName(songName);
                if (clip != null)
                {
                    playlist.Add(clip);
                    CustomLogger.Log($"PlayerController: Loaded song '{songName}' for {NickName}");
                }
                else
                {
                    CustomLogger.LogWarning($"PlayerController: Song '{songName}' not found in SongDatabase for {NickName}.");
                }
            }
        }

        if (playlist.Count == 0)
        {
            currentSongText.text = "No Songs in Playlist";
            CustomLogger.Log($"PlayerController: Empty playlist for {NickName}");
        }
        else
        {
            currentSongIndex = -1;
            currentSongText.text = "Playlist Loaded";
            CustomLogger.Log($"PlayerController: Loaded {playlist.Count} songs for {NickName}: {string.Join(", ", playlist.Select(c => c.name))}");
        }
    }
    private void TogglePlayPause()
    {
        if (musicPlayer == null)
        {
            CustomLogger.LogError($"PlayerController: MusicPlayer is null for {NickName}, cannot toggle play/pause");
            currentSongText.text = "Music Player Error";
            return;
        }
        if (playlist.Count == 0)
        {
            CustomLogger.LogWarning($"PlayerController: No songs in playlist for {NickName}, cannot toggle play/pause");
            currentSongText.text = "No Songs Loaded";
            return;
        }

        isSongPaused = !isSongPaused; // Toggle the paused state

        if (isSongPaused)
        {
            musicPlayer.Pause();
            currentSongText.text = currentSongIndex >= 0 ? $"Paused: {playlist[currentSongIndex].name}" : "Paused";
            CustomLogger.Log($"PlayerController: Paused music for {NickName}, isSongPaused={isSongPaused}");
        }
        else
        {
            musicPlayer.Play();
            currentSongText.text = currentSongIndex >= 0 ? $"Playing: {playlist[currentSongIndex].name}" : "Playing";
            CustomLogger.Log($"PlayerController: Resumed music for {NickName}, isSongPaused={isSongPaused}");
        }
    }
    private void PlayNextSong()
    {
        if (musicPlayer == null)
        {
            CustomLogger.LogError($"PlayerController: MusicPlayer is null for {NickName}, cannot play next song");
            currentSongText.text = "Music Player Error";
            return;
        }
        ResetMusicPanelTimer();
        if (playlist.Count == 0)
        {
            CustomLogger.LogWarning($"PlayerController: No songs in playlist for {NickName}, cannot play next song");
            currentSongText.text = "No Songs Loaded";
            return;
        }

        currentSongIndex = (currentSongIndex + 1) % playlist.Count;
        musicPlayer.clip = playlist[currentSongIndex];
        if (!isMuted)
        {
            musicPlayer.Play();
            currentSongText.text = $"Playing: {playlist[currentSongIndex].name}";
            CustomLogger.Log($"PlayerController: Played next song '{playlist[currentSongIndex].name}' for {NickName}");
        }
        else
        {
            currentSongText.text = $"Muted: {playlist[currentSongIndex].name}";
            CustomLogger.Log($"PlayerController: Selected next song '{playlist[currentSongIndex].name}' for {NickName}, but muted");
        }
    }
    private void PlayPreviousSong()
    {
        if (musicPlayer == null)
        {
            CustomLogger.LogError($"PlayerController: MusicPlayer is null for {NickName}, cannot play previous song");
            currentSongText.text = "Music Player Error";
            return;
        }
        if (playlist.Count == 0)
        {
            CustomLogger.LogWarning($"PlayerController: No songs in playlist for {NickName}, cannot play previous song");
            currentSongText.text = "No Songs Loaded";
            return;
        }

        currentSongIndex = (currentSongIndex - 1 + playlist.Count) % playlist.Count;
        musicPlayer.clip = playlist[currentSongIndex];
        if (!isMuted)
        {
            musicPlayer.Play();
            currentSongText.text = $"Playing: {playlist[currentSongIndex].name}";
            CustomLogger.Log($"PlayerController: Played previous song '{playlist[currentSongIndex].name}' for {NickName}");
        }
        else
        {
            currentSongText.text = $"Muted: {playlist[currentSongIndex].name}";
            CustomLogger.Log($"PlayerController: Selected previous song '{playlist[currentSongIndex].name}' for {NickName}, but muted");
        }
    }
    private void ToggleMute()
    {
        if (musicPlayer == null)
        {
            CustomLogger.LogError($"PlayerController: MusicPlayer is null for {NickName}, cannot toggle mute");
            currentSongText.text = "Music Player Error";
            return;
        }

        isMuted = !isMuted;
        if (isMuted)
        {
            previousVolume = musicPlayer.volume;
            musicPlayer.volume = 0f;
            currentSongText.text = currentSongIndex >= 0 ? $"Muted: {playlist[currentSongIndex].name}" : "Muted";
            CustomLogger.Log($"PlayerController: Muted music for {NickName}, previousVolume={previousVolume}");
        }
        else
        {
            musicPlayer.volume = previousVolume;
            if (currentSongIndex >= 0 && musicPlayer.clip != null)
            {
                // Only play if not paused
                if (musicPlayer != null && !musicPlayer.isPlaying && playlist.Count > 0 && !isMuted && !isSongPaused)
                {
                    PlayNextSong();
                }
                if (!isSongPaused && !musicPlayer.isPlaying)
                {
                    musicPlayer.Play();
                }
                currentSongText.text = currentSongIndex >= 0 ? $"Playing: {playlist[currentSongIndex].name}" : "Playing";
                CustomLogger.Log($"PlayerController: Unmuted music for {NickName}, volume={previousVolume}, isSongPaused={isSongPaused}");
            }
            else
            {
                currentSongText.text = "No Song Loaded";
                CustomLogger.Log($"PlayerController: Unmuted music for {NickName}, but no song loaded");
            }
        }
    }
    public void SetJoysticks(Joystick movement, Joystick shooting)
    {
        movementJoystick = movement;
        shootingJoystick = shooting;
        CustomLogger.Log($"PlayerController: Assigned movementJoystick={(movement != null ? movement.name : "null")}, shootingJoystick={(shooting != null ? shooting.name : "null")} for {NickName}");
    }

    public void ResetJoysticks()
    {
        if (movementJoystick != null)
        {
            movementJoystick.ForceReset();
            CustomLogger.Log($"PlayerController: Reset movementJoystick for {NickName}");
        }
        if (shootingJoystick != null)
        {
            shootingJoystick.ForceReset();
            DroidShooting droidShooting = GetComponentInChildren<DroidShooting>();
            if (droidShooting != null)
                droidShooting.SetJoystickInput(Vector2.zero);
            CustomLogger.Log($"PlayerController: Reset shootingJoystick for {NickName}");
        }
    }

    IEnumerator AssignCameraWithRetry()
    {
        int maxRetries = 15;
        int retries = 0;
        float retryDelay = 0.5f;
        while (retries < maxRetries)
        {
            if (PhotonNetwork.IsConnectedAndReady && photonView != null && PhotonNetwork.LocalPlayer != null)
            {
                CameraFollow cameraFollow = Camera.main?.GetComponent<CameraFollow>();
                if (cameraFollow != null)
                {
                    cameraFollow.target = transform;
                    cameraFollow.ForceRetargetPlayer();
                    CustomLogger.Log($"PlayerController: Assigned CameraFollow.target to {gameObject.name} and called ForceRetargetPlayer for local player {NickName}, ViewID={photonView.ViewID}, Retry={retries + 1}/{maxRetries}");
                    yield break;
                }
            }
            retries++;
            CustomLogger.Log($"PlayerController: Retry {retries}/{maxRetries} to assign CameraFollow for {NickName}, IsConnected={PhotonNetwork.IsConnectedAndReady}, PhotonView={(photonView != null)}, LocalPlayer={(PhotonNetwork.LocalPlayer != null)}");
            yield return new WaitForSeconds(retryDelay);
        }
        CustomLogger.LogError($"PlayerController: Failed to assign CameraFollow after {maxRetries} retries for {NickName}");
    }

    IEnumerator NotifyCameraFollow()
    {
        int maxRetries = 5;
        int retries = 0;
        float retryDelay = 1f;
        while (retries < maxRetries)
        {
            if (!photonView.IsMine || !PhotonNetwork.IsConnectedAndReady)
            {
                CustomLogger.Log($"PlayerController: Skipped NotifyCameraFollow for {NickName}, IsMine={photonView.IsMine}, IsConnected={PhotonNetwork.IsConnectedAndReady}, Retry={retries + 1}/{maxRetries}");
                yield return new WaitForSeconds(retryDelay);
                retries++;
                continue;
            }

            CameraFollow cameraFollow = Camera.main?.GetComponent<CameraFollow>();
            if (cameraFollow != null)
            {
                cameraFollow.target = transform;
                cameraFollow.ForceRetargetPlayer();
                CustomLogger.Log($"PlayerController: Notified CameraFollow to set target to {gameObject.name} for {NickName}, ViewID={photonView.ViewID}, Retry={retries + 1}/{maxRetries}");
                yield break;
            }
            else
            {
                CustomLogger.LogWarning($"PlayerController: CameraFollow not found during NotifyCameraFollow for {NickName}, Retry={retries + 1}/{maxRetries}");
            }
            retries++;
            yield return new WaitForSeconds(retryDelay);
        }
        CustomLogger.LogError($"PlayerController: Failed to notify CameraFollow after {maxRetries} retries for {NickName}");
    }

    IEnumerator EnsureSpaceshipAssignment()
    {
        yield return new WaitForSeconds(5f);
        if (!photonView.IsMine || !PhotonNetwork.IsConnectedAndReady)
        {
            CustomLogger.Log($"PlayerController: Skipped EnsureSpaceshipAssignment for {NickName}, IsMine={photonView.IsMine}, IsConnected={PhotonNetwork.IsConnectedAndReady}");
            yield break;
        }

        bool hasValidSpaceship = false;
        if (CustomProperties.TryGetValue("SpaceshipViewID", out object viewID))
        {
            PhotonView spaceshipView = PhotonView.Find((int)viewID);
            if (spaceshipView != null && spaceshipView.gameObject != null && spaceshipView.gameObject.CompareTag("SpaceShip"))
            {
                SpaceshipMarker marker = spaceshipView.gameObject.GetComponent<SpaceshipMarker>();
                if (marker != null && marker.ownerId == ActorNumber)
                {
                    hasValidSpaceship = true;
                    CustomLogger.Log($"PlayerController: Valid spaceship found for {NickName}, SpaceshipViewID={viewID}, ownerId={marker.ownerId}");
                }
            }
        }

        if (!hasValidSpaceship)
        {
            GameObject[] spaceships = GameObject.FindGameObjectsWithTag("SpaceShip");
            foreach (var ship in spaceships)
            {
                SpaceshipMarker marker = ship.GetComponent<SpaceshipMarker>();
                if (marker != null && marker.ownerId == ActorNumber)
                {
                    hasValidSpaceship = true;
                    CustomProperties["SpaceshipViewID"] = ship.GetComponent<PhotonView>().ViewID;
                    PhotonNetwork.LocalPlayer.SetCustomProperties(new Hashtable { { "SpaceshipViewID", ship.GetComponent<PhotonView>().ViewID } });
                    CustomLogger.Log($"PlayerController: Assigned existing spaceship via fallback for {NickName}, SpaceshipViewID={ship.GetComponent<PhotonView>().ViewID}");
                    break;
                }
            }
        }

        if (!hasValidSpaceship)
        {
            MatchTimerManager timerManager = UnityEngine.Object.FindFirstObjectByType<MatchTimerManager>();
            if (timerManager != null && PhotonNetwork.IsMasterClient)
            {
                timerManager.photonView.RPC("SpawnSpaceship", RpcTarget.All, ActorNumber);
                CustomLogger.Log($"PlayerController: Triggered MatchTimerManager.SpawnSpaceship for {NickName}, ActorNumber={ActorNumber}");
            }
            else if (!PhotonNetwork.IsMasterClient)
            {
                CustomLogger.LogWarning($"PlayerController: Not MasterClient, cannot trigger SpawnSpaceship for {NickName}, requesting MasterClient");
                photonView.RPC("RequestSpaceshipSpawn", RpcTarget.MasterClient, ActorNumber);
            }
        }

        if (compass != null && CustomProperties.ContainsKey("SpaceshipViewID"))
        {
            compass.ForceUpdateCompass();
            CustomLogger.Log($"PlayerController: Forced compass update after spaceship assignment for {NickName}");
        }
    }

    IEnumerator NotifyRandomPlanetGenerator()
    {
        while (!CustomProperties.ContainsKey("PlayerViewID"))
        {
            CustomLogger.Log("PlayerController: Waiting for PlayerViewID to be set before notifying RandomPlanetGenerator.");
            yield return new WaitForSeconds(2f);
        }

        int maxRetries = 10;
        int retries = 0;
        while (retries < maxRetries)
        {
            GameObject generatorObj = GameObject.FindWithTag("PlanetGenerator");
            if (generatorObj != null)
            {
                RandomPlanetGenerator generator = generatorObj.GetComponent<RandomPlanetGenerator>();
                if (generator != null)
                {
                    generator.AddPlayer(ActorNumber, gameObject);
                    CustomLogger.Log($"PlayerController: Notified RandomPlanetGenerator for ActorNumber={ActorNumber}");
                    yield break;
                }
            }
            retries++;
            CustomLogger.Log($"PlayerController: Retry {retries}/{maxRetries} to find RandomPlanetGenerator.");
            yield return new WaitForSeconds(2f);
        }
        CustomLogger.LogError($"PlayerController: Failed to find RandomPlanetGenerator after {maxRetries} retries for ActorNumber={ActorNumber}");
    }

    public override void OnPlayerEnteredRoom(Player newPlayer)
    {
        if (photonView.IsMine && newPlayer.IsLocal)
        {
            BoundaryManager boundaryManager = UnityEngine.Object.FindFirstObjectByType<BoundaryManager>();
            if (boundaryManager != null)
            {
                CustomLogger.Log($"PlayerController: Local player {newPlayer.NickName} entered room, checking BoundaryManager for spawn.");
            }
        }
    }

    void OnDestroy()
    {
        if (photonView.IsMine)
        {
            GameObject generatorObj = GameObject.FindWithTag("PlanetGenerator");
            if (generatorObj != null)
            {
                RandomPlanetGenerator generator = generatorObj.GetComponent<RandomPlanetGenerator>();
                if (generator != null)
                {
                    generator.RemovePlayer(ActorNumber);
                }
            }
            if (toggleImage != null)
            {
                Destroy(toggleImage.gameObject);
                CustomLogger.Log($"PlayerController: Destroyed ToggleImage for {NickName}");
            }
            if (toggleText != null)
            {
                TextClickHandler clickHandler = toggleText.GetComponent<TextClickHandler>();
                if (clickHandler != null)
                {
                    clickHandler.OnClick = null;
                    CustomLogger.Log($"PlayerController: Removed click handler from toggleText for {NickName}");
                }
            }
        }
    }

    public void Die()
    {
        if (!photonView.IsMine) return;

        HasDied = true;

        CustomLogger.Log($"PlayerController: Die called for ViewID={photonView.ViewID}, ActorNumber={ActorNumber}, NickName={NickName}");

        DroidShooting droidShooting = GetComponentInChildren<DroidShooting>();
        if (droidShooting != null)
        {
            droidShooting.canShoot = false;
            CustomLogger.Log($"PlayerController: Disabled shooting for {NickName}, canShoot={droidShooting.canShoot}");
        }

        if (twinTurretManager != null && twinTurretManager.TwinTurretActive)
        {
            twinTurretManager.DeactivateTwinTurret();
            CustomLogger.Log($"PlayerController: Deactivated twin turret for {NickName} on death");
        }

        if (brightMatterCollected > 0)
        {
            GameObject orbPrefab = Resources.Load<GameObject>("BrightMatterOrb");
            if (orbPrefab != null)
            {
                try
                {
                    GameObject orb = PhotonNetwork.Instantiate("BrightMatterOrb", transform.position, Quaternion.identity);
                    BrightMatterOrb orbScript = orb.GetComponent<BrightMatterOrb>();
                    if (orbScript != null)
                    {
                        orbScript.SetBrightMatter(brightMatterCollected);
                        CustomLogger.Log($"PlayerController: Dropped BrightMatterOrb with {brightMatterCollected} BrightMatter at {transform.position}, Orb ViewID={orb.GetComponent<PhotonView>().ViewID}");
                    }
                    else
                    {
                        CustomLogger.LogError($"PlayerController: BrightMatterOrb component missing on instantiated orb at {transform.position}");
                        PhotonNetwork.Destroy(orb);
                    }
                }
                catch (System.Exception e)
                {
                    CustomLogger.LogError($"PlayerController: Failed to instantiate BrightMatterOrb: {e.Message}");
                }
            }
            else
            {
                CustomLogger.LogError("PlayerController: BrightMatterOrb prefab not found at Assets/Resources/BrightMatterOrb.prefab");
            }
            brightMatterCollected = 0;
            SavePlayerData();
        }

        SavePoints();

        SpriteRenderer[] renderers = GetComponentsInChildren<SpriteRenderer>(true);
        Collider2D[] colliders = GetComponentsInChildren<Collider2D>(true);
        foreach (var renderer in renderers)
        {
            renderer.enabled = false;
        }
        foreach (var collider in colliders)
        {
            collider.enabled = false;
        }
        CustomLogger.Log($"PlayerController: Player and children hidden (renderers={renderers.Length}, colliders={colliders.Length}) after death, ViewID={photonView.ViewID}");

        if (!gameObject.activeInHierarchy)
        {
            gameObject.SetActive(true);
            CustomLogger.LogWarning($"PlayerController: Reactivated GameObject for {NickName} to ensure findability after death");
        }
        if (!gameObject.CompareTag("Player"))
        {
            gameObject.tag = "Player";
            CustomLogger.LogWarning($"PlayerController: Restored 'Player' tag for {NickName} after death");
        }
        if (photonView == null)
        {
            CustomLogger.LogError($"PlayerController: PhotonView missing for {NickName} after death, RespawnUIManager may fail");
        }
        else
        {
            PhotonNetwork.LocalPlayer.SetCustomProperties(new Hashtable { { "PlayerViewID", photonView.ViewID } });
            CustomLogger.Log($"PlayerController: Ensured PlayerViewID={photonView.ViewID} in CustomProperties for {NickName}");
        }

        Canvas playerCanvas = GetComponentInChildren<Canvas>(true);
        if (playerCanvas != null)
        {
            if (!playerCanvas.gameObject.activeInHierarchy)
            {
                playerCanvas.gameObject.SetActive(true);
                CustomLogger.Log($"PlayerController: Reactivated Player Canvas for {NickName} at {GetGameObjectPath(playerCanvas.gameObject)}");
            }
            if (playerCanvas.name != "Player Canvas")
            {
                playerCanvas.name = "Player Canvas";
                CustomLogger.Log($"PlayerController: Renamed canvas to 'Player Canvas' for {NickName}");
            }
        }
        else
        {
            CustomLogger.LogWarning($"PlayerController: No Player Canvas found for {NickName}, creating one");
            GameObject canvasObj = new GameObject("Player Canvas");
            canvasObj.transform.SetParent(transform, false);
            playerCanvas = canvasObj.AddComponent<Canvas>();
            playerCanvas.renderMode = RenderMode.ScreenSpaceOverlay;
            CanvasScaler scaler = canvasObj.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);
            canvasObj.AddComponent<GraphicRaycaster>();
            CustomLogger.Log($"PlayerController: Created new Player Canvas for {NickName} at {GetGameObjectPath(canvasObj)}");
        }

        ResetJoysticks();
        if (canvasController != null)
        {
            canvasController.ResetIcons();
            CustomLogger.Log($"PlayerController: Reset canvas icons for {NickName} on death");
        }

        if (compass != null)
        {
            compass.SetVisibility(true);
            CustomLogger.Log($"PlayerController: Set compass visibility to true for {NickName} on death");
        }
        if (monitorDistanceCoroutine != null)
        {
            StopCoroutine(monitorDistanceCoroutine);
        }
        monitorDistanceCoroutine = StartCoroutine(MonitorCompassDistanceDuringDeath());

        StartCoroutine(TriggerRespawnWithRetry());
    }

    private IEnumerator TriggerRespawnWithRetry()
    {
        RespawnUIManager respawnUI = RespawnUIManager.Instance;
        int retries = 0;
        const int maxRetries = 10;
        const float retryInterval = 0.5f;

        while (retries < maxRetries && (respawnUI == null || !respawnUI.IsReady))
        {
            respawnUI = RespawnUIManager.Instance;
            if (respawnUI != null && !respawnUI.IsReady)
            {
                CustomLogger.LogWarning($"PlayerController: RespawnUIManager not ready, forcing reinitialization, retry {retries + 1}/{maxRetries}");
                respawnUI.StartCoroutine(respawnUI.InitializeUIWithRetry());
            }
            retries++;
            yield return new WaitForSeconds(retryInterval);
        }

        if (respawnUI != null && respawnUI.IsReady)
        {
            CustomLogger.Log($"PlayerController: Starting respawn countdown for {NickName}");
            yield return StartCoroutine(respawnUI.StartRespawnCountdown(this));
        }
        else
        {
            CustomLogger.LogError($"PlayerController: RespawnUIManager failed after {maxRetries} retries, forcing manual respawn for {NickName}");
            yield return new WaitForSeconds(5f);
            respawnUI?.HandleRespawn(this);
        }
    }

    private IEnumerator DeathTimer()
    {
        CustomLogger.Log($"PlayerController: DeathTimer started for {NickName}, ViewID={photonView.ViewID}");

        Canvas playerCanvas = GetComponentInChildren<Canvas>(true);
        if (playerCanvas != null && !playerCanvas.gameObject.activeInHierarchy)
        {
            playerCanvas.gameObject.SetActive(true);
            CustomLogger.Log($"PlayerController: Reactivated Player Canvas for {NickName}");
        }

        RespawnUIManager respawnUI = RespawnUIManager.Instance;
        int retries = 0;
        const int maxRetries = 5;
        const float retryInterval = 0.5f;

        while (retries < maxRetries && (respawnUI == null || !respawnUI.IsReady))
        {
            respawnUI = RespawnUIManager.Instance;
            if (respawnUI == null || !respawnUI.IsReady)
            {
                CustomLogger.LogWarning($"PlayerController: RespawnUIManager not ready, retry {retries + 1}/{maxRetries}");
                retries++;
                yield return new WaitForSeconds(retryInterval);
            }
        }

        if (respawnUI != null && respawnUI.IsReady)
        {
            CustomLogger.Log($"PlayerController: Starting countdown for {NickName}");
            yield return StartCoroutine(respawnUI.StartRespawnCountdown(this));
        }
        else
        {
            CustomLogger.LogError($"PlayerController: RespawnUIManager failed after {maxRetries} retries, using fallback wait.");
            yield return new WaitForSeconds(5f);
            respawnUI?.HandleRespawn(this);
        }
    }

    private bool IsRespawnUIReady(RespawnUIManager respawnUI)
    {
        return respawnUI != null && respawnUI.IsReady;
    }

    private void SavePoints()
    {
        CustomProperties["Points"] = points;
        PhotonNetwork.LocalPlayer.SetCustomProperties(new Hashtable { { "Points", points } });
        CustomLogger.Log($"PlayerController: Saved points for {NickName}, Points={points}, ViewID={photonView.ViewID}");
    }

    public void LoadPoints()
    {
        if (CustomProperties.ContainsKey("Points"))
        {
            points = (int)CustomProperties["Points"];
            CustomLogger.Log($"PlayerController: Loaded points for {NickName}, Points={points}, ViewID={photonView.ViewID}");
        }
        else
        {
            points = 0;
            CustomProperties["Points"] = 0;
            PhotonNetwork.LocalPlayer.SetCustomProperties(new Hashtable { { "Points", 0 } });
            CustomLogger.Log($"PlayerController: No points found in CustomProperties for {NickName}, reset to 0");
        }
    }

    public void OnPhotonSerializeView(PhotonStream stream, PhotonMessageInfo info)
    {
        if (stream.IsWriting)
        {
            stream.SendNext(HasDied);
        }
        else
        {
            this.HasDied = (bool)stream.ReceiveNext();
            CustomLogger.Log($"PlayerController: Received HasDied={HasDied} for {NickName}, ViewID={photonView.ViewID}");
        }
    }

    [PunRPC]
    private void ResetPlayerStateRPC()
    {
        points = 0;
        string username = NickName;
        CustomProperties = new Hashtable
        {
            { "Username", username },
            { "Points", 0 },
            { "Crowns", crowns }
        };
        if (photonView.IsMine)
        {
            PhotonNetwork.LocalPlayer.SetCustomProperties(CustomProperties);
        }
        SavePoints();
        ScoreboardManager scoreboard = UnityEngine.Object.FindFirstObjectByType<ScoreboardManager>();
        if (scoreboard != null)
        {
            scoreboard.UpdateScoreboard();
            CustomLogger.Log($"PlayerController: Reset points to 0 and triggered ScoreboardManager.UpdateScoreboard for {NickName}, ViewID={photonView.ViewID}");
        }
        else
        {
            CustomLogger.LogWarning($"PlayerController: ScoreboardManager not found for {NickName} during reset");
        }
    }

    public void AddPoints(int amount)
    {
        if (!photonView.IsMine) return;
        points = Mathf.Max(0, points + amount);
        SavePoints();
        CheckForCrownDisplay();
        ScoreboardManager scoreboard = UnityEngine.Object.FindFirstObjectByType<ScoreboardManager>();
        if (scoreboard != null)
        {
            scoreboard.UpdateScoreboard();
            CustomLogger.Log($"PlayerController: Triggered ScoreboardManager.UpdateScoreboard for {NickName} after adding {amount} points, total={points}, ViewID={photonView.ViewID}");
        }
        else
        {
            CustomLogger.LogWarning($"PlayerController: ScoreboardManager not found for {NickName} when adding points");
        }
    }

    private void CheckForCrownDisplay()
    {
        string currentScene = SceneManager.GetActiveScene().name;
        photonView.RPC("UpdateCrownDisplay", RpcTarget.All, points, currentScene);
    }

    [PunRPC]
    private void UpdateCrownDisplay(int playerPoints, string sceneName)
    {
        bool shouldShowCrown = false;
        int maxPoints = playerPoints;

        if (sceneName == "Moon Ran")
        {
            foreach (Player player in PhotonNetwork.PlayerList)
            {
                if (player.CustomProperties.TryGetValue("Points", out object otherPoints))
                {
                    int otherPlayerPoints = (int)otherPoints;
                    if (otherPlayerPoints > maxPoints)
                    {
                        maxPoints = otherPlayerPoints;
                    }
                }
            }
            shouldShowCrown = playerPoints == maxPoints && playerPoints > 0;
        }
        else if (sceneName == "TeamMoonRan")
        {
            PlayerHealth playerHealth = GetComponent<PlayerHealth>();
            if (playerHealth != null)
            {
                int teamPoints = 0;
                int opponentPoints = 0;
                PlayerHealth.Team myTeam = playerHealth.GetTeam();

                foreach (Player player in PhotonNetwork.PlayerList)
                {
                    if (player.CustomProperties.TryGetValue("Points", out object otherPoints))
                    {
                        GameObject playerObj = (GameObject)player.TagObject;
                        if (playerObj != null)
                        {
                            PlayerHealth otherHealth = playerObj.GetComponent<PlayerHealth>();
                            if (otherHealth != null)
                            {
                                if (otherHealth.GetTeam() == myTeam)
                                {
                                    teamPoints += (int)otherPoints;
                                }
                                else
                                {
                                    opponentPoints += (int)otherPoints;
                                }
                            }
                        }
                    }
                }
                shouldShowCrown = teamPoints > opponentPoints && playerPoints > 0;
            }
        }

        GameObject crownObject = transform.Find("Crown")?.gameObject;
        if (crownObject != null)
        {
            crownObject.SetActive(shouldShowCrown);
            CustomLogger.Log($"PlayerController: UpdateCrownDisplay for {NickName}, Points={playerPoints}, Scene={sceneName}, ShowCrown={shouldShowCrown}, ViewID={photonView.ViewID}");
        }
        else if (shouldShowCrown)
        {
            CustomLogger.LogWarning($"PlayerController: Crown GameObject not found for {NickName}, cannot display crown, ViewID={photonView.ViewID}");
        }
    }

    public void TriggerTeleport()
    {
        if (!photonView.IsMine) return;
        PhasingTeleportation teleport = GetComponent<PhasingTeleportation>();
        if (teleport != null)
        {
            teleport.Teleport();
            Debug.Log($"PlayerController: {NickName} triggered teleport via {(Input.GetKeyDown(KeyCode.Space) ? "key" : "button")}.");
        }
        else
        {
            Debug.LogError("PlayerController: PhasingTeleportation not found.");
        }
    }

    public void TriggerShield()
    {
        if (!photonView.IsMine) return;

        ShockShield shieldScript = GetComponent<ShockShield>();
        if (shieldScript == null)
        {
            CustomLogger.LogError($"PlayerController: ShockShield not found for {NickName}, attempting to add, ViewID={photonView.ViewID}.");
            shieldScript = gameObject.AddComponent<ShockShield>();
            if (shieldScript == null)
            {
                CustomLogger.LogError($"PlayerController: Failed to add ShockShield component for {NickName}, ViewID={photonView.ViewID}.");
                return;
            }
        }

        bool wasActive = shieldScript.isShieldActive;
        shieldScript.ToggleShield();
        isShieldActive = shieldScript.isShieldActive;

        CustomLogger.Log($"PlayerController: {NickName} triggered shield, wasActive={wasActive}, nowActive={isShieldActive}, energy={shieldScript.GetEnergy():F2}, ViewID={photonView.ViewID}, time={Time.time:F2}.");
        photonView.RPC("SyncShieldState", RpcTarget.All, isShieldActive);
    }

    public void TriggerLaser()
    {
        if (!photonView.IsMine) return;

        if (laserBeam == null)
        {
            laserBeam = GetComponentInChildren<LaserBeam>();
            if (laserBeam == null)
            {
                Debug.LogError($"PlayerController: LaserBeam not found on {gameObject.name} or its children (e.g., LaserBeamGun).");
                return;
            }
        }

        Vector2 aimInput = (shootingJoystick != null && shootingJoystick.IsActive && shootingJoystick.InputVector.magnitude > 0.1f)
            ? shootingJoystick.InputVector
            : Vector2.zero;
        laserBeam.SetJoystickInput(aimInput);
        Debug.Log($"PlayerController: {NickName} triggered laser with aimInput={aimInput}, via {(Input.GetKeyDown(KeyCode.C) ? "key" : "button")}.");
        laserBeam.TryFireLaser();
    }

    void Update()
    {
        if (!photonView.IsMine || !areActionsReady || HasDied) return;

        if (EventSystem.current.IsPointerOverGameObject())
        {
            Debug.Log($"PlayerController: Skipping keyboard action inputs, pointer over UI");
        }
        else
        {
            if (Input.GetKeyDown(KeyCode.Space))
            {
                TriggerTeleport();
            }
            if (Input.GetKeyDown(KeyCode.F))
            {
                TriggerShield();
            }
            if (Input.GetKeyDown(KeyCode.C))
            {
                TriggerLaser();
            }
            if (Input.GetKeyDown(KeyCode.O))
            {
                ToggleMusicPanel();
            }
            if (Input.GetKeyDown(KeyCode.N))
            {
                PlayNextSong();
                CustomLogger.Log($"PlayerController: Next song triggered via N key for {NickName}");
            }
            if (Input.GetKeyDown(KeyCode.B))
            {
                PlayPreviousSong();
                CustomLogger.Log($"PlayerController: Previous song triggered via B key for {NickName}");
            }
            if (Input.GetKeyDown(KeyCode.M))
            {
                ToggleMute();
                CustomLogger.Log($"PlayerController: Mute toggled via M key for {NickName}");
            }
            if (Input.GetKeyDown(KeyCode.G))
            {
                TogglePlayPause();
                CustomLogger.Log($"PlayerController: Play/Pause toggled via G key for {NickName}");
            }
        }
        if (Input.GetKeyDown(KeyCode.I))
        {
            if (toggleImage != null)
            {
                isImageVisible = !isImageVisible;
                toggleImage.gameObject.SetActive(isImageVisible);
                CustomLogger.Log($"PlayerController: Toggled image for {NickName}, isVisible={isImageVisible}");
            }
            else
            {
                CustomLogger.LogError($"PlayerController: ToggleImage is null for {NickName}, cannot toggle");
            }
        }
        if (Input.touchCount == 0 && !Input.GetMouseButton(0))
        {
            if (movementJoystick != null && movementJoystick.IsActive)
            {
                movementJoystick.ForceReset();
                Debug.Log($"PlayerController: No touches or mouse, reset movementJoystick for {gameObject.name}");
            }
            if (shootingJoystick != null && shootingJoystick.IsActive)
            {
                shootingJoystick.ForceReset();
                DroidShooting droidShooting = GetComponentInChildren<DroidShooting>();
                if (droidShooting != null)
                    droidShooting.SetJoystickInput(Vector2.zero);
                Debug.Log($"PlayerController: No touches or mouse, reset shootingJoystick for {gameObject.name}");
            }
        }

        HandleMovementInput();
        HandleShootingAndLaserInput();
        HandleBombInput();
        HandleOreInteraction();
        HandleShipInteraction();
        CheckCompassDistance(GetComponent<PlayerHealth>());

        // Update music player
        if (musicPlayer != null && !musicPlayer.isPlaying && playlist.Count > 0 && !isMuted)
        {
            PlayNextSong();
        }
    }


    void HandleOreInteraction()
    {
        if (!photonView.IsMine || HasDied) return;

        if (Input.GetKeyDown(KeyCode.Q))
        {
            float collectionRadius = 2f;
            Collider2D[] hits = Physics2D.OverlapCircleAll(transform.position, collectionRadius, LayerMask.GetMask("Ore"));
            foreach (var hit in hits)
            {
                BrightMatterOrb orb = hit.GetComponent<BrightMatterOrb>();
                if (orb != null)
                {
                    int amount = orb.GetAmount();
                    if (amount > 0)
                    {
                        AddBrightMatter(amount);
                        PhotonNetwork.Destroy(hit.gameObject);
                        CustomLogger.Log($"PlayerController: Collected {amount} BrightMatter from {hit.gameObject.name}, ViewID={photonView.ViewID}");
                    }
                }
            }
        }
    }
    
    private IEnumerator FadePanel(bool show)
    {
        // Set interactable immediately to ensure clicks register during fade
        musicControlPanel.interactable = show;
        musicControlPanel.blocksRaycasts = show;
        float targetAlpha = show ? 1f : 0f;
        float startAlpha = musicControlPanel.alpha;
        float duration = 0.3f;
        float time = 0;
        while (time < duration)
        {
            time += Time.deltaTime;
            musicControlPanel.alpha = Mathf.Lerp(startAlpha, targetAlpha, time / duration);
            yield return null;
        }
        musicControlPanel.alpha = targetAlpha;
        musicControlPanel.interactable = show;
        musicControlPanel.blocksRaycasts = show;
        CustomLogger.Log($"PlayerController: Faded MusicControlPanel for {NickName}, isVisible={show}, alpha={targetAlpha}, interactable={show}");
    }
    private IEnumerator MusicPanelTimerCoroutine()
    {
        isMusicPanelTimerRunning = true;
        musicPanelTimer = 10f;
        CustomLogger.Log($"PlayerController: Started MusicPanelTimerCoroutine for {NickName}, timer={musicPanelTimer}");

        while (musicPanelTimer > 0f && musicControlPanel != null && musicControlPanel.alpha > 0f)
        {
            if (musicPanelTimerText != null)
            {
                musicPanelTimerText.text = $"Closes in: {Mathf.CeilToInt(musicPanelTimer)}s";
            }
            musicPanelTimer -= Time.deltaTime;
            yield return null;
        }

        if (musicControlPanel != null && musicControlPanel.alpha > 0f)
        {
            yield return StartCoroutine(FadePanel(false));
            if (musicPanelTimerText != null)
            {
                musicPanelTimerText.text = "";
            }
            CustomLogger.Log($"PlayerController: MusicControlPanel auto-closed after 10s for {NickName}");
        }
        isMusicPanelTimerRunning = false;
    }

    private void ResetMusicPanelTimer()
    {
        if (isMusicPanelTimerRunning && musicControlPanel != null && musicControlPanel.alpha > 0f)
        {
            StopCoroutine(MusicPanelTimerCoroutine());
            StartCoroutine(MusicPanelTimerCoroutine());
            CustomLogger.Log($"PlayerController: Reset MusicPanelTimerCoroutine for {NickName}");
        }
    }

    void HandleMovementInput()
    {
        if (playerMovement == null)
        {
            CustomLogger.LogError("PlayerController: playerMovement is null.");
            return;
        }

        Vector2 input = Vector2.zero;
        bool hasKeyboardInput = Input.GetKey(KeyCode.W) || Input.GetKey(KeyCode.A) ||
                               Input.GetKey(KeyCode.S) || Input.GetKey(KeyCode.D);

        if (hasKeyboardInput)
        {
            if (Input.GetKey(KeyCode.W)) input += Vector2.up;
            if (Input.GetKey(KeyCode.S)) input += Vector2.down;
            if (Input.GetKey(KeyCode.A)) input += Vector2.left;
            if (Input.GetKey(KeyCode.D)) input += Vector2.right;

            input = input.normalized;

            if (movementJoystick != null)
            {
                movementJoystick.SetInput(input);
                Debug.Log($"PlayerController: Keyboard movement input for {gameObject.name}, set movementJoystick to input={input}");
            }
        }
        else if (movementJoystick != null)
        {
            if (movementJoystick.IsActive && movementJoystick.InputVector.magnitude > 0.1f)
            {
                input = movementJoystick.InputVector;
                Debug.Log($"PlayerController: Joystick movement input for {gameObject.name}, input={input}, IsActive={movementJoystick.IsActive}");
            }
            else
            {
                input = Vector2.zero;
                Debug.Log($"PlayerController: No movement joystick input for {gameObject.name}, IsActive={movementJoystick.IsActive}");
            }
        }

        playerMovement.Move(input.x, input.y);
        playerMovement.FlipSprite(input.x);
    }

    private void ToggleImage()
    {
        if (toggleImage != null)
        {
            isImageVisible = !isImageVisible;
            toggleImage.gameObject.SetActive(isImageVisible);
            CustomLogger.Log($"PlayerController: Toggled image for {NickName}, isVisible={isImageVisible} via {(toggleText != null ? "text tap" : "key")}");
        }
        else
        {
            CustomLogger.LogError($"PlayerController: ToggleImage is null for {NickName}, cannot toggle");
        }
    }

    void HandleShootingAndLaserInput()
    {
        DroidShooting droidShooting = GetComponentInChildren<DroidShooting>();
        if (droidShooting == null || !droidShooting.IsInitialized)
        {
            CustomLogger.LogWarning($"PlayerController: DroidShooting not found or not initialized for {gameObject.name}");
        }

        Vector2 aimInput = Vector2.zero;
        bool hasKeyboardInput = Input.GetKey(KeyCode.UpArrow) || Input.GetKey(KeyCode.DownArrow) ||
                               Input.GetKey(KeyCode.LeftArrow) || Input.GetKey(KeyCode.RightArrow);

        if (hasKeyboardInput)
        {
            if (Input.GetKey(KeyCode.UpArrow)) aimInput += Vector2.up;
            if (Input.GetKey(KeyCode.DownArrow)) aimInput += Vector2.down;
            if (Input.GetKey(KeyCode.LeftArrow)) aimInput += Vector2.left;
            if (Input.GetKey(KeyCode.RightArrow)) aimInput += Vector2.right;

            aimInput = aimInput.normalized;

            if (shootingJoystick != null)
            {
                shootingJoystick.SetInput(aimInput);
                Debug.Log($"PlayerController: Keyboard shooting joystick set to aimInput={aimInput}");
            }
            if (droidShooting != null)
            {
                droidShooting.SetJoystickInput(aimInput);
            }
            if (laserBeam != null)
            {
                laserBeam.SetJoystickInput(aimInput);
                Debug.Log($"PlayerController: Keyboard aim input set for LaserBeam, aimInput={aimInput}");
            }
        }
        else if (shootingJoystick != null)
        {
            if (shootingJoystick.IsActive && shootingJoystick.InputVector.magnitude > 0.1f)
            {
                aimInput = shootingJoystick.InputVector;
                if (droidShooting != null)
                {
                    droidShooting.SetJoystickInput(aimInput);
                }
                if (laserBeam != null)
                {
                    laserBeam.SetJoystickInput(aimInput);
                }
                Debug.Log($"PlayerController: Joystick input for {gameObject.name}, aimInput={aimInput}, IsActive={shootingJoystick.IsActive}");
            }
            else
            {
                aimInput = Vector2.zero;
                if (droidShooting != null)
                {
                    droidShooting.SetJoystickInput(aimInput);
                }
                if (laserBeam != null)
                {
                    laserBeam.SetJoystickInput(aimInput);
                }
                Debug.Log($"PlayerController: No shooting joystick input for {gameObject.name}, IsActive={shootingJoystick.IsActive}");
            }
        }

        if (Input.GetKeyDown(KeyCode.T))
        {
            if (twinTurretManager != null && !twinTurretManager.TwinTurretActive && !twinTurretManager.OnCooldown)
            {
                twinTurretManager.ActivateTwinTurret();
                if (canvasController != null)
                {
                    canvasController.StartDroidIconReveal(10f + 15f);
                    CustomLogger.Log($"PlayerController: Started gradient reveal for {NickName} on TwinTurret activation");
                }
                CustomLogger.Log($"PlayerController: {NickName} activated twin turret");
            }
            else
            {
                CustomLogger.Log($"PlayerController: Cannot activate twin turret for {NickName}, twinTurretManager={(twinTurretManager != null ? "present" : "null")}, active={twinTurretManager?.TwinTurretActive}, onCooldown={twinTurretManager?.OnCooldown}");
            }
        }
    }

    public void HandleBombInput()
    {
        if (Input.GetKeyDown(KeyCode.E))
        {
            BombManager bombManager = GetComponentInChildren<BombManager>();
            if (bombManager != null)
            {
                bombManager.TryDeployBomb();
                CustomLogger.Log($"PlayerController: {NickName}, requested bomb deployment via BombManager, ViewID={photonView.ViewID}");
            }
            else
            {
                CustomLogger.LogError($"PlayerController: BombManager not found, {NickName}.");
            }
        }
    }

    [PunRPC]
    private void SyncShieldState(bool active)
    {
        ShockShield shieldScript = GetComponent<ShockShield>();
        if (shieldScript != null)
        {
            isShieldActive = active;
            shieldScript.isShieldActive = active;
            CustomLogger.Log($"PlayerController: SyncShieldState, isShieldActive={isShieldActive}, ViewID={photonView.ViewID}");
        }
        else
        {
            CustomLogger.LogError($"PlayerController: ShockShield component not found during event for {NickName}, ViewID={photonView.ViewID}");
        }
    }

    public void PerformRespawn()
    {
        if (!photonView.IsMine) return;

        RespawnUIManager respawnManager = FindFirstObjectByType<RespawnUIManager>();
        if (respawnManager != null)
        {
            respawnManager.HandleRespawn(this);
            CustomLogger.Log($"PlayerController: Triggered respawn via RespawnUIManager for {NickName}, ViewID={photonView.ViewID}");
        }
        else
        {
            CustomLogger.LogError($"PlayerController: RespawnUIManager not found, cannot perform respawn for {NickName}");
        }
    }

    [PunRPC]
    private void SyncRespawnState(Vector3 position, int health)
    {
        HasDied = false;

        CustomLogger.Log($"PlayerController: SyncRespawnState called for {NickName}, ViewID={photonView.ViewID}, position={position}, health={health}");

        transform.position = position;
        Rigidbody2D rb = GetComponent<Rigidbody2D>();
        if (rb != null)
        {
            rb.linearVelocity = Vector2.zero;
            rb.angularVelocity = 0f;
            CustomLogger.Log($"PlayerController: Reset Rigidbody2D velocity for {NickName}");
        }
        else
        {
            CustomLogger.LogWarning($"PlayerController: Rigidbody2D missing for {NickName}, ViewID={photonView.ViewID}");
        }

        PlayerHealth playerHealth = GetComponent<PlayerHealth>();
        if (playerHealth != null)
        {
            playerHealth.ResetHealth();
            playerHealth.hasDied = false;
            CustomLogger.Log($"PlayerController: Reset health and hasDied for {NickName}");
        }
        else
        {
            CustomLogger.LogError($"PlayerController: PlayerHealth component missing for {NickName}, ViewID={photonView.ViewID}");
        }

        ShockShield shieldScript = GetComponent<ShockShield>();
        if (shieldScript != null)
        {
            shieldScript.ShowShieldTemporarily(5f);
            CustomLogger.Log($"PlayerController: Triggered ShowShieldTemporarily for 5 seconds after respawn for {NickName}, ViewID={photonView.ViewID}");
        }
        else
        {
            CustomLogger.LogError($"PlayerController: ShockShield component missing for {NickName}, cannot show shield temporarily, ViewID={photonView.ViewID}");
        }

        SpriteRenderer[] renderers = GetComponentsInChildren<SpriteRenderer>();
        Collider2D[] colliders = GetComponentsInChildren<Collider2D>();
        foreach (var renderer in renderers)
        {
            renderer.enabled = true;
        }
        foreach (var collider in colliders)
        {
            collider.enabled = true;
        }
        CustomLogger.Log($"PlayerController: Restored visibility and interactions (renderers={renderers.Length}, colliders={colliders.Length}) for {NickName}");

        DroidShooting droidShooting = GetComponentInChildren<DroidShooting>();
        if (droidShooting != null)
        {
            droidShooting.canShoot = true;
            CustomLogger.Log($"PlayerController: Enabled shooting for {NickName}");
        }
        else
        {
            CustomLogger.LogWarning($"PlayerController: DroidShooting component missing for {NickName}, ViewID={photonView.ViewID}");
        }

        if (playerMovement != null)
        {
            playerMovement.enabled = true;
            CustomLogger.Log($"PlayerController: Enabled PlayerMovement for {NickName}");
        }
        else
        {
            CustomLogger.LogError($"PlayerController: PlayerMovement component missing for {NickName}, ViewID={photonView.ViewID}");
        }

        if (twinTurretManager != null)
        {
            twinTurretManager.DeactivateTwinTurret();
            CustomLogger.Log($"PlayerController: Deactivated twin turret for {NickName} after respawn");
        }

        if (canvasController != null)
        {
            canvasController.ResetIcons();
            CustomLogger.Log($"PlayerController: Reset canvas icons for {NickName} after respawn");
        }

        ResetJoysticks();

        if (monitorDistanceCoroutine != null)
        {
            StopCoroutine(monitorDistanceCoroutine);
            monitorDistanceCoroutine = null;
            CustomLogger.Log($"PlayerController: Stopped MonitorCompassDistanceDuringDeath for {NickName} after respawn");
        }

        if (photonView.IsMine)
        {
            StartCoroutine(AssignCameraWithRetry());

            if (playerFuel != null)
            {
                playerFuel.SetFuel(100f);
                CustomLogger.Log($"PlayerController: Restored full fuel for {NickName}");
            }
            else
            {
                CustomLogger.LogWarning($"PlayerController: PlayerFuel component missing for {NickName}, ViewID={photonView.ViewID}");
            }

            brightMatterCollected = 50;
            SavePlayerData();
            if (upgradeManager != null)
                upgradeManager.SyncBrightMatter(brightMatterCollected);
            CustomLogger.Log($"PlayerController: Reset brightMatterCollected=50 for {NickName} after respawn");

            StartCoroutine(EnsureSpaceshipAssignment());

            if (compass != null)
            {
                compass.ForceUpdateCompass();
                CustomLogger.Log($"PlayerController: Forced compass update after respawn for {NickName}");
            }
        }
    }

    private void HandleShipInteraction()
    {
        if (!photonView.IsMine || HasDied) return;

        // Only process Return key if not over UI to avoid button click conflicts
        if (Input.GetKeyDown(KeyCode.Return) && !EventSystem.current.IsPointerOverGameObject())
        {
            if (shipInteraction != null)
            {
                shipInteraction.InteractWithShip();
                if (PlayerPrefs.GetInt("InsideSpaceShip", 0) == 1)
                {
                    if (compass != null)
                    {
                        compass.SetVisibility(false);
                        CustomLogger.Log($"PlayerController: Set compass visibility to false for {NickName} on entering spaceship");
                    }
                }
                else
                {
                    if (compass != null)
                    {
                        compass.SetVisibility(true);
                        CustomLogger.Log($"PlayerController: Set compass visibility to true for {NickName} on exiting spaceship");
                    }
                }
                CustomLogger.Log($"PlayerController: {NickName} interacted with ship via Return key");
            }
            else
            {
                CustomLogger.LogError("PlayerController: SpaceShipInteraction component not found.");
            }
        }
    }

   

    private IEnumerator MonitorCompassDistanceDuringDeath()
    {
        CustomLogger.Log($"PlayerController: Started MonitorCompassDistanceDuringDeath for {NickName}, ViewID={photonView.ViewID}");
        while (true)
        {
            if (!photonView.IsMine)
            {
                yield break;
            }

            if (compass == null)
            {
                CustomLogger.LogWarning($"PlayerController: No Compass found for {NickName} during death distance check");
                yield return new WaitForSeconds(0.5f);
                continue;
            }

            Transform spaceshipTransform = null;
            if (CustomProperties.TryGetValue("SpaceshipViewID", out object viewID))
            {
                PhotonView spaceshipView = PhotonView.Find((int)viewID);
                if (spaceshipView != null && spaceshipView.gameObject != null && spaceshipView.gameObject.CompareTag("SpaceShip"))
                {
                    SpaceshipMarker marker = spaceshipView.gameObject.GetComponent<SpaceshipMarker>();
                    if (marker != null && marker.ownerId == ActorNumber)
                    {
                        spaceshipTransform = spaceshipView.transform;
                    }
                }
            }

            if (spaceshipTransform != null)
            {
                float distance = Vector3.Distance(transform.position, spaceshipTransform.position);
                if (distance > 5000f)
                {
                    TeleportToBoundaryCenter();
                    CustomLogger.Log($"PlayerController: {NickName} teleported to boundary center (0,0,0) during death, compass distance was {distance:F2} units");
                }
                else if (distance > 12000f)
                {
                    CustomLogger.Log($"PlayerController: {NickName} is {distance:F2} units from spaceship during death, no death triggered (already dead)");
                }
            }
            else
            {
                CustomLogger.LogWarning($"PlayerController: No spaceship found for {NickName} during death distance check");
            }

            yield return new WaitForSeconds(0.5f);
        }
    }

    private void TeleportToBoundaryCenter()
    {
        if (!photonView.IsMine) return;
        Vector3 boundaryCenter = Vector3.zero;
        transform.position = boundaryCenter;
        photonView?.RPC("SyncTeleportPosition", RpcTarget.All, boundaryCenter);
        if (compass != null)
        {
            compass.ForceUpdateCompass();
            CustomLogger.Log($"PlayerController: Forced compass update after teleport for {NickName}");
        }
    }

    [PunRPC]
    private void SyncTeleportPosition(Vector3 newPosition)
    {
        transform.position = newPosition;
        CustomLogger.Log($"PlayerController: Synced teleport position to {newPosition} for {NickName}, ViewID={photonView.ViewID}");
    }

    void LoadPlayerData()
    {
        string uid = PlayerPrefs.GetString("PlayerUID", "");
        if (!string.IsNullOrEmpty(uid))
        {
#if UNITY
            FirebaseLoadBrightMatter(uid);
            FirebaseLoadFuel(uid);
#else
            brightMatterCollected = PlayerPrefs.GetInt("BrightMatter", 0);
            if (playerFuel != null)
            {
                float loadedFuel = PlayerPrefs.GetFloat("Fuel", 100f);
                playerFuel.SetFuel(loadedFuel);
            }
#endif
        }
        else
        {
            if (playerFuel != null)
            {
                playerFuel.SetFuel(100f);
            }
        }
    }

    void SavePlayerData()
    {
        string uid = PlayerPrefs.GetString("PlayerUID", "");
        if (!string.IsNullOrEmpty(uid))
        {
#if UNITY
            FirebaseSaveBrightMatter(uid, brightMatterCollected);
#else
            PlayerPrefs.SetInt("brightMatter", brightMatterCollected);
            PlayerPrefs.Save();
#endif
        }
    }

    void LoadCrowns()
    {
        string uid = PlayerPrefs.GetString("PlayerUID", "");
        string currentScene = SceneManager.GetActiveScene().name;
        string gameMode = currentScene == "Moon Ran" ? "MoonRan" : "TeamMoonRan";
        if (!string.IsNullOrEmpty(uid))
        {
#if UNITY_WEBGL && !UNITY_EDITOR
        FirebaseLoadCrowns(uid, gameMode);
#else
            crowns = PlayerPrefs.GetInt($"Crowns_{gameMode}", 0);
            CustomProperties["Crowns"] = crowns;
            PhotonNetwork.LocalPlayer.SetCustomProperties(new Hashtable { { "Crowns", crowns } });
            CustomLogger.Log($"PlayerController: Loaded crowns={crowns} from PlayerPrefs for {NickName}, gameMode={gameMode}, ViewID={photonView.ViewID}");
#endif
        }
    }

    void SaveCrowns()
    {
        string uid = PlayerPrefs.GetString("PlayerUID", "");
        string currentScene = SceneManager.GetActiveScene().name;
        string gameMode = currentScene == "Moon Ran" ? "MoonRan" : "TeamMoonRan";
        if (!string.IsNullOrEmpty(uid))
        {
#if UNITY_WEBGL && !UNITY_EDITOR
        FirebaseSaveCrowns(uid, crowns, gameMode);
#else
            PlayerPrefs.SetInt($"Crowns_{gameMode}", crowns);
            PlayerPrefs.Save();
#endif
            CustomProperties["Crowns"] = crowns;
            PhotonNetwork.LocalPlayer.SetCustomProperties(new Hashtable { { "Crowns", crowns } });
            CustomLogger.Log($"PlayerController: Saved crowns={crowns} for {NickName}, gameMode={gameMode}, ViewID={photonView.ViewID}");
        }
    }

    public void AddBrightMatter(int amount)
    {
        if (amount == 0 || isAddingBrightMatter)
        {
            CustomLogger.LogWarning($"PlayerController: AddBrightMatter skipped, amount={amount}, isAddingBrightMatter={isAddingBrightMatter}, ViewID={photonView.ViewID}");
            return;
        }
        isAddingBrightMatter = true;
        addBrightMatterCount++;
        if (amount <= 1)
        {
            CustomLogger.LogWarning($"PlayerController: Unexpected AddBrightMatter amount: {amount}, Previous={brightMatterCollected}, CallCount={addBrightMatterCount}, ViewID={photonView.ViewID}, Expected 50 from OrePrefab.");
        }
        int previousBrightMatter = brightMatterCollected;
        brightMatterCollected += amount;
        if (brightMatterCollected < 0) brightMatterCollected = 0;
        CustomLogger.Log($"PlayerController: AddBrightMatter, Previous={previousBrightMatter}, Adding={amount}, New={brightMatterCollected}, CallCount={addBrightMatterCount}, ViewID={photonView.ViewID}");
        SavePlayerData();
        BrightMatterDisplay display = UnityEngine.Object.FindFirstObjectByType<BrightMatterDisplay>();
        if (display != null)
        {
            display.UpdateBrightMatter(brightMatterCollected);
        }
        if (upgradeManager != null)
        {
            upgradeManager.SyncBrightMatter(brightMatterCollected);
        }
        isAddingBrightMatter = false;
    }

    public int GetBrightMatter()
    {
        return brightMatterCollected;
    }

    public void SyncBrightMatter(int amount)
    {
        brightMatterCollected = amount;
        if (amount < 0) brightMatterCollected = 0;
        SavePlayerData();
        BrightMatterDisplay display = UnityEngine.Object.FindFirstObjectByType<BrightMatterDisplay>();
        if (display != null)
        {
            display.UpdateBrightMatter(brightMatterCollected);
        }
        CustomLogger.Log($"PlayerController: SyncBrightMatter, Set brightMatterCollected={brightMatterCollected}, ViewID={photonView.ViewID}");
    }

    public void OnBrightMatterLoaded(string data)
    {
        if (hasLoadedBrightMatter || Time.time > 10f)
        {
            CustomLogger.LogWarning($"PlayerController: OnBrightMatterLoaded ignored, hasLoaded={hasLoadedBrightMatter}, Time={Time.time:F2}, Data={data}");
            return;
        }
        hasLoadedBrightMatter = true;
        if (int.TryParse(data, out int value))
        {
            brightMatterCollected = value;
            SavePlayerData();
            BrightMatterDisplay display = UnityEngine.Object.FindFirstObjectByType<BrightMatterDisplay>();
            if (display != null)
            {
                display.UpdateBrightMatter(brightMatterCollected);
            }
            if (upgradeManager != null)
            {
                upgradeManager.SyncBrightMatter(brightMatterCollected);
            }
            CustomLogger.Log($"PlayerController: OnBrightMatterLoaded, Set brightMatterCollected={brightMatterCollected}, ViewID={photonView.ViewID}");
        }
        else
        {
            CustomLogger.LogError($"PlayerController: Failed to parse BrightMatter data: {data}");
        }
        hasLoadedBrightMatter = false;
    }

    public void OnFuelLoaded(string data)
    {
        if (float.TryParse(data, out float value) && playerFuel != null)
        {
            playerFuel.SetFuel(value);
            CustomLogger.Log($"PlayerController: OnFuelLoaded for {NickName}, set fuel={value}, ViewID={photonView.ViewID}");
        }
        else
        {
            CustomLogger.LogError($"PlayerController: Failed to parse Fuel data: {data} or PlayerFuel missing");
            if (playerFuel != null)
            {
                playerFuel.SetFuel(100f);
            }
        }
    }

    public void OnCrownsLoaded(string data)
    {
        if (int.TryParse(data, out int value))
        {
            crowns = value;
            CustomProperties["Crowns"] = crowns;
            PhotonNetwork.LocalPlayer.SetCustomProperties(new Hashtable { { "Crowns", crowns } });
            CustomLogger.Log($"PlayerController: OnCrownsLoaded, Set crowns={crowns}, ViewID={photonView.ViewID}");
        }
        else
        {
            CustomLogger.LogError($"PlayerController: Failed to parse Crowns data: {data}");
        }
    }

    public void OnMatchEnd()
    {
        if (!photonView.IsMine) return;
        StartCoroutine(CheckForMatchWinner());
    }

    private IEnumerator CheckForMatchWinner()
    {
        yield return new WaitForSeconds(1f);

        bool isWinner = false;
        string currentScene = SceneManager.GetActiveScene().name;
        if (currentScene == "Moon Ran")
        {
            int maxPoints = points;
            foreach (Player player in PhotonNetwork.PlayerList)
            {
                if (player.CustomProperties.TryGetValue("Points", out object otherPoints))
                {
                    if ((int)otherPoints > maxPoints)
                    {
                        maxPoints = (int)otherPoints;
                    }
                }
            }
            isWinner = points == maxPoints && points > 0;
        }
        else if (currentScene == "TeamMoonRan")
        {
            PlayerHealth playerHealth = GetComponent<PlayerHealth>();
            if (playerHealth != null)
            {
                int teamPoints = 0;
                int opponentPoints = 0;
                PlayerHealth.Team myTeam = playerHealth.GetTeam();

                foreach (Player player in PhotonNetwork.PlayerList)
                {
                    if (player.CustomProperties.TryGetValue("Points", out object otherPoints))
                    {
                        GameObject playerObj = (GameObject)player.TagObject;
                        if (playerObj != null)
                        {
                            PlayerHealth otherHealth = playerObj.GetComponent<PlayerHealth>();
                            if (otherHealth != null)
                            {
                                if (otherHealth.GetTeam() == myTeam)
                                {
                                    teamPoints += (int)otherPoints;
                                }
                                else
                                {
                                    opponentPoints += (int)otherPoints;
                                }
                            }
                        }
                    }
                }
                isWinner = teamPoints > opponentPoints;
            }
        }

        if (isWinner)
        {
            crowns++;
            SaveCrowns();
            CustomLogger.Log($"PlayerController: {NickName} won the match in {currentScene}, incremented crowns to {crowns}");
        }
    }

    public void SetTeam(PlayerHealth.Team team)
    {
        PlayerHealth playerHealth = GetComponent<PlayerHealth>();
        if (playerHealth != null)
        {
            playerHealth.SetTeam(team);
            CustomLogger.Log($"PlayerController: Set team for {NickName} to {team}");
        }
        else
            CustomLogger.LogError($"PlayerController: PlayerHealth not found for {NickName} when setting team {team}");
    }

    public void OnPlayerKilled(string killedPlayerName)
    {
        if (!photonView.IsMine) return;
        if (killMessageText == null)
        {
            CustomLogger.LogError($"PlayerController: Cannot display kill message for {killedPlayerName}, killMessageText is null");
            return;
        }

        photonView.RPC("DisplayKillMessageRPC", RpcTarget.All, killedPlayerName);
    }

    [PunRPC]
    private void DisplayKillMessageRPC(string killedPlayerName)
    {
        if (!photonView.IsMine) return;
        if (killMessageText == null)
        {
            CustomLogger.LogError($"Cannot display kill message for {NickName}, killMessageText is null in RPC");
            return;
        }

        killMessageText.text = $"Killed: {killedPlayerName}!";
        killMessageText.gameObject.SetActive(true);
        if (flashMessageCoroutine != null)
        {
            StopCoroutine(flashMessageCoroutine);
        }
        flashMessageCoroutine = StartCoroutine(FlashKillMessage());
        CustomLogger.Log($"PlayerController: Displaying kill message 'Killed: {killedPlayerName}!' for {NickName}, ViewID={photonView.ViewID}");
    }

    private IEnumerator FlashKillMessage()
    {
        float duration = 3f;
        float flashInterval = 0.5f;
        float elapsed = 0f;

        while (elapsed < duration)
        {
            killMessageText.color = Color.red;
            yield return new WaitForSeconds(flashInterval);
            killMessageText.color = Color.white;
            yield return new WaitForSeconds(flashInterval);
            elapsed += 2 * flashInterval;
        }

        killMessageText.gameObject.SetActive(false);
        killMessageText.text = "";
        flashMessageCoroutine = null;
        CustomLogger.Log($"PlayerController: Kill message hidden after {duration} seconds for {NickName}");
    }
}