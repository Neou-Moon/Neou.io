using UnityEngine;
using UnityEngine.UI;
using TMPro;
using Photon.Pun;
using Photon.Realtime;
using System.Collections;
using System.Linq;
using PhotonHashtable = ExitGames.Client.Photon.Hashtable;

public class LaserBeam : MonoBehaviourPunCallbacks
{
    public LineRenderer lineRenderer;
    public LayerMask hitLayers;
    public float laserRange = 50f;
    public Transform player;
    public Vector3 offset = new Vector3(0f, 1f, 0f);
    public Slider energyBar1;
    public TextMeshProUGUI chargingText;
    public float rechargeTime = 10f;
    public float jamDuration = 1f;
    public ParticleSystem jamEffect;
    public float laserGrowSpeed = 0.05f;

    private bool isLaserFiring;
    private Vector2 laserDirection = Vector2.right;
    private Vector2 lastValidDirection = Vector2.right;
    private bool isJammed;
    public bool IsJammed => isJammed;
    private float rechargeProgress;
    private PlayerHealth playerHealth;
    private BotController botController;
    private bool isBot;
    private float botEnergy = 1f;
    private int teamID = -1;

    public bool IsLaserFiring => isLaserFiring;

    [SerializeField] private SpriteRenderer flagIconRenderer;
    [SerializeField] private Sprite[] availableFlags; // Array of 141 flag sprites
    [SerializeField] private Sprite defaultFlagSprite;

    // Country names array for logging
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

    void Start()
    {
        if (!photonView.IsMine)
        {
            enabled = false;
            Debug.Log($"LaserBeam: Disabled for {gameObject.name}, photonView.IsMine=false.");
            return;
        }

        playerHealth = GetComponentInParent<PlayerHealth>();
        botController = GetComponentInParent<BotController>();
        isBot = botController != null;

        if (player == null)
        {
            player = GetComponentInParent<Transform>();
            if (player == null || !player.CompareTag(isBot ? "Bot" : "Player"))
            {
                Debug.LogError($"LaserBeam: Could not find {(isBot ? "Bot" : "Player")} Transform on {player.name}.");
                return;
            }
        }

        if (!isBot)
        {
            StartCoroutine(InitializeUIWithRetry());
        }
        else
        {
            Debug.Log($"LaserBeam: Skipping UI initialization for bot {gameObject.name}. Using internal energy system.");
        }

        if (lineRenderer == null)
        {
            Debug.LogError($"LaserBeam: lineRenderer is null on {gameObject.name}.");
            return;
        }

        if (flagIconRenderer == null)
        {
            Debug.LogError($"LaserBeam: flagIconRenderer is null on {gameObject.name}. Ensure FlagIcon child is assigned in Player/Bot prefab.");
            return;
        }

        // Ensure flagIconRenderer has a valid material to prevent blackout for players
        if (!isBot && (flagIconRenderer.material == null || flagIconRenderer.material.shader == null))
        {
            flagIconRenderer.material = new Material(Shader.Find("Sprites/Default"));
            Debug.Log($"LaserBeam: Assigned default sprite material to flagIconRenderer on {gameObject.name}.");
        }

        // Disable flag rendering for bots
        if (isBot)
        {
            flagIconRenderer.enabled = false;
            Debug.Log($"LaserBeam: Flag icon disabled for bot {gameObject.name}.");
        }

        lineRenderer.enabled = false;
        if (!isBot) UpdateFlagIcon();
        Debug.Log($"LaserBeam: Initialized on {gameObject.name} with rechargeTime={rechargeTime}, ViewID={photonView.ViewID}, isBot={isBot}");
    }

    private void UpdateFlagIcon()
    {
        if (isBot)
        {
            Debug.Log($"LaserBeam: Skipping flag icon update for bot {gameObject.name}.");
            return;
        }

        if (flagIconRenderer == null)
        {
            Debug.LogWarning($"LaserBeam: flagIconRenderer is null on {gameObject.name}, cannot update flag icon.");
            return;
        }

        if (availableFlags == null || availableFlags.Length != 141)
        {
            Debug.LogWarning($"LaserBeam: availableFlags is null or has {availableFlags?.Length} sprites, expected 141 on {gameObject.name}, using default sprite.");
            flagIconRenderer.sprite = defaultFlagSprite;
            flagIconRenderer.transform.localScale = new Vector3(0.5f, 0.5f, 1f); // Default sprite scale
            flagIconRenderer.enabled = true;
            Debug.Log($"LaserBeam: Updated flag icon on {gameObject.name} to default sprite, scale=(0.5, 0.5, 1)");
            return;
        }

        int flagIndex = -1;
        if (photonView.Owner != null && photonView.Owner.CustomProperties.ContainsKey("FlagIndex"))
        {
            flagIndex = (int)photonView.Owner.CustomProperties["FlagIndex"];
        }

        flagIconRenderer.sprite = flagIndex >= 0 && flagIndex < availableFlags.Length ? availableFlags[flagIndex] : defaultFlagSprite;

        // Set flag scale: 1.5f (0.5 * 3) for Macau (index 70), 1.3f (0.5 * 2.6) for Angola (index 2), 1f (0.5 * 2) for other valid flags, 0.5f for default
        float flagScale = flagIndex == 70 ? 1.5f : flagIndex == 2 ? 1.3f : (flagIndex >= 0 && flagIndex < availableFlags.Length) ? 1f : 0.5f;
        flagIconRenderer.transform.localScale = new Vector3(flagScale, flagScale, 1f);

        flagIconRenderer.enabled = true; // Ensure renderer is enabled for players
        string countryName = flagIndex >= 0 && flagIndex < countryNames.Length ? countryNames[flagIndex] : "Default";
        Debug.Log($"LaserBeam: Updated flag icon on {gameObject.name} to index={flagIndex}, sprite={flagIconRenderer.sprite?.name}, country={countryName}, scale=({flagScale}, {flagScale}, 1)");
    }

    private IEnumerator InitializeUIWithRetry()
    {
        int maxRetries = 10;
        int retryCount = 0;
        const float retryDelay = 0.5f;

        yield return new WaitForSeconds(1f);

        while (retryCount < maxRetries)
        {
            Canvas playerCanvas = null;
            Canvas[] canvases = FindObjectsByType<Canvas>(FindObjectsSortMode.None);
            foreach (var canvas in canvases)
            {
                if (canvas.name.Contains("Player Canvas") && canvas.transform.IsChildOf(transform.root))
                {
                    playerCanvas = canvas;
                    break;
                }
            }

            if (playerCanvas != null)
            {
                Debug.Log($"LaserBeam: Found Player Canvas at {GetGameObjectPath(playerCanvas.gameObject)}.");

                if (energyBar1 == null)
                {
                    Slider[] sliders = playerCanvas.GetComponentsInChildren<Slider>(true);
                    Debug.Log($"LaserBeam: Found {sliders.Length} Sliders in Player Canvas: {string.Join(", ", sliders.Select(s => s.name))}");
                    foreach (var slider in sliders)
                    {
                        if (slider.name == "LaserBar")
                        {
                            energyBar1 = slider;
                            Debug.Log($"LaserBeam: Found LaserBar in Player Canvas on {gameObject.name}.");
                            break;
                        }
                    }
                }

                if (chargingText == null)
                {
                    TextMeshProUGUI[] texts = playerCanvas.GetComponentsInChildren<TextMeshProUGUI>(true);
                    Debug.Log($"LaserBeam: Found {texts.Length} TextMeshProUGUI in Player Canvas: {string.Join(", ", texts.Select(t => t.name))}");
                    foreach (var text in texts)
                    {
                        if (text.name == "LaserChargeText")
                        {
                            chargingText = text;
                            Debug.Log($"LaserBeam: Found LaserChargeText in Player Canvas on {gameObject.name}.");
                            break;
                        }
                    }
                }

                bool isValid = energyBar1 != null && chargingText != null;
                Debug.Log($"LaserBeam: UI validation on retry {retryCount + 1}/{maxRetries}: energyBar1={(energyBar1 != null ? $"found (name={energyBar1.name})" : "null")}, chargingText={(chargingText != null ? $"found (name={chargingText.name})" : "null")}, valid={isValid}");

                if (isValid)
                {
                    energyBar1.value = 1f;
                    Debug.Log($"LaserBeam: Initialized energyBar1.value=1f on {gameObject.name}.");
                    UpdateChargingText();
                    yield break;
                }
                else
                {
                    Debug.LogWarning($"LaserBeam: Missing UI elements on retry {retryCount + 1}/{maxRetries}: energyBar1={(energyBar1 != null ? "found" : "null")}, chargingText={(chargingText != null ? "found" : "null")}.");
                }
            }
            else
            {
                Debug.LogWarning($"LaserBeam: Player Canvas not found in {GetGameObjectPath(gameObject)} on retry {retryCount + 1}/{maxRetries}. Found {canvases.Length} canvases: {string.Join(", ", canvases.Select(c => GetGameObjectPath(c.gameObject)))}");
            }

            retryCount++;
            yield return new WaitForSeconds(retryDelay);
        }

        Debug.LogError($"LaserBeam: Failed to initialize UI after {maxRetries} retries: energyBar1={(energyBar1 != null ? "found" : "null")}, chargingText={(chargingText != null ? "found" : "null")}.");
        if (energyBar1 == null)
            Debug.LogError($"LaserBeam: Could not find Slider named 'LaserBar' in Player Canvas on {gameObject.name}.");
        if (chargingText == null)
            Debug.LogError($"LaserBeam: Could not find TextMeshProUGUI named 'LaserChargeText' in Player Canvas on {gameObject.name}.");
    }

    void Update()
    {
        if (!photonView.IsMine)
            return;

        if (player != null)
        {
            transform.position = player.position + offset;
            transform.rotation = player.rotation;
        }
        else
        {
            Debug.LogWarning($"LaserBeam: player is null on {gameObject.name}.");
            return;
        }

        // Recharge energy
        if (!IsJammed)
        {
            rechargeProgress += Time.deltaTime / rechargeTime;
            if (isBot)
            {
                botEnergy = Mathf.Clamp01(rechargeProgress);
                Debug.Log($"LaserBeam: Bot {gameObject.name} recharging, rechargeTime={rechargeTime}, rechargeProgress={rechargeProgress:F2}, botEnergy={botEnergy:F2}");
            }
            else if (energyBar1 != null)
            {
                energyBar1.value = Mathf.Clamp01(rechargeProgress);
                UpdateChargingText();
                Debug.Log($"LaserBeam: Player {gameObject.name} recharging, rechargeTime={rechargeTime}, rechargeProgress={rechargeProgress:F2}, energyBar1.value={energyBar1.value:F2}");
            }
        }

        // Update charging text color for players
        if (!isBot && energyBar1 != null && energyBar1.value < 1f && chargingText != null)
        {
            float t = Mathf.PingPong(Time.time * 2f, 1f);
            chargingText.color = Color.Lerp(Color.white, Color.red, t);
        }

        // Update laser direction for players
        if (!isBot && botController == null)
        {
            Vector2 newDirection = Vector2.zero;
            if (Input.GetKey(KeyCode.UpArrow)) newDirection += Vector2.up;
            if (Input.GetKey(KeyCode.DownArrow)) newDirection += Vector2.down;
            if (Input.GetKey(KeyCode.LeftArrow)) newDirection += Vector2.left;
            if (Input.GetKey(KeyCode.RightArrow)) newDirection += Vector2.right;
            if (newDirection != Vector2.zero) laserDirection = newDirection.normalized;

            float angle = Mathf.Atan2(laserDirection.y, laserDirection.x) * Mathf.Rad2Deg;
            transform.rotation = Quaternion.Euler(0, 0, angle);
        }

        // Debug gun visibility
        SpriteRenderer gunRenderer = GetComponent<SpriteRenderer>();
        if (gunRenderer != null)
        {
            Debug.Log($"LaserBeam: Gun visibility check, enabled={gunRenderer.enabled}, sprite={gunRenderer.sprite?.name}");
        }
    }

    public void SetJoystickInput(Vector2 input)
    {
        if (!photonView.IsMine || isBot || botController != null) return;

        if (input.magnitude > 0.1f)
        {
            laserDirection = input.normalized;
            lastValidDirection = laserDirection;
            float angle = Mathf.Atan2(laserDirection.y, laserDirection.x) * Mathf.Rad2Deg;
            transform.rotation = Quaternion.Euler(0, 0, angle);
            Debug.Log($"LaserBeam: Joystick input set laserDirection={laserDirection}, angle={angle:F2} for {gameObject.name}");
        }
        else
        {
            laserDirection = lastValidDirection;
            Debug.Log($"LaserBeam: Joystick input zero, using lastValidDirection={lastValidDirection} for {gameObject.name}");
        }
    }

    public void TryFireLaser()
    {
        if (!photonView.IsMine || isLaserFiring || IsJammed || (isBot ? botEnergy < 1f : energyBar1 == null || energyBar1.value < 1f))
        {
            Debug.Log($"LaserBeam: TryFireLaser failed for {gameObject.name}, IsMine={photonView.IsMine}, IsFiring={isLaserFiring}, IsJammed={IsJammed}, Energy={(isBot ? botEnergy.ToString("F2") : energyBar1 != null ? energyBar1.value.ToString("F2") : "null")}");
            return;
        }

        if (botController != null && botController.HasDied)
        {
            Debug.Log($"LaserBeam: TryFireLaser skipped for {gameObject.name}, bot HasDied={botController.HasDied}");
            return;
        }
        if (playerHealth != null && playerHealth.HasDied)
        {
            Debug.Log($"LaserBeam: TryFireLaser skipped for {gameObject.name}, player HasDied={playerHealth.HasDied}");
            return;
        }

        Debug.Log($"LaserBeam: TryFireLaser started for {gameObject.name}, laserDirection={laserDirection}");
        StartCoroutine(ShootLaser());
    }

    public void TriggerLaser(Vector3? targetPosition = null)
    {
        if (!photonView.IsMine || isLaserFiring || IsJammed || (isBot ? botEnergy < 1f : energyBar1 == null || energyBar1.value < 1f))
        {
            Debug.Log($"LaserBeam: TriggerLaser failed for {gameObject.name}, IsMine={photonView.IsMine}, IsFiring={isLaserFiring}, IsJammed={IsJammed}, Energy={(isBot ? botEnergy.ToString("F2") : energyBar1 != null ? energyBar1.value.ToString("F2") : "null")}");
            return;
        }

        if (botController != null && botController.HasDied)
        {
            Debug.Log($"LaserBeam: TriggerLaser skipped for {gameObject.name}, bot HasDied={botController.HasDied}");
            return;
        }
        if (playerHealth != null && playerHealth.HasDied)
        {
            Debug.Log($"LaserBeam: TriggerLaser skipped for {gameObject.name}, player HasDied={playerHealth.HasDied}");
            return;
        }

        if (targetPosition.HasValue && botController != null)
        {
            Vector2 targetDir = (targetPosition.Value - transform.position).normalized;
            laserDirection = targetDir;
            float angle = Mathf.Atan2(laserDirection.y, laserDirection.x) * Mathf.Rad2Deg;
            transform.rotation = Quaternion.Euler(0, 0, angle);
            Debug.Log($"LaserBeam: {gameObject.name} set laser direction to target {targetPosition.Value}, direction={laserDirection}, angle={angle:F2}");
        }

        StartCoroutine(ShootLaser());
    }

    IEnumerator ShootLaser()
    {
        Debug.Log($"LaserBeam: Firing laser on {gameObject.name}, rechargeTime={rechargeTime}.");
        isLaserFiring = true;
        lineRenderer.enabled = true;
        if (isBot)
        {
            botEnergy = 0f;
        }
        else if (energyBar1 != null)
        {
            energyBar1.value = 0f;
        }
        rechargeProgress = 0f;
        if (!isBot) UpdateChargingText();

        Vector2 startPos = transform.position;
        Vector2 endPos = startPos + laserDirection * laserRange;
        float progress = 0f;
        RaycastHit2D hit = Physics2D.Raycast(startPos, laserDirection, laserRange, hitLayers);
        if (hit.collider != null)
        {
            endPos = hit.point;
        }

        while (progress < 1f && !IsJammed)
        {
            progress += Time.deltaTime / laserGrowSpeed;
            Vector2 currentPos = Vector2.Lerp(startPos, endPos, progress);
            lineRenderer.SetPosition(0, startPos);
            lineRenderer.SetPosition(1, currentPos);
            yield return null;
        }

        if (hit.collider != null)
        {
            if (hit.collider.CompareTag("Planet"))
            {
                hit.collider.GetComponent<Planet>()?.SplitIntoHalves();
                TriggerJam();
            }
            else if (hit.collider.CompareTag("halfPlanet"))
            {
                hit.collider.GetComponent<PlanetHalf>()?.SplitIntoQuarters();
                TriggerJam();
            }
            else if (hit.collider.CompareTag("quarterPlanet"))
            {
                Destroy(hit.collider.gameObject);
                TriggerJam();
            }
        }

        StopLaser();
    }

    void StopLaser()
    {
        Debug.Log($"LaserBeam: Stopping laser on {gameObject.name}, rechargeTime={rechargeTime}.");
        lineRenderer.enabled = false;
        isLaserFiring = false;
    }

    public void TriggerJam()
    {
        if (!IsJammed)
        {
            Debug.Log($"LaserBeam: Laser jammed on {gameObject.name}.");
            isJammed = true;
            if (jamEffect != null)
            {
                Instantiate(jamEffect, transform.position, Quaternion.identity);
            }
            else
            {
                Debug.LogWarning($"LaserBeam: jamEffect is null on {gameObject.name}.");
            }
            lineRenderer.enabled = false;
            Invoke(nameof(ResetJam), jamDuration);
        }
    }

    void ResetJam()
    {
        Debug.Log($"LaserBeam: Jam cleared on {gameObject.name}.");
        isJammed = false;
    }

    void UpdateChargingText()
    {
        if (chargingText != null)
        {
            chargingText.gameObject.SetActive(energyBar1 != null && energyBar1.value < 1f);
            chargingText.text = "Laser Charging";
        }
    }

    private string GetGameObjectPath(GameObject obj)
    {
        string path = "/" + obj.name;
        while (obj.transform.parent != null)
        {
            obj = obj.transform.parent.gameObject;
            path = "/" + obj.name + path;
        }
        return path;
    }

    public void SetRechargeTime(float newRechargeTime)
    {
        if (newRechargeTime < 1f)
        {
            Debug.LogWarning($"LaserBeam: Attempted to set rechargeTime={newRechargeTime} on {gameObject.name}, clamping to 1f to prevent division by zero.");
            newRechargeTime = 1f;
        }
        rechargeTime = newRechargeTime;
        Debug.Log($"LaserBeam: Set rechargeTime={rechargeTime} on {gameObject.name}, ViewID={photonView.ViewID}");
    }

    public void SetTeamID(int newTeamID)
    {
        teamID = newTeamID;
        Debug.Log($"LaserBeam: Set TeamID={teamID}, ViewID={photonView.ViewID}");
    }

    public override void OnPlayerPropertiesUpdate(Player targetPlayer, PhotonHashtable changedProps)
    {
        if (!isBot && photonView.IsMine && targetPlayer == photonView.Owner && changedProps.ContainsKey("FlagIndex"))
        {
            UpdateFlagIcon();
            Debug.Log($"LaserBeam: Flag index updated for {gameObject.name}, new FlagIndex={changedProps["FlagIndex"]}");
        }
    }
}