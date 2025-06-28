using UnityEngine;
using Photon.Pun;
using Photon.Realtime;
using TMPro;
using UnityEngine.UI;
using ExitGames.Client.Photon;
using System.Collections;
using UnityEngine.SceneManagement;
using PhotonHashtable = ExitGames.Client.Photon.Hashtable;
using System.Linq;

[RequireComponent(typeof(PhotonView))]
public class NameTag : MonoBehaviourPunCallbacks
{
    [SerializeField] private TextMeshProUGUI nameText;
    [SerializeField] private float nameTagHeight = 3f; // Height above player
    [SerializeField] private float canvasScale = 0.02f; // Uniform scale for Canvas
    [SerializeField] private float fontSize = 36f; // Font size for name tag text
    private Image crownImage; // Image component for the crown
    private BotController botController;
    private PlayerController playerController;
    private Transform canvasTransform;
    private RectTransform canvasRect;

    void Start()
    {
        botController = GetComponent<BotController>();
        playerController = GetComponent<PlayerController>();

        string currentScene = SceneManager.GetActiveScene().name;

        if (nameText == null)
        {
            CreateNameTagCanvas();
        }

        if (nameText == null)
        {
            CustomLogger.LogError($"NameTag: TextMeshProUGUI component not assigned on {gameObject.name}, ViewID={photonView.ViewID}");
            return;
        }

        if (botController != null)
        {
            BotController[] bots = Object.FindObjectsByType<BotController>(FindObjectsSortMode.None).OrderBy(b => b.ActorNumber).ToArray();
            int botIndex = System.Array.IndexOf(bots, botController) + 1;
            string botName = $"Bot_{botIndex}";
            nameText.text = botName;
            if (photonView.IsMine)
            {
                PhotonHashtable props = new PhotonHashtable { { "Nickname", botName }, { "Username", botName } };
                botController.photonView.RPC("UpdateCustomProperties", RpcTarget.AllBuffered, props);
                CustomLogger.Log($"NameTag: Initialized bot {photonView.ViewID} with Name: {botName}");
            }
            else
            {
                StartCoroutine(RetryFetchProperties());
            }
        }
        else if (playerController != null)
        {
            if (photonView.IsMine)
            {
                string nickname = PlayerPrefs.GetString("PlayerNickname", PlayerPrefs.GetString("PlayerUsername", $"Player_{PhotonNetwork.LocalPlayer.ActorNumber}"));
                string username = PlayerPrefs.GetString("PlayerUsername", $"Player_{PhotonNetwork.LocalPlayer.ActorNumber}");
                PhotonNetwork.NickName = nickname;
                PhotonHashtable customProps = new PhotonHashtable { { "Nickname", nickname }, { "Username", username } };
                PhotonNetwork.LocalPlayer.SetCustomProperties(customProps);
                nameText.text = nickname;
                photonView.RPC("UpdateName", RpcTarget.AllBuffered, nickname);
                CustomLogger.Log($"NameTag: Initialized local player {photonView.ViewID} with Name: {nickname} in scene {currentScene}");
            }
            else
            {
                UpdateNameFromProperties(photonView.Owner);
                StartCoroutine(RetryFetchProperties());
            }
        }
        else
        {
            string fallbackName = $"Unknown_{photonView.ViewID}";
            nameText.text = fallbackName;
            photonView.RPC("UpdateName", RpcTarget.AllBuffered, fallbackName);
            CustomLogger.LogWarning($"NameTag: No BotController or PlayerController on {gameObject.name}, ViewID={photonView.ViewID}, using fallback: {fallbackName}");
        }

        if (currentScene == "TeamMoonRan")
        {
            string team = botController != null && botController.CustomProperties.ContainsKey("Team") ?
                botController.CustomProperties["Team"].ToString() :
                (playerController != null && photonView.Owner != null && photonView.Owner.CustomProperties.ContainsKey("Team") ?
                    photonView.Owner.CustomProperties["Team"].ToString() : null);
            nameText.color = !string.IsNullOrEmpty(team) ? GetTeamColor(team) : Color.white;
            CustomLogger.Log($"NameTag: Set team color for {nameText.text} (ViewID={photonView.ViewID}) to {nameText.color} for team {team ?? "None"} in TeamMoonRan");
        }

        if (crownImage != null)
        {
            crownImage.gameObject.SetActive(false);
        }
    }

    void Update()
    {
        // Keep name tag static above character
        if (canvasTransform != null && Camera.main != null)
        {
            canvasTransform.position = transform.position + new Vector3(0, nameTagHeight, 0);
            canvasTransform.rotation = Quaternion.identity;
            float parentScaleX = transform.localScale.x;
            float adjustedScaleX = Mathf.Abs(canvasScale) * (parentScaleX < 0 ? -1 : 1);
            canvasRect.localScale = new Vector3(adjustedScaleX, Mathf.Abs(canvasScale), Mathf.Abs(canvasScale));
            nameText.rectTransform.localScale = new Vector3(1, 1, 1);
        }
    }

    private IEnumerator RetryFetchProperties()
    {
        int retries = 3;
        float delay = 1f;
        while (retries > 0)
        {
            yield return new WaitForSeconds(delay);
            if (botController != null && botController.CustomProperties.ContainsKey("Nickname"))
            {
                string nickname = botController.CustomProperties["Nickname"].ToString();
                if (!string.IsNullOrEmpty(nickname))
                {
                    nameText.text = nickname;
                    CustomLogger.Log($"NameTag: Retried and set bot Nickname to {nickname} for ViewID={photonView.ViewID}");
                    yield break;
                }
            }
            else if (playerController != null && photonView.Owner != null && photonView.Owner.CustomProperties.ContainsKey("Nickname"))
            {
                string nickname = photonView.Owner.CustomProperties["Nickname"].ToString();
                if (!string.IsNullOrEmpty(nickname))
                {
                    nameText.text = nickname;
                    photonView.RPC("UpdateName", RpcTarget.AllBuffered, nickname);
                    CustomLogger.Log($"NameTag: Retried and set player Nickname to {nickname} for ViewID={photonView.ViewID}");
                    yield break;
                }
            }
            retries--;
            CustomLogger.Log($"NameTag: Retry {4 - retries}/3 for Nickname on ViewID={photonView.ViewID}");
        }
        CustomLogger.LogWarning($"NameTag: Failed to fetch Nickname after retries for ViewID={photonView.ViewID}, keeping current name: {nameText.text}");
    }

    private Color GetTeamColor(string team)
    {
        switch (team)
        {
            case "Red":
                return new Color(1f, 0f, 0f); // Bright Red, matches ScoreboardManager.redTeamColor
            case "Cyan":
                return new Color(0f, 1f, 1f); // Cyan Blue, matches ScoreboardManager.cyanTeamColor
            default:
                CustomLogger.LogWarning($"NameTag: Unknown team {team}, defaulting to white");
                return Color.white;
        }
    }

    private void CreateNameTagCanvas()
    {
        GameObject canvasObj = new GameObject("NameTagCanvas");
        canvasObj.transform.SetParent(transform, false);
        Canvas canvas = canvasObj.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.WorldSpace;
        CanvasScaler scaler = canvasObj.AddComponent<CanvasScaler>();
        scaler.dynamicPixelsPerUnit = 100;
        canvasRect = canvasObj.GetComponent<RectTransform>();
        canvasRect.sizeDelta = new Vector2(200, 100); // Increased height to accommodate crown
        canvasRect.localScale = new Vector3(Mathf.Abs(canvasScale), Mathf.Abs(canvasScale), Mathf.Abs(canvasScale));
        canvasRect.localPosition = new Vector3(0, nameTagHeight, 0);

        // Create name text
        GameObject textObj = new GameObject("NameTagText");
        textObj.transform.SetParent(canvasObj.transform, false);
        nameText = textObj.AddComponent<TextMeshProUGUI>();
        nameText.rectTransform.sizeDelta = new Vector2(200, 50);
        nameText.rectTransform.localPosition = new Vector2(0, -25); // Lowered to make space for crown
        nameText.rectTransform.localScale = new Vector3(1, 1, 1);
        nameText.alignment = TextAlignmentOptions.Center;
        nameText.fontSize = fontSize;
        nameText.color = Color.white;
#pragma warning disable 0618
        nameText.enableWordWrapping = false;
#pragma warning restore 0618

        // Create crown image
        GameObject crownObj = new GameObject("CrownImage");
        crownObj.transform.SetParent(canvasObj.transform, false);
        crownImage = crownObj.AddComponent<Image>();
        crownImage.rectTransform.sizeDelta = new Vector2(150, 150);
        crownImage.rectTransform.localPosition = new Vector2(0, 250); // Above the name text
        crownImage.rectTransform.localScale = new Vector3(1, 1, 1);
        crownImage.gameObject.SetActive(false); // Hidden by default

        canvasTransform = canvasObj.transform;
        CustomLogger.Log($"NameTag: Dynamically created NameTagCanvas with crown for {gameObject.name}, ViewID={photonView.ViewID}");
    }

    public override void OnEnable()
    {
        base.OnEnable();
        PhotonNetwork.AddCallbackTarget(this);
    }

    public override void OnDisable()
    {
        base.OnDisable();
        PhotonNetwork.RemoveCallbackTarget(this);
    }

    public override void OnPlayerPropertiesUpdate(Player targetPlayer, PhotonHashtable changedProps)
    {
        if (playerController != null && !photonView.IsMine && photonView.Owner == targetPlayer && changedProps.ContainsKey("Nickname"))
        {
            UpdateNameFromProperties(targetPlayer);
        }
        else if (botController != null && changedProps.ContainsKey("Nickname"))
        {
            string newName = changedProps["Nickname"]?.ToString();
            if (!string.IsNullOrEmpty(newName))
            {
                nameText.text = newName;
                CustomLogger.Log($"NameTag: Updated bot {photonView.ViewID} with Nickname from properties: {newName}");
            }
        }
    }

    private void UpdateNameFromProperties(Player player)
    {
        if (player == null)
        {
            string fallbackName = $"Unknown_{photonView.ViewID}";
            nameText.text = fallbackName;
            photonView.RPC("UpdateName", RpcTarget.AllBuffered, fallbackName);
            CustomLogger.LogWarning($"NameTag: No player provided for UpdateNameFromProperties, ViewID={photonView.ViewID}, using fallback: {fallbackName}");
            return;
        }

        string currentScene = SceneManager.GetActiveScene().name;
        string nickname = null;
        if (player.CustomProperties.ContainsKey("Nickname") && !string.IsNullOrEmpty(player.CustomProperties["Nickname"]?.ToString()))
        {
            nickname = player.CustomProperties["Nickname"].ToString();
            CustomLogger.Log($"NameTag: Used Nickname {nickname} from CustomProperties for player {player.ActorNumber}");
        }
        else if (player.CustomProperties.ContainsKey("Username") && !string.IsNullOrEmpty(player.CustomProperties["Username"]?.ToString()))
        {
            nickname = player.CustomProperties["Username"].ToString();
            CustomLogger.Log($"NameTag: Used Username {nickname} from CustomProperties for player {player.ActorNumber}");
        }
        else
        {
            BotController bot = GetComponent<BotController>();
            if (bot != null)
            {
                BotController[] bots = Object.FindObjectsByType<BotController>(FindObjectsSortMode.None).OrderBy(b => b.ActorNumber).ToArray();
                int botIndex = System.Array.IndexOf(bots, bot) + 1;
                nickname = $"Bot_{botIndex}";
            }
            else
            {
                nickname = PlayerPrefs.GetString("PlayerUsername", $"Player_{player.ActorNumber}");
            }
            PhotonHashtable props = new PhotonHashtable { { "Nickname", nickname }, { "Username", nickname } };
            player.SetCustomProperties(props);
            CustomLogger.Log($"NameTag: Fell back to {nickname} for player {player.ActorNumber}");
        }

        nameText.text = nickname;
        photonView.RPC("UpdateName", RpcTarget.AllBuffered, nickname);
        CustomLogger.Log($"NameTag: Updated remote player {photonView.ViewID} with name: {nickname} in scene {currentScene}");
    }

    [PunRPC]
    void UpdateName(string newName)
    {
        if (nameText != null)
        {
            nameText.text = newName;
            CustomLogger.Log($"NameTag: Updated name to {newName} for ViewID={photonView.ViewID}");
        }
        else
        {
            CustomLogger.LogError($"NameTag: Cannot update name for ViewID={photonView.ViewID}, nameText is null");
        }
    }

    public void ShowCrown()
    {
        if (crownImage == null)
        {
            CustomLogger.LogWarning($"NameTag: Cannot show crown for ViewID={photonView.ViewID}, crownImage is null");
            return;
        }

        ScoreboardManager scoreboardManager = FindFirstObjectByType<ScoreboardManager>();
        if (scoreboardManager != null && scoreboardManager.GetCrownSprite() != null)
        {
            crownImage.sprite = scoreboardManager.GetCrownSprite();
        }
        else
        {
            // Fallback: Create a default sprite if none is assigned
            Texture2D tex = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            tex.SetPixels(new Color[] { Color.yellow, Color.yellow, Color.yellow, Color.yellow });
            tex.Apply();
            Sprite fallbackSprite = Sprite.Create(tex, new Rect(0, 0, 2, 2), new Vector2(0.5f, 0.5f));
            crownImage.sprite = fallbackSprite;
            CustomLogger.LogWarning($"NameTag: Using fallback yellow sprite for crown on {nameText.text} (ViewID={photonView.ViewID}) due to missing ScoreboardManager or CrownSprite");
        }

        crownImage.gameObject.SetActive(true);
        CustomLogger.Log($"NameTag: Showed crown for {nameText.text} (ViewID={photonView.ViewID})");
    }

    public void HideCrown()
    {
        if (crownImage != null)
        {
            crownImage.gameObject.SetActive(false);
            CustomLogger.Log($"NameTag: Hid crown for {nameText.text} (ViewID={photonView.ViewID})");
        }
    }
}