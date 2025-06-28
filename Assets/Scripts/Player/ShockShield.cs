using UnityEngine;
using UnityEngine.UI;
using System.Collections;
using Photon.Pun;
using TMPro;

[RequireComponent(typeof(PhotonView))]
public class ShockShield : MonoBehaviourPunCallbacks
{
    [SerializeField] private GameObject shieldPrefab;
    [SerializeField] private float knockbackForce = 50f;
    [SerializeField] private float shieldMaxSize = 2.5f;
    [SerializeField] private float expansionTime = 0.5f;
    [SerializeField] private Slider energySlider;
    [SerializeField] public float rechargeTime = 10f;
    [SerializeField] public float shieldDuration = 5f;

    public bool isShieldActive;
    private CircleCollider2D shieldCollider;
    private Animator shieldAnimator;
    private SpriteRenderer shieldRenderer;
    private Vector3 originalScale;
    private float shieldTimer;
    private GameObject shieldInstance;
    private bool wasFullyDepleted;
    public bool WasFullyDepleted => wasFullyDepleted;
    private float energy = 100f;
    private TextMeshProUGUI overheatText;
    private Canvas canvas;
    private float flashTimer;
    private readonly float flashDuration = 0.5f;
    private float lastToggleAttempt = 0f;
    private const float TOGGLE_ATTEMPT_INTERVAL = 0.2f;

    void Start()
    {
        if (!photonView.IsMine)
        {
            CustomLogger.Log("ShockShield: Start skipped, photonView.IsMine is false.");
            enabled = false;
            return;
        }

        if (GetComponent<PlayerController>() == null && GetComponent<BotController>() == null)
        {
            CustomLogger.LogError($"ShockShield: Neither PlayerController nor BotController found on {gameObject.name}. Disabling script.");
            enabled = false;
            return;
        }

        if (GetComponent<PlayerController>() != null)
        {
            canvas = GetComponentInChildren<Canvas>();
            if (canvas == null || canvas.name != "Player Canvas")
            {
                CustomLogger.LogWarning($"ShockShield: Player Canvas not found or incorrectly named in Player {gameObject.name}.");
            }
            else
            {
                if (energySlider == null)
                {
                    CustomLogger.Log($"ShockShield: energySlider not assigned for {gameObject.name}, using internal energy tracking.");
                }
                else
                {
                    energySlider.maxValue = 100f;
                    energySlider.value = energy;
                    CustomLogger.Log("ShockShield: energySlider initialized, maxValue=100.");
                }
            }
        }
        else
        {
            CustomLogger.Log($"ShockShield: Bot detected on {gameObject.name}, using internal energy tracking.");
        }

        if (GetComponent<PlayerController>() != null)
        {
            InitializeOverheatText();
        }

        InstantiateShield();
        InitializeComponents();
        wasFullyDepleted = false;
    }

    private void InitializeOverheatText()
    {
        if (canvas == null)
        {
            CustomLogger.LogWarning($"ShockShield: Cannot initialize overheat text, Player Canvas not available for {gameObject.name}.");
            return;
        }

        CustomLogger.Log($"ShockShield: Canvas renderMode={canvas.renderMode}, Player rotation={transform.eulerAngles}, Canvas rotation={canvas.transform.eulerAngles}");

        GameObject textObj = new GameObject("OverheatText");
        textObj.transform.SetParent(canvas.transform);
        overheatText = textObj.AddComponent<TextMeshProUGUI>();
        overheatText.text = "Shield Overheat (< 50%)";
        overheatText.fontSize = 24;
        overheatText.color = Color.white;
        overheatText.alignment = TextAlignmentOptions.Center;
        overheatText.textWrappingMode = TMPro.TextWrappingModes.NoWrap;

        RectTransform textRect = overheatText.GetComponent<RectTransform>();
        textRect.sizeDelta = new Vector2(200, 50);
        textRect.anchoredPosition = new Vector2(400, -420f);
        textRect.rotation = Quaternion.identity;

        CustomLogger.Log($"ShockShield: OverheatText initialized at anchoredPosition={textRect.anchoredPosition}, rotation={textRect.eulerAngles}");

        overheatText.gameObject.SetActive(false);
        CustomLogger.Log($"ShockShield: Initialized OverheatText for {gameObject.name}.");
    }

    public void InstantiateShield()
    {
        if (shieldInstance != null)
        {
            CustomLogger.Log("ShockShield: Shield already instantiated, skipping.");
            return;
        }

        if (shieldPrefab == null)
        {
            shieldPrefab = Resources.Load<GameObject>("Prefabs/Shield");
            if (shieldPrefab == null)
            {
                CustomLogger.LogError($"ShockShield: shieldPrefab not assigned and failed to load from Resources/Prefabs/Shield for {gameObject.name}.");
                return;
            }
            CustomLogger.Log($"ShockShield: Loaded shieldPrefab from Resources/Prefabs/Shield for {gameObject.name}.");
        }

        if (!PhotonNetwork.IsConnected)
        {
            CustomLogger.LogError("ShockShield: Cannot instantiate shield, not connected to Photon.");
            return;
        }

        try
        {
            shieldInstance = PhotonNetwork.Instantiate("Prefabs/Shield", transform.position, Quaternion.identity);
            shieldInstance.transform.SetParent(transform);
            shieldInstance.transform.localPosition = Vector3.zero;
            shieldInstance.name = "Shield";
            CustomLogger.Log($"ShockShield: Shield instantiated at {shieldInstance.transform.position}.");

            SpriteRenderer tempRenderer = shieldInstance.GetComponent<SpriteRenderer>();
            if (tempRenderer != null)
            {
                tempRenderer.sortingLayerName = "Default";
                tempRenderer.sortingOrder = -1;
                tempRenderer.enabled = false;
                CustomLogger.Log("ShockShield: Shield SpriteRenderer set to SortingLayer=Default, Order=-1, enabled=false.");
            }
            else
            {
                CustomLogger.LogError("Shield.prefab is missing SpriteRenderer.");
            }

            PlayerController playerController = GetComponent<PlayerController>();
            BotController botController = GetComponent<BotController>();
            if (playerController != null)
            {
                playerController.shockShield = shieldInstance;
                CustomLogger.Log("ShockShield: Assigned shieldInstance to PlayerController.shockShield.");
            }
            else if (botController != null)
            {
                CustomLogger.Log("ShockShield: Shield instantiated for BotController.");
            }
            else
            {
                CustomLogger.LogError("ShockShield: Neither PlayerController nor BotController found.");
            }
        }
        catch (System.Exception e)
        {
            CustomLogger.LogError($"ShockShield: Failed to instantiate Shield: {e.Message}");
            shieldInstance = null;
        }
    }

    private void InitializeComponents()
    {
        if (shieldInstance == null)
        {
            CustomLogger.LogError("ShockShield: shieldInstance is null, cannot initialize components.");
            return;
        }

        shieldCollider = shieldInstance.GetComponent<CircleCollider2D>();
        shieldAnimator = shieldInstance.GetComponent<Animator>();
        shieldRenderer = shieldInstance.GetComponent<SpriteRenderer>();

        if (shieldRenderer == null)
            CustomLogger.LogError("ShockShield: Shield.prefab is missing SpriteRenderer.");
        else
            CustomLogger.Log("ShockShield: shieldRenderer initialized.");

        if (shieldCollider == null)
            CustomLogger.LogError("ShockShield: Shield.prefab is missing CircleCollider2D.");
        else
            CustomLogger.Log("ShockShield: shieldCollider initialized.");

        if (shieldAnimator == null)
            CustomLogger.LogError("ShockShield: Shield.prefab is missing Animator.");
        else
            CustomLogger.Log("ShockShield: shieldAnimator initialized.");

        if (shieldRenderer != null && shieldCollider != null)
        {
            shieldCollider.enabled = shieldRenderer.enabled = false;
            isShieldActive = false;
            CustomLogger.Log("ShockShield: Shield visuals disabled initially.");
        }
        originalScale = shieldInstance != null ? shieldInstance.transform.localScale : Vector3.one;
    }

    void Update()
    {
        if (!photonView.IsMine) return;

        float currentEnergy = energySlider != null ? energySlider.value : energy;

        if (isShieldActive)
        {
            shieldTimer -= Time.deltaTime;
            currentEnergy -= Time.deltaTime * (100f / shieldDuration);
            if (currentEnergy <= 0)
            {
                wasFullyDepleted = true;
                currentEnergy = 0f;
                CustomLogger.Log("ShockShield: Shield energy fully depleted, setting wasFullyDepleted=true.");
            }
            if (shieldTimer <= 0 || currentEnergy <= 0)
            {
                CustomLogger.Log("ShockShield: Shield timer or energy depleted, deactivating.");
                ToggleShield();
            }
        }
        else
        {
            currentEnergy += Time.deltaTime * (100f / rechargeTime);
            if (currentEnergy > 50f && WasFullyDepleted)
            {
                wasFullyDepleted = false;
                CustomLogger.Log("ShockShield: Energy above 50%, resetting wasFullyDepleted=false.");
                photonView.RPC("SyncOverheatState", RpcTarget.All, false);
            }
        }

        currentEnergy = Mathf.Clamp(currentEnergy, 0, 100);
        if (energySlider != null)
            energySlider.value = currentEnergy;
        else
            energy = currentEnergy;

        if (overheatText != null)
        {
            bool shouldShowOverheat = WasFullyDepleted && currentEnergy <= 50f;
            overheatText.gameObject.SetActive(shouldShowOverheat);
            if (shouldShowOverheat)
            {
                flashTimer += Time.deltaTime;
                float t = Mathf.PingPong(flashTimer / flashDuration, 1f);
                overheatText.color = Color.Lerp(Color.white, Color.red, t);
                CustomLogger.Log($"ShockShield: Overheat text active, energy={currentEnergy:F2}, flashTimer={flashTimer:F2}, color={overheatText.color}");
            }
            else
            {
                flashTimer = 0f;
                overheatText.color = Color.white;
            }

            bool newOverheatState = WasFullyDepleted && currentEnergy <= 50f;
            if (newOverheatState != overheatText.gameObject.activeSelf)
            {
                photonView.RPC("SyncOverheatState", RpcTarget.All, newOverheatState);
            }
        }
    }

    [PunRPC]
    private void SyncOverheatState(bool showOverheat)
    {
        if (overheatText != null)
        {
            overheatText.gameObject.SetActive(showOverheat);
            CustomLogger.Log($"ShockShield: Synced overheat state, showOverheat={showOverheat}");
        }
    }

    public float GetEnergy()
    {
        return energySlider != null ? energySlider.value : energy;
    }

    public void ToggleShield()
    {
        if (Time.time - lastToggleAttempt < TOGGLE_ATTEMPT_INTERVAL)
        {
            CustomLogger.Log($"ShockShield: ToggleShield ignored for {gameObject.name}, too soon after last attempt (time={Time.time:F2}, last={lastToggleAttempt:F2})");
            return;
        }
        lastToggleAttempt = Time.time;

        Debug.Log($"ShockShield: ToggleShield called for {gameObject.name}, current isShieldActive={isShieldActive}, energy={GetEnergy():F2}, time={Time.time:F2}");
        if (shieldInstance == null)
        {
            CustomLogger.LogError("ShockShield: Cannot toggle shield, shieldInstance is null.");
            return;
        }

        float currentEnergy = GetEnergy();

        if (isShieldActive)
        {
            isShieldActive = false;
        }
        else
        {
            if (WasFullyDepleted && currentEnergy <= 50f)
            {
                CustomLogger.Log($"ShockShield: Cannot activate shield for {gameObject.name}, wasFullyDepleted=true and energy={currentEnergy:F2} <= 50%, time={Time.time:F2}");
                return;
            }
            if (currentEnergy < 2f)
            {
                CustomLogger.Log($"ShockShield: Cannot activate shield for {gameObject.name}, energy={currentEnergy:F2} < 2%, time={Time.time:F2}");
                return;
            }
            isShieldActive = true;
        }

        CustomLogger.Log($"ShockShield: ToggleShield for {gameObject.name}, isShieldActive={isShieldActive}, time={Time.time:F2}");

        if (shieldRenderer != null)
        {
            shieldRenderer.enabled = isShieldActive;
            CustomLogger.Log($"ShockShield: shieldRenderer.enabled={shieldRenderer.enabled}");
        }
        if (shieldCollider != null)
        {
            shieldCollider.enabled = isShieldActive;
            CustomLogger.Log($"ShockShield: shieldCollider.enabled={shieldCollider.enabled}");
        }
        if (shieldAnimator != null)
        {
            shieldAnimator.SetBool("IsShieldActive", isShieldActive);
            CustomLogger.Log($"ShockShield: shieldAnimator SetBool IsShieldActive={isShieldActive}");
        }

        if (isShieldActive)
        {
            shieldTimer = shieldDuration;
            CustomLogger.Log("ShockShield: Starting ExpandShield coroutine.");
            StartCoroutine(ExpandShield());
        }
        else
        {
            CustomLogger.Log("ShockShield: Starting ShrinkShield coroutine.");
            StartCoroutine(ShrinkShield());
        }

        PlayerController playerController = GetComponent<PlayerController>();
        if (playerController != null)
        {
            playerController.photonView.RPC("SyncShieldState", RpcTarget.All, isShieldActive);
            CustomLogger.Log($"ShockShield: Synced shield state for PlayerController, isShieldActive={isShieldActive}, time={Time.time:F2}");
        }
    }

    [PunRPC]
    public void AbsorbProjectileOrBlast()
    {
        if (!photonView.IsMine || !isShieldActive)
        {
            CustomLogger.Log($"ShockShield: AbsorbProjectileOrBlast skipped, isMine={photonView.IsMine}, isShieldActive={isShieldActive}.");
            return;
        }

        float previousCharge = GetEnergy();
        float newEnergy = previousCharge - 1f;
        newEnergy = Mathf.Clamp(newEnergy, 0, 100);

        if (energySlider != null)
            energySlider.value = newEnergy;
        else
            energy = newEnergy;

        if (newEnergy <= 0)
        {
            wasFullyDepleted = true;
            if (energySlider != null)
                energySlider.value = 0f;
            else
                energy = 0f;
            ToggleShield();
            CustomLogger.Log($"ShockShield: Absorbed projectile/blast, charge reduced from {previousCharge:F2} to 0, shield deactivated.");
        }
        else
        {
            CustomLogger.Log($"ShockShield: Absorbed projectile/blast, charge reduced from {previousCharge:F2} to {newEnergy:F2}.");
        }
    }


   
    public void DrainEnergy(float amount)
    {
        float currentEnergy = GetEnergy();
        float newEnergy = Mathf.Max(0, currentEnergy - amount);
        if (energySlider != null)
            energySlider.value = newEnergy;
        else
            energy = newEnergy;

        if (newEnergy <= 0)
        {
            wasFullyDepleted = true;
            ToggleShield();
            CustomLogger.Log($"ShockShield: Energy drained to 0, wasFullyDepleted=true, shield deactivated.");
        }
        CustomLogger.Log($"ShockShield: Drained {amount:F2} energy, newEnergy={newEnergy:F2}.");
    }
    IEnumerator ExpandShield()
    {
        if (shieldInstance == null || shieldCollider == null || shieldRenderer == null)
        {
            CustomLogger.LogError("ShockShield: Cannot expand shield, shieldInstance, shieldCollider, or shieldRenderer is null.");
            yield break;
        }

        CustomLogger.Log($"ShockShield: ExpandShield started, originalScale={originalScale}, targetScale={originalScale * shieldMaxSize}");
        float elapsed = 0f;
        Vector3 targetScale = originalScale * shieldMaxSize;
        shieldRenderer.sortingOrder = -1;

        while (elapsed < expansionTime)
        {
            elapsed += Time.deltaTime;
            float progress = elapsed / expansionTime;
            shieldInstance.transform.localScale = Vector3.Lerp(originalScale, targetScale, progress);
            shieldCollider.radius = Mathf.Lerp(0.1f, targetScale.x / 2, progress);
            yield return null;
        }
        CustomLogger.Log($"ShockShield: ExpandShield completed, finalScale={shieldInstance.transform.localScale}");
    }

    IEnumerator ShrinkShield()
    {
        if (shieldInstance == null || shieldCollider == null || shieldRenderer == null)
        {
            CustomLogger.LogError("ShockShield: Cannot shrink shield, shieldInstance, shieldCollider, or shieldRenderer is null.");
            yield break;
        }

        CustomLogger.Log($"ShockShield: ShrinkShield started, startScale={shieldInstance.transform.localScale}, targetScale={originalScale}");
        float elapsed = 0f;
        Vector3 startScale = shieldInstance.transform.localScale;
        Color originalColor = shieldRenderer.color;
        shieldRenderer.sortingOrder = -2;

        while (elapsed < expansionTime)
        {
            elapsed += Time.deltaTime;
            float progress = elapsed / expansionTime;
            shieldInstance.transform.localScale = Vector3.Lerp(startScale, originalScale, progress);
            shieldCollider.radius = Mathf.Lerp(startScale.x / 2, 0.1f, progress);

            float flashT = Mathf.PingPong(elapsed / (expansionTime / 4), 1f);
            Color flashColor = Color.Lerp(originalColor, new Color(1f, 1f, 1f, 0.5f), flashT);
            shieldRenderer.color = flashColor;

            yield return null;
        }

        shieldInstance.transform.localScale = originalScale;
        shieldCollider.radius = 0.1f;
        shieldRenderer.color = originalColor;
        shieldRenderer.sortingOrder = -1;
        CustomLogger.Log($"ShockShield: ShrinkShield completed, finalScale={shieldInstance.transform.localScale}, color={shieldRenderer.color}");
    }

    void OnTriggerEnter2D(Collider2D other)
    {
        if (!isShieldActive || !photonView.IsMine) return;

        switch (other.tag)
        {
            case "ElephantBomb":
                ElephantBomb bomb = other.GetComponent<ElephantBomb>();
                if (bomb != null)
                {
                    PhotonView bombView = other.GetComponent<PhotonView>();
                    if (bombView != null && bombView.ControllerActorNr != photonView.ControllerActorNr)
                    {
                        bomb.ForceExplode();
                        CustomLogger.Log($"ShockShield: Triggered explosion for {other.name} (tag={other.tag}), ownerViewID={bombView.ControllerActorNr}, shieldOwnerViewID={photonView.ControllerActorNr}.");
                    }
                    else
                    {
                        Rigidbody2D bombRb = other.GetComponent<Rigidbody2D>();
                        if (bombRb != null)
                        {
                            Vector2 knockbackDirection = (other.transform.position - shieldInstance.transform.position).normalized;
                            bombRb.linearVelocity = knockbackDirection * 100f;
                            CustomLogger.Log($"ShockShield: Knocked back owner's {other.name} (tag={other.tag}) with speed=100, ownerViewID={bombView?.ControllerActorNr}.");
                        }
                        else
                        {
                            CustomLogger.LogError($"ShockShield: {other.name} (tag={other.tag}) has no Rigidbody2D for knockback.");
                        }
                    }
                }
                else
                {
                    CustomLogger.LogError($"ShockShield: {other.name} has ElephantBomb tag but no ElephantBomb component.");
                }
                break;

            case "Player":
            case "Bot":
                PhotonView otherView = other.GetComponent<PhotonView>();
                if (otherView != null && otherView.ControllerActorNr == photonView.ControllerActorNr)
                {
                    CustomLogger.Log($"ShockShield: Ignored knockback for owner {other.name} (ViewID={otherView.ViewID}).");
                    break;
                }

                Rigidbody2D rbPlayerBot = other.GetComponent<Rigidbody2D>();
                if (rbPlayerBot != null)
                {
                    Vector2 knockbackDirection = (other.transform.position - shieldInstance.transform.position).normalized;
                    rbPlayerBot.linearVelocity = Vector2.zero;
                    rbPlayerBot.AddForce(knockbackDirection * knockbackForce, ForceMode2D.Impulse);
                    CustomLogger.Log($"ShockShield: Applied knockback force {knockbackForce} to {other.name} (tag={other.tag}).");
                }
                else
                {
                    CustomLogger.LogError($"ShockShield: {other.name} (tag={other.tag}) has no Rigidbody2D for knockback.");
                }
                break;

            case "Projectile":
                PhotonView projView = other.GetComponent<PhotonView>();
                Projectile projectile = other.GetComponent<Projectile>();
                if (projView == null || projectile == null || projView.gameObject == null)
                {
                    CustomLogger.LogWarning($"ShockShield: Skipped projectile {other.name}, missing PhotonView, Projectile component, or already destroyed.");
                    break;
                }

                bool isOwnerProjectile = projectile.OwnerActorNumber == photonView.ControllerActorNr;
                if (isOwnerProjectile)
                {
                    CustomLogger.Log($"ShockShield: Ignored projectile {other.name} from owner (projOwnerActorNumber={projectile.OwnerActorNumber}, shieldOwnerActorNr={photonView.ControllerActorNr}).");
                    break;
                }

                if (projView.IsMine)
                {
                    photonView.RPC("AbsorbProjectileOrBlast", RpcTarget.All);
                    PhotonNetwork.Destroy(projView.gameObject);
                    CustomLogger.Log($"ShockShield: Locally destroyed projectile {other.name} (ViewID={projView.ViewID}) and absorbed energy.");
                }
                else
                {
                    photonView.RPC("AbsorbProjectileOrBlast", RpcTarget.All);
                    CustomLogger.Log($"ShockShield: Absorbed projectile {other.name} (ViewID={projView.ViewID}) energy, destruction handled by owner.");
                }
                break;

            case "Blast":
                photonView.RPC("AbsorbProjectileOrBlast", RpcTarget.All);
                Blast blast = other.GetComponent<Blast>();
                if (blast != null)
                {
                    blast.Deflect(shieldInstance.transform.position);
                    CustomLogger.Log($"ShockShield: Deflected blast {other.name}.");
                }
                else
                {
                    PhotonView blastView = other.GetComponent<PhotonView>();
                    if (blastView != null && blastView.IsMine)
                    {
                        PhotonNetwork.Destroy(other.gameObject);
                        CustomLogger.Log($"ShockShield: Absorbed and destroyed blast {other.name} (no Blast component).");
                    }
                }
                break;

            case "Enemy":
                Rigidbody2D rbEnemy = other.GetComponent<Rigidbody2D>();
                if (rbEnemy != null)
                {
                    Vector2 knockbackDirection = (other.transform.position - shieldInstance.transform.position).normalized;
                    rbEnemy.linearVelocity = Vector2.zero;
                    rbEnemy.AddForce(knockbackDirection * knockbackForce, ForceMode2D.Impulse);
                    EnemyHealth enemyHealth = other.GetComponent<EnemyHealth>();
                    if (enemyHealth != null)
                    {
                        enemyHealth.HitByShockShield();
                        CustomLogger.Log($"ShockShield: Applied knockback to {other.name} (tag={other.tag}) and marked as hit by shield.");
                    }
                    else
                        CustomLogger.Log($"ShockShield: Applied knockback to {other.name} (tag={other.tag}), no EnemyHealth component.");
                }
                else
                {
                    CustomLogger.LogError($"ShockShield: {other.name} (tag={other.tag}) has no Rigidbody2D for knockback.");
                }
                break;

            case "halfPlanet":
            case "quarterPlanet":
                Rigidbody2D rbPlanet = other.GetComponent<Rigidbody2D>();
                if (rbPlanet != null)
                {
                    Vector2 knockbackDirection = (other.transform.position - shieldInstance.transform.position).normalized;
                    float appliedForce = knockbackForce * 100f;
                    rbPlanet.linearVelocity = Vector2.zero;
                    rbPlanet.AddForce(knockbackDirection * appliedForce, ForceMode2D.Impulse);
                    CustomLogger.Log($"ShockShield: Applied amplified knockback force {appliedForce} to {other.name} (tag={other.tag}).");

                    if (other.CompareTag("halfPlanet"))
                    {
                        PlanetHalf debris = other.GetComponent<PlanetHalf>();
                        if (debris != null)
                        {
                            debris.Launch(photonView.ViewID);
                            CustomLogger.Log($"ShockShield: Launched halfPlanet {other.name}, knockedByViewID={photonView.ViewID}.");
                        }
                    }
                    else
                    {
                        PlanetQuarter debris = other.GetComponent<PlanetQuarter>();
                        if (debris != null)
                        {
                            debris.Launch(photonView.ViewID);
                            CustomLogger.Log($"ShockShield: Launched quarterPlanet {other.name}, knockedByViewID={photonView.ViewID}.");
                        }
                    }
                }
                else
                {
                    CustomLogger.LogError($"ShockShield: {other.name} (tag={other.tag}) has no Rigidbody2D for knockback.");
                }
                break;
        }
    }

    void OnDestroy()
    {
        if (photonView.IsMine && shieldInstance != null)
        {
            CustomLogger.Log("ShockShield: Destroying shieldInstance.");
            PhotonNetwork.Destroy(shieldInstance);
        }
    }

    public void OnPhotonSerializeView(PhotonStream stream, PhotonMessageInfo info)
    {
        if (stream.IsWriting)
        {
            stream.SendNext(energy);
            stream.SendNext(wasFullyDepleted);
        }
        else
        {
            energy = (float)stream.ReceiveNext();
            wasFullyDepleted = (bool)stream.ReceiveNext();
            if (energySlider != null)
                energySlider.value = energy;
        }
    }
    public void ShowShieldTemporarily(float duration)
    {
        if (shieldRenderer == null || shieldInstance == null)
        {
            CustomLogger.LogError("ShockShield: Cannot show shield temporarily, shieldRenderer or shieldInstance is null.");
            return;
        }

        CustomLogger.Log($"ShockShield: Starting ShowShieldTemporarily for {duration} seconds, ViewID={photonView.ViewID}");
        photonView.RPC("SyncShowShieldTemporarily", RpcTarget.All, duration);
    }

    [PunRPC]
    private void SyncShowShieldTemporarily(float duration)
    {
        if (shieldRenderer == null || shieldInstance == null)
        {
            CustomLogger.LogError("ShockShield: Cannot sync temporary shield visibility, shieldRenderer or shieldInstance is null.");
            return;
        }

        StartCoroutine(TemporaryShieldVisibility(duration));
    }

    private IEnumerator TemporaryShieldVisibility(float duration)
    {
        if (shieldRenderer == null || shieldInstance == null)
        {
            CustomLogger.LogError("ShockShield: Cannot execute TemporaryShieldVisibility, shieldRenderer or shieldInstance is null.");
            yield break;
        }

        shieldRenderer.enabled = true;
        Color originalColor = shieldRenderer.color;
        CustomLogger.Log($"ShockShield: Shield renderer enabled temporarily with original color={originalColor}, ViewID={photonView.ViewID}");

        if (!isShieldActive)
        {
            if (shieldCollider != null)
                shieldCollider.enabled = false;
            if (shieldAnimator != null)
                shieldAnimator.SetBool("IsShieldActive", false);
        }

        float elapsed = 0f;
        float flashInterval = 0.3f; // Flash every 0.3 seconds
        float pulseInterval = 1f; // Complete pulse cycle every 1 second
        Vector3 minScale = originalScale;
        Vector3 maxScale = originalScale * shieldMaxSize;

        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;
            // Pulsate scale
            float pulseT = (Mathf.Sin(elapsed / pulseInterval * 2 * Mathf.PI) + 1) / 2; // Oscillate between 0 and 1
            shieldInstance.transform.localScale = Vector3.Lerp(minScale, maxScale, pulseT);
            // Update collider radius to match scale
            if (shieldCollider != null)
                shieldCollider.radius = Mathf.Lerp(0.1f, maxScale.x / 2, pulseT);

            // Flash alpha
            float flashT = Mathf.PingPong(elapsed / flashInterval, 1f);
            Color flashColor = originalColor;
            flashColor.a = Mathf.Lerp(0.4f, 1f, flashT); // Alpha oscillates between 0.4 and 1
            shieldRenderer.color = flashColor;

            yield return null;
        }

        // Shrink the shield before disabling
        float shrinkTime = 0.5f; // Matches expansionTime in ShockShield
        Vector3 startScale = shieldInstance.transform.localScale; // Current scale from pulsation
        Vector3 targetScale = originalScale; // Shrink to original scale
        float startRadius = shieldCollider != null ? shieldCollider.radius : 0.1f;
        elapsed = 0f;
        CustomLogger.Log($"ShockShield: Starting shrink phase, startScale={startScale}, targetScale={targetScale}, startRadius={startRadius}, shrinkTime={shrinkTime}, ViewID={photonView.ViewID}");

        while (elapsed < shrinkTime)
        {
            elapsed += Time.deltaTime;
            float progress = elapsed / shrinkTime;
            shieldInstance.transform.localScale = Vector3.Lerp(startScale, targetScale, progress);
            if (shieldCollider != null)
                shieldCollider.radius = Mathf.Lerp(startRadius, 0.1f, progress);

            float flashT = Mathf.PingPong(elapsed / (flashInterval / 2), 1f); // Faster flash during shrink
            Color flashColor = originalColor;
            flashColor.a = Mathf.Lerp(0.4f, 1f, flashT);
            shieldRenderer.color = flashColor;
            yield return null;
        }

        shieldInstance.transform.localScale = targetScale; // Ensure final scale
        if (shieldCollider != null)
            shieldCollider.radius = 0.1f; // Ensure final radius
        shieldRenderer.color = originalColor; // Restore original color
        if (!isShieldActive)
        {
            shieldRenderer.enabled = false;
            CustomLogger.Log($"ShockShield: Shield renderer disabled after {duration} seconds and shrink, isShieldActive={isShieldActive}, finalScale={targetScale}, finalRadius={(shieldCollider != null ? shieldCollider.radius : 0.1f)}, restored color={originalColor}, ViewID={photonView.ViewID}");
        }
    }
}