using Photon.Pun;
using Photon.Realtime;
using ExitGames.Client.Photon;
using System.Linq;
using System.Collections.Generic;
using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;


[RequireComponent(typeof(PhotonView))]
[RequireComponent(typeof(Rigidbody2D))]
[RequireComponent(typeof(PhotonTransformView))]
public class BotController : MonoBehaviourPunCallbacks, IPlayer, IPunObservable
{
    public string NickName { get; set; }
    public ExitGames.Client.Photon.Hashtable CustomProperties { get; set; }
    public int ActorNumber { get; private set; }
    public bool IsLocal => false;
    private float laserFireCooldownTimer = 0f; // Tracks cooldown between laser firing attempts
    private LaserBeam laserBeam;
    private ShockShield shockShield;
    private TwinTurretManager twinTurret;
    private BotUpgradeManager upgradeManager;
    private BotFuel botFuel;
    private PhasingTeleportation phasingTeleportation;
    private Vector2 moveDirection;
    private float moveSpeed = 100f; // Increased for more aggressive movement
    private PlayerHealth.Team botTeam = PlayerHealth.Team.None;
    private float changeDirectionTimer;
    private float actionTimer;
    private float miningTimer;
    private float upgradeTimer;
    private GameObject targetOre;
    private GameObject targetPlayer;
    private GameObject targetEnemy;
    private int brightMatterCollected;
    private enum BotState { Wandering, Mining, ChasingPlayer, Defending, Idle, ReturningToBounds, CollectingBrightMatter }
    private BotState currentState = BotState.Wandering;
    private Rigidbody2D rb;
    private float preferredDistanceMin = 40f; // Minimum preferred distance to player
    private float preferredDistanceMax = 50f; // Maximum preferred distance to player
    private float minDistanceToOthers = 25f; // Minimum allowed distance to others
    private int maxHealth = 100;
    private int currentHealth;
    public bool HasDied { get; private set; } = false;
    private int lastKillerViewID = -1;
    private PlayerHealth.DeathCause lastDeathCause = PlayerHealth.DeathCause.None;
    private float fireTimer = 0f;
    private float fireRate = 0.3f; // Increased fire rate for aggression
    private float bulletDamage = 7f; // Increased damage for aggression
    private readonly float boundarySize = 3000f;
    private readonly float bufferDistance = 50f;
    private Vector2 returnTarget;
    private SpriteRenderer spriteRenderer;
    public int damageReductionLevel = 0;
    private readonly object customPropertiesLock = new object();
    private bool isUpdatingProperties = false;
    private GameObject targetBrightMatter;
    // Evasive maneuver fields
    private float evasiveManeuverTimer = 0f;
    private float evasiveManeuverDuration = 0.5f; // Shortened for quicker maneuvers
    private float evasiveManeuverCooldown = 1.5f; // Reduced cooldown for more frequent maneuvers
    private float lastEvasiveManeuverTime = -1.5f;
    private Vector2 evasiveDirection;

    // Teleportation fields
    private float teleportTimer = 0f;
    private float teleportCooldown = 2f; // Reduced for more frequent teleports
    private float lastTeleportTime = -2f;
    private readonly float teleportRange = 30f; // Increased for better positioning
    private readonly float chaseTeleportThreshold = 80f; // Reduced to trigger teleports sooner
    private readonly float chaseTeleportTargetDistance = 40f; // Target distance for teleport when chasing

    // Planet propulsion fields
    private float planetAttackTimer = 0f;
    private float planetAttackCooldown = 5f; // Cooldown for planet laser attacks
    private float lastPlanetAttackTime = -5f;

    public void SetActorNumber(int actorNumber)
    {
        if (ActorNumber != actorNumber)
        {
            // Assign a unique positive ActorNumber, offset from player ActorNumbers
            ActorNumber = Mathf.Abs(actorNumber) + 1000; // Offset to avoid conflicts with players
            NickName = "Bot_" + ActorNumber;
            CustomProperties["Username"] = NickName;
            CustomProperties["BotViewID"] = photonView.ViewID;
            photonView.RPC("UpdateCustomProperties", RpcTarget.AllBuffered, new ExitGames.Client.Photon.Hashtable
        {
            { "Username", NickName },
            { "BotViewID", photonView.ViewID }
        });
            CustomLogger.Log($"BotController: Set ActorNumber={ActorNumber}, updated NickName={NickName}, BotViewID={photonView.ViewID}");
        }
        else
        {
            CustomLogger.Log($"BotController: ActorNumber={actorNumber} already set for {NickName}");
        }
    }

    void Awake()
    {
        if (photonView == null)
        {
            CustomLogger.LogError($"BotController: Missing PhotonView on {gameObject.name}, destroying.");
            Destroy(gameObject);
            return;
        }

        CustomProperties = new ExitGames.Client.Photon.Hashtable();
        // Initialize Team for TeamMoonRan scene
        if (SceneManager.GetActiveScene().name == "TeamMoonRan")
        {
            string team = Random.Range(0, 2) == 0 ? "Red" : "Cyan";
            CustomProperties["Team"] = team;
            CustomLogger.Log($"BotController: Initialized Team={team} for {gameObject.name} in TeamMoonRan");
        }
        else
        {
            CustomProperties["Team"] = "None";
            CustomLogger.Log($"BotController: Initialized Team=None for {gameObject.name} in non-TeamMoonRan scene");
        }
        StartCoroutine(InitializeBot());
        rb = GetComponent<Rigidbody2D>();
        currentHealth = maxHealth;
    }

    void Start()
    {
        BotController[] allBots = Object.FindObjectsByType<BotController>(FindObjectsSortMode.None);
        if (allBots.Length > 9)
        {
            CustomLogger.Log($"BotController: {NickName} destroyed, bot count exceeded (current={allBots.Length}, max=9)");
            PhotonNetwork.Destroy(gameObject);
            return;
        }
        CustomLogger.Log($"BotController: {NickName} spawned, total bots={allBots.Length}");

        if (rb == null)
        {
            CustomLogger.LogError($"BotController: Missing Rigidbody2D on {NickName}, destroying.");
            PhotonNetwork.Destroy(gameObject);
            return;
        }

        spriteRenderer = GetComponentInChildren<SpriteRenderer>();
        if (spriteRenderer == null)
        {
            Debug.LogWarning($"BotController: SpriteRenderer not found on {NickName} or its children for damage flash");
        }

        StartCoroutine(TryPositionSpaceship());

        rb.bodyType = RigidbodyType2D.Dynamic;
        rb.constraints = RigidbodyConstraints2D.FreezeRotation;
        rb.linearDamping = 0f;
        rb.angularDamping = 0f;
        Debug.Log($"BotController: {NickName} Rigidbody2D setup, bodyType={rb.bodyType}, constraints={rb.constraints}, drag={rb.linearDamping}, position={rb.position}");

        photonView.TransferOwnership(0);
        Debug.Log($"BotController: {NickName} ownership set to master client, IsMine={photonView.IsMine}, ViewID={photonView.ViewID}");

        PhotonTransformView transformView = GetComponent<PhotonTransformView>();
        if (transformView == null)
        {
            transformView = gameObject.AddComponent<PhotonTransformView>();
            CustomLogger.Log($"BotController: Added PhotonTransformView to {NickName}");
        }
        transformView.m_SynchronizePosition = true;
        transformView.m_SynchronizeRotation = true;

        laserBeam = GetComponentInChildren<LaserBeam>();
        if (laserBeam == null)
        {
            Debug.LogWarning($"BotController: LaserBeam component not found in children of {NickName}. Searching for LaserBeamGun object.");
            Transform laserGun = transform.Find("LaserBeamGun");
            if (laserGun != null)
            {
                laserBeam = laserGun.GetComponent<LaserBeam>();
                if (laserBeam == null)
                {
                    Debug.LogWarning($"BotController: No LaserBeam component on LaserBeamGun. Adding one to {NickName}.");
                    laserBeam = laserGun.gameObject.AddComponent<LaserBeam>();
                }
            }
            else
            {
                Debug.LogError($"BotController: LaserBeamGun object not found in {NickName}. Laser firing will fail.");
            }
        }
        if (laserBeam != null)
        {
            if (laserBeam.lineRenderer == null)
            {
                Debug.LogWarning($"BotController: lineRenderer is null on {NickName}’s LaserBeam. Laser visuals may not work.");
            }
            Debug.Log($"BotController: LaserBeam initialized on {NickName}, isBot={laserBeam.IsJammed}, ViewID={laserBeam.photonView.ViewID}");
        }
        else
        {
            Debug.LogError($"BotController: Failed to initialize LaserBeam for {NickName}. Laser firing disabled.");
        }

        shockShield = GetComponent<ShockShield>();
        twinTurret = GetComponent<TwinTurretManager>();
        upgradeManager = GetComponent<BotUpgradeManager>();
        botFuel = GetComponent<BotFuel>();
        phasingTeleportation = GetComponent<PhasingTeleportation>();

        if (shockShield == null)
        {
            Debug.LogWarning($"BotController: ShockShield component not found on {NickName}. Adding one.");
            shockShield = gameObject.AddComponent<ShockShield>();
        }
        else
        {
            Debug.Log($"BotController: ShockShield found on {NickName}");
        }

        if (twinTurret == null)
        {
            Debug.LogWarning($"BotController: TwinTurretManager component not found on {NickName}. Adding one.");
            twinTurret = gameObject.AddComponent<TwinTurretManager>();
        }
        else
        {
            Debug.Log($"BotController: TwinTurretManager found on {NickName}, droidPrefab={(twinTurret.droidPrefab != null ? twinTurret.droidPrefab.name : "null")}");
        }

        if (upgradeManager == null)
        {
            Debug.LogWarning($"BotController: BotUpgradeManager component not found on {NickName}. Adding one.");
            upgradeManager = gameObject.AddComponent<BotUpgradeManager>();
        }
        else
        {
            Debug.Log($"BotController: BotUpgradeManager found on {NickName}");
        }

        if (botFuel == null)
        {
            Debug.LogWarning($"BotController: BotFuel component not found on {NickName}. Adding one.");
            botFuel = gameObject.AddComponent<BotFuel>();
        }
        else
        {
            Debug.Log($"BotController: BotFuel found on {NickName}");
        }

        if (phasingTeleportation == null)
        {
            Debug.LogWarning($"BotController: PhasingTeleportation component not found on {NickName}. Adding one.");
            phasingTeleportation = gameObject.AddComponent<PhasingTeleportation>();
        }
        else
        {
            Debug.Log($"BotController: PhasingTeleportation found on {NickName}");
        }

        if (twinTurret.droidPrefab == null)
        {
            GameObject droidPrefab = Resources.Load<GameObject>("Prefabs/Droid");
            if (droidPrefab != null)
            {
                twinTurret.droidPrefab = droidPrefab;
                Debug.Log($"BotController: Assigned droidPrefab from Resources/Prefabs/Droid for {NickName}");
            }
            else
            {
                Debug.LogError($"BotController: Failed to load Droid prefab from Resources/Prefabs/Droid for {NickName}. Twin turret functionality will fail.");
            }
        }

        NameTag nameTag = GetComponent<NameTag>();
        if (nameTag == null)
        {
            nameTag = gameObject.AddComponent<NameTag>();
            CustomLogger.Log($"BotController: Added NameTag component to {NickName}");
        }

        if (string.IsNullOrEmpty(NickName))
        {
            CustomLogger.LogWarning($"BotController: NickName not set for ViewID={photonView.ViewID}, defaulting to temporary name");
            NickName = "Bot_Temp_" + photonView.ViewID;
        }

        ExitGames.Client.Photon.Hashtable updatedProps = new ExitGames.Client.Photon.Hashtable
    {
        { "BotViewID", photonView.ViewID },
        { "Username", NickName }
    };
        lock (customPropertiesLock)
        {
            foreach (DictionaryEntry entry in updatedProps)
            {
                CustomProperties[entry.Key] = entry.Value;
            }
        }
        if (photonView.IsMine)
        {
            photonView.RPC("UpdateCustomProperties", RpcTarget.AllBuffered, updatedProps);
            CustomLogger.Log($"BotController: Set up for {NickName}, ViewID={photonView.ViewID}, synchronized CustomProperties");
        }
        else
        {
            CustomLogger.Log($"BotController: Set up for {NickName}, ViewID={photonView.ViewID}, not synchronized (IsMine=false)");
        }

        BotIdentifier botIdentifier = GetComponent<BotIdentifier>();
        if (botIdentifier == null)
        {
            botIdentifier = gameObject.AddComponent<BotIdentifier>();
            CustomLogger.Log($"BotController: Added BotIdentifier component to {NickName}");
        }
        botIdentifier.Initialize(NickName);

        DroidShooting[] droidShootings = GetComponentsInChildren<DroidShooting>();
        if (droidShootings.Length > 1)
        {
            Debug.LogWarning($"BotController: {NickName} has {droidShootings.Length} DroidShooting components! Keeping only the first.");
            for (int i = 1; i < droidShootings.Length; i++)
            {
                Destroy(droidShootings[i]);
            }
        }

        DroidShooting droidShooting = GetComponentInChildren<DroidShooting>();
        if (droidShooting != null)
        {
            droidShooting.bulletDamage = bulletDamage;
            Debug.Log($"BotController: Initialized DroidShooting.bulletDamage={bulletDamage} for {NickName}");
        }

        if (upgradeManager != null)
        {
            upgradeManager.enabled = true;
            upgradeManager.InitializeForBot(this);
            SyncBrightMatter(brightMatterCollected);
            Debug.Log($"BotController: Initialized BotUpgradeManager for {NickName}, brightMatterCollected={brightMatterCollected}");
        }

        StartCoroutine(NotifyRandomPlanetGenerator());
        StartCoroutine(DelayedScoreboardRegistration());

        actionTimer = Random.Range(1f, 3f);
        upgradeTimer = Random.Range(3f, 10f);
        Debug.Log($"BotController: Initialized actionTimer={actionTimer:F2}, upgradeTimer={upgradeTimer:F2} for {NickName} to stagger actions");
    }

    private IEnumerator InitializeBot()
    {
        int maxRetries = 10;
        int retryCount = 0;
        while (ActorNumber == 0 && retryCount < maxRetries)
        {
            CustomLogger.Log($"BotController: Waiting for ActorNumber to be set, retry {retryCount + 1}/{maxRetries}");
            yield return new WaitForSeconds(0.1f);
            retryCount++;
        }
        if (ActorNumber == 0)
        {
            CustomLogger.LogError($"BotController: ActorNumber not set after {maxRetries} retries, using default -1");
            ActorNumber = -1;
        }
        NickName = "Bot_" + Mathf.Abs(ActorNumber);
        ExitGames.Client.Photon.Hashtable updatedProps = new ExitGames.Client.Photon.Hashtable
    {
        { "Username", NickName },
        { "Points", 0 }
    };
        lock (customPropertiesLock)
        {
            foreach (DictionaryEntry entry in updatedProps)
            {
                CustomProperties[entry.Key] = entry.Value;
            }
        }
        if (photonView.IsMine)
        {
            photonView.RPC("UpdateCustomProperties", RpcTarget.AllBuffered, updatedProps);
            CustomLogger.Log($"BotController: Initialized bot {NickName}, ActorNumber={ActorNumber}, Points=0, synchronized CustomProperties");
        }
        else
        {
            CustomLogger.Log($"BotController: Initialized bot {NickName}, ActorNumber={ActorNumber}, Points=0, not synchronized (IsMine=false)");
        }
    }

    public bool SetCustomProperties(ExitGames.Client.Photon.Hashtable propertiesToSet)
    {
        lock (customPropertiesLock)
        {
            if (photonView.IsMine && !isUpdatingProperties)
            {
                isUpdatingProperties = true;
                try
                {
                    foreach (DictionaryEntry entry in propertiesToSet)
                    {
                        CustomProperties[entry.Key] = entry.Value;
                    }
                    photonView.RPC("UpdateCustomProperties", RpcTarget.AllBuffered, propertiesToSet);
                    CustomLogger.Log($"BotController: SetCustomProperties called for {NickName}, ViewID={photonView.ViewID}, Properties={string.Join(", ", propertiesToSet.Keys.Cast<object>().Select(k => $"{k}={propertiesToSet[k]}"))}");
                    return true;
                }
                finally
                {
                    isUpdatingProperties = false;
                }
            }
            CustomLogger.LogWarning($"BotController: Failed to set custom properties for {NickName}, IsMine={photonView.IsMine}, isUpdatingProperties={isUpdatingProperties}");
            return false;
        }
    }

    private IEnumerator TryPositionSpaceship()
    {
        int maxRetries = 10;
        int retryCount = 0;
        GameObject spaceship = null;
        while (retryCount < maxRetries)
        {
            GameObject[] spaceships = GameObject.FindGameObjectsWithTag("SpaceShip");
            foreach (GameObject ship in spaceships)
            {
                SpaceshipMarker marker = ship.GetComponent<SpaceshipMarker>();
                if (marker != null && marker.ownerId == ActorNumber)
                {
                    spaceship = ship;
                    break;
                }
            }
            if (spaceship == null && CustomProperties.TryGetValue("SpaceshipViewID", out object viewID))
            {
                PhotonView spaceshipView = PhotonView.Find((int)viewID);
                if (spaceshipView != null && spaceshipView.gameObject != null && spaceshipView.gameObject.CompareTag("SpaceShip"))
                {
                    spaceship = spaceshipView.gameObject;
                }
            }
            if (spaceship != null)
            {
                bool validPosition = false;
                int maxAttempts = 20;
                int attempts = 0;
                Vector3 newPosition = spaceship.transform.position;
                float minDistance = 700f;
                while (attempts < maxAttempts && !validPosition)
                {
                    validPosition = true;
                    foreach (GameObject otherShip in spaceships)
                    {
                        if (otherShip == spaceship) continue;
                        float distance = Vector3.Distance(newPosition, otherShip.transform.position);
                        if (distance < minDistance)
                        {
                            validPosition = false;
                            float safeBoundary = boundarySize / 2 - bufferDistance;
                            newPosition = new Vector3(
                                Random.Range(-safeBoundary, safeBoundary),
                                Random.Range(-safeBoundary, safeBoundary),
                                0f
                            );
                            CustomLogger.Log($"BotController: {NickName} spaceship reposition attempt {attempts + 1}/{maxAttempts}, newPosition={newPosition}, distance to {otherShip.name}={distance:F2}");
                            break;
                        }
                    }
                    attempts++;
                    yield return new WaitForSeconds(0.1f);
                }
                if (validPosition)
                {
                    spaceship.transform.position = newPosition;
                    CustomLogger.Log($"BotController: {NickName} spaceship positioned at {newPosition}");
                }
                else
                {
                    CustomLogger.LogWarning($"BotController: {NickName} failed to find valid spaceship position after {maxAttempts} attempts, keeping original position {spaceship.transform.position}");
                }
                yield break;
            }
            CustomLogger.LogWarning($"BotController: {NickName} no spaceship found, retry {retryCount + 1}/{maxRetries}");
            yield return new WaitForSeconds(0.5f);
            retryCount++;
        }
        CustomLogger.LogError($"BotController: {NickName} no spaceship found after {maxRetries} retries, cannot enforce 700-unit separation");
    }

    private IEnumerator DelayedScoreboardRegistration()
    {
        yield return new WaitForSeconds(2f);
        ScoreboardManager scoreboardManager = FindFirstObjectByType<ScoreboardManager>();
        if (scoreboardManager != null)
        {
            scoreboardManager.UpdateScoreboard();
            CustomLogger.Log($"BotController: Triggered scoreboard update for {NickName}, ViewID={photonView.ViewID}, Points={(CustomProperties["Points"] ?? 0)}");
        }
        else
        {
            CustomLogger.LogWarning($"BotController: ScoreboardManager not found for {NickName} during DelayedScoreboardRegistration");
        }
    }

    private IEnumerator NotifyRandomPlanetGenerator()
    {
        while (!CustomProperties.ContainsKey("BotViewID"))
        {
            CustomLogger.Log($"BotController: {NickName} Waiting for BotViewID to be set before notifying RandomPlanetGenerator.");
            yield return new WaitForSeconds(0.5f);
        }

        int maxRetries = 10;
        int retryCount = 0;
        while (retryCount < maxRetries)
        {
            RandomPlanetGenerator generator = FindFirstObjectByType<RandomPlanetGenerator>();
            if (generator == null)
            {
                retryCount++;
                CustomLogger.LogWarning($"BotController: {NickName} RandomPlanetGenerator not found, retry {retryCount}/{maxRetries}");
                yield return new WaitForSeconds(1f);
                continue;
            }

            generator.AddPlayer(ActorNumber, gameObject);
            CustomLogger.Log($"BotController: Notified RandomPlanetGenerator for ActorNumber={ActorNumber}, NickName={NickName}");
            yield break;
        }
        CustomLogger.LogError($"BotController: {NickName} failed to find RandomPlanetGenerator after {maxRetries} retries");
    }

    void Update()
    {
        if (!photonView.IsMine)
        {
            Debug.Log($"BotController: {NickName} Update skipped, photonView.IsMine=false");
            return;
        }
        if (HasDied)
        {
            Debug.Log($"BotController: {NickName} Update skipped, HasDied={HasDied}");
            return;
        }

        Debug.Log($"BotController: {NickName} Update, state={currentState}, targetPlayer={(targetPlayer != null ? targetPlayer.name : "none")} targetBrightMatter={(targetBrightMatter != null ? targetBrightMatter.name : "none")}, position={transform.position}, rbBodyType={rb.bodyType}, rbConstraints={rb.constraints}, health={currentHealth}, brightMatter={brightMatterCollected}, damageReductionLevel={damageReductionLevel}");

        fireTimer -= Time.deltaTime;
        upgradeTimer -= Time.deltaTime;
        evasiveManeuverTimer -= Time.deltaTime;
        teleportTimer -= Time.deltaTime;
        planetAttackTimer -= Time.deltaTime;
        laserFireCooldownTimer -= Time.deltaTime; // Decrement laser cooldown timer

        if (currentState != BotState.ReturningToBounds)
        {
            UpdateState();
        }

        actionTimer -= Time.deltaTime;
        if (actionTimer <= 0)
        {
            PerformRandomAction();
            actionTimer = Random.Range(1f, 3f);
        }

        if (upgradeTimer <= 0)
        {
            TryPurchaseUpgrade();
            upgradeTimer = Random.Range(3f, 10f);
            Debug.Log($"BotController: {NickName} attempted upgrade, reset upgradeTimer={upgradeTimer:F2}");
        }

        if (planetAttackTimer <= 0 && currentState == BotState.ChasingPlayer && targetPlayer != null)
        {
            TryPropelPlanetAtPlayer();
            planetAttackTimer = planetAttackCooldown;
        }

        CheckSpaceshipDistance();
    }

    void FixedUpdate()
    {
        if (!photonView.IsMine)
            return;

        float halfBoundary = boundarySize / 2;
        float safeBoundary = halfBoundary - bufferDistance;
        Vector2 pos = transform.position;

        if (pos.x < -halfBoundary || pos.x > halfBoundary || pos.y < -halfBoundary || pos.y > halfBoundary)
        {
            if (currentState != BotState.ReturningToBounds)
            {
                float targetX = Mathf.Clamp(pos.x, -safeBoundary, safeBoundary);
                float targetY = Mathf.Clamp(pos.y, -safeBoundary, safeBoundary);
                returnTarget = new Vector2(targetX, targetY);
                currentState = BotState.ReturningToBounds;
                rb.linearVelocity = Vector2.zero;
                Debug.Log($"BotController: {NickName} out of bounds at {pos}, switching to ReturningToBounds, target={returnTarget}");
            }

            Vector2 direction = (returnTarget - pos).normalized;
            Vector2 newPos = Vector2.MoveTowards(pos, returnTarget, moveSpeed * Time.fixedDeltaTime);
            rb.MovePosition(newPos);

            if (Vector2.Distance(pos, returnTarget) < 0.1f)
            {
                currentState = BotState.Idle;
                Debug.Log($"BotController: {NickName} returned to bounds at {newPos}, switching to Idle");
            }
        }
        else
        {
            float clampedX = Mathf.Clamp(pos.x, -safeBoundary, safeBoundary);
            float clampedY = Mathf.Clamp(pos.y, -safeBoundary, safeBoundary);
            if (pos.x != clampedX || pos.y != clampedY)
            {
                rb.MovePosition(new Vector2(clampedX, clampedY));
                rb.linearVelocity = Vector2.zero;
                Debug.Log($"BotController: {NickName} clamped to safe bounds, position=({clampedX}, {clampedY})");
            }

            switch (currentState)
            {
                case BotState.Wandering:
                    Wander();
                    break;
                case BotState.Mining:
                    MineOre();
                    break;
                case BotState.ChasingPlayer:
                    ChasePlayer();
                    break;
                case BotState.Defending:
                    DefendAgainstEnemy();
                    break;
                case BotState.Idle:
                    break;
                case BotState.ReturningToBounds:
                    break;
                case BotState.CollectingBrightMatter:
                    CollectBrightMatter();
                    break;
            }
        }
    }

    private void UpdateState()
    {
        Debug.Log($"BotController: {NickName} UpdateState, currentState={currentState}, targetPlayer={(targetPlayer != null ? targetPlayer.name : "none")}, targetBrightMatter={(targetBrightMatter != null ? targetBrightMatter.name : "none")}");

        // Check if current BrightMatter target is valid
        if (targetBrightMatter != null && (!targetBrightMatter.activeInHierarchy || Vector2.Distance(transform.position, targetBrightMatter.transform.position) > 50f))
        {
            targetBrightMatter = null;
            if (currentState == BotState.CollectingBrightMatter)
            {
                currentState = BotState.Idle;
                Debug.Log($"BotController: {NickName} lost BrightMatter target, switching to Idle");
            }
        }

        // Prioritize collecting BrightMatter
        if (currentState != BotState.CollectingBrightMatter)
        {
            targetBrightMatter = FindNearestBrightMatter(50f);
            if (targetBrightMatter != null)
            {
                currentState = BotState.CollectingBrightMatter;
                Debug.Log($"BotController: {NickName} switched to CollectingBrightMatter, target={targetBrightMatter.name}");
                return;
            }
        }

        // Check if current player target is valid
        if (targetPlayer != null && targetPlayer.gameObject.activeInHierarchy)
        {
            PlayerHealth playerHealth = targetPlayer.GetComponent<PlayerHealth>();
            BotController botController = targetPlayer.GetComponent<BotController>();
            bool isDead = (playerHealth != null && playerHealth.HasDied) || (botController != null && botController.HasDied);
            float distance = Vector2.Distance(transform.position, targetPlayer.transform.position);
            if (isDead || distance > 150f)
            {
                targetPlayer = null;
                currentState = BotState.Idle;
                Debug.Log($"BotController: {NickName} targetPlayer {(isDead ? "died" : "out of 150-unit range")}, switching to Idle");
            }
        }

        // Find new player target if none exists
        if (targetPlayer == null || !targetPlayer.gameObject.activeInHierarchy)
        {
            targetPlayer = FindNearestPlayer(150f);
            if (targetPlayer != null)
            {
                currentState = BotState.ChasingPlayer;
                Debug.Log($"BotController: {NickName} switched to ChasingPlayer, target={targetPlayer.name}");
                return;
            }
        }

        // Check for enemies if not chasing or collecting
        if (currentState != BotState.ChasingPlayer && currentState != BotState.CollectingBrightMatter)
        {
            targetEnemy = FindNearestEnemy(50f);
            if (targetEnemy != null)
            {
                currentState = BotState.Defending;
                Debug.Log($"BotController: {NickName} switched to Defending, target={targetEnemy.name}");
                return;
            }
        }

        // Check for ore if not chasing, defending, or collecting
        if (currentState != BotState.ChasingPlayer && currentState != BotState.Defending && currentState != BotState.CollectingBrightMatter)
        {
            targetOre = FindNearestOre(50f);
            if (targetOre != null)
            {
                currentState = BotState.Mining;
                Debug.Log($"BotController: {NickName} switched to Mining, target={targetOre.name}");
                return;
            }
        }

        // Default to Wandering if no other state applies
        if (currentState != BotState.ChasingPlayer && currentState != BotState.Defending && currentState != BotState.Mining && currentState != BotState.CollectingBrightMatter)
        {
            currentState = BotState.Wandering;
            Debug.Log($"BotController: {NickName} switched to Wandering");
        }
    }

    private GameObject FindNearestPlayer(float maxDistance)
    {
        GameObject[] players = GameObject.FindGameObjectsWithTag("Player");
        GameObject[] bots = GameObject.FindGameObjectsWithTag("Bot");
        GameObject nearest = null;
        float minDistance = maxDistance;

        Debug.Log($"BotController: {NickName} FindNearestPlayer, maxDistance={maxDistance}, playersFound={players.Length}, botsFound={bots.Length}");

        string botTeam = CustomProperties != null && CustomProperties.ContainsKey("Team") ? CustomProperties["Team"].ToString() : "None";
        bool isTeamMoonRan = SceneManager.GetActiveScene().name == "TeamMoonRan";

        foreach (GameObject target in players.Concat(bots))
        {
            if (target == gameObject)
            {
                Debug.Log($"BotController: {NickName} skipped self, target={target.name}, tag={target.tag}");
                continue;
            }
            if (!target.activeInHierarchy)
            {
                Debug.Log($"BotController: {NickName} skipped inactive target={target.name}, tag={target.tag}");
                continue;
            }

            bool isInRespawn = false;
            PlayerHealth playerHealth = target.GetComponent<PlayerHealth>();
            BotController botController = target.GetComponent<BotController>();

            if (playerHealth != null)
            {
                isInRespawn = playerHealth.HasDied;
            }
            else if (botController != null)
            {
                isInRespawn = botController.HasDied;
            }

            if (isInRespawn)
            {
                Debug.Log($"BotController: {NickName} skipped target={target.name}, tag={target.tag}, isInRespawn={isInRespawn}");
                continue;
            }

            if (isTeamMoonRan)
            {
                string targetTeam = "None";
                if (playerHealth != null && playerHealth.photonView != null)
                {
                    ExitGames.Client.Photon.Hashtable targetProps = playerHealth.photonView.Owner.CustomProperties;
                    targetTeam = targetProps != null && targetProps.ContainsKey("Team") ? targetProps["Team"].ToString() : "None";
                }
                else if (botController != null && botController.CustomProperties != null)
                {
                    targetTeam = botController.CustomProperties.ContainsKey("Team") ? botController.CustomProperties["Team"].ToString() : "None";
                }

                if (botTeam != "None" && targetTeam == botTeam)
                {
                    Debug.Log($"BotController: {NickName} skipped target={target.name}, tag={target.tag}, same team={targetTeam} in TeamMoonRan");
                    continue;
                }
            }

            float distance = Vector2.Distance(transform.position, target.transform.position);
            Debug.Log($"BotController: {NickName} checking target={target.name}, tag={target.tag}, distance={distance:F2}, position={target.transform.position}, isInRespawn={isInRespawn}");
            if (distance < minDistance)
            {
                minDistance = distance;
                nearest = target;
            }
        }

        Debug.Log($"BotController: {NickName} FindNearestPlayer result, nearest={(nearest != null ? nearest.name : "none")}, distance={(nearest != null ? minDistance.ToString("F2") : "N/A")}");
        return nearest;
    }

    private GameObject FindNearestEnemy(float range)
    {
        GameObject[] enemies = GameObject.FindGameObjectsWithTag("Enemy");
        GameObject nearest = null;
        float minDistance = range;
        foreach (var enemy in enemies)
        {
            if (!enemy.activeInHierarchy) continue;
            float distance = Vector2.Distance(transform.position, enemy.transform.position);
            if (distance < minDistance)
            {
                minDistance = distance;
                nearest = enemy;
            }
        }
        Debug.Log($"BotController: {NickName} FindNearestEnemy, nearest={(nearest != null ? nearest.name : "none")}");
        return nearest;
    }

    private GameObject FindNearestOre(float range)
    {
        Collider2D[] hits = Physics2D.OverlapCircleAll(transform.position, range);
        GameObject nearest = null;
        float minDistance = range;
        foreach (var hit in hits)
        {
            if (hit.CompareTag("Ore") && hit.gameObject.activeInHierarchy)
            {
                float distance = Vector2.Distance(transform.position, hit.transform.position);
                if (distance < minDistance)
                {
                    minDistance = distance;
                    nearest = hit.gameObject;
                }
            }
        }
        Debug.Log($"BotController: {NickName} FindNearestOre, nearest={(nearest != null ? nearest.name : "none")}");
        return nearest;
    }

    private GameObject FindNearestBrightMatter(float range)
    {
        Collider2D[] hits = Physics2D.OverlapCircleAll(transform.position, range);
        GameObject nearest = null;
        float minDistance = range;
        foreach (var hit in hits)
        {
            if (hit.CompareTag("BrightMatterOrb") && hit.gameObject.activeInHierarchy)
            {
                float distance = Vector2.Distance(transform.position, hit.transform.position);
                if (distance < minDistance)
                {
                    minDistance = distance;
                    nearest = hit.gameObject;
                }
            }
        }
        Debug.Log($"BotController: {NickName} FindNearestBrightMatter, nearest={(nearest != null ? nearest.name : "none")}");
        return nearest;
    }

    private GameObject FindNearestPlanetWithoutOre(float range)
    {
        string[] planetTags = { "Planet", "halfPlanet", "quarterPlanet" };
        GameObject nearest = null;
        float minDistance = range;

        foreach (var tag in planetTags)
        {
            GameObject[] planets = GameObject.FindGameObjectsWithTag(tag);
            foreach (var planet in planets)
            {
                if (!planet.activeInHierarchy) continue;
                float distance = Vector2.Distance(transform.position, planet.transform.position);
                if (distance >= minDistance) continue;

                Collider2D[] oreHits = Physics2D.OverlapCircleAll(planet.transform.position, 10f);
                bool hasOre = false;
                foreach (var hit in oreHits)
                {
                    if (hit.CompareTag("Ore") && hit.gameObject.activeInHierarchy)
                    {
                        hasOre = true;
                        break;
                    }
                }

                if (!hasOre)
                {
                    minDistance = distance;
                    nearest = planet;
                }
            }
        }

        Debug.Log($"BotController: {NickName} FindNearestPlanetWithoutOre, nearest={(nearest != null ? nearest.name : "none")}, distance={(nearest != null ? minDistance.ToString("F2") : "N/A")}");
        return nearest;
    }

    private void Wander()
    {
        if (moveDirection == Vector2.zero || Vector2.Distance(transform.position, returnTarget) < 5f)
        {
            // Define safe boundary for target selection
            float safeBoundary = boundarySize / 2 - bufferDistance;
            // Pick a random target point within the safe boundary
            returnTarget = new Vector2(
                Random.Range(-safeBoundary, safeBoundary),
                Random.Range(-safeBoundary, safeBoundary)
            );
            moveDirection = (returnTarget - (Vector2)transform.position).normalized;
            changeDirectionTimer = Random.Range(2f, 5f); // Reduced for more frequent direction changes
            Debug.Log($"BotController: {NickName} Wander, new target={returnTarget}, direction={moveDirection}, timer={changeDirectionTimer:F2}");
        }

        changeDirectionTimer -= Time.deltaTime;
        if (changeDirectionTimer <= 0)
        {
            // Reset to trigger new target selection
            moveDirection = Vector2.zero;
            Debug.Log($"BotController: {NickName} Wander, reached timer limit, resetting for new target");
        }

        // Check if stuck near planets and teleport if needed
        if (IsStuckNearPlanets())
        {
            TryTeleport(transform.position, true); // Evasive teleport to escape
        }

        // Move toward the target point
        rb.linearVelocity = moveDirection * moveSpeed;
        Debug.Log($"BotController: {NickName} Wander, velocity={rb.linearVelocity}, position={rb.position}, target={returnTarget}");
    }

    private void MineOre()
    {
        if (targetOre == null || !targetOre.activeInHierarchy)
        {
            targetOre = null;
            currentState = BotState.Wandering;
            Debug.Log($"BotController: {NickName} MineOre, targetOre lost, switching to Wandering");
            return;
        }

        MoveToPoint(targetOre.transform.position, minDistanceToOthers);

        float distance = Vector2.Distance(transform.position, targetOre.transform.position);
        if (distance < 5f)
        {
            OrePrefab ore = targetOre.GetComponent<OrePrefab>();
            if (ore != null)
            {
                ore.Interact(this);
                Debug.Log($"BotController: {NickName} interacting with {targetOre.name}, distance={distance:F2}");
            }
        }
    }

    private void CollectBrightMatter()
    {
        if (targetBrightMatter == null || !targetBrightMatter.activeInHierarchy)
        {
            targetBrightMatter = null;
            currentState = BotState.Wandering;
            Debug.Log($"BotController: {NickName} CollectBrightMatter, targetBrightMatter lost, switching to Wandering");
            return;
        }

        MoveToPoint(targetBrightMatter.transform.position, 5f);

        float distance = Vector2.Distance(transform.position, targetBrightMatter.transform.position);
        Debug.Log($"BotController: {NickName} CollectBrightMatter, target={targetBrightMatter.name}, distance={distance:F2}");
    }

    private void ChasePlayer()
    {
        if (targetPlayer == null || !targetPlayer.gameObject.activeInHierarchy)
        {
            Debug.Log($"BotController: {NickName} ChasePlayer, lost target, switching to Idle");
            targetPlayer = null;
            currentState = BotState.Idle;
            return;
        }

        float distance = Vector2.Distance(transform.position, targetPlayer.transform.position);
        Debug.Log($"BotController: {NickName} ChasePlayer, target={targetPlayer.name}, distance={distance:F2}");
        if (distance > 150f)
        {
            Debug.Log($"BotController: {NickName} ChasePlayer, target out of 150-unit range, switching to Idle");
            targetPlayer = null;
            currentState = BotState.Idle;
            return;
        }

        if (distance > chaseTeleportThreshold)
        {
            TryTeleport(targetPlayer.transform.position, false);
        }

        MoveToPoint(targetPlayer.transform.position, Random.Range(preferredDistanceMin, preferredDistanceMax));

        DroidShooting[] droidShootings = GetComponentsInChildren<DroidShooting>();
        if (droidShootings.Length == 0)
        {
            Debug.LogError($"BotController: {NickName} missing DroidShooting components on Droid children!");
            return;
        }

        foreach (DroidShooting droidShooting in droidShootings)
        {
            if (droidShooting != null && droidShooting.IsInitialized && droidShooting.canShoot)
            {
                Vector2 aimDir = (targetPlayer.transform.position - droidShooting.transform.position).normalized;
                droidShooting.aimDirection = aimDir;
                Debug.Log($"BotController: {NickName} aiming droid (parent={droidShooting.transform.parent?.name}) at {targetPlayer.name}, aimDirection={aimDir}");
                if (fireTimer <= 0)
                {
                    Debug.Log($"BotController: {NickName} directing droid (parent={droidShooting.transform.parent?.name}) to fire at {targetPlayer.name}");
                    droidShooting.FireProjectile();
                }
            }
        }

        if (fireTimer <= 0)
        {
            fireTimer = fireRate;
            if (Random.value < 0.7f) // Increased chance for evasive maneuvers
            {
                TryStartEvasiveManeuver(targetPlayer.transform.position);
            }
            else
            {
                TryTeleport(targetPlayer.transform.position, true);
            }
        }
    }

    private void DefendAgainstEnemy()
    {
        if (targetEnemy == null || !targetEnemy.activeInHierarchy)
        {
            currentState = BotState.Wandering;
            Debug.Log($"BotController: {NickName} DefendAgainstEnemy, target lost, switching to Wandering");
            return;
        }

        float distance = Vector2.Distance(transform.position, targetEnemy.transform.position);
        Debug.Log($"BotController: {NickName} DefendAgainstEnemy, target={targetEnemy.name}, distance={distance:F2}");

        if (distance > chaseTeleportThreshold)
        {
            TryTeleport(targetEnemy.transform.position, false);
        }

        MoveToPoint(targetEnemy.transform.position, minDistanceToOthers);

        DroidShooting[] droidShootings = GetComponentsInChildren<DroidShooting>();
        if (droidShootings.Length == 0)
        {
            Debug.LogError($"BotController: {NickName} missing DroidShooting components on Droid children!");
            return;
        }

        foreach (DroidShooting droidShooting in droidShootings)
        {
            if (droidShooting != null && droidShooting.IsInitialized && droidShooting.canShoot)
            {
                Vector2 aimDir = (targetEnemy.transform.position - droidShooting.transform.position).normalized;
                droidShooting.aimDirection = aimDir;
                Debug.Log($"BotController: {NickName} aiming droid (parent={droidShooting.transform.parent?.name}) at {targetEnemy.name}, aimDirection={aimDir}");
                if (fireTimer <= 0)
                {
                    Debug.Log($"BotController: {NickName} directing droid (parent={droidShooting.transform.parent?.name}) to fire at {targetEnemy.name}");
                    droidShooting.FireProjectile();
                }
            }
        }

        if (fireTimer <= 0)
        {
            fireTimer = fireRate;
            if (Random.value < 0.7f) // Increased chance for evasive maneuvers
            {
                TryStartEvasiveManeuver(targetEnemy.transform.position);
            }
            else
            {
                TryTeleport(targetEnemy.transform.position, true);
            }
        }
    }

    private void MoveToPoint(Vector3 point, float customStoppingDistance)
    {
        Vector2 direction = (point - transform.position).normalized;
        float distance = Vector2.Distance(transform.position, point);
        Debug.Log($"BotController: {NickName} MoveToPoint, target={point}, distance={distance:F2}, customStoppingDistance={customStoppingDistance}, rb={rb != null}, bodyType={rb.bodyType}");

        float effectiveStoppingDistance = Mathf.Max(minDistanceToOthers, Mathf.Clamp(customStoppingDistance, preferredDistanceMin, preferredDistanceMax));

        if (distance < minDistanceToOthers)
        {
            Vector2 awayDirection = -direction;
            rb.linearVelocity = awayDirection * moveSpeed;
            Debug.Log($"BotController: {NickName} MoveToPoint, too close (distance={distance:F2} < {minDistanceToOthers}), moving away, velocity={rb.linearVelocity}, position={rb.position}");
        }
        else if (distance > effectiveStoppingDistance)
        {
            if (evasiveManeuverTimer > 0)
            {
                rb.linearVelocity = evasiveDirection * moveSpeed;
                Debug.Log($"BotController: {NickName} MoveToPoint, performing evasive maneuver, velocity={rb.linearVelocity}, position={rb.position}");
            }
            else
            {
                rb.linearVelocity = direction * moveSpeed;
                Debug.Log($"BotController: {NickName} MoveToPoint, moving toward target, velocity={rb.linearVelocity}, position={rb.position}");
            }
        }
        else
        {
            if (evasiveManeuverTimer > 0)
            {
                rb.linearVelocity = evasiveDirection * moveSpeed;
                Debug.Log($"BotController: {NickName} MoveToPoint, at preferred distance, continuing evasive maneuver, velocity={rb.linearVelocity}, position={rb.position}");
            }
            else
            {
                rb.linearVelocity = Vector2.zero;
                Debug.Log($"BotController: {NickName} MoveToPoint, stopped, distance={distance:F2} within preferred range {minDistanceToOthers}-{effectiveStoppingDistance}");
            }
        }
    }

    private void TryStartEvasiveManeuver(Vector3 targetPosition)
    {
        if (Time.time - lastEvasiveManeuverTime < evasiveManeuverCooldown)
        {
            Debug.Log($"BotController: {NickName} evasive maneuver on cooldown, time since last={Time.time - lastEvasiveManeuverTime:F2}");
            return;
        }

        if (Random.value < 0.8f) // Increased chance for evasive maneuvers
        {
            Vector2 directionToTarget = (targetPosition - transform.position).normalized;
            evasiveDirection = Random.value < 0.5f
                ? new Vector2(-directionToTarget.y, directionToTarget.x)
                : new Vector2(directionToTarget.y, -directionToTarget.x);
            evasiveManeuverTimer = evasiveManeuverDuration;
            lastEvasiveManeuverTime = Time.time;
            Debug.Log($"BotController: {NickName} started evasive maneuver, direction={evasiveDirection}, duration={evasiveManeuverDuration}");
        }
    }

    private void TryTeleport(Vector3 targetPosition, bool isEvasive)
    {
        if (Time.time - lastTeleportTime < teleportCooldown)
        {
            Debug.Log($"BotController: {NickName} teleport on cooldown, time since last={Time.time - lastTeleportTime:F2}");
            return;
        }

        if (phasingTeleportation == null || botFuel == null)
        {
            Debug.LogWarning($"BotController: {NickName} cannot teleport, phasingTeleportation={(phasingTeleportation != null ? "present" : "null")}, botFuel={(botFuel != null ? "present" : "null")}");
            return;
        }

        if (!botFuel.CanAffordFuel(phasingTeleportation.fuelCostPerTeleport))
        {
            Debug.Log($"BotController: {NickName} cannot teleport, insufficient fuel, currentFuel={botFuel.CurrentFuel}, fuelCost={phasingTeleportation.fuelCostPerTeleport}");
            return;
        }

        Vector2 newPosition;
        if (isEvasive)
        {
            // Random evasive teleport within teleportRange
            Vector2 randomOffset = Random.insideUnitCircle.normalized * Random.Range(10f, teleportRange);
            newPosition = (Vector2)transform.position + randomOffset;
        }
        else
        {
            // Teleport toward target, maintaining chaseTeleportTargetDistance
            Vector2 directionToTarget = (targetPosition - transform.position).normalized;
            newPosition = (Vector2)targetPosition - directionToTarget * chaseTeleportTargetDistance;
        }

        float safeBoundary = boundarySize / 2 - bufferDistance;
        newPosition.x = Mathf.Clamp(newPosition.x, -safeBoundary, safeBoundary);
        newPosition.y = Mathf.Clamp(newPosition.y, -safeBoundary, safeBoundary);

        bool validPosition = true;
        GameObject[] players = GameObject.FindGameObjectsWithTag("Player");
        GameObject[] bots = GameObject.FindGameObjectsWithTag("Bot");
        foreach (GameObject other in players.Concat(bots))
        {
            if (other == gameObject) continue;
            if (!other.activeInHierarchy) continue;
            float distance = Vector2.Distance(newPosition, other.transform.position);
            if (distance < minDistanceToOthers)
            {
                validPosition = false;
                Debug.Log($"BotController: {NickName} teleport position invalid, too close to {other.name}, distance={distance:F2}");
                break;
            }
        }

        // Additional check for planet proximity to avoid teleporting into another stuck position
        string[] planetTags = { "Planet", "halfPlanet", "quarterPlanet" };
        foreach (var tag in planetTags)
        {
            GameObject[] planets = GameObject.FindGameObjectsWithTag(tag);
            foreach (var planet in planets)
            {
                if (!planet.activeInHierarchy) continue;
                float distance = Vector2.Distance(newPosition, planet.transform.position);
                if (distance < 15f) // Avoid teleporting too close to planets
                {
                    validPosition = false;
                    Debug.Log($"BotController: {NickName} teleport position invalid, too close to planet {planet.name}, distance={distance:F2}");
                    break;
                }
            }
        }

        if (!validPosition)
        {
            Debug.Log($"BotController: {NickName} teleport aborted, no valid position found");
            return;
        }

        phasingTeleportation.Teleport(newPosition);
        botFuel.DrainFuel(phasingTeleportation.fuelCostPerTeleport);
        lastTeleportTime = Time.time;
        teleportTimer = teleportCooldown;
        Debug.Log($"BotController: {NickName} teleported to {newPosition}, isEvasive={isEvasive}, fuel remaining={botFuel.CurrentFuel}");
    }
    [PunRPC]
    private void TriggerLaserRPC(Vector3 targetPosition)
    {
        if (laserBeam != null && !laserBeam.IsLaserFiring)
        {
            laserBeam.TriggerLaser(targetPosition);
            Debug.Log($"BotController: {NickName} TriggerLaserRPC executed, target={targetPosition}");
        }
        else
        {
            Debug.LogWarning($"BotController: {NickName} TriggerLaserRPC failed, laserBeam={(laserBeam != null ? "present" : "null")}, IsLaserFiring={laserBeam?.IsLaserFiring}");
        }
    }
    private bool IsStuckNearPlanets()
    {
        string[] planetTags = { "Planet", "halfPlanet", "quarterPlanet" };
        int nearbyPlanets = 0;
        float stuckRadius = 20f; // Radius to check for planets
        float velocityThreshold = 10f; // Velocity below which bot is considered stuck

        foreach (var tag in planetTags)
        {
            GameObject[] planets = GameObject.FindGameObjectsWithTag(tag);
            foreach (var planet in planets)
            {
                if (!planet.activeInHierarchy) continue;
                float distance = Vector2.Distance(transform.position, planet.transform.position);
                if (distance < stuckRadius)
                {
                    nearbyPlanets++;
                }
            }
        }

        bool isStuck = nearbyPlanets >= 2 && rb.linearVelocity.magnitude < velocityThreshold;
        if (isStuck)
        {
            Debug.Log($"BotController: {NickName} detected as stuck, nearbyPlanets={nearbyPlanets}, velocity={rb.linearVelocity.magnitude:F2}");
        }
        return isStuck;
    }

    private void FireLaserAtTarget(Vector3 targetPosition)
    {
        if (!photonView.IsMine)
        {
            Debug.Log($"BotController: {NickName} FireLaserAtTarget skipped, photonView.IsMine=false");
            return;
        }

        if (laserBeam == null)
        {
            Debug.LogError($"BotController: {NickName} cannot fire laser, laserBeam is null");
            return;
        }

        if (laserBeam.IsLaserFiring || laserBeam.IsJammed)
        {
            Debug.Log($"BotController: {NickName} laser cannot fire, IsLaserFiring={laserBeam.IsLaserFiring}, IsJammed={laserBeam.IsJammed}");
            return;
        }

        if (laserFireCooldownTimer > 0)
        {
            Debug.Log($"BotController: {NickName} laser fire on cooldown, timer={laserFireCooldownTimer:F2}");
            return;
        }

        float rechargeTime = 10f; // Default recharge time
        if (upgradeManager != null && upgradeManager.UpgradeLevels.Length > 2)
        {
            rechargeTime = Mathf.Max(1f, 10f - upgradeManager.UpgradeLevels[2]); // 10s at level 0 to 5s at level 5
            laserBeam.SetRechargeTime(rechargeTime); // Sync with LaserBeam
        }

        Debug.Log($"BotController: {NickName} attempting to fire laser at {targetPosition}, rechargeTime={rechargeTime}");

        photonView.RPC("TriggerLaserRPC", RpcTarget.All, targetPosition);
        laserFireCooldownTimer = rechargeTime; // Set cooldown to recharge time
        CustomLogger.Log($"BotController: {NickName} fired laser at target position {targetPosition}, reset cooldown to {rechargeTime}s");
    }

    private void TryPropelPlanetAtPlayer()
    {
        if (targetPlayer == null || !targetPlayer.activeInHierarchy)
        {
            Debug.Log($"BotController: {NickName} TryPropelPlanetAtPlayer skipped, no valid targetPlayer");
            return;
        }

        GameObject targetPlanet = FindNearestPlanet(100f);
        if (targetPlanet == null)
        {
            Debug.Log($"BotController: {NickName} no planet found within 100 units for laser propulsion");
            return;
        }

        DroidShooting[] droidShootings = GetComponentsInChildren<DroidShooting>();
        foreach (DroidShooting droidShooting in droidShootings)
        {
            if (droidShooting != null && droidShooting.IsInitialized && droidShooting.canShoot)
            {
                Vector2 aimDir = (targetPlanet.transform.position - droidShooting.transform.position).normalized;
                droidShooting.aimDirection = aimDir;
                if (fireTimer <= 0)
                {
                    droidShooting.FireProjectile();
                    Debug.Log($"BotController: {NickName} firing projectile at planet {targetPlanet.name}, aimDirection={aimDir}");
                }
            }
        }
        if (fireTimer <= 0)
        {
            fireTimer = fireRate;
        }

        Vector3 playerPos = targetPlayer.transform.position;
        Vector3 planetPos = targetPlanet.transform.position;
        Vector3 directionToPlayer = (playerPos - planetPos).normalized;
        Vector3 laserTarget = planetPos - directionToPlayer * 5f;

        FireLaserAtTarget(laserTarget);

        if (photonView.IsMine && PhotonNetwork.IsMasterClient)
        {
            Rigidbody2D planetRb = targetPlanet.GetComponent<Rigidbody2D>();
            PhotonView planetView = targetPlanet.GetComponent<PhotonView>();
            if (planetRb != null && planetView != null)
            {
                float forceMagnitude = 500f;
                planetRb.AddForce(directionToPlayer * forceMagnitude, ForceMode2D.Impulse);
                photonView.RPC("SyncPlanetPropulsion", RpcTarget.All, planetView.ViewID, directionToPlayer * forceMagnitude);
                CustomLogger.Log($"BotController: {NickName} propelled planet {targetPlanet.name} toward {targetPlayer.name}, force={directionToPlayer * forceMagnitude}");
            }
            else
            {
                Debug.LogWarning($"BotController: {NickName} cannot propel planet {targetPlanet.name}, Rigidbody2D={(planetRb != null ? "present" : "null")}, PhotonView={(planetView != null ? "present" : "null")}");
            }
        }

        lastPlanetAttackTime = Time.time;
        planetAttackTimer = planetAttackCooldown;
    }

    [PunRPC]
    private void SyncPlanetPropulsion(int planetViewID, Vector3 force)
    {
        PhotonView planetView = PhotonView.Find(planetViewID);
        if (planetView != null)
        {
            Rigidbody2D planetRb = planetView.GetComponent<Rigidbody2D>();
            if (planetRb != null)
            {
                planetRb.AddForce(new Vector2(force.x, force.y), ForceMode2D.Impulse);
                Debug.Log($"BotController: Synced planet propulsion for planet ViewID={planetViewID}, force={force}");
            }
            else
            {
                Debug.LogWarning($"BotController: Rigidbody2D not found on planet ViewID={planetViewID}");
            }
        }
        else
        {
            Debug.LogWarning($"BotController: PhotonView not found for planet ViewID={planetViewID}");
        }
    }
    private GameObject FindNearestPlanet(float range)
    {
        string[] planetTags = { "Planet", "halfPlanet", "quarterPlanet" };
        GameObject nearest = null;
        float minDistance = range;

        foreach (var tag in planetTags)
        {
            GameObject[] planets = GameObject.FindGameObjectsWithTag(tag);
            foreach (var planet in planets)
            {
                if (!planet.activeInHierarchy) continue;
                float distance = Vector2.Distance(transform.position, planet.transform.position);
                if (distance < minDistance)
                {
                    minDistance = distance;
                    nearest = planet;
                }
            }
        }

        Debug.Log($"BotController: {NickName} FindNearestPlanet, nearest={(nearest != null ? nearest.name : "none")}, distance={(nearest != null ? minDistance.ToString("F2") : "N/A")}");
        return nearest;
    }
    private void PerformRandomAction()
    {
        if (!photonView.IsMine || HasDied)
        {
            Debug.Log($"BotController: {NickName} PerformRandomAction skipped, IsMine={photonView.IsMine}, HasDied={HasDied}");
            return;
        }

        if (currentState == BotState.Mining)
        {
            Debug.Log($"BotController: {NickName} PerformRandomAction skipped, in Mining state, prioritizing ore");
            return;
        }

        GameObject targetPlanet = FindNearestPlanet(100f);
        if (targetPlanet != null && laserBeam != null && !laserBeam.IsLaserFiring && !laserBeam.IsJammed)
        {
            FireLaserAtTarget(targetPlanet.transform.position);
            CustomLogger.Log($"BotController: {NickName} fired laser at planet {targetPlanet.name}");

            DroidShooting[] droidShootings = GetComponentsInChildren<DroidShooting>();
            foreach (DroidShooting droidShooting in droidShootings)
            {
                if (droidShooting != null && droidShooting.IsInitialized && droidShooting.canShoot)
                {
                    Vector2 aimDir = (targetPlanet.transform.position - droidShooting.transform.position).normalized;
                    droidShooting.aimDirection = aimDir;
                    if (fireTimer <= 0)
                    {
                        droidShooting.FireProjectile();
                        Debug.Log($"BotController: {NickName} firing projectile at planet {targetPlanet.name}, aimDirection={aimDir}");
                    }
                }
            }
            if (fireTimer <= 0)
            {
                fireTimer = fireRate;
            }
            return;
        }
        else
        {
            Debug.Log($"BotController: {NickName} skipped laser firing, targetPlanet={(targetPlanet != null ? targetPlanet.name : "null")}, laserBeam={(laserBeam != null ? "present" : "null")}, IsLaserFiring={laserBeam?.IsLaserFiring}, IsJammed={laserBeam?.IsJammed}");
        }

        float[] weights = { 0.3f, 0.3f, 0.3f, 0.1f }; // Shield, turret, bomb, idle
        float random = Random.value;
        float cumulative = 0f;
        int action = 3; // Default to idle

        for (int i = 0; i < weights.Length; i++)
        {
            cumulative += weights[i];
            if (random < cumulative)
            {
                action = i;
                break;
            }
        }

        Debug.Log($"BotController: {NickName} PerformRandomAction, selected action={action}");

        switch (action)
        {
            case 0: // Activate shield
                if (shockShield != null && !shockShield.isShieldActive && shockShield.GetEnergy() >= 50f)
                {
                    shockShield.ToggleShield();
                    CustomLogger.Log($"BotController: {NickName} activated shield");
                }
                else
                {
                    Debug.Log($"BotController: {NickName} cannot activate shield, shockShield={(shockShield != null ? "present" : "null")}, isShieldActive={shockShield?.isShieldActive}, energy={shockShield?.GetEnergy()}");
                }
                break;
            case 1: // Activate twin turret
                if (twinTurret != null && !twinTurret.TwinTurretActive && !twinTurret.OnCooldown && twinTurret.droidPrefab != null)
                {
                    twinTurret.ActivateTwinTurret();
                    CustomLogger.Log($"BotController: {NickName} activated twin turret");
                }
                else
                {
                    Debug.Log($"BotController: {NickName} cannot activate twin turret, twinTurret={(twinTurret != null ? "present" : "null")}, active={twinTurret?.TwinTurretActive}, onCooldown={twinTurret?.OnCooldown}, droidPrefab={(twinTurret?.droidPrefab != null ? "present" : "null")}");
                }
                break;
            case 2: // Deploy bomb
                BombManager bombManager = GetComponentInChildren<BombManager>();
                if (bombManager != null)
                {
                    bombManager.TryDeployBomb();
                    CustomLogger.Log($"BotController: {NickName} requested bomb deployment via BombManager");
                }
                else
                {
                    CustomLogger.LogError($"BotController: BombManager not found for {NickName}.");
                }
                break;
            case 3: // Idle
                Debug.Log($"BotController: {NickName} performing idle action");
                break;
        }
    }

    public void AddBrightMatter(int amount)
    {
        if (amount == 0)
        {
            Debug.Log($"BotController: {NickName} AddBrightMatter skipped, amount={amount}");
            return;
        }
        brightMatterCollected += amount;
        if (brightMatterCollected < 0) brightMatterCollected = 0;
        CustomLogger.Log($"BotController: {NickName} collected {amount} BrightMatter, total={brightMatterCollected}");
        SyncBrightMatter(brightMatterCollected);
    }

    public void SyncBrightMatter(int amount)
    {
        brightMatterCollected = amount;
        if (brightMatterCollected < 0) brightMatterCollected = 0;
        if (upgradeManager != null)
        {
            upgradeManager.SyncBrightMatter(brightMatterCollected);
            Debug.Log($"BotController: {NickName} Synced BrightMatter={brightMatterCollected} with BotUpgradeManager");
        }
    }

    public int GetBrightMatter()
    {
        return brightMatterCollected;
    }

    private void TryPurchaseUpgrade()
    {
        if (!photonView.IsMine || HasDied || upgradeManager == null)
        {
            Debug.Log($"BotController: {NickName} TryPurchaseUpgrade skipped, IsMine={photonView.IsMine}, HasDied={HasDied}, upgradeManager={(upgradeManager != null ? "present" : "null")}");
            return;
        }

        int[] upgradeLevels = upgradeManager.UpgradeLevels;
        int[] upgradeCosts = upgradeManager.UpgradeCosts;
        int maxLevel = 5;

        // Prioritize aggressive upgrades: Bullet Damage, Bullet Speed, Fire Rate, Shield, Teleport
        int[] preferredUpgrades = { 8, 4, 3, 6, 7 };
        List<int> availableUpgrades = new List<int>();

        foreach (int index in preferredUpgrades)
        {
            if (upgradeLevels[index] < maxLevel && brightMatterCollected >= upgradeCosts[upgradeLevels[index]])
            {
                availableUpgrades.Add(index);
            }
        }

        if (availableUpgrades.Count == 0)
        {
            for (int index = 0; index < 9; index++)
            {
                if (!preferredUpgrades.Contains(index) && upgradeLevels[index] < maxLevel && brightMatterCollected >= upgradeCosts[upgradeLevels[index]])
                {
                    availableUpgrades.Add(index);
                }
            }
        }

        if (availableUpgrades.Count > 0)
        {
            int selectedIndex = availableUpgrades[Random.Range(0, availableUpgrades.Count)];
            upgradeManager.TryPurchaseUpgrade(selectedIndex);
            Debug.Log($"BotController: {NickName} purchased upgrade index={selectedIndex} (BrightMatter={brightMatterCollected})");
        }
        else
        {
            Debug.Log($"BotController: {NickName} no affordable upgrades available, BrightMatter={brightMatterCollected}");
        }
    }

    public void AddPoints(int points)
    {
        int currentPoints = (int)(CustomProperties["Points"] ?? 0);
        currentPoints += points;
        CustomProperties["Points"] = currentPoints;
        photonView.RPC("SyncPoints", RpcTarget.AllBuffered, currentPoints);
        Debug.Log($"BotController: Awarded {points} points to {NickName}, total={currentPoints}");

        ScoreboardManager scoreboard = FindFirstObjectByType<ScoreboardManager>();
        if (scoreboard != null)
        {
            scoreboard.UpdateScoreboard();
            Debug.Log($"BotController: Triggered ScoreboardManager.UpdateScoreboard for {NickName}, Points={currentPoints}");
        }
        else
        {
            Debug.LogWarning($"BotController: ScoreboardManager not found for {NickName} when adding points");
        }
    }
    private IPlayer FindPlayerByActorNumber(int actorNumber)
    {
        if (actorNumber == -1)
        {
            CustomLogger.Log($"BotController: killerActorNumber=-1, no player lookup needed");
            return null;
        }

        foreach (Player photonPlayer in PhotonNetwork.PlayerList)
        {
            if (photonPlayer.ActorNumber == actorNumber)
            {
                GameObject playerObj = photonPlayer.TagObject as GameObject;
                if (playerObj != null)
                {
                    PlayerController playerController = playerObj.GetComponent<PlayerController>();
                    if (playerController != null)
                    {
                        CustomLogger.Log($"BotController: Found PlayerController for ActorNumber={actorNumber}, NickName={photonPlayer.NickName}, ViewID={playerObj.GetComponent<PhotonView>().ViewID}");
                        return playerController;
                    }
                }
                CustomLogger.LogWarning($"BotController: No valid TagObject or PlayerController for ActorNumber={actorNumber}, NickName={photonPlayer.NickName}");
                return null;
            }
        }

        BotController[] bots = FindObjectsByType<BotController>(FindObjectsSortMode.None);
        foreach (BotController bot in bots)
        {
            if (bot.ActorNumber == actorNumber)
            {
                CustomLogger.Log($"BotController: Found BotController for ActorNumber={actorNumber}, NickName={bot.NickName}, ViewID={bot.GetComponent<PhotonView>().ViewID}");
                return bot;
            }
        }

        CustomLogger.LogWarning($"BotController: No player or bot found for ActorNumber={actorNumber}. Active players: {string.Join(", ", PhotonNetwork.PlayerList.Select(p => $"Actor={p.ActorNumber},Nick={p.NickName}"))}, Active bots: {string.Join(", ", bots.Select(b => $"Actor={b.ActorNumber},Nick={b.NickName}"))}");
        return null;
    }

    [PunRPC]
    private void UpdateCustomProperties(ExitGames.Client.Photon.Hashtable props)
    {
        lock (customPropertiesLock)
        {
            ExitGames.Client.Photon.Hashtable propsCopy = new ExitGames.Client.Photon.Hashtable();
            foreach (DictionaryEntry entry in props)
            {
                propsCopy[entry.Key] = entry.Value;
            }

            foreach (DictionaryEntry entry in propsCopy)
            {
                CustomProperties[entry.Key] = entry.Value;
            }
            CustomLogger.Log($"BotController: Updated CustomProperties for bot {NickName}, ActorNumber={ActorNumber}, Properties={string.Join(", ", CustomProperties.Keys.Cast<object>().Select(k => $"{k}={CustomProperties[k]}"))}");
        }
    }

    [PunRPC]
    private void SyncPoints(int points)
    {
        CustomProperties["Points"] = points;
        Debug.Log($"BotController: Synced points for {NickName}, points={points}");
        if (!photonView.IsMine)
        {
            ScoreboardManager scoreboard = FindFirstObjectByType<ScoreboardManager>();
            if (scoreboard != null)
            {
                scoreboard.UpdateScoreboard();
                Debug.Log($"BotController: Triggered ScoreboardManager.UpdateScoreboard on non-owner client for {NickName}, Points={points}");
            }
        }
    }

    public void TakeDamage(float damage, bool ignoreShield, int killerActorNumber, PlayerHealth.DeathCause cause)
    {
        if (HasDied) return;

        if (SceneManager.GetActiveScene().name == "TeamMoonRan" && killerActorNumber != -1)
        {
            IPlayer attacker = FindPlayerByActorNumber(killerActorNumber);
            if (attacker != null && attacker.CustomProperties.ContainsKey("Team") && CustomProperties.ContainsKey("Team"))
            {
                string attackerTeam = attacker.CustomProperties["Team"].ToString();
                string botTeam = CustomProperties["Team"].ToString();
                if (attackerTeam == botTeam)
                {
                    CustomLogger.Log($"BotController: {NickName} (Team {botTeam}) ignored damage from ActorNumber={killerActorNumber} (Team {attackerTeam}) due to same team in TeamMoonRan");
                    return;
                }
            }
        }

        float adjustedDamage = damage * (1f - (damageReductionLevel * 0.05f));
        if (!ignoreShield && shockShield != null && shockShield.isShieldActive && shockShield.GetEnergy() > 0)
        {
            float shieldAbsorption = Mathf.Min(shockShield.GetEnergy(), adjustedDamage);
            shockShield.DrainEnergy(shieldAbsorption);
            adjustedDamage -= shieldAbsorption;
            CustomLogger.Log($"BotController: {NickName} shield absorbed {shieldAbsorption} damage, remaining shield={shockShield.GetEnergy()}, remaining damage={adjustedDamage}");
            if (shockShield.GetEnergy() <= 0)
            {
                shockShield.isShieldActive = false;
                photonView.RPC("SyncShieldState", RpcTarget.All, false);
                CustomLogger.Log($"BotController: {NickName} shield depleted, ViewID={photonView.ViewID}");
            }
        }

        if (adjustedDamage > 0)
        {
            currentHealth = (int)Mathf.Max(0, currentHealth - adjustedDamage);
            CustomLogger.Log($"BotController: {NickName} took {adjustedDamage} damage, currentHealth={currentHealth}, ViewID={photonView.ViewID}, killerActorNumber={killerActorNumber}, cause={cause}");
            StartCoroutine(FlashDamage());
            if (currentHealth <= 0)
            {
                lastDeathCause = cause;
                lastKillerViewID = killerActorNumber; // Store as ActorNumber for consistency
                Die();
            }
        }
    }

    private IEnumerator FlashDamage()
    {
        if (spriteRenderer == null) yield break;
        Color originalColor = spriteRenderer.color;
        spriteRenderer.color = Color.red;
        yield return new WaitForSeconds(0.1f);
        spriteRenderer.color = originalColor;
        yield return new WaitForSeconds(0.1f);
        spriteRenderer.color = Color.red;
        yield return new WaitForSeconds(0.1f);
        spriteRenderer.color = originalColor;
    }

    private void Die()
    {
        if (HasDied || !photonView.IsMine)
        {
            Debug.Log($"BotController: Die skipped for {NickName}, HasDied={HasDied}, photonView.IsMine={photonView.IsMine}");
            return;
        }
        HasDied = true;

        DroidShooting droidShooting = GetComponentsInChildren<DroidShooting>().FirstOrDefault();
        if (droidShooting != null)
        {
            droidShooting.canShoot = false;
            Debug.Log($"BotController: Disabled shooting for {NickName}, canShoot={droidShooting.canShoot}");
        }

        if (twinTurret != null && twinTurret.TwinTurretActive)
        {
            twinTurret.DeactivateTwinTurret();
            Debug.Log($"BotController: Deactivated twin turret for {NickName} on death");
        }

        CustomLogger.Log($"BotController: {NickName} died, processing death with DeathCause={lastDeathCause}, KillerActorNumber={lastKillerViewID}");

        // Sync death state
        CustomProperties["DeathCause"] = lastDeathCause.ToString();
        CustomProperties["KillerViewID"] = lastKillerViewID;
        photonView.RPC("SyncDeathState", RpcTarget.AllBuffered, lastDeathCause.ToString(), lastKillerViewID);
        Debug.Log($"BotController: Synced DeathCause={lastDeathCause}, KillerActorNumber={lastKillerViewID} for {NickName}");

        bool isPlayerKill = lastDeathCause == PlayerHealth.DeathCause.Projectile ||
                            lastDeathCause == PlayerHealth.DeathCause.ElephantBomb ||
                            lastDeathCause == PlayerHealth.DeathCause.PlanetDebris;

        // Deduct points for non-player kills
        if (!isPlayerKill)
        {
            AddPoints(-50);
            Debug.Log($"BotController: Deducted 50 points for {NickName} (cause={lastDeathCause})");
        }

        // Award points to killer for player kills
        if (isPlayerKill && lastKillerViewID != -1 && lastKillerViewID != ActorNumber)
        {
            IPlayer killer = FindPlayerByActorNumber(lastKillerViewID);
            if (killer != null)
            {
                bool awardPoints = true;
                if (SceneManager.GetActiveScene().name == "TeamMoonRan")
                {
                    string killerTeam = killer.CustomProperties.ContainsKey("Team") ? killer.CustomProperties["Team"].ToString() : "None";
                    string victimTeam = CustomProperties.ContainsKey("Team") ? CustomProperties["Team"].ToString() : "None";
                    if (killerTeam != "None" && killerTeam == victimTeam)
                    {
                        awardPoints = false;
                        CustomLogger.Log($"BotController: No points awarded, killer {killer.NickName} (Team={killerTeam}) and victim {NickName} (Team={victimTeam}) are on the same team in TeamMoonRan");
                    }
                }

                if (awardPoints)
                {
                    ScoreboardManager scoreboard = FindFirstObjectByType<ScoreboardManager>();
                    int pointsToAward = (scoreboard != null && scoreboard.IsTopPlayer(ActorNumber)) ? 200 : 100;
                    killer.AddPoints(pointsToAward);
                    killer.OnPlayerKilled(NickName);
                    CustomLogger.Log($"BotController: Awarded {pointsToAward} points to killer {killer.NickName} (ActorNumber={lastKillerViewID}) for killing {NickName}");
                }
            }
            else
            {
                CustomLogger.LogWarning($"BotController: Killer not found for ActorNumber={lastKillerViewID} when processing death of {NickName}");
            }
        }

        StartCoroutine(PerformRespawnWithSync());
    }

    [PunRPC]
    private void SyncDeathState(string deathCause, int killerViewID)
    {
        CustomProperties["DeathCause"] = deathCause;
        CustomProperties["KillerViewID"] = killerViewID;
        Debug.Log($"BotController: Synced death state for {NickName}, DeathCause={deathCause}, KillerViewID={killerViewID}");
    }

    private IEnumerator PerformRespawnWithSync()
    {
        CustomLogger.Log($"BotController: Respawn started for {NickName}, ViewID={photonView.ViewID}");

        Vector3 respawnPosition = Vector3.zero;
        GameObject spaceship = null;
        BoundaryManager boundaryManager = FindFirstObjectByType<BoundaryManager>();

        GameObject[] spaceships = GameObject.FindGameObjectsWithTag("SpaceShip");
        foreach (GameObject ship in spaceships)
        {
            SpaceshipMarker marker = ship.GetComponent<SpaceshipMarker>();
            if (marker != null && marker.ownerId == ActorNumber)
            {
                spaceship = ship;
                if (ship.GetComponent<PhotonView>() != null)
                {
                    int newViewID = ship.GetComponent<PhotonView>().ViewID;
                    CustomProperties["SpaceshipViewID"] = newViewID;
                    CustomLogger.Log($"BotController: Found own spaceship via ownerId={ActorNumber}, Name={ship.name}, Set SpaceshipViewID={newViewID}, Position={ship.transform.position}");
                }
                break;
            }
        }

        if (spaceship == null && CustomProperties.TryGetValue("SpaceshipViewID", out object viewID))
        {
            PhotonView spaceshipView = PhotonView.Find((int)viewID);
            if (spaceshipView != null && spaceshipView.gameObject != null && spaceshipView.gameObject.CompareTag("SpaceShip"))
            {
                spaceship = spaceshipView.gameObject;
                CustomLogger.Log($"BotController: Found spaceship via SpaceshipViewID={viewID}, Name={spaceship.name}, Position={spaceship.transform.position}");
            }
            else
            {
                CustomLogger.LogWarning($"BotController: SpaceshipViewID={viewID} invalid or not tagged 'SpaceShip', spaceshipView={spaceshipView != null}, gameObject={spaceshipView?.gameObject != null}");
            }
        }

        if (spaceship != null && boundaryManager != null)
        {
            const int maxAttempts = 10;
            int attempts = 0;
            bool validPosition = false;
            Vector3 spaceshipPos = spaceship.transform.position;
            float spawnDistance = 25f;
            float safeBoundary = boundarySize / 2 - bufferDistance;

            while (attempts < maxAttempts && !validPosition)
            {
                Vector2 offset = Random.insideUnitCircle.normalized * Random.Range(5f, spawnDistance);
                respawnPosition = spaceshipPos + new Vector3(offset.x, offset.y, 0f);
                respawnPosition.x = Mathf.Clamp(respawnPosition.x, -safeBoundary, safeBoundary);
                respawnPosition.y = Mathf.Clamp(respawnPosition.y, -safeBoundary, safeBoundary);

                validPosition = boundaryManager.IsValidBotPosition(respawnPosition, ActorNumber);
                CustomLogger.Log($"BotController: Respawn attempt {attempts + 1}/{maxAttempts} for {NickName}, position={respawnPosition}, valid={validPosition}");

                if (!validPosition)
                {
                    GameObject[] bots = GameObject.FindGameObjectsWithTag("Bot");
                    foreach (GameObject bot in bots)
                    {
                        if (bot.activeInHierarchy && bot != gameObject)
                        {
                            BotController botController = bot.GetComponent<BotController>();
                            if (botController != null && botController.ActorNumber != ActorNumber)
                            {
                                float distance = Vector3.Distance(respawnPosition, bot.transform.position);
                                if (distance < boundaryManager.minBotSpacing)
                                {
                                    CustomLogger.Log($"BotController: Invalid position due to bot {botController.NickName}, distance={distance:F2}, minBotSpacing={boundaryManager.minBotSpacing}");
                                }
                            }
                        }
                    }
                    GameObject[] otherShips = GameObject.FindGameObjectsWithTag("SpaceShip");
                    foreach (GameObject ship in otherShips)
                    {
                        if (ship.activeInHierarchy && ship != spaceship)
                        {
                            SpaceshipMarker marker = ship.GetComponent<SpaceshipMarker>();
                            if (marker != null && marker.ownerId != ActorNumber)
                            {
                                float distance = Vector3.Distance(respawnPosition, ship.transform.position);
                                if (distance < boundaryManager.minBotSpacing)
                                {
                                    CustomLogger.Log($"BotController: Invalid position due to spaceship ownerId={marker.ownerId}, distance={distance:F2}, minBotSpacing={boundaryManager.minBotSpacing}");
                                }
                            }
                        }
                    }
                }
                attempts++;
            }

            if (!validPosition)
            {
                CustomLogger.LogWarning($"BotController: Could not find valid respawn position for {NickName} after {maxAttempts} attempts, trying closer fallback");
                float fallbackDistance = 10f;
                for (int i = 0; i < 5 && !validPosition; i++)
                {
                    Vector2 offset = Random.insideUnitCircle.normalized * fallbackDistance;
                    respawnPosition = spaceshipPos + new Vector3(offset.x, offset.y, 0f);
                    respawnPosition.x = Mathf.Clamp(respawnPosition.x, -safeBoundary, safeBoundary);
                    respawnPosition.y = Mathf.Clamp(respawnPosition.y, -safeBoundary, safeBoundary);
                    validPosition = boundaryManager.IsValidBotPosition(respawnPosition, ActorNumber);
                    CustomLogger.Log($"BotController: Fallback attempt {i + 1}/5 for {NickName}, position={respawnPosition}, valid={validPosition}, fallbackDistance={fallbackDistance}");
                    fallbackDistance += 5f;
                }

                if (!validPosition)
                {
                    respawnPosition = spaceshipPos + new Vector3(5f, 5f, 0f);
                    respawnPosition.x = Mathf.Clamp(respawnPosition.x, -safeBoundary, safeBoundary);
                    respawnPosition.y = Mathf.Clamp(respawnPosition.y, -safeBoundary, safeBoundary);
                    CustomLogger.LogWarning($"BotController: All fallback attempts failed for {NickName}, using minimal offset position={respawnPosition}");
                }
            }
            CustomLogger.Log($"BotController: Set respawn position to {respawnPosition} near spaceship {spaceship.name}");
        }
        else
        {
            const int maxAttempts = 10;
            int attempts = 0;
            bool validPosition = false;
            float safeBoundary = boundarySize / 2 - bufferDistance;

            while (attempts < maxAttempts && !validPosition)
            {
                respawnPosition = new Vector3(
                    Random.Range(-safeBoundary, safeBoundary),
                    Random.Range(-safeBoundary, safeBoundary),
                    0f
                );
                if (boundaryManager != null)
                {
                    validPosition = boundaryManager.IsValidBotPosition(respawnPosition, ActorNumber);
                    CustomLogger.Log($"BotController: Fallback respawn attempt {attempts + 1}/{maxAttempts} for {NickName}, position={respawnPosition}, valid={validPosition}");
                }
                else
                {
                    CustomLogger.LogWarning($"BotController: BoundaryManager not found for spawn position check, assuming valid position");
                    validPosition = true;
                }
                attempts++;
            }

            if (!validPosition)
            {
                CustomLogger.LogWarning($"BotController: Could not find valid fallback respawn position for {NickName} after {maxAttempts} attempts, using origin");
                respawnPosition = Vector3.zero;
            }
            CustomLogger.LogWarning($"BotController: No spaceship found for {NickName}, respawning at {respawnPosition}. Check SpaceshipMarker.ownerId and 'SpaceShip' tag.");
        }

        transform.position = respawnPosition;
        rb.linearVelocity = Vector2.zero;
        CustomLogger.Log($"BotController: Moved {NickName} to position {transform.position}");

        SpriteRenderer[] renderers = GetComponentsInChildren<SpriteRenderer>();
        Collider2D[] colliders = GetComponentsInChildren<Collider2D>();
        foreach (var renderer in renderers)
        {
            renderer.enabled = true;
        }
        foreach (var collider in colliders)
        {
            collider.enabled = true;
        }
        if (renderers.Length == 0)
            CustomLogger.LogError("BotController: No SpriteRenderer components found, bot or children may not be visible");
        if (colliders.Length == 0)
            CustomLogger.LogError("BotController: No Collider2D components found, bot or children may not interact");
        CustomLogger.Log($"BotController: Ensured visibility and interactions (renderers={renderers.Length}, colliders={colliders.Length}) for {NickName}");

        DroidShooting droidShooting = GetComponentsInChildren<DroidShooting>().FirstOrDefault();
        if (droidShooting != null)
        {
            droidShooting.canShoot = true;
            CustomLogger.Log($"BotController: Re-enabled shooting for {NickName}, canShoot={droidShooting.canShoot}");
        }

        currentHealth = maxHealth;
        HasDied = false;
        CustomLogger.Log($"BotController: Reset health for {NickName}, Health={currentHealth}, HasDied={HasDied}");

        SyncBrightMatter(brightMatterCollected);
        CustomLogger.Log($"BotController: Preserved brightMatterCollected={brightMatterCollected} for {NickName}");

        int currentPoints = (int)(CustomProperties["Points"] ?? 0);
        CustomProperties["Points"] = currentPoints;
        photonView.RPC("SyncPoints", RpcTarget.AllBuffered, currentPoints);
        CustomLogger.Log($"BotController: Restored points for {NickName}, Points={currentPoints}");

        RandomPlanetGenerator generator = FindFirstObjectByType<RandomPlanetGenerator>();
        if (generator != null)
        {
            generator.ReAddPlayer(ActorNumber, gameObject);
            CustomLogger.Log($"BotController: Notified RandomPlanetGenerator to re-add ActorNumber={ActorNumber} after respawn");
        }
        else
        {
            CustomLogger.LogWarning($"BotController: PlanetGenerator not found, retrying notification for {NickName}");
            StartCoroutine(RetryNotifyRandomPlanetGenerator());
        }

        ScoreboardManager scoreboardManager = FindFirstObjectByType<ScoreboardManager>();
        if (scoreboardManager != null)
        {
            scoreboardManager.UpdateScoreboard();
            CustomLogger.Log($"BotController: Triggered scoreboard update after respawn for {NickName}, Points={currentPoints}");
        }
        else
        {
            CustomLogger.LogWarning($"BotController: ScoreboardManager not found for {NickName} during respawn");
        }

        currentState = BotState.Idle;
        moveDirection = Vector2.zero;
        targetPlayer = null;
        targetOre = null;
        targetEnemy = null;
        CustomLogger.Log($"BotController: Respawn completed for {NickName}, state reset to Idle");

        yield return new WaitForSeconds(0.5f);
        photonView.RPC("SyncRespawnState", RpcTarget.AllBuffered, transform.position, currentHealth, (int)currentState);
    }

    [PunRPC]
    private void SyncRespawnState(Vector3 position, int health, int state)
    {
        transform.position = position;
        currentHealth = health;
        currentState = (BotState)state;
        HasDied = false;
        CustomLogger.Log($"BotController: Synced respawn state for {NickName}, Position={position}, Health={currentHealth}, State={currentState}");
    }

    private IEnumerator RetryNotifyRandomPlanetGenerator()
    {
        int maxRetries = 5;
        int retryCount = 0;
        while (retryCount < maxRetries)
        {
            RandomPlanetGenerator generator = FindFirstObjectByType<RandomPlanetGenerator>();
            if (generator != null)
            {
                generator.ReAddPlayer(ActorNumber, gameObject);
                CustomLogger.Log($"BotController: Successfully re-notified RandomPlanetGenerator for {NickName} on retry {retryCount + 1}");
                yield break;
            }
            retryCount++;
            CustomLogger.LogWarning($"BotController: RandomPlanetGenerator not found for {NickName} on retry {retryCount}/{maxRetries}");
            yield return new WaitForSeconds(1f);
        }
        CustomLogger.LogError($"BotController: Failed to re-notify RandomPlanetGenerator for {NickName} after {maxRetries} retries");
    }

    private void CheckSpaceshipDistance()
    {
        StartCoroutine(CheckSpaceshipDistanceWithRetry());
    }

    private IEnumerator CheckSpaceshipDistanceWithRetry()
    {
        int maxRetries = 10;
        int retryCount = 0;
        GameObject spaceship = null;
        while (retryCount < maxRetries)
        {
            GameObject[] spaceships = GameObject.FindGameObjectsWithTag("SpaceShip");
            foreach (GameObject ship in spaceships)
            {
                SpaceshipMarker marker = ship.GetComponent<SpaceshipMarker>();
                if (marker != null && marker.ownerId == ActorNumber)
                {
                    spaceship = ship;
                    CustomLogger.Log($"BotController: Found spaceship for {NickName}, ownerId={marker.ownerId}, position={ship.transform.position}");
                    break;
                }
            }
            if (spaceship == null && CustomProperties.TryGetValue("SpaceshipViewID", out object viewID))
            {
                PhotonView spaceshipView = PhotonView.Find((int)viewID);
                if (spaceshipView != null && spaceshipView.gameObject != null && spaceshipView.gameObject.CompareTag("SpaceShip"))
                {
                    spaceship = spaceshipView.gameObject;
                    CustomLogger.Log($"BotController: Found spaceship for {NickName} via SpaceshipViewID={viewID}, position={spaceship.transform.position}");
                }
            }
            if (spaceship != null)
            {
                float distance = Vector3.Distance(transform.position, spaceship.transform.position);
                if (distance > 6000f)
                {
                    CustomLogger.Log($"BotController: {NickName} is {distance:F2} units from spaceship, triggering death due to out-of-range");
                    TakeDamage(maxHealth, true, -1, PlayerHealth.DeathCause.OutOfRange);
                }
                else
                {
                    CustomLogger.Log($"BotController: {NickName} distance to spaceship {spaceship.name} is {distance:F2} units");
                }
                yield break;
            }
            CustomLogger.LogWarning($"BotController: No spaceship found for {NickName}, ActorNumber={ActorNumber}, retry {retryCount + 1}/{maxRetries}");
            yield return new WaitForSeconds(1f);
            retryCount++;
        }
        CustomLogger.LogWarning($"BotController: No spaceship found for {NickName} after {maxRetries} retries, triggering respawn");
        TakeDamage(maxHealth, true, -1, PlayerHealth.DeathCause.OutOfRange);
    }

    public void OnPhotonSerializeView(PhotonStream stream, PhotonMessageInfo info)
    {
        if (stream.IsWriting)
        {
            stream.SendNext(currentHealth);
            stream.SendNext(HasDied);
            stream.SendNext((int)currentState);
            stream.SendNext(brightMatterCollected);
            stream.SendNext(damageReductionLevel);
        }
        else
        {
            currentHealth = (int)stream.ReceiveNext();
            HasDied = (bool)stream.ReceiveNext();
            currentState = (BotState)stream.ReceiveNext();
            brightMatterCollected = (int)stream.ReceiveNext();
            damageReductionLevel = (int)stream.ReceiveNext();
            SyncBrightMatter(brightMatterCollected);
        }
    }
    public void SetTeam(PlayerHealth.Team team)
    {
        CustomProperties["Team"] = team.ToString();
        if (photonView.IsMine)
        {
            photonView.RPC("UpdateCustomProperties", RpcTarget.AllBuffered, CustomProperties);
        }
        botTeam = team;
        CustomLogger.Log($"BotController: Set team for {NickName} to {team}, ActorNumber={ActorNumber}, ViewID={photonView.ViewID}");
    }
    public override void OnPlayerLeftRoom(Player otherPlayer)
    {
        base.OnPlayerLeftRoom(otherPlayer);
        if (PhotonNetwork.IsMasterClient)
        {
            Debug.Log($"BotController: Player {otherPlayer.NickName} left room, ActorNumber={otherPlayer.ActorNumber}");
            RandomPlanetGenerator generator = FindFirstObjectByType<RandomPlanetGenerator>();
            if (generator != null)
            {
                generator.RemovePlayer(otherPlayer.ActorNumber);
            }
        }
    }
    public void OnPlayerKilled(string killedPlayerName)
    {
        if (!photonView.IsMine) return;
        Debug.Log($"BotController: {NickName} received kill notification for {killedPlayerName}, but bots don't display UI");
    }

}