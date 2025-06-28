using UnityEngine;
using UnityEngine.UI;
using UnityEngine.SceneManagement;
using UnityEngine.Video;
using UnityEngine.EventSystems;
using Photon.Pun;
using Photon.Realtime;
using TMPro;
using System.Text.RegularExpressions;
using System.Runtime.InteropServices;
using System.Collections;

public class MenuManager : MonoBehaviourPunCallbacks, IPointerClickHandler
{
    public VideoPlayer videoPlayer;
    public RawImage videoDisplay;
    public Button[] menuButtons; // Includes P.O.D button at index 7
    public GameObject ButtonCanvas;
    [SerializeField] private TextMeshProUGUI adjectiveText; // TextMeshProUGUI for Adjective or Guest
    [SerializeField] private TextMeshProUGUI nounText;      // TextMeshProUGUI for Noun (empty for Guest)
    [SerializeField] private TextMeshProUGUI numberText;    // TextMeshProUGUI for Number
    [SerializeField] private TMP_Text tapText;              // Text for "(Or tap here...)"

    private int selectedIndex;
    private int lastLeftColumnIndex; // Track the last selected index in the left column (0-6)
    private bool menuActive, buttonsVisible;

    [DllImport("__Internal")]
    private static extern void FirebaseSignOut();

    void Start()
    {
        videoPlayer.prepareCompleted += OnVideoPrepared;
        videoPlayer.loopPointReached += OnVideoLoopPointReached;
        // Don't set isLooping here - let it use the Inspector setting
        ButtonCanvas.SetActive(false);
        videoPlayer.Play();
        Invoke(nameof(ShowButtonCanvas), 22f); // Adjusted to 22 seconds

        if (!PhotonNetwork.IsConnected)
        {
            PhotonNetwork.ConnectUsingSettings();
            Debug.Log("MenuManager: Connecting to Photon...");
        }

        // Validate TextMeshProUGUI components
        if (adjectiveText == null || nounText == null || numberText == null)
        {
            Debug.LogError("MenuManager: One or more TextMeshProUGUI components (adjectiveText, nounText, numberText) not assigned in Inspector.");
        }

        // Initialize Tap Text
        if (tapText != null)
        {
            tapText.text = "(Or tap here...)";
            tapText.gameObject.SetActive(false); // Initially hidden
            // Add EventTrigger for click detection
            EventTrigger trigger = tapText.gameObject.GetComponent<EventTrigger>() ?? tapText.gameObject.AddComponent<EventTrigger>();
            EventTrigger.Entry clickEntry = new EventTrigger.Entry { eventID = EventTriggerType.PointerClick };
            clickEntry.callback.AddListener((data) => OnPointerClick((PointerEventData)data));
            trigger.triggers.Clear();
            trigger.triggers.Add(clickEntry);
            StartCoroutine(ManageTapTextVisibility());
        }
        else
        {
            Debug.LogError("MenuManager: tapText is not assigned in Inspector.");
        }

        // Ensure all buttons have click listeners and are interactable
        if (menuButtons.Length != 8)
        {
            Debug.LogError($"MenuManager: Expected 8 buttons in menuButtons array, found {menuButtons.Length}.");
        }
        for (int i = 0; i < menuButtons.Length; i++)
        {
            if (menuButtons[i] == null)
            {
                Debug.LogError($"MenuManager: Button at index {i} is not assigned in Inspector.");
                continue;
            }
            menuButtons[i].interactable = true; // Ensure button is interactable
            int index = i; // Capture index for lambda
            menuButtons[i].onClick.RemoveAllListeners(); // Clear existing listeners
            menuButtons[i].onClick.AddListener(() => HandleButtonClick(index));
            Debug.Log($"MenuManager: Added click listener to button {i} ({menuButtons[i].gameObject.name})");
        }

        // Initialize navigation
        lastLeftColumnIndex = 0; // Default to first button in left column
    }

    void Update()
    {
        if (Input.GetKeyDown(KeyCode.S)) SkipToTime(19);
        if (menuActive) HandleNavigation();

        if (!buttonsVisible && videoPlayer.time >= 22) ShowButtonCanvas();

        if (!menuActive && videoPlayer.time >= 20f && videoPlayer.time < 32f) menuActive = true;

        if (menuActive && Input.GetKeyDown(KeyCode.Return)) HandleSelection();
    }

    private IEnumerator ManageTapTextVisibility()
    {
        // Wait until video reaches 3 seconds
        while (videoPlayer.time < 3f)
        {
            yield return null;
        }
        if (tapText != null)
        {
            tapText.gameObject.SetActive(true);
            Debug.Log("MenuManager: Showing tapText at 3 seconds.");
        }

        // Wait until video reaches 12 seconds
        while (videoPlayer.time < 12f)
        {
            yield return null;
        }
        if (tapText != null)
        {
            tapText.gameObject.SetActive(false);
            Debug.Log("MenuManager: Hiding tapText at 12 seconds.");
        }
    }

    public void OnPointerClick(PointerEventData eventData)
    {
        if (eventData.pointerPress == tapText.gameObject)
        {
            SkipToTime(19);
            Debug.Log("MenuManager: tapText clicked, skipping to 19 seconds.");
        }
    }

    void OnVideoLoopPointReached(VideoPlayer vp)
    {
        // If we want to loop back to 22s instead of the start
        if (videoPlayer.isLooping)
        {
            videoPlayer.time = 22f;
            videoPlayer.Play();
        }
    }
    void ShowButtonCanvas()
    {
        ButtonCanvas.SetActive(true);
        buttonsVisible = true;
        if (menuButtons.Length > 0)
        {
            selectedIndex = 0;
            lastLeftColumnIndex = 0;
            EventSystem.current.SetSelectedGameObject(menuButtons[0].gameObject);
        }

        // Display username in tower format
        if (adjectiveText != null && nounText != null && numberText != null)
        {
            string username = PlayerPrefs.GetString("PlayerUsername", "");
            var (adjective, noun, number) = ParseUsername(username);
            adjectiveText.text = adjective;
            nounText.text = noun;
            numberText.text = number;
            Debug.Log($"MenuManager: Displayed username: {username} as Adjective: {adjective}, Noun: {noun}, Number: {number}");
        }
        else
        {
            Debug.LogWarning("MenuManager: Cannot display username, one or more TextMeshProUGUI components are null.");
        }
    }

    (string, string, string) ParseUsername(string username)
    {
        // Handle empty or invalid username
        if (string.IsNullOrEmpty(username))
        {
            return ("Guest", "", "");
        }

        // Check for Guest_xxxx format
        Regex regexGuest = new Regex(@"^Guest_(\d+)$");
        Match guestMatch = regexGuest.Match(username);
        if (guestMatch.Success)
        {
            return ("Guest", "", guestMatch.Groups[1].Value);
        }

        // Check for AdjectiveNounNumber format
        Regex regexStandard = new Regex(@"^([A-Z][a-z]*)([A-Z][a-z]*)(\d+)$");
        Match standardMatch = regexStandard.Match(username);
        if (standardMatch.Success)
        {
            return (standardMatch.Groups[1].Value, standardMatch.Groups[2].Value, standardMatch.Groups[3].Value);
        }

        Debug.LogWarning($"MenuManager: Username '{username}' does not match AdjectiveNounNumber or Guest_ format, displaying as-is.");
        return (username, "", "");
    }

    private void HandleNavigation()
    {
        if (Input.GetKeyDown(KeyCode.DownArrow))
        {
            if (selectedIndex == 7) // P.O.D -> first match (index 0)
            {
                selectedIndex = 0;
            }
            else if (selectedIndex == 6) // Log Out -> P.O.D (index 7)
            {
                selectedIndex = 7;
            }
            else // Left column (0-6), move down, wrap to 0
            {
                lastLeftColumnIndex = selectedIndex;
                selectedIndex = (selectedIndex + 1) % 7;
            }
            HighlightButton();
        }
        else if (Input.GetKeyDown(KeyCode.UpArrow))
        {
            if (selectedIndex == 7) // P.O.D -> Log Out (index 6)
            {
                selectedIndex = 6;
            }
            else if (selectedIndex == 6) // Log Out -> Friends (index 4)
            {
                selectedIndex = 4;
            }
            else if (selectedIndex == 0) // First match -> P.O.D (index 7)
            {
                lastLeftColumnIndex = selectedIndex;
                selectedIndex = 7;
            }
            else // Left column (1-6), move up, wrap to 6
            {
                lastLeftColumnIndex = selectedIndex;
                selectedIndex = (selectedIndex - 1 + 7) % 7;
            }
            HighlightButton();
        }
    }

    void HandleButtonClick(int index)
    {
        selectedIndex = index;
        Debug.Log($"MenuManager: Button clicked, index {selectedIndex}");
        LoadSceneForIndex(selectedIndex);
    }

    void HandleSelection()
    {
        Debug.Log($"MenuManager: Selected index {selectedIndex}");
        LoadSceneForIndex(selectedIndex);
    }

    private void LoadSceneForIndex(int index)
    {
        string sceneName = index switch
        {
            0 => "LoadingMatch",
            1 => "TeamLoadingMatch",
            2 => "Friends",
            3 => "Leaderboard",
            4 => "FlagNickname",
            5 => "Settings",
            6 => "StartScreen",
            7 => "P.O.D",
            _ => null
        };

        if (sceneName == null || !IsSceneValid(sceneName))
        {
            Debug.LogError($"MenuManager: Invalid or missing scene for index {index}");
            return;
        }

        Debug.Log($"MenuManager: Loading scene {sceneName} for index {index}, PhotonNetwork.IsConnected={PhotonNetwork.IsConnected}, InRoom={PhotonNetwork.InRoom}");
        if (index >= 4 && index <= 7)
        {
            ButtonCanvas.SetActive(false);
        }

        if (index == 6)
        {
            if (Application.platform == RuntimePlatform.WebGLPlayer)
            {
                FirebaseSignOut();
            }
            PhotonNetwork.Disconnect();
            PlayerPrefs.DeleteKey("PlayerUID");
            PlayerPrefs.DeleteKey("PlayerUsername");
            PlayerPrefs.DeleteKey("PlayerPassword");
            PlayerPrefs.DeleteKey("IsGuest");
            PlayerPrefs.DeleteKey("PartyID");
            PlayerPrefs.DeleteKey("IsPartyLeader");
            PlayerPrefs.Save();
        }

        SceneManager.LoadScene(sceneName);
    }

    private bool IsSceneValid(string sceneName)
    {
        for (int i = 0; i < SceneManager.sceneCountInBuildSettings; i++)
        {
            string path = SceneUtility.GetScenePathByBuildIndex(i);
            string name = System.IO.Path.GetFileNameWithoutExtension(path);
            if (name == sceneName)
                return true;
        }
        Debug.LogError($"MenuManager: Scene '{sceneName}' not found in Build Settings.");
        return false;
    }

    void HighlightButton()
    {
        if (menuButtons.Length == 0) return;
        selectedIndex = Mathf.Clamp(selectedIndex, 0, menuButtons.Length - 1);
        EventSystem.current.SetSelectedGameObject(menuButtons[selectedIndex].gameObject);
    }

    void SkipToTime(float targetTime)
    {
        videoPlayer.time = targetTime;
        videoPlayer.Play();
        if (tapText != null)
        {
            tapText.gameObject.SetActive(false); // Hide tap text immediately on skip
            Debug.Log("MenuManager: Hid tapText on video skip.");
        }
    }

    void OnVideoPrepared(VideoPlayer vp)
    {
        float videoRatio = (float)vp.texture.width / vp.texture.height;
        float screenRatio = (float)Screen.width / Screen.height;
        RectTransform rt = videoDisplay.GetComponent<RectTransform>();
        rt.sizeDelta = videoRatio > screenRatio ?
            new Vector2(Screen.height * videoRatio, Screen.height) :
            new Vector2(Screen.width, Screen.width / videoRatio);
    }

    public override void OnJoinedRoom()
    {
        Debug.Log("MenuManager: Joined room successfully.");
    }

    public override void OnJoinRandomFailed(short returnCode, string message)
    {
        Debug.Log($"MenuManager: Join random failed ({returnCode}): {message}. Creating room...");
        PhotonNetwork.CreateRoom(null, new RoomOptions { MaxPlayers = 2 });
    }

    public override void OnLeftRoom()
    {
        Debug.Log("MenuManager: Left room. Loading InsideSpaceShip...");
        if (SceneManager.GetActiveScene().name != "InsideSpaceShip")
        {
            PhotonNetwork.LoadLevel("InsideSpaceShip");
        }
    }
}