using UnityEngine;
using UnityEngine.UI;
using UnityEngine.SceneManagement;
using UnityEngine.Video;
using TMPro;
using System.Runtime.InteropServices;
using Photon.Pun;
using Photon.Realtime;
using PhotonHashtable = ExitGames.Client.Photon.Hashtable;
using System.Collections;

public class StartScreenController : MonoBehaviourPunCallbacks
{
    [SerializeField] private TMP_InputField usernameInput;
    [SerializeField] private TMP_InputField passwordInput;
    [SerializeField] private Button newPlayerButton;
    [SerializeField] private Button registerButton;
    [SerializeField] private Button logInButton;
    [SerializeField] private Button guestButton;
    [SerializeField] private Button forgotPasswordButton;
    [SerializeField] private TMP_Text feedbackText;
    [SerializeField] private TMP_Text disappearingText;
    [SerializeField] private TMP_Text betaText;
    [SerializeField] private UsernameGenerator usernameGenerator;
    [SerializeField] private VideoPlayer videoPlayer;
    [SerializeField] private RawImage videoDisplay;
    [SerializeField] private Canvas canvas;
    [SerializeField] private TMP_InputField emailInput;
    [SerializeField] private Button sendRecoveryEmailButton;
    private const float PULSE_SPEED = 2f;

    private bool buttonClicked = false;
    private string username;
    private string password;
    private bool canClickSendRecovery = true;
    private bool isEmailPanelVisible = false;
    private string lastFeedbackMessage = "";
    private Coroutine pulseCoroutine;
    private Coroutine betaPulseCoroutine;

    [DllImport("__Internal")]
    private static extern void FirebaseSignUp(string username, string password);

    [DllImport("__Internal")]
    private static extern void FirebaseSignIn(string username, string password);

    [DllImport("__Internal")]
    private static extern void FirebaseSendPasswordReset(string email, string callbackObjectName);

    void Start()
    {
        if (Application.platform != RuntimePlatform.WebGLPlayer)
        {
            PlayerPrefs.DeleteKey("PlayerUID");
            PlayerPrefs.Save();
            Debug.Log("StartScreenController: Cleared PlayerUID in Editor mode.");
        }

        if (videoPlayer != null)
        {
            videoPlayer.prepareCompleted += OnVideoPrepared;
            videoPlayer.loopPointReached += OnVideoLoopPointReached;
            videoPlayer.isLooping = true;
            videoPlayer.Play();
        }
        else
        {
            Debug.LogError("StartScreenController: videoPlayer is not assigned in Inspector.");
        }

        if (videoDisplay == null)
            Debug.LogError("StartScreenController: videoDisplay is not assigned in Inspector.");

        if (canvas == null)
            Debug.LogError("StartScreenController: canvas is not assigned in Inspector.");

        if (newPlayerButton != null)
            newPlayerButton.onClick.AddListener(() => { buttonClicked = true; OnNewPlayerClicked(); HideDisappearingText(); HideBetaText(); });
        else
            Debug.LogError("StartScreenController: newPlayerButton is not assigned in Inspector.");

        if (registerButton != null)
            registerButton.onClick.AddListener(() => { buttonClicked = true; OnRegisterClicked(); HideDisappearingText(); HideBetaText(); });
        else
            Debug.LogError("StartScreenController: registerButton is not assigned in Inspector.");

        if (logInButton != null)
            logInButton.onClick.AddListener(() => { buttonClicked = true; OnLogInClicked(); HideDisappearingText(); HideBetaText(); });
        else
            Debug.LogError("StartScreenController: logInButton is not assigned in Inspector.");

        if (guestButton != null)
            guestButton.onClick.AddListener(() => { buttonClicked = true; OnGuestClicked(); HideDisappearingText(); HideBetaText(); });
        else
            Debug.LogError("StartScreenController: guestButton is not assigned in Inspector.");

        if (forgotPasswordButton != null)
            forgotPasswordButton.onClick.AddListener(() => { buttonClicked = true; ToggleEmailPanel(); HideDisappearingText(); HideBetaText(); });
        else
            Debug.LogError("StartScreenController: forgotPasswordButton is not assigned in Inspector.");

        if (sendRecoveryEmailButton != null)
            sendRecoveryEmailButton.onClick.AddListener(() => { buttonClicked = true; SendPasswordReset(); HideDisappearingText(); HideBetaText(); });
        else
            Debug.LogError("StartScreenController: sendRecoveryEmailButton is not assigned in Inspector.");

        if (usernameInput == null)
            Debug.LogError("StartScreenController: usernameInput is not assigned in Inspector.");
        if (passwordInput == null)
            Debug.LogError("StartScreenController: passwordInput is not assigned in Inspector.");
        else
        {
            passwordInput.contentType = TMP_InputField.ContentType.Password;
            Debug.Log("StartScreenController: Set passwordInput to Password content type.");
        }
        if (feedbackText == null)
            Debug.LogError("StartScreenController: feedbackText is not assigned in Inspector.");
        else
            Debug.Log($"StartScreenController: feedbackText parent is {feedbackText.transform.parent?.name ?? "none"}");
        if (disappearingText == null)
            Debug.LogError("StartScreenController: disappearingText is not assigned in Inspector.");
        else
            Debug.Log($"StartScreenController: disappearingText parent is {disappearingText.transform.parent?.name ?? "none"}");
        if (betaText == null)
            Debug.LogError("StartScreenController: betaText is not assigned in Inspector.");
        else
            Debug.Log($"StartScreenController: betaText parent is {betaText.transform.parent?.name ?? "none"}");
        if (usernameGenerator == null)
            Debug.LogError("StartScreenController: usernameGenerator is not assigned in Inspector.");
        if (emailInput == null)
            Debug.LogError("StartScreenController: emailInput is not assigned in Inspector.");

        if (usernameInput != null)
            usernameInput.gameObject.SetActive(false);
        if (passwordInput != null)
            passwordInput.gameObject.SetActive(false);
        if (feedbackText != null)
            feedbackText.gameObject.SetActive(false);
        if (disappearingText != null)
            disappearingText.gameObject.SetActive(false);
        if (betaText != null)
            betaText.gameObject.SetActive(false);
        if (emailInput != null)
            emailInput.gameObject.SetActive(false);
        if (sendRecoveryEmailButton != null)
            sendRecoveryEmailButton.gameObject.SetActive(false);

        Invoke("EnableInputFields", 4f);

        ClearFields();

        if (!PhotonNetwork.IsConnected)
        {
            Debug.Log("StartScreenController: Initiating Photon connection...");
            PhotonNetwork.ConnectUsingSettings();
        }
    }

    private void EnableInputFields()
    {
        if (usernameInput != null)
        {
            usernameInput.gameObject.SetActive(true);
            Debug.Log("StartScreenController: Enabled usernameInput after 4 seconds.");
        }
        if (passwordInput != null)
        {
            passwordInput.gameObject.SetActive(true);
            Debug.Log("StartScreenController: Enabled passwordInput after 4 seconds.");
        }
        if (feedbackText != null)
        {
            feedbackText.gameObject.SetActive(true);
            feedbackText.text = lastFeedbackMessage;
            Debug.Log($"StartScreenController: Enabled feedbackText after 4 seconds with text: '{feedbackText.text}', parent: {feedbackText.transform.parent?.name ?? "none"}");
        }
        else
        {
            Debug.LogError("StartScreenController: feedbackText is null in EnableInputFields.");
        }

        if (disappearingText != null && !buttonClicked)
        {
            disappearingText.gameObject.SetActive(true);
            pulseCoroutine = StartCoroutine(PulseText());
            Debug.Log($"StartScreenController: Enabled disappearingText after 4 seconds, started pulsation, parent: {disappearingText.transform.parent?.name ?? "none"}");
        }
        else if (disappearingText != null)
        {
            disappearingText.gameObject.SetActive(false);
            Debug.Log("StartScreenController: Skipped showing disappearing text because a button was already clicked.");
        }
        else
        {
            Debug.LogError("StartScreenController: disappearingText is null in EnableInputFields.");
        }

        if (betaText != null && !buttonClicked)
        {
            betaText.gameObject.SetActive(true);
            betaPulseCoroutine = StartCoroutine(PulseBetaText());
            Debug.Log($"StartScreenController: Enabled betaText after 4 seconds, started pulsation, parent: {betaText.transform.parent?.name ?? "none"}");
        }
        else if (betaText != null)
        {
            betaText.gameObject.SetActive(false);
            Debug.Log("StartScreenController: Skipped showing betaText because a button was already clicked.");
        }
        else
        {
            Debug.LogError("StartScreenController: betaText is null in EnableInputFields.");
        }

        if (isEmailPanelVisible)
        {
            if (emailInput != null)
            {
                emailInput.gameObject.SetActive(true);
                Debug.Log("StartScreenController: Enabled emailInput after 4 seconds.");
            }
            if (sendRecoveryEmailButton != null)
            {
                sendRecoveryEmailButton.gameObject.SetActive(true);
                Debug.Log("StartScreenController: Enabled sendRecoveryEmailButton after 4 seconds.");
            }
            if (feedbackText != null)
            {
                lastFeedbackMessage = "Enter your email and click Send Recovery Email to reset your password.";
                feedbackText.text = lastFeedbackMessage;
                Debug.Log("StartScreenController: Set feedbackText to email panel instructions.");
            }
        }
    }

    private IEnumerator PulseText()
    {
        while (true)
        {
            if (disappearingText != null)
            {
                float scale = 1f + Mathf.Sin(Time.time * PULSE_SPEED) * 0.05f;
                disappearingText.transform.localScale = new Vector3(scale, scale, 1f);
            }
            yield return null;
        }
    }

    private IEnumerator PulseBetaText()
    {
        while (true)
        {
            if (betaText != null)
            {
                float scale = 1f + Mathf.Sin(Time.time * PULSE_SPEED) * 0.05f;
                betaText.transform.localScale = new Vector3(scale, scale, 1f);
            }
            yield return null;
        }
    }

    private void HideDisappearingText()
    {
        if (disappearingText != null)
        {
            if (pulseCoroutine != null)
            {
                StopCoroutine(pulseCoroutine);
                pulseCoroutine = null;
                disappearingText.transform.localScale = Vector3.one;
                Debug.Log("StartScreenController: Stopped pulsation coroutine for disappearingText.");
            }
            disappearingText.gameObject.SetActive(false);
            Debug.Log("StartScreenController: Hid disappearingText due to button click.");
        }
    }

    private void HideBetaText()
    {
        if (betaText != null)
        {
            if (betaPulseCoroutine != null)
            {
                StopCoroutine(betaPulseCoroutine);
                betaPulseCoroutine = null;
                betaText.transform.localScale = Vector3.one;
                Debug.Log("StartScreenController: Stopped pulsation coroutine for betaText.");
            }
            betaText.gameObject.SetActive(false);
            Debug.Log("StartScreenController: Hid betaText due to button click.");
        }
    }

    private void ToggleEmailPanel()
    {
        isEmailPanelVisible = !isEmailPanelVisible;
        if (isEmailPanelVisible)
        {
            if (feedbackText != null && feedbackText.text != "Enter your email and click Send Recovery Email to reset your password.")
            {
                lastFeedbackMessage = feedbackText.text;
            }
            if (emailInput != null)
            {
                emailInput.gameObject.SetActive(true);
                emailInput.text = "";
            }
            if (sendRecoveryEmailButton != null)
                sendRecoveryEmailButton.gameObject.SetActive(true);
            if (feedbackText != null)
            {
                feedbackText.text = "Enter your email and click Send Recovery Email to reset your password.";
                Debug.Log($"StartScreenController: Email panel shown, set feedbackText to instructions, parent: {feedbackText.transform.parent?.name ?? "none"}");
            }
        }
        else
        {
            if (emailInput != null)
                emailInput.gameObject.SetActive(false);
            if (sendRecoveryEmailButton != null)
                sendRecoveryEmailButton.gameObject.SetActive(false);
            if (feedbackText != null)
            {
                feedbackText.text = lastFeedbackMessage;
                Debug.Log($"StartScreenController: Email panel hidden, restored feedback: '{lastFeedbackMessage}', parent: {feedbackText.transform.parent?.name ?? "none"}");
            }
            else
            {
                Debug.Log("StartScreenController: Email panel hidden, no feedback to restore.");
            }
        }
    }
    private System.Collections.IEnumerator checkUsernameAvailability(string usernameToCheck)
    {
        if (feedbackText != null)
        {
            lastFeedbackMessage = "Checking username availability...";
            feedbackText.text = lastFeedbackMessage;
        }
        Debug.Log($"StartScreenController: Checking availability for username={usernameToCheck}");

#if UNITY_WEBGL && !UNITY_EDITOR
    FirebaseSignUp(usernameToCheck, ""); // Pass empty password for availability check
    yield return new WaitUntil(() => usernameInput.text != "" || feedbackText.text.Contains("taken") || feedbackText.text.Contains("error") || feedbackText.text.Contains("invalid"));
#else
        OnUsernameCheckResult("available"); // Simulate available in Editor
#endif

        // Check the result after the callback
        if (feedbackText != null && feedbackText.text.Contains("taken"))
        {
            Debug.LogWarning($"StartScreenController: Username {usernameToCheck} is taken.");
            usernameInput.text = ""; // Clear if taken
        }
        else if (feedbackText != null && (feedbackText.text.Contains("error") || feedbackText.text.Contains("invalid")))
        {
            Debug.LogError($"StartScreenController: Username check failed: {feedbackText.text}");
            usernameInput.text = ""; // Clear on error
        }
        else if (usernameInput.text == "")
        {
            usernameInput.text = usernameToCheck; // Set if available
            Debug.Log($"StartScreenController: Username {usernameToCheck} is available and set.");
        }

        yield break; // Explicitly terminate the coroutine
    }

    public void OnUsernameCheckResult(string result)
    {
        if (result == "available" && usernameInput != null)
        {
            usernameInput.text = username;
        }
        else
        {
            usernameInput.text = ""; // Clear if taken or error
        }
    }
    private void SendPasswordReset()
    {
        if (!canClickSendRecovery) return;
        canClickSendRecovery = false;
        Invoke(nameof(ResetButtonCooldown), 2f);

        string email = emailInput != null ? emailInput.text.Trim() : "";
        if (!IsValidEmail(email))
        {
            if (feedbackText != null)
            {
                lastFeedbackMessage = "Enter a valid email.";
                feedbackText.text = lastFeedbackMessage;
            }
            canClickSendRecovery = true;
            Debug.LogWarning("StartScreenController: Invalid email format.");
            return;
        }

        if (Application.platform == RuntimePlatform.WebGLPlayer)
        {
            FirebaseSendPasswordReset(email, gameObject.name);
        }
        else
        {
            if (feedbackText != null)
            {
                lastFeedbackMessage = $"Password reset email sent to {email} (Editor simulation).";
                feedbackText.text = lastFeedbackMessage;
            }
            canClickSendRecovery = true;
            Debug.Log("StartScreenController: Simulated password reset email sent in Editor.");
        }
    }

    private void ResetButtonCooldown()
    {
        canClickSendRecovery = true;
        Debug.Log("StartScreenController: SendRecoveryEmailButton cooldown reset.");
    }

    private bool IsValidEmail(string email)
    {
        try
        {
            var addr = new System.Net.Mail.MailAddress(email);
            return addr.Address == email;
        }
        catch
        {
            return false;
        }
    }

    public void OnPasswordResetSent(string result)
    {
        if (feedbackText != null)
        {
            lastFeedbackMessage = result == "success" ? "Password reset email sent!" : $"Error: {result}";
            feedbackText.text = lastFeedbackMessage;
        }
        canClickSendRecovery = true;
        if (result == "success" && emailInput != null)
        {
            emailInput.text = "";
            isEmailPanelVisible = false;
            emailInput.gameObject.SetActive(false);
            sendRecoveryEmailButton.gameObject.SetActive(false);
            Debug.Log("StartScreenController: Email panel hidden on successful password reset.");
        }
        Debug.Log($"StartScreenController: Password reset result: {result}");
    }

    void OnVideoLoopPointReached(VideoPlayer vp)
    {
        Debug.Log("StartScreenController: Video reached end, looping to 11 seconds.");
        videoPlayer.time = 11f;
        videoPlayer.Play();
    }

    void OnVideoPrepared(VideoPlayer vp)
    {
        Debug.Log("StartScreenController: Video prepared, adjusting aspect ratio.");
        if (canvas == null || videoDisplay == null) return;

        Rect canvasRect = canvas.GetComponent<RectTransform>().rect;
        float canvasWidth = canvasRect.width;
        float canvasHeight = canvasRect.height;
        float canvasRatio = canvasWidth / canvasHeight;

        float videoRatio = (float)vp.texture.width / vp.texture.height;

        RectTransform rt = videoDisplay.GetComponent<RectTransform>();
        rt.sizeDelta = videoRatio > canvasRatio ?
            new Vector2(canvasHeight * videoRatio, canvasHeight) :
            new Vector2(canvasWidth, canvasWidth / videoRatio);

        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;
    }
    private void OnNewPlayerClicked()
    {
        if (usernameGenerator == null)
        {
            Debug.LogError("StartScreenController: Cannot generate username, usernameGenerator is null.");
            if (feedbackText != null)
            {
                lastFeedbackMessage = "Error: Username generator not set up.";
                feedbackText.text = lastFeedbackMessage;
            }
            return;
        }

        StartCoroutine(GenerateAndCheckUsername());
    }

    private System.Collections.IEnumerator GenerateAndCheckUsername()
    {
        if (feedbackText != null)
        {
            lastFeedbackMessage = "Checking username availability...";
            feedbackText.text = lastFeedbackMessage;
        }

        while (true)
        {
            username = usernameGenerator.GenerateUsername();
            if (string.IsNullOrEmpty(username))
            {
                Debug.LogError("StartScreenController: GenerateUsername returned empty or null.");
                if (feedbackText != null)
                {
                    lastFeedbackMessage = "Error: Failed to generate username.";
                    feedbackText.text = lastFeedbackMessage;
                }
                yield break;
            }

            Debug.Log($"StartScreenController: Checking availability for username={username}");

#if UNITY_WEBGL && !UNITY_EDITOR
        FirebaseSignUp(username, ""); // Pass empty password for availability check
#else
            OnUsernameCheckResult("available"); // Simulate available in Editor
#endif

            // Wait for the result (handled by OnUsernameCheckResult callback)
            yield return new WaitUntil(() => usernameInput.text != "" || feedbackText.text.Contains("taken"));

            if (usernameInput.text != "") // Available username found
            {
                passwordInput.text = "";
                if (feedbackText != null)
                {
                    lastFeedbackMessage = "Enter a password and register! (You can set a nickname in next screen)";
                    feedbackText.text = lastFeedbackMessage;
                }
                Debug.Log($"StartScreenController: Username {username} is available and set.");
                yield break;
            }
            // If taken, loop to generate a new one
            if (feedbackText != null)
            {
                lastFeedbackMessage = "Username taken, generating new one...";
                feedbackText.text = lastFeedbackMessage;
            }
            Debug.LogWarning($"StartScreenController: Username {username} is taken, regenerating.");
        }
    }

    private void OnRegisterClicked()
    {
        username = usernameInput != null ? usernameInput.text.Trim() : "";
        password = passwordInput != null ? passwordInput.text : "";

        Debug.Log($"StartScreenController: OnRegisterClicked - Raw input: username='{username}', password length={password?.Length ?? 0}");
        if (string.IsNullOrEmpty(username) || password.Length < 6)
        {
            if (feedbackText != null)
            {
                lastFeedbackMessage = "Username must not be empty and password must be 6+ characters.";
                feedbackText.text = lastFeedbackMessage;
            }
            Debug.LogWarning("StartScreenController: Registration failed - invalid input.");
            return;
        }

        if (username.Length < 3 || username.Length > 20 || !System.Text.RegularExpressions.Regex.IsMatch(username, @"^[a-zA-Z0-9]+$"))
        {
            if (feedbackText != null)
            {
                lastFeedbackMessage = "Username must be 3–20 alphanumeric characters.";
                feedbackText.text = lastFeedbackMessage;
            }
            Debug.LogWarning("StartScreenController: Registration failed - invalid username format.");
            return;
        }

        if (feedbackText != null)
        {
            lastFeedbackMessage = "Registering...";
            feedbackText.text = lastFeedbackMessage;
        }
        Debug.Log($"StartScreenController: Attempting registration for username='{username}', password length={password.Length}");

#if UNITY_WEBGL && !UNITY_EDITOR
    FirebaseSignUp(username, password);
#else
        string testUid = "test-" + username;
        PlayerPrefs.SetString("PlayerUID", testUid);
        PlayerPrefs.SetString("PlayerUsername", username);
        PlayerPrefs.SetString("PlayerPassword", password);
        PlayerPrefs.SetInt("IsGuest", 0);
        PlayerPrefs.DeleteKey("PlayerNickname");
        if (PhotonNetwork.IsConnectedAndReady)
        {
            PhotonNetwork.NickName = username;
            PhotonHashtable props = new PhotonHashtable { { "Username", username }, { "Nickname", username } };
            PhotonNetwork.LocalPlayer.SetCustomProperties(props);
            Debug.Log($"StartScreenController: Set Photon Nickname and properties to username={username} for local player");
        }
        if (feedbackText != null)
        {
            lastFeedbackMessage = "Registration successful! (Editor)";
            feedbackText.text = lastFeedbackMessage;
        }
        Debug.Log("StartScreenController: Registration successful, loading InsideSpaceShip.");
        SceneManager.LoadScene("InsideSpaceShip");
#endif
    }

    private void OnLogInClicked()
    {
        username = usernameInput != null ? usernameInput.text.Trim() : "";
        password = passwordInput != null ? passwordInput.text : "";

        if (string.IsNullOrEmpty(username) || string.IsNullOrEmpty(password))
        {
            if (feedbackText != null)
            {
                lastFeedbackMessage = "Please enter both username and password.";
                feedbackText.text = lastFeedbackMessage;
            }
            Debug.LogWarning("StartScreenController: Login failed - empty fields.");
            return;
        }

        if (username.Length < 3 || username.Length > 20 || !System.Text.RegularExpressions.Regex.IsMatch(username, @"^[a-zA-Z0-9]+$"))
        {
            if (feedbackText != null)
            {
                lastFeedbackMessage = "Username must be 3–20 alphanumeric characters.";
                feedbackText.text = lastFeedbackMessage;
            }
            Debug.LogWarning("StartScreenController: Login failed - invalid username format.");
            return;
        }

        if (feedbackText != null)
        {
            lastFeedbackMessage = "Logging in...";
            feedbackText.text = lastFeedbackMessage;
        }
        Debug.Log($"StartScreenController: Attempting login for {username}");

#if UNITY_WEBGL && !UNITY_EDITOR
        FirebaseSignIn(username, password);
#else
        string storedUid = PlayerPrefs.GetString("PlayerUID", "");
        string storedUsername = PlayerPrefs.GetString("PlayerUsername", "");
        string storedPassword = PlayerPrefs.GetString("PlayerPassword", "");
        if (storedUid.StartsWith("test-") && storedUsername == username && storedPassword == password)
        {
            PlayerPrefs.SetInt("IsGuest", 0);
            PlayerPrefs.DeleteKey("PlayerNickname");
            if (PhotonNetwork.IsConnectedAndReady)
            {
                PhotonNetwork.NickName = username;
                PhotonHashtable props = new PhotonHashtable { { "Username", username }, { "Nickname", username } };
                PhotonNetwork.LocalPlayer.SetCustomProperties(props);
                Debug.Log($"StartScreenController: Set Photon Nickname and properties to username={username} for local player");
            }
            if (feedbackText != null)
            {
                lastFeedbackMessage = "Login successful! (Editor)";
                feedbackText.text = lastFeedbackMessage;
            }
            Debug.Log("StartScreenController: Login successful, loading InsideSpaceShip.");
            SceneManager.LoadScene("InsideSpaceShip");
        }
        else
        {
            if (feedbackText != null)
            {
                lastFeedbackMessage = "Invalid username or password. Please register first.";
                feedbackText.text = lastFeedbackMessage;
            }
            Debug.LogWarning("StartScreenController: Login failed - invalid credentials.");
        }
#endif
    }

    private void OnGuestClicked()
    {
        username = "Guest_" + Random.Range(1000, 10000).ToString("D4");

        string guestUid = "guest-" + System.Guid.NewGuid().ToString();
        PlayerPrefs.SetString("PlayerUID", guestUid);
        PlayerPrefs.SetString("PlayerUsername", username);
        PlayerPrefs.SetString("PlayerPassword", "guest");
        PlayerPrefs.SetInt("IsGuest", 1);
        PlayerPrefs.DeleteKey("PlayerNickname");
        if (PhotonNetwork.IsConnectedAndReady)
        {
            PhotonNetwork.NickName = username;
            PhotonHashtable props = new PhotonHashtable { { "Username", username }, { "Nickname", username } };
            PhotonNetwork.LocalPlayer.SetCustomProperties(props);
            Debug.Log($"StartScreenController: Set Photon Nickname and properties to username={username} for guest player");
        }
        if (feedbackText != null)
        {
            lastFeedbackMessage = "Entering as guest: " + username;
            feedbackText.text = lastFeedbackMessage;
        }
        Debug.Log($"StartScreenController: Guest login successful, username={username}, UID={guestUid}, loading InsideSpaceShip.");
        SceneManager.LoadScene("InsideSpaceShip");
    }

    public void OnSignUpSuccess(string data)
    {
        string[] parts = data.Split('|');
        if (parts.Length == 2 && !string.IsNullOrEmpty(parts[0]) && !string.IsNullOrEmpty(parts[1]))
        {
            PlayerPrefs.SetString("PlayerUID", parts[0]);
            PlayerPrefs.SetString("PlayerUsername", parts[1]);
            PlayerPrefs.SetInt("IsGuest", 0);
            PlayerPrefs.DeleteKey("PlayerNickname");
            if (PhotonNetwork.IsConnectedAndReady)
            {
                PhotonNetwork.NickName = parts[1];
                PhotonHashtable props = new PhotonHashtable { { "Username", parts[1] }, { "Nickname", parts[1] } };
                PhotonNetwork.LocalPlayer.SetCustomProperties(props);
                Debug.Log($"StartScreenController: Set Photon Nickname and properties to username={parts[1]} for local player");
            }
            PlayerPrefs.Save();
            if (feedbackText != null)
            {
                lastFeedbackMessage = "Registration successful!";
                feedbackText.text = lastFeedbackMessage;
            }
            Debug.Log($"StartScreenController: SignUp successful, UID={parts[0]}, Username={parts[1]}, loading InsideSpaceShip.");
            SceneManager.LoadScene("InsideSpaceShip");
        }
        else
        {
            Debug.LogError($"StartScreenController: Invalid signup data format: {data}");
            if (feedbackText != null)
            {
                lastFeedbackMessage = "Registration failed: Invalid server response.";
                feedbackText.text = lastFeedbackMessage;
            }
        }
    }

    public void OnSignUpFailed(string error)
    {
        Debug.LogError($"StartScreenController: Signup failed: {error}");
        if (feedbackText != null)
        {
            lastFeedbackMessage = $"Registration failed: {error}";
            feedbackText.text = lastFeedbackMessage;
        }
        if (error.Contains("Username already taken"))
        {
            OnNewPlayerClicked();
        }
    }

    public void OnSignInSuccess(string data)
    {
        string[] parts = data.Split('|');
        if (parts.Length == 2 && !string.IsNullOrEmpty(parts[0]) && !string.IsNullOrEmpty(parts[1]))
        {
            PlayerPrefs.SetString("PlayerUID", parts[0]);
            PlayerPrefs.SetString("PlayerUsername", parts[1]);
            PlayerPrefs.SetInt("IsGuest", 0);
            PlayerPrefs.DeleteKey("PlayerNickname");
            if (PhotonNetwork.IsConnectedAndReady)
            {
                PhotonNetwork.NickName = parts[1];
                PhotonHashtable props = new PhotonHashtable { { "Username", parts[1] }, { "Nickname", parts[1] } };
                PhotonNetwork.LocalPlayer.SetCustomProperties(props);
                Debug.Log($"StartScreenController: Set Photon Nickname and properties to username={parts[1]} for local player");
            }
            PlayerPrefs.Save();
            if (feedbackText != null)
            {
                lastFeedbackMessage = "Login successful!";
                feedbackText.text = lastFeedbackMessage;
            }
            Debug.Log($"StartScreenController: SignIn successful, UID={parts[0]}, Username={parts[1]}, loading InsideSpaceShip.");
            SceneManager.LoadScene("InsideSpaceShip");
        }
        else
        {
            Debug.LogError($"StartScreenController: Invalid signin data format: {data}");
            if (feedbackText != null)
            {
                lastFeedbackMessage = "Login failed: Invalid server response.";
                feedbackText.text = lastFeedbackMessage;
            }
        }
    }

    public void OnSignInFailed(string error)
    {
        Debug.LogError($"StartScreenController: Signin failed: {error}");
        if (feedbackText != null)
        {
            lastFeedbackMessage = $"Login failed: {error}";
            feedbackText.text = lastFeedbackMessage;
        }
    }

    private void ClearFields()
    {
        if (usernameInput != null)
            usernameInput.text = "";
        if (passwordInput != null)
            passwordInput.text = "";
        if (feedbackText != null)
        {
            feedbackText.text = "";
            lastFeedbackMessage = "";
        }
        if (emailInput != null)
            emailInput.text = "";
    }

    public void TriggerNewPlayerButton()
    {
        if (newPlayerButton != null)
        {
            buttonClicked = true;
            newPlayerButton.onClick.Invoke();
            Debug.Log("StartScreenController: Programmatically triggered newPlayerButton click.");
        }
        else
        {
            Debug.LogError("StartScreenController: Cannot trigger newPlayerButton, it is null.");
        }
    }

    public override void OnConnectedToMaster()
    {
        Debug.Log("StartScreenController: Connected to Photon Master Server");
        string username = PlayerPrefs.GetString("PlayerUsername", $"Player_{PhotonNetwork.LocalPlayer.ActorNumber}");
        PhotonNetwork.NickName = username;
        PhotonHashtable props = new PhotonHashtable { { "Username", username }, { "Nickname", username } };
        PhotonNetwork.LocalPlayer.SetCustomProperties(props);
        Debug.Log($"StartScreenController: Set Photon Nickname and properties to username={username} for local player");
        PhotonNetwork.JoinLobby();
    }

    void OnApplicationQuit()
    {
        if (PlayerPrefs.GetInt("IsGuest", 0) == 1)
        {
            PlayerPrefs.DeleteKey("PlayerUID");
            PlayerPrefs.DeleteKey("PlayerUsername");
            PlayerPrefs.DeleteKey("PlayerPassword");
            PlayerPrefs.DeleteKey("IsGuest");
            Debug.Log("StartScreenController: Cleared guest data on application quit.");
        }
    }

    public override void OnDisconnected(DisconnectCause cause)
    {
        Debug.LogWarning($"Disconnected from Photon: {cause}");
        PhotonNetwork.Reconnect();
    }
}

