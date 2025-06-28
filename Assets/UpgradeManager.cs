using UnityEngine;
using UnityEngine.UI;
using TMPro;
using Photon.Pun;
using UnityEngine.SceneManagement;
using System.Collections;

public class UpgradeManager : MonoBehaviourPunCallbacks
{
    [SerializeField] public GameObject upgradePanel;
    [SerializeField] private Button[] upgradeButtons = new Button[9];
    [SerializeField] private TextMeshProUGUI[] upgradeLabels = new TextMeshProUGUI[9];
    [SerializeField] private TextMeshProUGUI[] levelLabels = new TextMeshProUGUI[9];
    [SerializeField] private TextMeshProUGUI brightMatterText;
    [SerializeField] private TextMeshProUGUI timerText;
    [SerializeField] private PlayerController playerController;
    [SerializeField] private BrightMatterDisplay brightMatterDisplay;
    [SerializeField] private Button toggleUpgradeButton; // New button for toggling upgrade panel

    private readonly int[] upgradeLevels = new int[9];
    private int lastBrightMatterTrigger;
    private readonly int[] upgradeCosts = { 5, 15, 40, 70, 100 };
    private readonly string[] upgradeNames = {
        "Shield Recharge", "Shield Duration", "Laser Recharge",
        "Fuel Absorption", "Bullet Speed", "Turret Duration",
        "Damage Resist", "Teleport Fuel", "Bullet Damage"
    };
    private readonly int maxLevel = 5;
    private bool isPanelActive;
    private bool isAddingBrightMatter;
    private Coroutine autoHideCoroutine;

    public int[] UpgradeLevels => upgradeLevels;
    public int[] UpgradeCosts => upgradeCosts;

    void Start()
    {
        if (!photonView.IsMine)
        {
            if (upgradePanel != null)
                upgradePanel.SetActive(false);
            enabled = false;
            Debug.Log($"UpgradeManager: Disabled for {gameObject.name}, photonView.IsMine=false.");
            return;
        }

        // Find PlayerUI Canvas with retry
        GameObject playerUICanvas = null;
        float waitTime = 0f;
        const float maxWaitTime = 5f;
        while (waitTime < maxWaitTime && playerUICanvas == null)
        {
            playerUICanvas = GameObject.Find("PlayerUI");
            if (playerUICanvas == null)
            {
                Canvas[] canvases = Object.FindObjectsByType<Canvas>(FindObjectsSortMode.None);
                foreach (var canvas in canvases)
                {
                    if (canvas.name.Contains("PlayerUI"))
                    {
                        playerUICanvas = canvas.gameObject;
                        Debug.Log($"UpgradeManager: Found PlayerUI canvas named {canvas.name}.");
                        break;
                    }
                }
            }
            if (playerUICanvas == null)
            {
                Debug.Log($"UpgradeManager: PlayerUI canvas not found, waiting. Elapsed time {waitTime:F2}/{maxWaitTime} seconds.");
                waitTime += 0.5f;
            }
        }

        if (playerUICanvas == null)
        {
            Debug.LogError("UpgradeManager: PlayerUI Canvas not found in scene after retries. Ensure a canvas named 'PlayerUI' exists.");
            return;
        }

        // Find UpgradePanel
        if (upgradePanel == null)
        {
            Canvas[] childCanvases = playerUICanvas.GetComponentsInChildren<Canvas>(true);
            foreach (var canvas in childCanvases)
            {
                if (canvas.name.Contains("UpgradePanel"))
                {
                    upgradePanel = canvas.gameObject;
                    Debug.Log($"UpgradeManager: Found UpgradePanel ({canvas.name}) in PlayerUI Canvas.");
                    break;
                }
            }
            if (upgradePanel == null)
            {
                Debug.LogError("UpgradeManager: Could not find UpgradePanel in PlayerUI Canvas.");
                return;
            }
        }

        // Find UpgradeButtons
        if (upgradeButtons == null || upgradeButtons.Length != 9)
        {
            upgradeButtons = new Button[9];
            Button[] buttons = playerUICanvas.GetComponentsInChildren<Button>(true);
            for (int i = 0; i < 9 && i < buttons.Length; i++)
            {
                if (buttons[i].name.Contains($"UpgradeButton{i + 1}"))
                {
                    upgradeButtons[i] = buttons[i];
                    Debug.Log($"UpgradeManager: Found UpgradeButton{i + 1} ({buttons[i].name}).");
                }
            }
        }

        // Find UpgradeLabels
        if (upgradeLabels == null || upgradeLabels.Length != 9)
        {
            upgradeLabels = new TextMeshProUGUI[9];
            TextMeshProUGUI[] texts = playerUICanvas.GetComponentsInChildren<TextMeshProUGUI>(true);
            for (int i = 0; i < 9; i++)
            {
                foreach (var text in texts)
                {
                    if (text.name.Contains($"UpgradeLabel{i + 1}"))
                    {
                        upgradeLabels[i] = text;
                        Debug.Log($"UpgradeManager: Found UpgradeLabel{i + 1} ({text.name}).");
                        break;
                    }
                }
            }
        }

        // Find LevelLabels
        if (levelLabels == null || levelLabels.Length != 9)
        {
            levelLabels = new TextMeshProUGUI[9];
            TextMeshProUGUI[] texts = playerUICanvas.GetComponentsInChildren<TextMeshProUGUI>(true);
            for (int i = 0; i < 9; i++)
            {
                foreach (var text in texts)
                {
                    if (text.name.Contains($"LevelLabel{i + 1}"))
                    {
                        levelLabels[i] = text;
                        Debug.Log($"UpgradeManager: Found LevelLabel{i + 1} ({text.name}).");
                        break;
                    }
                }
            }
        }

        // Find BrightMatterText
        if (brightMatterText == null)
        {
            TextMeshProUGUI[] texts = playerUICanvas.GetComponentsInChildren<TextMeshProUGUI>(true);
            foreach (var text in texts)
            {
                if (text.gameObject.name == "BrightMatterText")
                {
                    brightMatterText = text;
                    Debug.Log($"UpgradeManager: Found BrightMatterText ({text.name}) in PlayerUI Canvas.");
                    break;
                }
            }
            if (brightMatterText == null)
            {
                Debug.LogWarning("UpgradeManager: Could not find BrightMatterText in PlayerUI Canvas. Checking Player Canvas.");
                GameObject[] players = GameObject.FindGameObjectsWithTag("Player");
                foreach (var player in players)
                {
                    if (player.GetComponent<PhotonView>().IsMine)
                    {
                        Canvas playerCanvas = player.GetComponentInChildren<Canvas>();
                        if (playerCanvas != null && playerCanvas.gameObject.name == "Player Canvas")
                        {
                            texts = playerCanvas.GetComponentsInChildren<TextMeshProUGUI>(true);
                            foreach (var text in texts)
                            {
                                if (text.gameObject.name == "BrightMatterText")
                                {
                                    brightMatterText = text;
                                    Debug.Log($"UpgradeManager: Found BrightMatterText in Player Canvas at {GetGameObjectPath(text.gameObject)}.");
                                    break;
                                }
                            }
                        }
                    }
                }
            }
        }

        // Find TimerText
        if (timerText == null)
        {
            TextMeshProUGUI[] texts = playerUICanvas.GetComponentsInChildren<TextMeshProUGUI>(true);
            foreach (var text in texts)
            {
                if (text.gameObject.name == "TimerText")
                {
                    timerText = text;
                    Debug.Log($"UpgradeManager: Found TimerText ({text.name}) in PlayerUI Canvas.");
                    break;
                }
            }
            if (timerText == null)
            {
                Debug.LogWarning("UpgradeManager: Could not find TimerText in PlayerUI Canvas.");
            }
        }

        // Find ToggleUpgradeButton on Player Canvas
        if (toggleUpgradeButton == null)
        {
            GameObject[] players = GameObject.FindGameObjectsWithTag("Player");
            foreach (var player in players)
            {
                if (player.GetComponent<PhotonView>().IsMine)
                {
                    Canvas playerCanvas = player.GetComponentInChildren<Canvas>();
                    if (playerCanvas != null && playerCanvas.gameObject.name == "Player Canvas")
                    {
                        Button[] buttons = playerCanvas.GetComponentsInChildren<Button>(true);
                        foreach (var button in buttons)
                        {
                            if (button.gameObject.name == "ToggleUpgradeButton")
                            {
                                toggleUpgradeButton = button;
                                Debug.Log($"UpgradeManager: Found ToggleUpgradeButton in Player Canvas at {GetGameObjectPath(button.gameObject)}.");
                                break;
                            }
                        }
                    }
                }
            }
            if (toggleUpgradeButton == null)
            {
                Debug.LogWarning("UpgradeManager: Could not find ToggleUpgradeButton in Player Canvas. Ensure a button named 'ToggleUpgradeButton' exists.");
            }
            else
            {
                toggleUpgradeButton.onClick.RemoveAllListeners();
                toggleUpgradeButton.onClick.AddListener(ToggleUpgradePanel);
            }
        }

        // Find PlayerController
        if (playerController == null)
        {
            playerController = GetComponent<PlayerController>();
            if (playerController == null)
            {
                Debug.LogError("UpgradeManager: PlayerController not assigned and not found on this GameObject.");
                return;
            }
        }

        if (brightMatterDisplay == null)
        {
            brightMatterDisplay = Object.FindFirstObjectByType<BrightMatterDisplay>();
            if (brightMatterDisplay == null)
            {
                Debug.LogWarning("UpgradeManager: BrightMatterDisplay not found in scene.");
            }
            else
            {
                Debug.Log($"UpgradeManager: Found BrightMatterDisplay in scene at {GetGameObjectPath(brightMatterDisplay.gameObject)}.");
            }
        }

        // Initialize UI
        for (int i = 0; i < 9; i++)
        {
            if (upgradeButtons[i] != null && upgradeLabels[i] != null && levelLabels[i] != null)
            {
                int index = i;
                upgradeButtons[i].onClick.RemoveAllListeners();
                upgradeButtons[i].onClick.AddListener(() => TryPurchaseUpgrade(index));
                upgradeLabels[i].text = upgradeNames[i];
                UpdateButtonUI(i);
            }
            else
            {
                Debug.LogWarning($"UpgradeManager: Skipping UI setup for index {i} due to missing Button, UpgradeLabel, or LevelLabel.");
            }
        }

        upgradePanel.SetActive(false);
        if (timerText != null)
        {
            timerText.gameObject.SetActive(false);
        }

        string currentScene = SceneManager.GetActiveScene().name;
        if (currentScene == "Moon Ran" || currentScene == "TeamMoonRan")
        {
            Debug.Log($"UpgradeManager: Detected {currentScene} scene, resetting upgrades to level 0 for player.");
            for (int i = 0; i < 9; i++)
            {
                upgradeLevels[i] = 0;
                UpdateButtonUI(i);
                if (i == 5)
                {
                    StartCoroutine(ApplyTurretDurationUpgradeDelayed());
                }
                else
                {
                    ApplyUpgradeEffect(i);
                }
            }
            PlayerPrefs.DeleteKey("UpgradeLevels");
            PlayerPrefs.DeleteKey("BrightMatter");
            PlayerPrefs.DeleteKey("InsideSpaceShip");
            playerController.SyncBrightMatter(50);
            Debug.Log($"UpgradeManager: Reset BrightMatter to 50 for player in {currentScene}.");
            SaveUpgrades();
        }
        else
        {
            if (PlayerPrefs.HasKey("UpgradeLevels"))
            {
                string[] savedLevels = PlayerPrefs.GetString("UpgradeLevels").Split(',');
                if (savedLevels.Length == 9)
                {
                    for (int i = 0; i < 9; i++)
                    {
                        upgradeLevels[i] = int.Parse(savedLevels[i]);
                        UpdateButtonUI(i);
                        if (i == 5)
                        {
                            StartCoroutine(ApplyTurretDurationUpgradeDelayed());
                        }
                        else
                        {
                            ApplyUpgradeEffect(i);
                        }
                    }
                    Debug.Log($"UpgradeManager: Loaded upgrade levels from PlayerPrefs: {string.Join(",", upgradeLevels)}");
                }
                else
                {
                    Debug.LogWarning("UpgradeManager: Invalid UpgradeLevels format in PlayerPrefs, resetting to 0.");
                    for (int i = 0; i < 9; i++)
                    {
                        upgradeLevels[i] = 0;
                        UpdateButtonUI(i);
                        if (i == 5)
                        {
                            StartCoroutine(ApplyTurretDurationUpgradeDelayed());
                        }
                        else
                        {
                            ApplyUpgradeEffect(i);
                        }
                    }
                    SaveUpgrades();
                }
            }
            else
            {
                Debug.Log("UpgradeManager: No UpgradeLevels in PlayerPrefs, initializing to 0.");
                for (int i = 0; i < 9; i++)
                {
                    upgradeLevels[i] = 0;
                    UpdateButtonUI(i);
                    if (i == 5)
                    {
                        StartCoroutine(ApplyTurretDurationUpgradeDelayed());
                    }
                    else
                    {
                        ApplyUpgradeEffect(i);
                    }
                }
                playerController.SyncBrightMatter(50);
                SaveUpgrades();
            }
        }

        lastBrightMatterTrigger = 0;
        UpdateBrightMatterUI();
        int brightMatter = playerController.GetBrightMatter();
        if (brightMatter >= 50)
        {
            isPanelActive = true;
            upgradePanel.SetActive(true);
            if (timerText != null)
            {
                timerText.gameObject.SetActive(true);
            }
            lastBrightMatterTrigger = brightMatter;
            if (autoHideCoroutine != null)
            {
                StopCoroutine(autoHideCoroutine);
            }
            autoHideCoroutine = StartCoroutine(AutoHideUpgradePanel(10f));
        }
    }

    private IEnumerator ApplyTurretDurationUpgradeDelayed()
    {
        yield return new WaitForEndOfFrame();
        TwinTurretManager turret = GetComponent<TwinTurretManager>();
        if (turret != null)
        {
            turret.twinTurretDuration = 10f + 2f * upgradeLevels[5];
            Debug.Log($"UpgradeManager: Applied Turret Duration delayed, level={upgradeLevels[5]}, twinTurretDuration={turret.twinTurretDuration}, GameObject={gameObject.name}");
        }
        else
        {
            Debug.LogWarning($"UpgradeManager: TwinTurretManager component not found for Turret Duration upgrade. Ensure TwinTurretManager is attached to {gameObject.name}.");
        }
    }

    private IEnumerator AutoHideUpgradePanel(float duration)
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
        if (isPanelActive)
        {
            isPanelActive = false;
            if (timerText != null)
            {
                timerText.gameObject.SetActive(false);
            }
            upgradePanel.SetActive(false);
            Debug.Log($"UpgradeManager: Upgrade panel auto-hidden after {duration} seconds");
        }
        autoHideCoroutine = null;
    }

    public void ToggleUpgradePanel()
    {
        if (upgradePanel != null)
        {
            isPanelActive = !isPanelActive;
            upgradePanel.SetActive(isPanelActive);
            if (timerText != null)
            {
                timerText.gameObject.SetActive(isPanelActive);
            }
            if (isPanelActive)
            {
                if (autoHideCoroutine != null)
                {
                    StopCoroutine(autoHideCoroutine);
                }
                autoHideCoroutine = StartCoroutine(AutoHideUpgradePanel(10f));
                Debug.Log("UpgradeManager: Upgrade panel activated via button, starting 10-second auto-hide timer");
            }
            else
            {
                if (autoHideCoroutine != null)
                {
                    StopCoroutine(autoHideCoroutine);
                    autoHideCoroutine = null;
                }
                Debug.Log("UpgradeManager: Upgrade panel deactivated via button");
            }
        }
    }

    void Update()
    {
        if (!photonView.IsMine) return;

        if (Input.GetKeyDown(KeyCode.U) && upgradePanel != null)
        {
            ToggleUpgradePanel();
        }

        for (int i = 0; i < 9; i++)
        {
            if (Input.GetKeyDown(KeyCode.Alpha1 + i))
            {
                TryPurchaseUpgrade(i);
            }
        }
    }

    public void TryPurchaseUpgrade(int index)
    {
        if (upgradeLevels[index] >= maxLevel) return;

        int cost = upgradeCosts[upgradeLevels[index]];
        int currentBrightMatter = playerController?.GetBrightMatter() ?? 0;
        if (currentBrightMatter >= cost)
        {
            playerController.SyncBrightMatter(currentBrightMatter - cost);
            upgradeLevels[index]++;
            UpdateBrightMatterUI();
            UpdateButtonUI(index);
            ApplyUpgradeEffect(index);
            SaveUpgrades();

            if (PhotonNetwork.IsConnectedAndReady)
            {
                photonView.RPC("SyncUpgradeRPC", RpcTarget.All, index, upgradeLevels[index]);
            }
            Debug.Log($"UpgradeManager: Purchased {upgradeNames[index]} to level {upgradeLevels[index]}, cost={cost}, new BrightMatter={currentBrightMatter - cost}");
        }
    }

    [PunRPC]
    void SyncUpgradeRPC(int index, int level)
    {
        upgradeLevels[index] = level;
        UpdateButtonUI(index);
        ApplyUpgradeEffect(index);
        Debug.Log($"UpgradeManager: Synced {upgradeNames[index]} to level {level} for {gameObject.name}.");
    }

    void ApplyUpgradeEffect(int index)
    {
        switch (index)
        {
            case 0:
                ShockShield shield = GetComponent<ShockShield>();
                if (shield != null)
                {
                    shield.rechargeTime = 10f - upgradeLevels[0];
                    Debug.Log($"UpgradeManager: Applied Shield Recharge, level={upgradeLevels[0]}, rechargeTime={shield.rechargeTime}");
                }
                else
                {
                    Debug.LogWarning("UpgradeManager: ShockShield component not found for Shield Recharge upgrade.");
                }
                break;
            case 1:
                shield = GetComponent<ShockShield>();
                if (shield != null)
                {
                    shield.shieldDuration = 5f + upgradeLevels[1];
                    Debug.Log($"UpgradeManager: Applied Shield Duration, level={upgradeLevels[1]}, shieldDuration={shield.shieldDuration}");
                }
                else
                {
                    Debug.LogWarning("UpgradeManager: ShockShield component not found for Shield Duration upgrade.");
                }
                break;
            case 2:
                LaserBeam laser = GetComponentInChildren<LaserBeam>();
                if (laser != null)
                {
                    laser.SetRechargeTime(10f - upgradeLevels[2]);
                    Debug.Log($"UpgradeManager: Applied Laser Recharge, level={upgradeLevels[2]}, rechargeTime={10f - upgradeLevels[2]}");
                }
                else
                {
                    Debug.LogWarning("UpgradeManager: LaserBeam component not found for Laser Recharge upgrade.");
                }
                break;
            case 3:
                PlayerFuel fuel = GetComponent<PlayerFuel>();
                if (fuel != null)
                {
                    Debug.Log($"UpgradeManager: Applied Fuel Absorption, level={upgradeLevels[3]}");
                }
                else
                {
                    Debug.LogWarning("UpgradeManager: PlayerFuel component not found for Fuel Absorption upgrade.");
                }
                break;
            case 4:
                DroidShooting shooting = GetComponentInChildren<DroidShooting>();
                if (shooting != null)
                {
                    shooting.projectileSpeed = 100f + 8f * upgradeLevels[4];
                    Debug.Log($"UpgradeManager: Applied Bullet Speed, level={upgradeLevels[4]}, projectileSpeed={shooting.projectileSpeed}");
                }
                else
                {
                    Debug.LogWarning("UpgradeManager: DroidShooting component not found for Bullet Speed upgrade.");
                }
                break;
            case 5:
                break;
            case 6:
                PlayerHealth health = GetComponent<PlayerHealth>();
                if (health != null)
                {
                    health.damageReductionLevel = upgradeLevels[6];
                    Debug.Log($"UpgradeManager: Applied Damage Resist, level={upgradeLevels[6]}, damageReductionLevel={health.damageReductionLevel}");
                }
                else
                {
                    Debug.LogWarning("UpgradeManager: PlayerHealth component not found for Damage Resist upgrade.");
                }
                break;
            case 7:
                PhasingTeleportation teleport = GetComponent<PhasingTeleportation>();
                if (teleport != null)
                {
                    teleport.fuelCostPerTeleport = 10f - upgradeLevels[7];
                    Debug.Log($"UpgradeManager: Applied Teleport Fuel, level={upgradeLevels[7]}, fuelCostPerTeleport={teleport.fuelCostPerTeleport}");
                }
                else
                {
                    Debug.LogWarning("UpgradeManager: PhasingTeleportation component not found for Teleport Fuel upgrade.");
                }
                break;
            case 8:
                shooting = GetComponentInChildren<DroidShooting>();
                if (shooting != null)
                {
                    shooting.bulletDamage = 5f + 5f * upgradeLevels[8];
                    Debug.Log($"UpgradeManager: Applied Bullet Damage, level={upgradeLevels[8]}, bulletDamage={shooting.bulletDamage}");
                }
                else
                {
                    Debug.LogWarning("UpgradeManager: DroidShooting component not found for Bullet Damage upgrade.");
                }
                break;
        }
    }

    void UpdateButtonUI(int index)
    {
        if (levelLabels[index] != null && upgradeButtons[index] != null)
        {
            int brightMatter = playerController?.GetBrightMatter() ?? 0;
            levelLabels[index].text = $"{upgradeLevels[index]}/{maxLevel}";
            upgradeButtons[index].interactable = upgradeLevels[index] < maxLevel && brightMatter >= upgradeCosts[upgradeLevels[index]];
        }
    }

    void UpdateBrightMatterUI()
    {
        int brightMatter = playerController?.GetBrightMatter() ?? 0;
        if (brightMatterText != null)
        {
            brightMatterText.text = $"BrightMatter: {brightMatter}";
        }
        if (brightMatterDisplay != null)
        {
            brightMatterDisplay.UpdateBrightMatter(brightMatter);
        }
        for (int i = 0; i < 9; i++)
        {
            UpdateButtonUI(i);
        }
    }

    public void SyncBrightMatter(int amount)
    {
        if (isAddingBrightMatter) return;
        isAddingBrightMatter = true;
        Debug.Log($"UpgradeManager: SyncBrightMatter({amount}), Current={playerController?.GetBrightMatter() ?? 0}, ViewID={photonView.ViewID}");
        playerController.SyncBrightMatter(amount);
        UpdateBrightMatterUI();
        if (amount >= 50 && lastBrightMatterTrigger < 50 && upgradePanel != null)
        {
            isPanelActive = true;
            upgradePanel.SetActive(true);
            if (timerText != null)
            {
                timerText.gameObject.SetActive(true);
            }
            lastBrightMatterTrigger = amount;
            if (autoHideCoroutine != null)
            {
                StopCoroutine(autoHideCoroutine);
            }
            autoHideCoroutine = StartCoroutine(AutoHideUpgradePanel(10f));
        }
        isAddingBrightMatter = false;
    }

    public void AddBrightMatter(int amount)
    {
        if (amount == 0 || isAddingBrightMatter) return;
        isAddingBrightMatter = true;
        Debug.Log($"UpgradeManager: AddBrightMatter({amount}), Current={playerController?.GetBrightMatter() ?? 0}, ViewID={photonView.ViewID}");
        playerController.AddBrightMatter(amount);
        UpdateBrightMatterUI();
        int newBrightMatter = playerController.GetBrightMatter();
        if (newBrightMatter >= 50 && lastBrightMatterTrigger < 50 && upgradePanel != null)
        {
            isPanelActive = true;
            upgradePanel.SetActive(true);
            if (timerText != null)
            {
                timerText.gameObject.SetActive(true);
            }
            lastBrightMatterTrigger = newBrightMatter;
            if (autoHideCoroutine != null)
            {
                StopCoroutine(autoHideCoroutine);
            }
            autoHideCoroutine = StartCoroutine(AutoHideUpgradePanel(10f));
        }
        isAddingBrightMatter = false;
    }

    void SaveUpgrades()
    {
        string levels = string.Join(",", upgradeLevels);
        PlayerPrefs.SetString("UpgradeLevels", levels);
        if (playerController != null)
        {
            PlayerPrefs.SetInt("BrightMatter", playerController.GetBrightMatter());
        }
        PlayerPrefs.Save();
        Debug.Log($"UpgradeManager: Saved upgrades: {levels}, BrightMatter={playerController?.GetBrightMatter() ?? 0}");
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