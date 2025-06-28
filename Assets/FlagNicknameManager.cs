using UnityEngine;
using UnityEngine.UI;
using TMPro;
using Photon.Pun;
using Photon.Realtime;
using PhotonHashtable = ExitGames.Client.Photon.Hashtable;
using UnityEngine.SceneManagement;
using System.Text.RegularExpressions;
using System.Runtime.InteropServices;
using System.Collections.Generic;

public class FlagNicknameManager : MonoBehaviourPunCallbacks
{
    [Header("UI Elements")]
    [SerializeField] private Button confirmButton;
    [SerializeField] private Button backButton;
    [SerializeField] private Button nextButton;
    [SerializeField] private Button previousButton;
    [SerializeField] private TMP_InputField nicknameInput;
    [SerializeField] private TMP_InputField countrySearchInput;
    [SerializeField] private Image flagPreview;
    [SerializeField] private TextMeshProUGUI countryNameText;
    [SerializeField] private TextMeshProUGUI warningText; // New: Displays validation errors
    [SerializeField] private Sprite[] availableFlags;
    [SerializeField] private Sprite defaultFlagSprite;
    private bool isAutoCycling = true;
    private float flagCycleInterval = 2f; // Time between flag changes in seconds
    private float cycleTimer = 0f;
    private bool userHasInteracted = false;

    private int selectedFlagIndex = -1;
    private bool isNicknameValid = false;
    private bool isCheckingFirebase = false;
    private string pendingNickname = "";

    private readonly string[] countryNames = {
        "Albania", "Algeria", "Angola", "Argentina", "Australia", "Austria", "Azerbaijan",
        "Bahamas", "Bahrain", "Bangladesh", "Belgium", "Benin", "Bosnia", "Brazil",
        "Bulgaria", "Burkina Faso", "Burundi", "Cameroon", "Canada", "Central African Republic",
        "Chile", "China", "Colombia", "Costa Rica", "Croatia", "Cuba", "Czech-Republic",
        "Denmark", "Djibouti", "Dominican-Republic", "Egypt",
        "England", "Estonia", "Ethiopia", "European-Union", "Faroe-Islands", "Finland", "France",
        "French Guiana", "Gambia", "Georgia", "Germany", "Ghana", "Greece",
        "Greenland", "Guatemala", "Guinea", "Guyana", "Honduras", "Hong-Kong", "Hungary",
        "Iceland", "India", "Indonesia", "Iran", "Ireland", "Israel", "Italy",
        "Ivory-Coast", "Jamaica", "Japan", "Jordan", "Kazakhstan", "Kosovo", "Kuwait",
        "Kyrgyzstan", "Laos", "Latvia", "Libya", "Lithuania", "Luxembourg", "Macau",
        "Macedonia", "Madagascar", "Malaysia", "Malta", "Mauritania", "Mauritius", "Mexico",
        "Monaco", "Mongolia", "Montenegro", "Morocco", "Nepal", "Netherlands", "New-Syria",
        "New Zealand", "Niger", "Nigeria", "North Korea", "Norway", "Oman", "Pakistan",
        "Palestine", "Paraguay", "Peru", "Philippines", "Poland", "Portugal", "Puerto-Rico",
        "Qatar", "Republic-Of-Congo", "Romania", "Russia", "Saint-Vincent-and-the-Grenadines",
        "Saudi-Arabia", "Scotland", "Senegal", "Serbia", "Sierra Leone", "Singapore", "Slovakia",
        "Slovenia", "Somalia", "South-Africa", "South-Korea", "South-Sudan", "Spain", "Sudan",
        "Suriname", "Sweden", "Switzerland", "Taiwan", "Tajikistan", "Tanzania", "Thailand",
        "Togo", "Tonga", "Trinidad-and-Tobago", "Tunisia", "Turkey", "Turkmenistan", "Ukraine",
        "United-Arab-Emirates", "United-Kingdom", "United-States", "Uruguay", "Uzbekistan", "Venezuela",
        "Vietnam", "Yemen"
    };

    // Bad words list: 40 English, 20 each in 9 other languages (220 total)
    private readonly string[] badWords = {
        // English (40)
        "damn", "hell", "ass", "bitch", "fuck", "shit", "cunt", "dick", "bastard", "arse",
        "faggot", "nigger", "whore", "slut", "piss", "cock", "prick", "twat", "wanker", "crap",
        "bugger", "bollocks", "knob", "tosser", "shag", "fucker", "motherfucker", "asshole",
        "douche", "jerkoff", "cum", "blowjob", "fanny", "chink", "spic", "kike", "retard",
        "dyke", "queer", "tard",
        // Spanish (20)
        "mierda", "puta", "culo", "joder", "coño", "cabron", "pendejo", "maricon", "verga", "chingar",
        "pinche", "culero", "mamada", "cabrón", "puto", "zorra", "huevos", "chingada", "joto", "panocha",
        // French (20)
        "merde", "pute", "cul", "connard", "salope", "encule", "bordel", "con", "foutre", "chier",
        "batard", "petasse", "filsdepute", "couillon", "branleur", "tagueule", "nique", "putain",
        "enfoire", "chiant",
        // German (20)
        "scheisse", "arsch", "ficken", "hurensohn", "fotze", "schlampe", "wichser", "dreck", "kacke", "schwanz",
        "arschloch", "mistkerl", "dummkopf", "fick", "nutte", "trottel", "schmutz", "blödmann", "pisse", "kot",
        // Portuguese (20)
        "merda", "puta", "cu", "foder", "caralho", "bosta", "viado", "porra", "cacete", "vagabunda",
        "filhodaputa", "pau", "arrombado", "safado", "piranha", "corno", "bunda", "chupa", "putaria", "babaca",
        // Russian (transliterated, 20)
        "govno", "suka", "zhopa", "blyat", "mudak", "pizdets", "khuy", "debil", "shlyukha", "zassal",
        "pidor", "yob", "dermo", "gandon", "chmo", "srat", "ebat", "zvezdets", "prostitutka", "durak",
        // Chinese (Pinyin, 20)
        "cao", "nima", "biaozi", "hundan", "shabi", "jianhuo", "gouri", "tamade", "diao", "bendan",
        "erbi", "caonima", "jiba", "lanzi", "saozi", "bizi", "choubi", "xiongbing", "kao", "gunnima",
        // Arabic (transliterated, 20)
        "khara", "zift", "sharmuta", "qahba", "zub", "ayr", "harami", "jaban", "kuss", "tiz",
        "wahash", "magnoon", "najis", "kafir", "battikh", "himar", "kelb", "zamel", "fadiha", "ghabi",
        // Hindi (transliterated, 20)
        "gand", "chutiya", "madarchod", "bhosdi", "harami", "kutta", "sala", "randi", "chut", "loda",
        "bhenchod", "gandu", "lavda", "saala", "kamini", "kameena", "suwar", "bakchod", "jhaant", "bhadwa",
        // Japanese (Romaji, 20)
        "kuso", "baka", "manko", "chinpo", "yaro", "kusokurae", "shibaraku", "uzai", "hentai", "kusottare",
        "shine", "ahou", "kichigai", "zako", "onani", "gesu", "boke", "shikoru", "dame", "kuzukurae"
    };

    [DllImport("__Internal")]
    private static extern void FirebaseSignUp(string username, string password);

    void Start()
    {
        if (!PhotonNetwork.IsConnected)
        {
            Debug.LogError("FlagNicknameManager: Not connected to Photon. Connecting...");
            PhotonNetwork.ConnectUsingSettings();
        }

        // Validate UI references
        if (confirmButton == null || backButton == null || nextButton == null || previousButton == null ||
            nicknameInput == null || countrySearchInput == null || flagPreview == null ||
            countryNameText == null || warningText == null || availableFlags == null)
        {
            Debug.LogError("FlagNicknameManager: Missing UI references. Please assign in Inspector.");
            return;
        }

        if (availableFlags.Length != 141)
        {
            Debug.LogError($"FlagNicknameManager: availableFlags has {availableFlags.Length} flags, expected 141.");
        }

        // Initialize button listeners
        nextButton.onClick.AddListener(NextFlag);
        previousButton.onClick.AddListener(PreviousFlag);
        confirmButton.onClick.AddListener(OnConfirm);
        backButton.onClick.AddListener(OnBack);
        countrySearchInput.onValueChanged.AddListener(OnCountrySearchChanged);
        nicknameInput.onValueChanged.AddListener(OnNicknameChanged);

        // Ensure input fields are interactable
        EnsureInputFieldsInteractable();
        LoadPreviousSelections();

        // Initialize confirm button as disabled
        confirmButton.interactable = false;
        warningText.text = "Enter a nickname";
        ValidateNickname(nicknameInput.text);

        // Register scene unload handler
        SceneManager.sceneUnloaded += OnSceneUnloaded;
       
        StartAutoCycling();
    }
    private void StartAutoCycling()
    {
        isAutoCycling = true;
        userHasInteracted = false;
        selectedFlagIndex = -1; // Start with default flag
        cycleTimer = flagCycleInterval; // Start immediately
        Debug.Log("FlagNicknameManager: Started auto-cycling flags");
        UpdateFlagDisplay(); // Show the initial flag
    }

    private void StopAutoCycling()
    {
        isAutoCycling = false;
        Debug.Log("FlagNicknameManager: Stopped auto-cycling flags");
    }
    public override void OnDisable()
    {
        base.OnDisable();
        SceneManager.sceneUnloaded -= OnSceneUnloaded;
    }

    private void EnsureInputFieldsInteractable()
    {
        if (nicknameInput != null)
        {
            nicknameInput.interactable = true;
            nicknameInput.enabled = true;
            nicknameInput.Select();
            nicknameInput.ActivateInputField();
            Debug.Log($"FlagNicknameManager: nicknameInput interactable={nicknameInput.interactable}, enabled={nicknameInput.enabled}");
        }
        if (countrySearchInput != null)
        {
            countrySearchInput.interactable = true;
            countrySearchInput.enabled = true;
            Debug.Log($"FlagNicknameManager: countrySearchInput interactable={countrySearchInput.interactable}, enabled={countrySearchInput.enabled}");
        }
    }

    private void Update()
    {
        if (isAutoCycling && !userHasInteracted)
        {
            cycleTimer -= Time.deltaTime;
            if (cycleTimer <= 0f)
            {
                AutoCycleNextFlag();
                cycleTimer = flagCycleInterval;
            }
        }
    }
    private void OnNicknameChanged(string text)
    {
        ValidateNickname(text);
        Debug.Log($"FlagNicknameManager: Nickname input changed to '{text}'");
    }

    private void ValidateNickname(string nickname)
    {
        nickname = nickname.Trim();
        warningText.text = "";
        isNicknameValid = false;
        confirmButton.interactable = false;

        // Check length
        if (string.IsNullOrEmpty(nickname))
        {
            warningText.text = "Nickname cannot be empty";
            return;
        }
        if (nickname.Length > 12)
        {
            warningText.text = "Nickname must be 12 characters or less";
            return;
        }
        if (nickname.Length < 3)
        {
            warningText.text = "Nickname must be at least 3 characters";
            return;
        }

        // Check for bad words
        string normalizedNickname = NormalizeText(nickname);
        foreach (string badWord in badWords)
        {
            string normalizedBadWord = NormalizeText(badWord);
            if (normalizedNickname.Contains(normalizedBadWord))
            {
                warningText.text = "Nickname contains inappropriate content";
                Debug.Log($"FlagNicknameManager: Nickname '{nickname}' rejected due to bad word '{badWord}'");
                return;
            }
        }

        // Check for valid characters (alphanumeric, underscores, hyphens)
        if (!Regex.IsMatch(nickname, @"^[a-zA-Z0-9_-]+$"))
        {
            warningText.text = "Nickname can only contain letters, numbers, underscores, or hyphens";
            return;
        }

        // If in WebGL, check Firebase for username uniqueness
        if (Application.platform == RuntimePlatform.WebGLPlayer)
        {
            if (!isCheckingFirebase)
            {
                isCheckingFirebase = true;
                pendingNickname = nickname;
                FirebaseSignUp(nickname, "temp_password_123"); // Temp password for validation
                warningText.text = "Checking nickname availability...";
            }
        }
        else
        {
            // Editor simulation: Assume nickname is unique
            isNicknameValid = true;
            confirmButton.interactable = true;
            warningText.text = "Nickname is valid";
        }
    }

    private string NormalizeText(string text)
    {
        // Convert to lowercase and remove spaces/special characters
        text = text.ToLower();
        text = Regex.Replace(text, @"[\s\W]", "");

        // Handle common obfuscations
        Dictionary<char, char> substitutions = new Dictionary<char, char>
        {
            { '0', 'o' }, { '1', 'i' }, { '3', 'e' }, { '4', 'a' }, { '5', 's' },
            { '7', 't' }, { '@', 'a' }, { '$', 's' }, { '!', 'i' }
        };
        foreach (var sub in substitutions)
        {
            text = text.Replace(sub.Key, sub.Value);
        }

        return text;
    }

    public void OnSignUpSuccess(string data)
    {
        isCheckingFirebase = false;
        string[] parts = data.Split('|');
        if (parts.Length >= 2 && parts[1] == pendingNickname)
        {
            isNicknameValid = true;
            confirmButton.interactable = true;
            warningText.text = "Nickname is valid";
            Debug.Log($"FlagNicknameManager: Firebase validated nickname '{pendingNickname}'");
        }
        else
        {
            warningText.text = "Unexpected Firebase response";
            Debug.LogError($"FlagNicknameManager: Invalid Firebase success data: {data}");
        }
    }

    public void OnSignUpFailed(string error)
    {
        isCheckingFirebase = false;
        if (error.Contains("Username already taken"))
        {
            warningText.text = "Nickname is already taken";
        }
        else
        {
            warningText.text = "Error checking nickname: " + error;
        }
        Debug.Log($"FlagNicknameManager: Firebase rejected nickname '{pendingNickname}': {error}");
    }

    private void OnBack()
    {
        SceneManager.LoadScene("InsideSpaceShip");
        Debug.Log("FlagNicknameManager: Back button pressed, returning to InsideSpaceShip scene.");
    }
    private void AutoCycleNextFlag()
    {
        // Move to next flag (or wrap around)
        selectedFlagIndex = (selectedFlagIndex + 1) % availableFlags.Length;
        UpdateFlagDisplay();
        Debug.Log($"FlagNicknameManager: Auto-cycling to next flag, index={selectedFlagIndex}");
    }


    private void NextFlag()
    {
        if (!userHasInteracted)
        {
            userHasInteracted = true;
            StopAutoCycling();
        }

        selectedFlagIndex++;
        if (selectedFlagIndex >= availableFlags.Length) selectedFlagIndex = -1;
        UpdateFlagDisplay();
        countrySearchInput.text = selectedFlagIndex >= 0 ? GetCountryName(selectedFlagIndex) : "";
        Debug.Log($"FlagNicknameManager: Next flag, index={selectedFlagIndex}");
    }

    private void PreviousFlag()
    {
        if (!userHasInteracted)
        {
            userHasInteracted = true;
            StopAutoCycling();
        }

        selectedFlagIndex--;
        if (selectedFlagIndex < -1) selectedFlagIndex = availableFlags.Length - 1;
        UpdateFlagDisplay();
        countrySearchInput.text = selectedFlagIndex >= 0 ? GetCountryName(selectedFlagIndex) : "";
        Debug.Log($"FlagNicknameManager: Previous flag, index={selectedFlagIndex}");
    }
    private void OnCountrySearchChanged(string searchText)
    {
        if (!userHasInteracted && !string.IsNullOrEmpty(searchText))
        {
            userHasInteracted = true;
            StopAutoCycling();
        }

        Debug.Log($"FlagNicknameManager: Country search changed to '{searchText}'");
        if (string.IsNullOrEmpty(searchText))
        {
            selectedFlagIndex = -1;
            UpdateFlagDisplay();
            return;
        }

        string normalizedSearch = searchText.Replace(" ", "-").ToLower();
        for (int i = 0; i < countryNames.Length; i++)
        {
            if (countryNames[i].ToLower().StartsWith(normalizedSearch))
            {
                selectedFlagIndex = i;
                UpdateFlagDisplay();
                return;
            }
        }

        countryNameText.text = "No matching country";
        Debug.Log($"FlagNicknameManager: No flag found for search '{searchText}'");
    }

    private void UpdateFlagDisplay()
    {
        if (flagPreview == null)
        {
            Debug.LogError("FlagNicknameManager: flagPreview is null, cannot update display.");
            return;
        }
        flagPreview.sprite = selectedFlagIndex >= 0 && selectedFlagIndex < availableFlags.Length ? availableFlags[selectedFlagIndex] : defaultFlagSprite;
        string countryName = selectedFlagIndex >= 0 ? GetCountryName(selectedFlagIndex) : "Default";
        countryNameText.text = countryName;
        Debug.Log($"FlagNicknameManager: Updated display, flagIndex={selectedFlagIndex}, countryName={countryName}");
    }

    private string GetCountryName(int flagIndex)
    {
        if (flagIndex < 0 || flagIndex >= countryNames.Length) return "Unknown Country";
        return countryNames[flagIndex];
    }

    private void LoadPreviousSelections()
    {
        if (PhotonNetwork.LocalPlayer != null && PhotonNetwork.LocalPlayer.CustomProperties.ContainsKey("Nickname"))
        {
            nicknameInput.text = PhotonNetwork.LocalPlayer.CustomProperties["Nickname"]?.ToString();
            Debug.Log($"FlagNicknameManager: Loaded nickname '{nicknameInput.text}' from CustomProperties");
        }
        else
        {
            nicknameInput.text = PlayerPrefs.GetString("PlayerUsername", $"Player_{PhotonNetwork.LocalPlayer?.ActorNumber ?? Random.Range(1, 1000)}");
            Debug.Log($"FlagNicknameManager: Loaded default nickname '{nicknameInput.text}'");
        }

        if (PhotonNetwork.LocalPlayer != null && PhotonNetwork.LocalPlayer.CustomProperties.ContainsKey("FlagIndex"))
        {
            selectedFlagIndex = (int)PhotonNetwork.LocalPlayer.CustomProperties["FlagIndex"];
            Debug.Log($"FlagNicknameManager: Loaded flagIndex={selectedFlagIndex} from CustomProperties");
        }
        else
        {
            selectedFlagIndex = -1;
            Debug.Log("FlagNicknameManager: No flagIndex found, defaulting to -1");
        }
        UpdateFlagDisplay();
        countrySearchInput.text = selectedFlagIndex >= 0 ? GetCountryName(selectedFlagIndex) : "";
        ValidateNickname(nicknameInput.text);
    }

    private void OnConfirm()
    {
        if (!isNicknameValid)
        {
            warningText.text = "Please select a valid nickname";
            Debug.LogWarning("FlagNicknameManager: Confirm attempted with invalid nickname");
            return;
        }

        string nickname = nicknameInput.text.Trim();
        if (nickname.Length > 12)
        {
            nickname = nickname.Substring(0, 12);
            Debug.LogWarning($"FlagNicknameManager: Nickname truncated to '{nickname}' (max 12 chars)");
        }

        string defaultUsername = PlayerPrefs.GetString("PlayerUsername", $"Player_{PhotonNetwork.LocalPlayer?.ActorNumber ?? Random.Range(1, 1000)}");
        PhotonHashtable props = new PhotonHashtable
        {
            { "Nickname", nickname },
            { "Username", defaultUsername },
            { "FlagIndex", selectedFlagIndex }
        };

        PhotonNetwork.LocalPlayer?.SetCustomProperties(props);
        PhotonNetwork.NickName = nickname;
        PlayerPrefs.SetString("PlayerNickname", nickname);
        PlayerPrefs.Save();

        SceneManager.LoadScene("InsideSpaceShip");
        Debug.Log($"FlagNicknameManager: Confirmed nickname='{nickname}', username='{defaultUsername}', flagIndex={selectedFlagIndex}, returning to InsideSpaceShip scene.");
    }

    private void ClearNickname(string data = null)
    {
        string defaultUsername = PlayerPrefs.GetString("PlayerUsername", $"Player_{PhotonNetwork.LocalPlayer?.ActorNumber ?? Random.Range(1, 1000)}");
        PlayerPrefs.DeleteKey("PlayerNickname");
        PhotonHashtable props = new PhotonHashtable
        {
            { "Nickname", defaultUsername },
            { "Username", defaultUsername }
        };
        PhotonNetwork.LocalPlayer?.SetCustomProperties(props);
        PhotonNetwork.NickName = defaultUsername;
        PlayerPrefs.Save();
        Debug.Log($"FlagNicknameManager: Cleared nickname, restored to default username={defaultUsername}.");
    }
    private void OnSceneUnloaded(Scene scene)
    {
        // Clean up any resources if needed
        Debug.Log($"FlagNicknameManager: Scene {scene.name} was unloaded");
    }

    
    public override void OnPlayerPropertiesUpdate(Player targetPlayer, PhotonHashtable changedProps)
    {
        if (targetPlayer == PhotonNetwork.LocalPlayer && (changedProps.ContainsKey("Nickname") || changedProps.ContainsKey("FlagIndex")))
        {
            Debug.Log($"FlagNicknameManager: Player properties updated: Nickname={changedProps["Nickname"]}, FlagIndex={changedProps["FlagIndex"]}");
            LoadPreviousSelections();
        }
    }
}