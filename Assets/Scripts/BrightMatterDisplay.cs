using UnityEngine;
using TMPro;
using System.Runtime.InteropServices;
using System.Collections;
using Photon.Pun;

public class BrightMatterDisplay : MonoBehaviour
{
    [SerializeField] private TextMeshProUGUI brightMatterText; // Assign in Inspector if possible
    private PlayerController playerController;

    [DllImport("__Internal")]
    private static extern void FirebaseGetBrightMatter(string uid, string callbackObject);

    void Start()
    {
        StartCoroutine(InitializeWithRetry());
    }

    private IEnumerator InitializeWithRetry()
    {
        float waitTime = 0f;
        const float maxWaitTime = 15f;
        const float checkInterval = 0.5f;

        while (waitTime < maxWaitTime)
        {
            var components = FindLocalPlayerComponents();
            playerController = components.playerController;
            if (brightMatterText == null)
                brightMatterText = components.brightMatterText;

            if (playerController != null && brightMatterText != null)
            {
                Debug.Log($"BrightMatterDisplay: Found PlayerController and BrightMatterText after {waitTime:F2} seconds.");
                yield return StartCoroutine(InitializePlayerControllerAndFirebase());
                yield break;
            }

            waitTime += checkInterval;
            Debug.Log($"BrightMatterDisplay: Waiting for PlayerController and BrightMatterText, elapsed time {waitTime:F2}/{maxWaitTime} seconds. " +
                      $"PlayerController={(playerController != null ? "found" : "null")}, " +
                      $"BrightMatterText={(brightMatterText != null ? "found" : "null")}, " +
                      $"PhotonConnected={PhotonNetwork.IsConnectedAndReady}, " +
                      $"LocalPlayer={(PhotonNetwork.LocalPlayer != null ? PhotonNetwork.LocalPlayer.NickName : "null")}");
            yield return new WaitForSeconds(checkInterval);
        }

        Debug.LogError("BrightMatterDisplay: Failed to initialize after 15 seconds. Ensure Player.Prefab has PlayerController, PhotonView (IsMine=true), " +
                       "and Player Canvas/PlayerUI with BrightMatterText exist in the scene or assign BrightMatterText in the Inspector.");
        UpdateBrightMatter(0);
    }

    private (PlayerController playerController, TextMeshProUGUI brightMatterText) FindLocalPlayerComponents()
    {
        PlayerController foundController = null;
        TextMeshProUGUI foundText = brightMatterText; // Use assigned text if available

        // Wait for Photon to be ready
        if (!PhotonNetwork.IsConnectedAndReady || PhotonNetwork.LocalPlayer == null)
        {
            Debug.Log($"BrightMatterDisplay: Photon not ready, waiting. IsConnected={PhotonNetwork.IsConnectedAndReady}, LocalPlayer={(PhotonNetwork.LocalPlayer != null ? "exists" : "null")}");
            return (null, foundText);
        }

        // Try finding the local player via PhotonNetwork.LocalPlayer.TagObject
        if (PhotonNetwork.LocalPlayer.TagObject != null)
        {
            GameObject localPlayer = PhotonNetwork.LocalPlayer.TagObject as GameObject;
            if (localPlayer != null)
            {
                PhotonView photonView = localPlayer.GetComponent<PhotonView>();
                if (photonView != null && photonView.IsMine)
                {
                    foundController = localPlayer.GetComponent<PlayerController>();
                    if (foundController != null)
                        Debug.Log($"BrightMatterDisplay: Found local PlayerController via LocalPlayer.TagObject at {GetGameObjectPath(localPlayer)}.");
                }
            }
        }

        // Fallback: Search players with "Player" tag
        if (foundController == null)
        {
            GameObject[] players = GameObject.FindGameObjectsWithTag("Player");
            Debug.Log($"BrightMatterDisplay: Found {players.Length} GameObjects with tag 'Player'.");
            foreach (var player in players)
            {
                PhotonView photonView = player.GetComponent<PhotonView>();
                if (photonView != null && photonView.IsMine)
                {
                    foundController = player.GetComponent<PlayerController>();
                    if (foundController != null)
                    {
                        Debug.Log($"BrightMatterDisplay: Found local PlayerController via tag search at {GetGameObjectPath(player)}.");
                        break;
                    }
                }
            }
        }

        // Final fallback: FindObjectOfType
        if (foundController == null)
        {
            PlayerController[] controllers = Object.FindObjectsByType<PlayerController>(FindObjectsSortMode.None);
            Debug.Log($"BrightMatterDisplay: Found {controllers.Length} PlayerController components via FindObjectsByType.");
            foreach (var controller in controllers)
            {
                PhotonView photonView = controller.GetComponent<PhotonView>();
                if (photonView != null && photonView.IsMine)
                {
                    foundController = controller;
                    Debug.Log($"BrightMatterDisplay: Found local PlayerController via FindObjectsByType at {GetGameObjectPath(controller.gameObject)}.");
                    break;
                }
            }
        }

        // Find BrightMatterText if not assigned
        if (foundText == null && foundController != null)
        {
            foundText = FindBrightMatterText(foundController.gameObject);
        }
        // Fallback: Search in PlayerUI canvas
        if (foundText == null)
        {
            GameObject playerUICanvas = GameObject.Find("PlayerUI");
            if (playerUICanvas != null)
            {
                TextMeshProUGUI[] texts = playerUICanvas.GetComponentsInChildren<TextMeshProUGUI>(true);
                foreach (var text in texts)
                {
                    if (text.gameObject.name == "BrightMatterText")
                    {
                        foundText = text;
                        Debug.Log($"BrightMatterDisplay: Found BrightMatterText in PlayerUI canvas at {GetGameObjectPath(text.gameObject)}.");
                        break;
                    }
                }
            }
        }

        if (foundController == null)
            Debug.LogWarning("BrightMatterDisplay: No local PlayerController found. Ensure Player.Prefab has PlayerController and PhotonView with IsMine=true.");
        if (foundText == null && brightMatterText == null)
            Debug.LogWarning("BrightMatterDisplay: No BrightMatterText found in Player Canvas or PlayerUI. Ensure BrightMatterText exists or assign in Inspector.");

        return (foundController, foundText);
    }

    private TextMeshProUGUI FindBrightMatterText(GameObject player)
    {
        Canvas playerCanvas = player.GetComponentInChildren<Canvas>();
        if (playerCanvas != null && playerCanvas.gameObject.name == "Player Canvas")
        {
            Debug.Log($"BrightMatterDisplay: Found Player Canvas at {GetGameObjectPath(playerCanvas.gameObject)}.");
            TextMeshProUGUI[] texts = playerCanvas.GetComponentsInChildren<TextMeshProUGUI>(true);
            foreach (var text in texts)
            {
                if (text.gameObject.name == "BrightMatterText")
                {
                    Debug.Log($"BrightMatterDisplay: Found BrightMatterText in Player Canvas at {GetGameObjectPath(text.gameObject)}.");
                    return text;
                }
            }
            Debug.LogWarning($"BrightMatterDisplay: BrightMatterText not found in Player Canvas. Found {texts.Length} TextMeshProUGUI components: {string.Join(", ", System.Array.ConvertAll(texts, t => t.gameObject.name))}.");
        }
        else
        {
            Debug.LogWarning($"BrightMatterDisplay: Player Canvas not found in Player.Prefab at {GetGameObjectPath(player)}.");
        }
        return null;
    }

    private IEnumerator InitializePlayerControllerAndFirebase()
    {
        if (playerController == null)
        {
            Debug.LogError("BrightMatterDisplay: PlayerController not found.");
            UpdateBrightMatter(0);
            yield break;
        }

        UpdateBrightMatter(playerController.GetBrightMatter());

        string uid = PlayerPrefs.GetString("PlayerUID", "");
        if (string.IsNullOrEmpty(uid))
        {
            Debug.LogError("BrightMatterDisplay: PlayerUID not found.");
            UpdateBrightMatter(0);
            yield break;
        }

        if (Application.platform == RuntimePlatform.WebGLPlayer)
        {
            FirebaseGetBrightMatter(uid, gameObject.name);
        }
        else
        {
            int brightMatter = PlayerPrefs.GetInt("BrightMatter", 0);
            playerController.SyncBrightMatter(brightMatter);
            UpdateBrightMatter(brightMatter);
            Debug.Log($"BrightMatterDisplay: Editor simulation, loaded BrightMatter={brightMatter} for UID={uid}");
        }
    }

    public void UpdateBrightMatter(int points)
    {
        if (brightMatterText != null)
        {
            brightMatterText.text = $"BrightMatter: {points}";
            Debug.Log($"BrightMatterDisplay: Updated to BrightMatter: {points}, ViewID={(playerController != null ? playerController.photonView.ViewID.ToString() : "null")}");
        }
        else
        {
            Debug.LogWarning($"BrightMatterDisplay: Cannot update BrightMatter to {points}, brightMatterText is null.");
        }
    }

    public void OnBrightMatterRetrieved(string data)
    {
        if (Time.time > 15f) // Increased to match maxWaitTime
        {
            Debug.Log($"BrightMatterDisplay: OnBrightMatterRetrieved ignored, Time={Time.time:F2}, Data={data}");
            return;
        }
        Debug.Log($"BrightMatterDisplay: OnBrightMatterRetrieved data={data}");
        if (int.TryParse(data, out int points) && playerController != null)
        {
            playerController.SyncBrightMatter(points);
        }
        else
        {
            Debug.LogError($"BrightMatterDisplay: Failed to parse Bright Matter data: {data}");
            UpdateBrightMatter(0);
        }
    }

    public void OnBrightMatterFailed(string error)
    {
        Debug.LogError($"BrightMatterDisplay: Failed to retrieve Bright Matter: {error}");
        UpdateBrightMatter(0);
    }

    public int GetBrightMatter()
    {
        return playerController != null ? playerController.GetBrightMatter() : 0;
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
}