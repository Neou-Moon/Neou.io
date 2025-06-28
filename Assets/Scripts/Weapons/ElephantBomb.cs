using UnityEngine;
using Photon.Pun;
using System.Collections;

public class ElephantBomb : MonoBehaviourPun
{
    public GameObject explosionPrefab;
    private const float EXPLOSION_DELAY = 5f; // Always 5 seconds
    public float blastRadius = 5f;
    public float debrisForce = 70000f;
    public int enemyDamage = 100;
    public int playerDamage = 25; // 25% of max health (100) at damage resistance level 0
    public float moveSpeed = 5f;

    private bool hasExploded;
    private Transform target;
    private int ownerActorNumber = -1; // Store owner's ActorNumber
    private int ownerViewID = -1; // Store BombManager's PhotonView ID
    private Rigidbody2D rb;
    private Coroutine explosionCountdownCoroutine;
    private float deploymentTime; // Tracks when bomb was deployed
    private bool hasLoggedInvalidOwner; // Prevent warning spam

    void Start()
    {
        if (photonView == null)
        {
            Debug.LogError($"ElephantBomb: PhotonView missing on {gameObject.name}, destroying object, frame={Time.frameCount}.");
            Destroy(gameObject);
            return;
        }

        if (photonView.IsMine)
        {
            rb = GetComponent<Rigidbody2D>();
            if (rb == null)
            {
                rb = gameObject.AddComponent<Rigidbody2D>();
                rb.mass = 1f;
                rb.linearDamping = 0f;
                rb.angularDamping = 0f;
                rb.gravityScale = 0f;
                rb.bodyType = RigidbodyType2D.Dynamic;
                rb.constraints = RigidbodyConstraints2D.None;
                rb.simulated = true;
                Debug.Log($"ElephantBomb: Added Rigidbody2D to {gameObject.name}, ViewID={photonView.ViewID}, frame={Time.frameCount}.");
            }

            Collider2D collider = GetComponent<Collider2D>();
            if (collider == null)
            {
                collider = gameObject.AddComponent<CircleCollider2D>();
                Debug.Log($"ElephantBomb: Added CircleCollider2D to {gameObject.name}, ViewID={photonView.ViewID}, frame={Time.frameCount}.");
            }
            if (!collider.isTrigger)
            {
                collider.isTrigger = true;
                Debug.Log($"ElephantBomb: Set {gameObject.name}'s Collider2D to trigger, ViewID={photonView.ViewID}, frame={Time.frameCount}.");
            }

            deploymentTime = Time.time;
            explosionCountdownCoroutine = StartCoroutine(ExplosionCountdown());
            StartCoroutine(DelayedUpdateTarget()); // Delayed start
            Debug.Log($"ElephantBomb: Spawned, ActorNr={ownerActorNumber}, ManagerViewID={ownerViewID}, ViewID={photonView.ViewID}, deploymentTime={deploymentTime:F2}, frame={Time.frameCount}.");
        }
    }

    [PunRPC]
    public void SetOwner(int actorNumber, int managerViewID)
    {
        if (actorNumber <= 0)
        {
            Debug.LogWarning($"ElephantBomb: Invalid ActorNumber={actorNumber} received in SetOwner, ViewID={photonView.ViewID}, frame={Time.frameCount}. Using default -1.");
            ownerActorNumber = -1;
        }
        else
        {
            ownerActorNumber = actorNumber;
        }
        ownerViewID = managerViewID;
        Debug.Log($"ElephantBomb: Set owner ActorNumber={ownerActorNumber}, ManagerViewID={ownerViewID}, ViewID={photonView.ViewID}, frame={Time.frameCount}.");
    }

    private IEnumerator DelayedUpdateTarget()
    {
        yield return new WaitForSeconds(0.1f); // Delay to allow network sync
        StartCoroutine(UpdateTarget());
    }

    private IEnumerator ExplosionCountdown()
    {
        Debug.Log($"ElephantBomb: ExplosionCountdown started, waiting {EXPLOSION_DELAY}s, ViewID={photonView.ViewID}, frame={Time.frameCount}.");
        yield return new WaitForSeconds(EXPLOSION_DELAY);
        Debug.Log($"ElephantBomb: ExplosionCountdown completed, triggering explosion, ViewID={photonView.ViewID}, frame={Time.frameCount}.");
        photonView.RPC("Explode", RpcTarget.All);
    }

    private IEnumerator UpdateTarget()
    {
        while (!hasExploded)
        {
            GameObject[] enemies = GameObject.FindGameObjectsWithTag("Enemy");
            float closestDist = float.MaxValue;
            Transform closestTarget = null;

            // Prioritize enemies (no team check needed for enemies)
            foreach (GameObject enemy in enemies)
            {
                float dist = Vector2.Distance(transform.position, enemy.transform.position);
                if (dist < closestDist)
                {
                    closestDist = dist;
                    closestTarget = enemy.transform;
                }
            }

            bool isTeamMoonRan = UnityEngine.SceneManagement.SceneManager.GetActiveScene().name == "TeamMoonRan";
            string ownerTeam = "None";
            yield return StartCoroutine(GetOwnerTeamCoroutine(team => ownerTeam = team));

            // Check players if no enemy is closer
            if (closestTarget == null)
            {
                PlayerController[] players = Object.FindObjectsByType<PlayerController>(FindObjectsSortMode.None);
                foreach (PlayerController player in players)
                {
                    if (player.photonView != null && player.photonView.ControllerActorNr != ownerActorNumber)
                    {
                        if (isTeamMoonRan)
                        {
                            string playerTeam = GetPlayerTeam(player.photonView);
                            if (ownerTeam != "None" && playerTeam == ownerTeam)
                            {
                                Debug.Log($"ElephantBomb: Skipped same-team player {player.name} (Team={playerTeam}, ActorNumber={player.photonView.ControllerActorNr}) in TeamMoonRan, frame={Time.frameCount}.");
                                continue;
                            }
                        }
                        float dist = Vector2.Distance(transform.position, player.transform.position);
                        if (dist < closestDist)
                        {
                            closestDist = dist;
                            closestTarget = player.transform;
                        }
                    }
                }
            }

            // Check bots if no enemy or player is closer
            if (closestTarget == null)
            {
                BotController[] bots = Object.FindObjectsByType<BotController>(FindObjectsSortMode.None);
                foreach (BotController bot in bots)
                {
                    if (bot.photonView != null && bot.ActorNumber != ownerActorNumber)
                    {
                        if (isTeamMoonRan)
                        {
                            string botTeam = GetBotTeam(bot);
                            if (ownerTeam != "None" && botTeam == ownerTeam)
                            {
                                Debug.Log($"ElephantBomb: Skipped same-team bot {bot.NickName} (Team={botTeam}, ActorNumber={bot.ActorNumber}) in TeamMoonRan, frame={Time.frameCount}.");
                                continue;
                            }
                        }
                        float dist = Vector2.Distance(transform.position, bot.transform.position);
                        if (dist < closestDist)
                        {
                            closestDist = dist;
                            closestTarget = bot.transform;
                        }
                    }
                }
            }

            target = closestTarget;
            Debug.Log($"ElephantBomb: Updated target to {(closestTarget != null ? closestTarget.name : "none")}, distance={(closestTarget != null ? closestDist.ToString("F2") : "N/A")}, ViewID={photonView.ViewID}, frame={Time.frameCount}.");
            yield return new WaitForSeconds(0.5f);
        }
    }

    void Update()
    {
        if (photonView.IsMine && !hasExploded)
        {
            // Fallback: Ensure explosion after 5 seconds
            if (Time.time >= deploymentTime + EXPLOSION_DELAY)
            {
                ForceExplode();
                Debug.Log($"ElephantBomb: Fallback explosion triggered after {EXPLOSION_DELAY}s, ViewID={photonView.ViewID}, frame={Time.frameCount}.");
            }

            // Move toward target
            if (target != null)
            {
                Vector2 direction = (target.position - transform.position).normalized;
                transform.position += (Vector3)(direction * moveSpeed * Time.deltaTime);
            }
        }
    }

    [PunRPC]
    public void Explode()
    {
        if (hasExploded) return;
        hasExploded = true;

        Debug.Log($"ElephantBomb: Explode RPC executed, ViewID={photonView.ViewID}, position={transform.position}, frame={Time.frameCount}.");
        Instantiate(explosionPrefab, transform.position, Quaternion.identity);

        string ownerTeam = GetOwnerTeam();
        Collider2D[] objects = Physics2D.OverlapCircleAll(transform.position, blastRadius);
        foreach (Collider2D obj in objects)
        {
            if (obj.CompareTag("Enemy"))
            {
                obj.GetComponent<EnemyHealth>()?.TakeDamage(enemyDamage);
                Debug.Log($"ElephantBomb: Dealt {enemyDamage} to enemy {obj.name}, ViewID={photonView.ViewID}, frame={Time.frameCount}.");
                continue;
            }

            PlayerController player = obj.GetComponent<PlayerController>();
            if (player != null)
            {
                if (player.photonView.ControllerActorNr == ownerActorNumber)
                {
                    Debug.Log($"ElephantBomb: Skipped damage to owner player {player.name} (ActorNumber={player.photonView.ControllerActorNr}), frame={Time.frameCount}.");
                    continue;
                }
                string playerTeam = GetPlayerTeam(player.photonView);
                if (UnityEngine.SceneManagement.SceneManager.GetActiveScene().name == "TeamMoonRan" && ownerTeam == playerTeam)
                {
                    Debug.Log($"ElephantBomb: Skipped damage to same-team player {player.name} (Team={playerTeam}, ActorNumber={player.photonView.ControllerActorNr}), frame={Time.frameCount}.");
                    continue;
                }
                ShockShield shield = player.GetComponent<ShockShield>();
                if (shield != null && shield.isShieldActive)
                {
                    player.photonView.RPC("HandleBombProtection", RpcTarget.All, ownerViewID);
                    Debug.Log($"ElephantBomb: {player.name} shielded, bomb protection activated, killerViewID={ownerViewID}, frame={Time.frameCount}.");
                }
                else
                {
                    PlayerHealth health = obj.GetComponent<PlayerHealth>();
                    if (health != null)
                    {
                        health.TakeDamage(playerDamage, false, ownerActorNumber, PlayerHealth.DeathCause.ElephantBomb);
                        Debug.Log($"ElephantBomb: Dealt {playerDamage} to {player.name}, killerActorNumber={ownerActorNumber}, deathCause=ElephantBomb, frame={Time.frameCount}.");
                    }
                }
                continue;
            }

            BotController botController = obj.GetComponent<BotController>();
            if (botController != null)
            {
                if (botController.ActorNumber == ownerActorNumber)
                {
                    Debug.Log($"ElephantBomb: Skipped damage to owner bot {botController.NickName} (ActorNumber={botController.ActorNumber}), frame={Time.frameCount}.");
                    continue;
                }
                string botTeam = GetBotTeam(botController);
                if (UnityEngine.SceneManagement.SceneManager.GetActiveScene().name == "TeamMoonRan" && ownerTeam == botTeam)
                {
                    Debug.Log($"ElephantBomb: Skipped damage to same-team bot {botController.NickName} (Team={botTeam}, ActorNumber={botController.ActorNumber}), frame={Time.frameCount}.");
                    continue;
                }
                ShockShield shield = botController.GetComponent<ShockShield>();
                if (shield != null && shield.isShieldActive)
                {
                    Debug.Log($"ElephantBomb: {botController.NickName} shielded, bomb damage blocked, killerActorNumber={ownerActorNumber}, frame={Time.frameCount}.");
                }
                else
                {
                    botController.TakeDamage(playerDamage, false, ownerActorNumber, PlayerHealth.DeathCause.ElephantBomb);
                    Debug.Log($"ElephantBomb: Dealt {playerDamage} to bot {botController.NickName}, killerActorNumber={ownerActorNumber}, deathCause=ElephantBomb, frame={Time.frameCount}.");
                }
                continue;
            }

            if (obj.CompareTag("halfPlanet") || obj.CompareTag("quarterPlanet"))
            {
                Rigidbody2D rb = obj.GetComponent<Rigidbody2D>();
                if (rb != null)
                {
                    Vector2 pushDirection = (obj.transform.position - transform.position).normalized;
                    rb.linearVelocity = Vector2.zero;
                    rb.AddForce(pushDirection * debrisForce, ForceMode2D.Impulse);
                    PlanetHalf half = obj.GetComponent<PlanetHalf>();
                    PlanetQuarter quarter = obj.GetComponent<PlanetQuarter>();
                    if (half != null)
                    {
                        half.Launch(ownerViewID);
                        Debug.Log($"ElephantBomb: Launched halfPlanet {obj.name}, force={debrisForce}, knockedByViewID={ownerViewID}, frame={Time.frameCount}.");
                    }
                    else if (quarter != null)
                    {
                        quarter.Launch(ownerViewID);
                        Debug.Log($"ElephantBomb: Launched quarterPlanet {obj.name}, force={debrisForce}, knockedByViewID={ownerViewID}, frame={Time.frameCount}.");
                    }
                }
            }
        }

        if (photonView.IsMine && ownerViewID != -1)
        {
            PhotonView managerView = PhotonView.Find(ownerViewID);
            if (managerView != null)
            {
                BombManager bombManager = managerView.GetComponent<BombManager>();
                if (bombManager != null)
                {
                    bombManager.OnBombExploded();
                    Debug.Log($"ElephantBomb: Notified BombManager of explosion, ManagerViewID={ownerViewID}, frame={Time.frameCount}.");
                }
            }
        }

        PhotonNetwork.Destroy(gameObject);
        Debug.Log($"ElephantBomb: Destroyed after explosion, ViewID={photonView.ViewID}, frame={Time.frameCount}.");
    }

    public void ForceExplode()
    {
        if (!hasExploded && photonView != null)
        {
            if (explosionCountdownCoroutine != null)
            {
                StopCoroutine(explosionCountdownCoroutine);
                explosionCountdownCoroutine = null;
                Debug.Log($"ElephantBomb: Stopped ExplosionCountdown coroutine for forced explosion, ViewID={photonView.ViewID}, frame={Time.frameCount}.");
            }
            photonView.RPC("Explode", RpcTarget.All);
            Debug.Log($"ElephantBomb: Force exploded, ViewID={photonView.ViewID}, frame={Time.frameCount}.");
        }
    }

    private void OnTriggerEnter2D(Collider2D other)
    {
        if (!hasExploded)
        {
            if (explosionCountdownCoroutine != null)
            {
                StopCoroutine(explosionCountdownCoroutine);
                explosionCountdownCoroutine = null;
                Debug.Log($"ElephantBomb: Stopped ExplosionCountdown coroutine due to contact explosion, ViewID={photonView.ViewID}, frame={Time.frameCount}.");
            }

            PlayerController player = other.GetComponent<PlayerController>();
            if (player != null)
            {
                if (player.photonView.ControllerActorNr == ownerActorNumber)
                {
                    Debug.Log($"ElephantBomb: Ignored collision with owner player {other.name} (ViewID={player.photonView.ViewID}), frame={Time.frameCount}.");
                    return;
                }
                string ownerTeam = GetOwnerTeam();
                string playerTeam = GetPlayerTeam(player.photonView);
                if (UnityEngine.SceneManagement.SceneManager.GetActiveScene().name == "TeamMoonRan" && ownerTeam == playerTeam)
                {
                    Debug.Log($"ElephantBomb: Ignored collision with same-team player {other.name} (Team={playerTeam}, ViewID={player.photonView.ViewID}), frame={Time.frameCount}.");
                    return;
                }
                photonView.RPC("Explode", RpcTarget.All);
                Debug.Log($"ElephantBomb: Exploded on contact with non-owner player {other.name} (ViewID={player.photonView.ViewID}), frame={Time.frameCount}.");
            }
            else if (other.CompareTag("Bot"))
            {
                BotController bot = other.GetComponent<BotController>();
                if (bot != null)
                {
                    if (bot.ActorNumber == ownerActorNumber)
                    {
                        Debug.Log($"ElephantBomb: Ignored collision with owner bot {bot.NickName} (ActorNumber={bot.ActorNumber}), frame={Time.frameCount}.");
                        return;
                    }
                    string ownerTeam = GetOwnerTeam();
                    string botTeam = GetBotTeam(bot);
                    if (UnityEngine.SceneManagement.SceneManager.GetActiveScene().name == "TeamMoonRan" && ownerTeam == botTeam)
                    {
                        Debug.Log($"ElephantBomb: Ignored collision with same-team bot {bot.NickName} (Team={botTeam}, ActorNumber={bot.ActorNumber}), frame={Time.frameCount}.");
                        return;
                    }
                    photonView.RPC("Explode", RpcTarget.All);
                    Debug.Log($"ElephantBomb: Exploded on contact with non-owner bot {other.name} (ActorNumber={bot.ActorNumber}), frame={Time.frameCount}.");
                }
            }
            else if (other.CompareTag("Enemy"))
            {
                photonView.RPC("Explode", RpcTarget.All);
                Debug.Log($"ElephantBomb: Exploded on contact with {other.name} (tag=Enemy), frame={Time.frameCount}.");
            }
        }
    }

    void OnDestroy()
    {
        if (explosionCountdownCoroutine != null)
        {
            StopCoroutine(explosionCountdownCoroutine);
            explosionCountdownCoroutine = null;
            Debug.Log($"ElephantBomb: Stopped ExplosionCountdown on destroy, ViewID={photonView?.ViewID}, frame={Time.frameCount}.");
        }
    }
    private string GetOwnerTeam()
    {
        string result = "None";
        bool isDone = false;

        StartCoroutine(GetOwnerTeamCoroutine(team =>
        {
            result = team;
            isDone = true;
        }));

        // Block until coroutine completes (safe for Unity main thread)
        while (!isDone)
        {
            return result; // Return immediately to avoid blocking
        }

        return result;
    }
    private IEnumerator GetOwnerTeamCoroutine(System.Action<string> callback)
    {
        if (ownerActorNumber <= 0)
        {
            if (!hasLoggedInvalidOwner)
            {
                Debug.LogWarning($"ElephantBomb: Invalid owner ActorNumber={ownerActorNumber}, returning Team=None, ViewID={photonView.ViewID}, frame={Time.frameCount}.");
                hasLoggedInvalidOwner = true;
            }
            callback("None");
            yield break;
        }

        PhotonView ownerView = PhotonView.Find(ownerViewID);
        if (ownerView == null)
        {
            if (!hasLoggedInvalidOwner)
            {
                Debug.LogWarning($"ElephantBomb: Owner PhotonView not found for ViewID={ownerViewID}, ActorNumber={ownerActorNumber}, returning Team=None, frame={Time.frameCount}.");
                hasLoggedInvalidOwner = true;
            }
            callback("None");
            yield break;
        }

        const int maxRetries = 5;
        const float retryDelay = 0.1f;
        int retryCount = 0;

        while (retryCount < maxRetries)
        {
            PlayerController player = ownerView.GetComponent<PlayerController>();
            if (player != null && player.photonView != null && player.photonView.Owner != null &&
                player.photonView.Owner.CustomProperties != null && player.photonView.Owner.CustomProperties.ContainsKey("Team"))
            {
                string team = player.photonView.Owner.CustomProperties["Team"].ToString();
                if (retryCount > 0)
                {
                    Debug.Log($"ElephantBomb: Found Team={team} for player owner ActorNumber={ownerActorNumber}, ViewID={ownerViewID} after {retryCount} retries, frame={Time.frameCount}.");
                }
                callback(team);
                yield break;
            }

            BotController bot = ownerView.GetComponent<BotController>();
            if (bot != null && bot.CustomProperties != null && bot.CustomProperties.ContainsKey("Team"))
            {
                string team = bot.CustomProperties["Team"].ToString();
                if (retryCount > 0)
                {
                    Debug.Log($"ElephantBomb: Found Team={team} for bot owner ActorNumber={ownerActorNumber}, ViewID={ownerViewID} after {retryCount} retries, frame={Time.frameCount}.");
                }
                callback(team);
                yield break;
            }

            retryCount++;
            if (retryCount < maxRetries)
            {
                yield return new WaitForSeconds(retryDelay);
            }
        }

        // Silently return "None" without logging warning
        callback("None");
    }

    private string GetPlayerTeam(PhotonView playerView)
    {
        if (playerView != null && playerView.Owner != null && playerView.Owner.CustomProperties != null && playerView.Owner.CustomProperties.ContainsKey("Team"))
        {
            return playerView.Owner.CustomProperties["Team"].ToString();
        }
        Debug.LogWarning($"ElephantBomb: No team found for player ViewID={playerView?.ViewID}, frame={Time.frameCount}.");
        return "None";
    }

    private string GetBotTeam(BotController bot)
    {
        if (bot != null && bot.CustomProperties != null && bot.CustomProperties.ContainsKey("Team"))
        {
            return bot.CustomProperties["Team"].ToString();
        }
        Debug.LogWarning($"ElephantBomb: No team found for bot ActorNumber={bot?.ActorNumber}, frame={Time.frameCount}.");
        return "None";
    }
}