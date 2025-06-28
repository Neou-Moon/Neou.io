using UnityEngine;
using UnityEngine.UI;
using UnityEngine.SceneManagement;
using TMPro;
using System.Runtime.InteropServices;
using UnityEngine.Video;

public class SettingsManager : MonoBehaviour
{
    private bool canClickForgotPassword = true;
    [SerializeField] private TMP_Dropdown qualityDropdown;
    [SerializeField] private Slider videoPlayerSlider;
    [SerializeField] private TextMeshProUGUI videoPlayerValueText;
    [SerializeField] private TMP_InputField emailInput;
    [SerializeField] private Button linkEmailButton;
    [SerializeField] private Button forgotPasswordButton;
    [SerializeField] private TextMeshProUGUI emailStatusText;
    [SerializeField] private TextMeshProUGUI feedbackText;
    [SerializeField] private Button backButton;

    // Static event to notify other scripts of volume changes
    public static event System.Action<float> OnVideoPlayerVolumeChanged;

    [DllImport("__Internal")]
    private static extern void FirebaseLinkEmail(string uid, string email, string callbackObjectName);
    [DllImport("__Internal")]
    private static extern void FirebaseSendPasswordReset(string email, string callbackObjectName);
    [DllImport("__Internal")]
    private static extern void FirebaseGetEmail(string uid, string callbackObjectName);
    [DllImport("__Internal")]
    private static extern void FirebaseSendVerificationEmail(string callbackObjectName);
    [DllImport("__Internal")]
    private static extern void FirebaseGetVerificationStatus(string callbackObjectName);

    void Start()
    {
        // Initialize quality dropdown
        if (qualityDropdown != null)
        {
            qualityDropdown.options.Clear();
            qualityDropdown.options.Add(new TMP_Dropdown.OptionData("Low"));
            qualityDropdown.options.Add(new TMP_Dropdown.OptionData("Medium"));
            qualityDropdown.options.Add(new TMP_Dropdown.OptionData("High"));
            int savedQuality = PlayerPrefs.GetInt("QualityLevel", 2); // Default: High
            qualityDropdown.value = savedQuality;
            UpdateQuality(savedQuality);
            qualityDropdown.RefreshShownValue();
            qualityDropdown.onValueChanged.AddListener(UpdateQuality);
        }
        else
        {
            Debug.LogError("SettingsManager: qualityDropdown is not assigned.");
        }

        // Initialize volume slider
        if (videoPlayerSlider != null && videoPlayerValueText != null)
        {
            float savedVolume = PlayerPrefs.GetFloat("VideoPlayerVolume", 0.5f);
            videoPlayerSlider.value = savedVolume;
            UpdateVideoPlayerVolume(savedVolume);
            videoPlayerSlider.onValueChanged.AddListener(UpdateVideoPlayerVolume);
        }
        else
        {
            Debug.LogError("SettingsManager: videoPlayerSlider or videoPlayerValueText is not assigned.");
        }

        // Initialize email status
        if (emailStatusText != null && feedbackText != null && emailInput != null && linkEmailButton != null && forgotPasswordButton != null)
        {
            if (PlayerPrefs.GetInt("IsGuest", 0) == 1)
            {
                emailStatusText.text = "Guest accounts cannot link emails.";
                emailInput.interactable = false;
                linkEmailButton.interactable = false;
                forgotPasswordButton.interactable = false;
                feedbackText.text = "Please register to link an email or reset password.";
                Debug.Log("SettingsManager: Blocked email linking and password reset for guest account.");
            }
            else
            {
                string uid = PlayerPrefs.GetString("PlayerUID", "");
                if (!string.IsNullOrEmpty(uid) && Application.platform == RuntimePlatform.WebGLPlayer)
                {
                    FirebaseGetEmail(uid, gameObject.name);
                    FirebaseGetVerificationStatus(gameObject.name);
                    Debug.Log($"SettingsManager: Fetching linked email and verification status for UID={uid}");
                }
                else
                {
                    emailStatusText.text = "Linked Email: None";
                    if (Application.platform != RuntimePlatform.WebGLPlayer)
                    {
                        feedbackText.text = "Running in Editor (Firebase simulated)";
                    }
                }
            }
        }
        else
        {
            Debug.LogError("SettingsManager: Email UI components (emailStatusText, feedbackText, emailInput, linkEmailButton, or forgotPasswordButton) are not assigned.");
        }

        // Add back button listener
        if (backButton != null)
        {
            backButton.onClick.AddListener(OnBack);
        }
        else
        {
            Debug.LogError("SettingsManager: backButton is not assigned.");
        }

        // Add email-related listeners
        if (linkEmailButton != null)
            linkEmailButton.onClick.AddListener(LinkEmail);
        if (forgotPasswordButton != null)
            forgotPasswordButton.onClick.AddListener(SendPasswordReset);
    }

    void UpdateQuality(int index)
    {
        switch (index)
        {
            case 0: // Low
                QualitySettings.SetQualityLevel(0, true);
                QualitySettings.globalTextureMipmapLimit = 2;
                QualitySettings.anisotropicFiltering = AnisotropicFiltering.Disable;
                QualitySettings.antiAliasing = 0;
                QualitySettings.shadows = ShadowQuality.Disable;
                QualitySettings.shadowDistance = 0f;
                QualitySettings.realtimeReflectionProbes = false;
                QualitySettings.pixelLightCount = 1;
                QualitySettings.lodBias = 0.3f;
                QualitySettings.vSyncCount = 0;
                break;
            case 1: // Medium
                QualitySettings.SetQualityLevel(1, true);
                QualitySettings.globalTextureMipmapLimit = 1;
                QualitySettings.anisotropicFiltering = AnisotropicFiltering.Enable;
                QualitySettings.antiAliasing = 2;
                QualitySettings.shadows = ShadowQuality.HardOnly;
                QualitySettings.shadowDistance = 30f;
                QualitySettings.realtimeReflectionProbes = false;
                QualitySettings.pixelLightCount = 2;
                QualitySettings.lodBias = 0.7f;
                QualitySettings.vSyncCount = 1;
                break;
            case 2: // High
                QualitySettings.SetQualityLevel(2, true);
                QualitySettings.globalTextureMipmapLimit = 0;
                QualitySettings.anisotropicFiltering = AnisotropicFiltering.ForceEnable;
                QualitySettings.antiAliasing = 4;
                QualitySettings.shadows = ShadowQuality.All;
                QualitySettings.shadowDistance = 60f;
                QualitySettings.realtimeReflectionProbes = true;
                QualitySettings.pixelLightCount = 4;
                QualitySettings.lodBias = 1.0f;
                QualitySettings.vSyncCount = 1;
                break;
        }

        PlayerPrefs.SetInt("QualityLevel", index);
        if (feedbackText != null)
            feedbackText.text = $"Graphics set to {qualityDropdown.options[index].text}";
        Debug.Log($"SettingsManager: Set quality to {qualityDropdown.options[index].text} (index {index})");
    }

    void UpdateVideoPlayerVolume(float value)
    {
        VideoPlayer[] videoPlayers = Object.FindObjectsByType<VideoPlayer>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        foreach (var videoPlayer in videoPlayers)
        {
            videoPlayer.SetDirectAudioVolume(0, value);
        }

        PlayerPrefs.SetFloat("VideoPlayerVolume", value);
        if (videoPlayerValueText != null)
            videoPlayerValueText.text = $"{(int)(value * 100)}%";
        OnVideoPlayerVolumeChanged?.Invoke(value);
        Debug.Log($"SettingsManager: Set video player volume to {value}");
    }

    void LinkEmail()
    {
        if (PlayerPrefs.GetInt("IsGuest", 0) == 1)
        {
            if (feedbackText != null)
                feedbackText.text = "Guest accounts cannot link emails. Please register.";
            Debug.Log("SettingsManager: Blocked email linking for guest account.");
            return;
        }

        string email = emailInput != null ? emailInput.text.Trim() : "";
        if (!IsValidEmail(email))
        {
            if (feedbackText != null)
                feedbackText.text = "Invalid email format.";
            Debug.LogWarning("SettingsManager: Invalid email format.");
            return;
        }

        string uid = PlayerPrefs.GetString("PlayerUID", "");
        if (string.IsNullOrEmpty(uid))
        {
            if (feedbackText != null)
                feedbackText.text = "Not signed in. Please log in or register.";
            Debug.LogWarning("SettingsManager: No UID found for email linking.");
            return;
        }

        if (Application.platform == RuntimePlatform.WebGLPlayer)
        {
            FirebaseLinkEmail(uid, email, gameObject.name);
            if (feedbackText != null)
                feedbackText.text = "Linking email...";
            Debug.Log($"SettingsManager: Attempting to link email {email} for UID={uid}");
        }
        else
        {
            if (feedbackText != null)
                feedbackText.text = $"Email {email} linked (Editor simulation).";
            if (emailStatusText != null)
                emailStatusText.text = $"Linked Email: {email}";
            if (emailInput != null)
                emailInput.text = "";
            Debug.Log($"SettingsManager: Simulated linking email {email} in Editor.");
        }
    }

    void SendPasswordReset()
    {
        if (!canClickForgotPassword) return;
        canClickForgotPassword = false;
        Invoke(nameof(ResetButtonCooldown), 2f);

        if (PlayerPrefs.GetInt("IsGuest", 0) == 1)
        {
            if (feedbackText != null)
                feedbackText.text = "Guest accounts cannot reset passwords. Please register.";
            canClickForgotPassword = true;
            Debug.Log("SettingsManager: Blocked password reset for guest account.");
            return;
        }

        string email = emailInput != null ? emailInput.text.Trim() : "";
        if (!IsValidEmail(email))
        {
            if (feedbackText != null)
                feedbackText.text = "Enter a valid email.";
            canClickForgotPassword = true;
            Debug.LogWarning("SettingsManager: Invalid email format for password reset.");
            return;
        }

        string uid = PlayerPrefs.GetString("PlayerUID", "");
        if (string.IsNullOrEmpty(uid))
        {
            if (feedbackText != null)
                feedbackText.text = "Not signed in. Please log in or register.";
            canClickForgotPassword = true;
            Debug.LogWarning("SettingsManager: No UID found for password reset.");
            return;
        }

        if (Application.platform == RuntimePlatform.WebGLPlayer)
        {
            FirebaseGetVerificationStatus(gameObject.name);
            if (feedbackText != null)
                feedbackText.text = "Checking email verification status...";
            Debug.Log($"SettingsManager: Checking verification status before password reset for email {email}");
        }
        else
        {
            if (feedbackText != null)
                feedbackText.text = $"Password reset email sent to {email} (Editor simulation).";
            canClickForgotPassword = true;
            Debug.Log($"SettingsManager: Simulated password reset email sent to {email} in Editor.");
        }
    }

    void ResetButtonCooldown() => canClickForgotPassword = true;

    void OnBack()
    {
        PlayerPrefs.Save();
        SceneManager.LoadScene("InsideSpaceShip");
        Debug.Log("SettingsManager: Returning to InsideSpaceShip scene.");
    }

    bool IsValidEmail(string email)
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

    public void OnEmailLinked(string result)
    {
        if (result == "success")
        {
            string email = emailInput != null ? emailInput.text.Trim() : "";
            if (feedbackText != null)
                feedbackText.text = "Email linked successfully! Verifying email...";
            if (emailStatusText != null)
                emailStatusText.text = $"Linked Email: {email}";
            if (Application.platform == RuntimePlatform.WebGLPlayer)
            {
                FirebaseSendVerificationEmail(gameObject.name);
                Debug.Log($"SettingsManager: Email {email} linked, requesting verification.");
            }
            else
            {
                if (feedbackText != null)
                    feedbackText.text = $"Email {email} linked and verified (Editor simulation).";
                if (emailInput != null)
                    emailInput.text = "";
                FirebaseGetEmail(PlayerPrefs.GetString("PlayerUID", ""), gameObject.name);
            }
        }
        else
        {
            if (feedbackText != null)
                feedbackText.text = $"Failed to link email: {result}";
            canClickForgotPassword = true;
            Debug.LogError($"SettingsManager: Email linking failed: {result}");
        }
    }

    public void OnPasswordResetSent(string result)
    {
        if (feedbackText != null)
            feedbackText.text = result == "success" ? "Password reset email sent! Check your inbox." : $"Error: {result}";
        if (result == "success" && emailInput != null)
            emailInput.text = "";
        canClickForgotPassword = true;
        Debug.Log($"SettingsManager: Password reset result: {result}");
    }

    public void OnEmailLoaded(string email)
    {
        if (!string.IsNullOrEmpty(email))
        {
            if (emailStatusText != null)
                emailStatusText.text = $"Linked Email: {email}";
            if (emailInput != null)
                emailInput.text = email;
            if (feedbackText != null)
                feedbackText.text = "Linked email loaded. Use it to reset password or enter a new email.";
            Debug.Log($"SettingsManager: Loaded linked email: {email}");
        }
        else
        {
            if (emailStatusText != null)
                emailStatusText.text = "Linked Email: None";
            if (feedbackText != null)
                feedbackText.text = "No email linked. Enter an email to link and enable password reset.";
            Debug.Log("SettingsManager: No linked email found.");
        }
    }

    public void OnEmailVerified(string result)
    {
        if (result == "success")
        {
            if (feedbackText != null)
                feedbackText.text = "Email verified! Checking verification status...";
            if (Application.platform == RuntimePlatform.WebGLPlayer)
            {
                FirebaseGetVerificationStatus(gameObject.name);
                Debug.Log("SettingsManager: Email verification successful, checking status.");
            }
        }
        else
        {
            if (feedbackText != null)
                feedbackText.text = $"Email verification failed: {result}";
            canClickForgotPassword = true;
            Debug.LogError($"SettingsManager: Email verification failed: {result}");
        }
    }

    public void OnVerificationStatusReceived(string status)
    {
        if (status == "verified")
        {
            if (feedbackText != null)
                feedbackText.text = "Email is verified! Sending password reset email...";
            if (emailInput != null && !string.IsNullOrEmpty(emailInput.text.Trim()) && Application.platform == RuntimePlatform.WebGLPlayer)
            {
                FirebaseSendPasswordReset(emailInput.text.Trim(), gameObject.name);
                Debug.Log($"SettingsManager: Email verified, sending password reset email to {emailInput.text.Trim()}");
            }
        }
        else if (status == "unverified")
        {
            if (feedbackText != null)
                feedbackText.text = "Email is not verified. Please check your inbox for the verification email.";
            canClickForgotPassword = true;
            Debug.Log("SettingsManager: Email is unverified, prompting user to verify.");
        }
        else if (status == "no_user")
        {
            if (feedbackText != null)
                feedbackText.text = "Not signed in. Please log in or register.";
            canClickForgotPassword = true;
            Debug.LogError("SettingsManager: No user signed in for verification status check.");
        }
        else
        {
            if (feedbackText != null)
                feedbackText.text = "Error checking email verification status.";
            canClickForgotPassword = true;
            Debug.LogError($"SettingsManager: Verification status check failed: {status}");
        }
    }
}