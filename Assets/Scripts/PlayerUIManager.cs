using UnityEngine;
using UnityEngine.UI;
using Photon.Pun;
using System.Collections;
using UnityEngine.SceneManagement;
using UnityEngine.EventSystems;

public class PlayerUIManager : MonoBehaviour
{
    [SerializeField] private Canvas playerUICanvas; [SerializeField] private Button teleportButton; [SerializeField] private Button shieldButton; [SerializeField] private Button laserButton; [SerializeField] private Button twinButton; [SerializeField] private Button bombButton; private PlayerController playerController;

    void Awake()
    {
        if (playerUICanvas == null)
        {
            playerUICanvas = GetComponent<Canvas>();
            if (playerUICanvas == null)
            {
                CustomLogger.LogError("PlayerUIManager: Canvas component missing on PlayerUI. Disabling script.");
                enabled = false;
                return;
            }
        }

        EnsureCanvasSetup();
        InitializeComponents();
        CustomLogger.Log($"PlayerUIManager: Initialized on PlayerUI Canvas '{playerUICanvas.name}' in scene '{SceneManager.GetActiveScene().name}'.");
    }
    private void EnsureCanvasSetup()
    {
        GraphicRaycaster raycaster = playerUICanvas.GetComponent<GraphicRaycaster>();
        if (raycaster == null)
        {
            raycaster = playerUICanvas.gameObject.AddComponent<GraphicRaycaster>();
            CustomLogger.Log($"PlayerUIManager: Added GraphicRaycaster to canvas '{playerUICanvas.name}'.");
        }
        if (!raycaster.enabled)
        {
            raycaster.enabled = true;
            CustomLogger.Log($"PlayerUIManager: Enabled GraphicRaycaster on canvas '{playerUICanvas.name}'.");
        }

        CanvasScaler scaler = playerUICanvas.GetComponent<CanvasScaler>();
        if (scaler == null)
        {
            scaler = playerUICanvas.gameObject.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);
            CustomLogger.Log($"PlayerUIManager: Added CanvasScaler to canvas '{playerUICanvas.name}' with reference resolution 1920x1080.");
        }

        if (!playerUICanvas.gameObject.activeInHierarchy)
        {
            playerUICanvas.gameObject.SetActive(true);
            CustomLogger.Log($"PlayerUIManager: Activated canvas '{playerUICanvas.name}'.");
        }

        EventSystem eventSystem = FindFirstObjectByType<EventSystem>();
        if (eventSystem == null)
        {
            GameObject esObj = new GameObject("EventSystem");
            eventSystem = esObj.AddComponent<EventSystem>();
            esObj.AddComponent<StandaloneInputModule>();
            CustomLogger.Log($"PlayerUIManager: Created new EventSystem for scene '{SceneManager.GetActiveScene().name}'.");
        }
        else if (!eventSystem.gameObject.activeInHierarchy)
        {
            eventSystem.gameObject.SetActive(true);
            CustomLogger.Log($"PlayerUIManager: Activated existing EventSystem.");
        }
    }
    private void InitializeComponents()
    {
        if (teleportButton == null) teleportButton = FindButtonByTag("TeleportButton");
        if (shieldButton == null) shieldButton = FindButtonByTag("ShieldButton");
        if (laserButton == null) laserButton = FindButtonByTag("LaserButton");
        if (twinButton == null) twinButton = FindButtonByTag("TwinButton");
        if (bombButton == null) bombButton = FindButtonByTag("BombButton");

        EnsureButtonSetup(teleportButton, "TeleportButton");
        EnsureButtonSetup(shieldButton, "ShieldButton");
        EnsureButtonSetup(laserButton, "LaserButton");
        EnsureButtonSetup(twinButton, "TwinButton");
        EnsureButtonSetup(bombButton, "BombButton");

        string buttonStatus = $"Teleport={IsButtonValid(teleportButton)}, Shield={IsButtonValid(shieldButton)}, Laser={IsButtonValid(laserButton)}, Twin={IsButtonValid(twinButton)}, Bomb={IsButtonValid(bombButton)}";
        if (teleportButton == null || shieldButton == null || laserButton == null || twinButton == null || bombButton == null)
        {
            CustomLogger.LogError($"PlayerUIManager: One or more buttons not found in PlayerUI Canvas '{playerUICanvas.name}'. Button status: {buttonStatus}");
        }
        else
        {
            CustomLogger.Log($"PlayerUIManager: All buttons initialized in canvas '{playerUICanvas.name}'. Button status: {buttonStatus}");
        }
    }
    private void EnsureButtonSetup(Button button, string expectedTag)
    {
        if (button == null) return;

        var handler = button.GetComponent<ActionButtonHandler>();
        if (handler == null)
        {
            handler = button.gameObject.AddComponent<ActionButtonHandler>();
            CustomLogger.Log($"PlayerUIManager: Added ActionButtonHandler to button '{button.name}' with tag '{expectedTag}'.");
        }

        // Only set tag if it's unset or invalid to avoid overwriting
        if (string.IsNullOrEmpty(button.tag) || !IsValidTag(button.tag))
        {
            button.tag = expectedTag;
            CustomLogger.Log($"PlayerUIManager: Set tag on button '{button.name}' to '{expectedTag}'.");
        }

        if (!button.interactable)
        {
            button.interactable = true;
            CustomLogger.Log($"PlayerUIManager: Set button '{button.name}' to interactable.");
        }

        if (!button.gameObject.activeInHierarchy)
        {
            button.gameObject.SetActive(true);
            CustomLogger.Log($"PlayerUIManager: Activated button '{button.name}' with tag '{expectedTag}'.");
        }
    }
    private bool IsValidTag(string tag)
    {
        return tag == "TeleportButton" || tag == "ShieldButton" || tag == "LaserButton" || tag == "TwinButton" || tag == "BombButton";
    }

    private bool IsButtonValid(Button button)
    {
        return button != null && button.gameObject.activeInHierarchy && button.interactable && button.GetComponent<ActionButtonHandler>() != null;
    }

    private Button FindButtonByTag(string tag)
    {
        Button[] buttons = playerUICanvas.GetComponentsInChildren<Button>(true);
        foreach (var btn in buttons)
        {
            if (btn.tag == tag)
                return btn;
        }
        return null;
    }

    void Start()
    {
        string sceneName = SceneManager.GetActiveScene().name;
        if (sceneName != "MoonRan" && sceneName != "TeamMoonRan")
        {
            CustomLogger.Log($"PlayerUIManager: Skipping initialization for non-gameplay scene {sceneName}");
            enabled = false;
            return;
        }
        StartCoroutine(InitializePlayerReferences());
    }

    private IEnumerator InitializePlayerReferences()
    {
        int maxRetries = 20;
        int retries = 0;
        float initialDelay = 0.5f;

        while (retries < maxRetries)
        {
            if (PhotonNetwork.LocalPlayer == null)
            {
                CustomLogger.LogWarning($"PlayerUIManager: PhotonNetwork.LocalPlayer is null on retry {retries + 1}/{maxRetries}, waiting...");
                retries++;
                yield return new WaitForSeconds(initialDelay);
                continue;
            }

            // Check TagObject
            if (PhotonNetwork.LocalPlayer.TagObject != null)
            {
                GameObject playerObj = PhotonNetwork.LocalPlayer.TagObject as GameObject;
                if (playerObj != null && playerObj.CompareTag("Player"))
                {
                    PhotonView photonView = playerObj.GetComponent<PhotonView>();
                    if (photonView != null && photonView.IsMine)
                    {
                        playerController = playerObj.GetComponent<PlayerController>();
                        if (playerController != null)
                        {
                            CustomLogger.Log($"PlayerUIManager: Found local PlayerController via TagObject on {playerObj.name}, ViewID={photonView.ViewID}, NickName={playerController.NickName}");
                            yield break;
                        }
                    }
                }
            }

            // Check PlayerViewID
            if (PhotonNetwork.LocalPlayer.CustomProperties.TryGetValue("PlayerViewID", out object viewIDObj) && viewIDObj is int viewID)
            {
                PhotonView playerView = PhotonView.Find(viewID);
                if (playerView != null && playerView.IsMine && playerView.gameObject.CompareTag("Player"))
                {
                    playerController = playerView.GetComponent<PlayerController>();
                    if (playerController != null)
                    {
                        PhotonNetwork.LocalPlayer.TagObject = playerView.gameObject;
                        CustomLogger.Log($"PlayerUIManager: Found local PlayerController via PlayerViewID on {playerView.gameObject.name}, ViewID={viewID}, NickName={playerController.NickName}");
                        yield break;
                    }
                }
            }

            // Fallback: Search by tag
            GameObject[] players = GameObject.FindGameObjectsWithTag("Player");
            foreach (GameObject playerObj in players)
            {
                PhotonView photonView = playerObj.GetComponent<PhotonView>();
                if (photonView != null && photonView.IsMine)
                {
                    playerController = playerObj.GetComponent<PlayerController>();
                    if (playerController != null)
                    {
                        PhotonNetwork.LocalPlayer.TagObject = playerObj;
                        PhotonNetwork.LocalPlayer.SetCustomProperties(new ExitGames.Client.Photon.Hashtable { { "PlayerViewID", photonView.ViewID } });
                        CustomLogger.Log($"PlayerUIManager: Found local PlayerController via tag on {playerObj.name}, ViewID={photonView.ViewID}, NickName={playerController.NickName}");
                        yield break;
                    }
                }
            }

            retries++;
            CustomLogger.Log($"PlayerUIManager: Retry {retries}/{maxRetries} to find local PlayerController, TagObject={(PhotonNetwork.LocalPlayer.TagObject != null ? "exists" : "null")}, PlayerViewID={(PhotonNetwork.LocalPlayer.CustomProperties.ContainsKey("PlayerViewID") ? "set" : "unset")}, PlayersFound={players.Length}");
            yield return new WaitForSeconds(initialDelay);
            initialDelay = Mathf.Min(initialDelay * 1.2f, 2f); // Exponential backoff
        }

        CustomLogger.LogError($"PlayerUIManager: Failed to find local PlayerController after {maxRetries} retries. Disabling script.");
        enabled = false;
    }

   

    public void ResetUI()
    {
        if (teleportButton != null) teleportButton.GetComponent<ActionButtonHandler>()?.ForceResetButton();
        if (shieldButton != null) shieldButton.GetComponent<ActionButtonHandler>()?.ForceResetButton();
        if (laserButton != null) laserButton.GetComponent<ActionButtonHandler>()?.ForceResetButton();
        if (twinButton != null) twinButton.GetComponent<ActionButtonHandler>()?.ForceResetButton();
        if (bombButton != null) bombButton.GetComponent<ActionButtonHandler>()?.ForceResetButton();

        if (playerController != null)
        {
            PlayerCanvasController canvasController = playerController.GetComponentInChildren<PlayerCanvasController>();
            if (canvasController != null)
            {
                canvasController.ResetIcons();
                CustomLogger.Log("PlayerUIManager: Called ResetIcons on PlayerCanvasController.");
            }
        }

        CustomLogger.Log("PlayerUIManager: Reset UI elements (buttons and icons).");
    }

}