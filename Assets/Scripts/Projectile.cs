using UnityEngine;
using Photon.Realtime;
using Photon.Pun;

public class Projectile : MonoBehaviourPunCallbacks
{
    private DroidShooting spawner;
    private Vector2 movementDirection;
    private float movementSpeed;
    private bool useTransformMovement;
    private int ownerActorNumber = -1;
    public int OwnerActorNumber => ownerActorNumber;
    private float damage;
    private bool loggedInvalidOwner = false;
    private bool isBeingDestroyed = false;

    void Start()
    {
        if (!gameObject.scene.IsValid())
        {
            CustomLogger.LogWarning($"Projectile: Invalid scene, skipping initialization for {gameObject.name}");
            return;
        }

        Destroy(gameObject, 2f); // Destroy after 2 seconds

        Rigidbody2D rb = GetComponent<Rigidbody2D>();
        if (rb != null)
        {
            rb.bodyType = RigidbodyType2D.Dynamic;
            rb.linearDamping = 0;
            rb.angularDamping = 0;
            CustomLogger.Log($"Projectile: Forced Rigidbody2D, bodyType={rb.bodyType}, velocity={rb.linearVelocity}, drag={rb.linearDamping}, ViewID={photonView.ViewID}, frame={Time.frameCount}");
        }

        if (spawner != null)
        {
            Collider2D[] spawnerColliders = spawner.GetComponentsInParent<Collider2D>();
            Collider2D projectileCollider = GetComponent<Collider2D>();
            if (projectileCollider != null)
            {
                foreach (var spawnerCollider in spawnerColliders)
                {
                    if (spawnerCollider != null)
                    {
                        Physics2D.IgnoreCollision(projectileCollider, spawnerCollider);
                        CustomLogger.Log($"Projectile: Ignoring collision with {spawnerCollider.gameObject.name}, ViewID={photonView.ViewID}, frame={Time.frameCount}");
                    }
                }
            }
        }
        else
        {
            CustomLogger.LogWarning($"Projectile: Spawner not set for {gameObject.name}, collision ignoring skipped, ViewID={photonView.ViewID}, frame={Time.frameCount}");
        }

        PhotonView pv = GetComponent<PhotonView>();
        if (pv != null)
        {
            CustomLogger.Log($"Projectile: Initialized, ownerActorNumber={ownerActorNumber}, position={transform.position}, damage={damage}, ViewID={pv.ViewID}, IsMine={pv.IsMine}, frame={Time.frameCount}");
        }
        else
        {
            CustomLogger.LogWarning($"Projectile: No PhotonView on {gameObject.name}, ownerActorNumber={ownerActorNumber}, position={transform.position}, frame={Time.frameCount}");
        }
    }

    void Update()
    {
        if (!gameObject.scene.IsValid()) return;

        if (useTransformMovement)
        {
            transform.Translate(movementDirection * movementSpeed * Time.deltaTime);
            CustomLogger.Log($"Projectile: Transform movement, position={transform.position}, direction={movementDirection}, speed={movementSpeed}, ViewID={photonView.ViewID}, frame={Time.frameCount}");
        }
        else
        {
            Rigidbody2D rb = GetComponent<Rigidbody2D>();
            if (rb != null)
            {
                CustomLogger.Log($"Projectile: Rigidbody2D movement, position={rb.position}, velocity={rb.linearVelocity}, ViewID={photonView.ViewID}, frame={Time.frameCount}");
            }
        }
    }

    void OnTriggerEnter2D(Collider2D collision)
    {
        if (!photonView.IsMine || isBeingDestroyed)
        {
            CustomLogger.Log($"Projectile: Ignoring collision, isMine={photonView.IsMine}, isBeingDestroyed={isBeingDestroyed}, target={collision.name}, ViewID={photonView.ViewID}, frame={Time.frameCount}");
            return;
        }

        PhotonView targetView = collision.GetComponent<PhotonView>();
        if (targetView != null)
        {
            PlayerController targetPlayer = targetView.GetComponent<PlayerController>();
            BotController targetBot = targetView.GetComponent<BotController>();
            if ((targetPlayer != null && targetPlayer.ActorNumber == ownerActorNumber) ||
                (targetBot != null && targetBot.ActorNumber == ownerActorNumber))
            {
                CustomLogger.Log($"Projectile: Ignored collision with owner {collision.name} (ActorNumber={ownerActorNumber}), frame={Time.frameCount}");
                return;
            }
        }

        int killerActorNumber = ownerActorNumber;
        if (ownerActorNumber != -1)
        {
            bool ownerExists = false;
            foreach (Player player in PhotonNetwork.PlayerList)
            {
                if (player.ActorNumber == ownerActorNumber)
                {
                    ownerExists = true;
                    break;
                }
            }
            if (!ownerExists)
            {
                BotController[] bots = FindObjectsByType<BotController>(FindObjectsSortMode.None);
                foreach (BotController bot in bots)
                {
                    if (bot.ActorNumber == ownerActorNumber)
                    {
                        ownerExists = true;
                        break;
                    }
                }
            }
            if (!ownerExists && !loggedInvalidOwner)
            {
                CustomLogger.LogWarning($"Projectile: ownerActorNumber={ownerActorNumber} is invalid (owner not found), setting killerActorNumber to -1 for collision with {collision.name}, frame={Time.frameCount}");
                loggedInvalidOwner = true;
                killerActorNumber = -1;
            }
        }
        else
        {
            if (!loggedInvalidOwner)
            {
                CustomLogger.LogWarning($"Projectile: ownerActorNumber not set for collision with {collision.name}, point awarding may fail, frame={Time.frameCount}");
                loggedInvalidOwner = true;
            }
        }

        string ownerTeam = GetOwnerTeam();
        string killerHierarchy = targetView != null ? GetHierarchyString(targetView.gameObject) : "null";
        CustomLogger.Log($"Projectile: Processing collision with {collision.name} (tag={collision.tag}, targetViewID={targetView?.ViewID}, ownerActorNumber={ownerActorNumber}, killerActorNumber={killerActorNumber}), killer hierarchy={killerHierarchy}, frame={Time.frameCount}");

        if (collision.CompareTag("Enemy"))
        {
            EnemyHealth enemyHealth = collision.GetComponent<EnemyHealth>();
            if (enemyHealth != null)
            {
                enemyHealth.TakeDamage((int)damage);
                CustomLogger.Log($"Projectile: Dealt {damage} to enemy {collision.name} (ViewID={targetView?.ViewID}), frame={Time.frameCount}");
            }
            DestroyProjectile();
        }
        else if (collision.CompareTag("Player"))
        {
            PlayerController player = collision.GetComponent<PlayerController>();
            if (player != null)
            {
                string playerTeam = GetPlayerTeam(player.photonView);
                if (UnityEngine.SceneManagement.SceneManager.GetActiveScene().name == "TeamMoonRan" && ownerTeam == playerTeam)
                {
                    CustomLogger.Log($"Projectile: Ignored collision with same-team player {collision.name} (Team={playerTeam}, ViewID={targetView?.ViewID}), frame={Time.frameCount}");
                    return;
                }
                PlayerHealth playerHealth = collision.GetComponent<PlayerHealth>();
                if (playerHealth != null)
                {
                    if (player.isShieldActive)
                    {
                        CustomLogger.Log($"Projectile: {collision.name} shielded, no damage (ViewID={targetView?.ViewID}), frame={Time.frameCount}");
                    }
                    else
                    {
                        playerHealth.TakeDamage((int)damage, false, killerActorNumber, PlayerHealth.DeathCause.Projectile);
                        CustomLogger.Log($"Projectile: Dealt {damage} to player {collision.name} (ViewID={targetView?.ViewID}, killerActorNumber={killerActorNumber}), frame={Time.frameCount}");
                    }
                }
                else
                {
                    CustomLogger.LogWarning($"Projectile: No PlayerHealth on {collision.name} (tag=Player), frame={Time.frameCount}");
                }
                DestroyProjectile();
            }
        }
        else if (collision.CompareTag("Bot"))
        {
            BotController bot = collision.GetComponent<BotController>();
            if (bot != null)
            {
                string botTeam = GetBotTeam(bot);
                if (UnityEngine.SceneManagement.SceneManager.GetActiveScene().name == "TeamMoonRan" && ownerTeam == botTeam)
                {
                    CustomLogger.Log($"Projectile: Ignored collision with same-team bot {collision.name} (Team={botTeam}, ViewID={targetView?.ViewID}), frame={Time.frameCount}");
                    return;
                }
                bot.TakeDamage((int)damage, false, killerActorNumber, PlayerHealth.DeathCause.Projectile);
                CustomLogger.Log($"Projectile: Dealt {damage} to bot {collision.name} (ViewID={targetView?.ViewID}, killerActorNumber={killerActorNumber}), frame={Time.frameCount}");
                DestroyProjectile();
            }
        }
        else if (collision.CompareTag("Planet"))
        {
            CustomLogger.Log($"Projectile: Collided with planet {collision.name}, destroying projectile, frame={Time.frameCount}");
            DestroyProjectile();
        }
        else
        {
            CustomLogger.Log($"Projectile: Collided with {collision.name} (tag={collision.tag}), no action taken, frame={Time.frameCount}");
        }
    }

    private string GetHierarchyString(GameObject obj)
    {
        string path = obj.name;
        Transform parent = obj.transform.parent;
        while (parent != null)
        {
            path = parent.name + "/" + path;
            parent = parent.parent;
        }
        return path;
    }

    public void SetSpawner(DroidShooting droidShooting)
    {
        spawner = droidShooting;
        CustomLogger.Log($"Projectile: Set spawner to {spawner?.gameObject.name ?? "null"}, ViewID={photonView.ViewID}, frame={Time.frameCount}");
    }

    public void SetDamage(float damageValue)
    {
        damage = damageValue;
        CustomLogger.Log($"Projectile: Set damage to {damage}, ViewID={photonView.ViewID}, frame={Time.frameCount}");
    }

    public void SetOwnerActorNumber(int actorNumber)
    {
        if (actorNumber == -1)
        {
            ownerActorNumber = -1;
            CustomLogger.LogWarning($"Projectile: SetOwnerActorNumber received actorNumber=-1 for {gameObject.name}, setting ownerActorNumber to -1, frame={Time.frameCount}");
            return;
        }

        ownerActorNumber = actorNumber;
        CustomLogger.Log($"Projectile: Set ownerActorNumber to {ownerActorNumber} for {gameObject.name}, frame={Time.frameCount}");
    }

    public void UseTransformMovement(Vector2 direction, float speed)
    {
        useTransformMovement = true;
        movementDirection = direction;
        movementSpeed = speed;
        CustomLogger.Log($"Projectile: Configured transform movement, direction={direction}, speed={speed}, ViewID={photonView.ViewID}, frame={Time.frameCount}");
    }

    private void DestroyProjectile()
    {
        if (!gameObject.scene.IsValid() || isBeingDestroyed)
        {
            CustomLogger.Log($"Projectile: Already destroyed or invalid scene for {gameObject.name}, isBeingDestroyed={isBeingDestroyed}, frame={Time.frameCount}");
            return;
        }

        isBeingDestroyed = true;

        if (spawner != null)
        {
            DroidShooting.RemoveProjectile(gameObject);
            CustomLogger.Log($"Projectile: Removed from spawner {spawner.gameObject.name}, ViewID={photonView.ViewID}, frame={Time.frameCount}");
        }

        PhotonView pv = GetComponent<PhotonView>();
        if (PhotonNetwork.IsConnected && pv != null && pv.IsMine)
        {
            PhotonNetwork.Destroy(gameObject);
            CustomLogger.Log($"Projectile: PhotonNetwork.Destroy called, position={transform.position}, ViewID={pv.ViewID}, frame={Time.frameCount}");
        }
        else
        {
            Destroy(gameObject);
            CustomLogger.Log($"Projectile: Local Destroy called, position={transform.position}, PhotonView={(pv != null ? pv.ViewID : "null")}, frame={Time.frameCount}");
        }
    }

    void OnDestroy()
    {
        CustomLogger.Log($"Projectile: OnDestroy called for {gameObject.name}, ViewID={photonView.ViewID}, IsMine={photonView.IsMine}, frame={Time.frameCount}");
    }

    private string GetOwnerTeam()
    {
        if (ownerActorNumber == -1) return "None";
        foreach (Player player in PhotonNetwork.PlayerList)
        {
            if (player.ActorNumber == ownerActorNumber && player.CustomProperties.ContainsKey("Team"))
            {
                return player.CustomProperties["Team"].ToString();
            }
        }
        BotController[] bots = FindObjectsByType<BotController>(FindObjectsSortMode.None);
        foreach (BotController bot in bots)
        {
            if (bot.ActorNumber == ownerActorNumber && bot.CustomProperties.ContainsKey("Team"))
            {
                return bot.CustomProperties["Team"].ToString();
            }
        }
        CustomLogger.LogWarning($"Projectile: No team found for owner ActorNumber={ownerActorNumber}, frame={Time.frameCount}.");
        return "None";
    }

    private string GetPlayerTeam(PhotonView playerView)
    {
        if (playerView != null && playerView.Owner != null && playerView.Owner.CustomProperties.ContainsKey("Team"))
        {
            return playerView.Owner.CustomProperties["Team"].ToString();
        }
        CustomLogger.LogWarning($"Projectile: No team found for player ViewID={playerView?.ViewID}, frame={Time.frameCount}.");
        return "None";
    }

    private string GetBotTeam(BotController bot)
    {
        if (bot != null && bot.CustomProperties.ContainsKey("Team"))
        {
            return bot.CustomProperties["Team"].ToString();
        }
        CustomLogger.LogWarning($"Projectile: No team found for bot ActorNumber={bot?.ActorNumber}, frame={Time.frameCount}.");
        return "None";
    }

    public override void OnPlayerLeftRoom(Player otherPlayer)
    {
        base.OnPlayerLeftRoom(otherPlayer);
        if (otherPlayer.ActorNumber == ownerActorNumber)
        {
            CustomLogger.Log($"Projectile: Owner ActorNumber={ownerActorNumber} left room, destroying projectile ViewID={photonView.ViewID}");
            if (photonView.IsMine)
            {
                PhotonNetwork.Destroy(gameObject);
            }
        }
    }
}