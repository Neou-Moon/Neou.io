using UnityEngine;
using System.Linq; // Added for Concat

public class EnemyMovement : MonoBehaviour
{
    public float moveSpeed = 30f;
    public GameObject blastPrefab;
    public Transform firePoint;
    public float fireRate = 1f;
    public float maxDistance = 50f;
    public int health = 50;
    public float spawnRadius = 10f;
    public float minSpawnDistance = 5f;

    private Transform player;
    private float nextFireTime;
    private Vector3 lastPosition;

    void Start()
    {
        lastPosition = transform.position;
        if (blastPrefab != null && blastPrefab.GetComponent<Blast>() == null)
        {
            Debug.LogError($"EnemyMovement: blastPrefab {blastPrefab.name} on {gameObject.name} lacks Blast script. Please assign a valid Blast prefab.");
        }
        if (firePoint == null)
        {
            Debug.LogError($"EnemyMovement: firePoint is not assigned on {gameObject.name}. Please assign a Transform in the Inspector.");
        }

        // Attempt to find a valid target if none is set
        if (player == null)
        {
            FindInitialTarget();
        }

        Debug.Log($"EnemyMovement: Initialized {gameObject.name}, moveSpeed={moveSpeed}, timeScale={Time.timeScale}, parent={(transform.parent != null ? transform.parent.name : "none")}, localScale={transform.localScale}, blastPrefab={(blastPrefab != null ? blastPrefab.name : "null")}, player={(player != null ? player.name : "null")}");
    }

    void Update()
    {
        if (player == null)
        {
            FindInitialTarget();
            if (player == null)
            {
                Debug.Log($"EnemyMovement: {gameObject.name} no valid player target found, skipping Update.");
                return;
            }
        }

        PlayerHealth playerHealth = player.GetComponent<PlayerHealth>();
        BotController botController = player.GetComponent<BotController>();
        bool isDead = (playerHealth != null && playerHealth.HasDied) || (botController != null && botController.HasDied);

        if (isDead || !player.gameObject.activeInHierarchy)
        {
            Debug.Log($"EnemyMovement: {gameObject.name} player target invalid, HasDied={(playerHealth?.HasDied ?? botController?.HasDied)}, active={player.gameObject.activeInHierarchy}. Clearing target.");
            player = null;
            return;
        }

        // Calculate movement
        Vector3 direction = (player.position - transform.position).normalized;
        Vector3 movement = direction * moveSpeed * Time.deltaTime;
        transform.position += movement;

        // Debug actual speed (units/second)
        float actualSpeed = Vector3.Distance(transform.position, lastPosition) / Time.deltaTime;
        Debug.Log($"EnemyMovement: {gameObject.name} moving, moveSpeed={moveSpeed}, actualSpeed={actualSpeed:F2} units/s, direction={direction}, deltaTime={Time.deltaTime:F4}s, position={transform.position}, timeScale={Time.timeScale}");

        lastPosition = transform.position;

        if (Time.time >= nextFireTime)
        {
            ShootBlast();
            nextFireTime = Time.time + 1f / fireRate;
        }

        if (Vector3.Distance(transform.position, player.position) > maxDistance)
        {
            Destroy(gameObject);
            Debug.Log($"EnemyMovement: Destroyed {gameObject.name} due to exceeding maxDistance={maxDistance}, position={transform.position}");
        }
    }

    void ShootBlast()
    {
        if (blastPrefab == null || firePoint == null)
        {
            Debug.LogWarning($"EnemyMovement: blastPrefab={blastPrefab}, firePoint={firePoint} for {gameObject.name}. Cannot shoot blast.");
            return;
        }

        GameObject blast = Instantiate(blastPrefab, firePoint.position, firePoint.rotation);
        if (blast == null)
        {
            Debug.LogError($"EnemyMovement: Failed to instantiate blastPrefab {blastPrefab.name} for {gameObject.name}.");
            return;
        }

        // Ensure blast is on the correct layer
        int blastLayer = LayerMask.NameToLayer("Blast");
        if (blastLayer >= 0 && blastLayer <= 31)
        {
            blast.layer = blastLayer;
            Debug.Log($"EnemyMovement: Set blast {blast.name} layer to 'Blast' (index {blastLayer}) for {gameObject.name}");
        }
        else
        {
            blast.layer = 0; // Fallback to Default
            Debug.LogWarning($"EnemyMovement: 'Blast' layer not found for blast {blast.name} in {gameObject.name}. Using 'Default' layer.");
        }

        // Set blast direction
        Vector3 blastDirection = (player.position - firePoint.position).normalized;
        blast.transform.right = blastDirection;

        // Additional debug for blast components
        Debug.Log($"EnemyMovement: Fired blast {blast.name} from {gameObject.name} at {firePoint.position}, direction={blastDirection}, layer={LayerMask.LayerToName(blast.layer)}, hasBlastScript={(blast.GetComponent<Blast>() != null)}, hasRigidbody2D={(blast.GetComponent<Rigidbody2D>() != null)}, hasCollider2D={(blast.GetComponent<Collider2D>() != null)}");
    }

    public void SetTarget(Transform playerTransform)
    {
        if (playerTransform == null)
        {
            Debug.LogError($"EnemyMovement: SetTarget received null playerTransform for {gameObject.name}.");
            return;
        }

        PlayerHealth playerHealth = playerTransform.GetComponent<PlayerHealth>();
        BotController botController = playerTransform.GetComponent<BotController>();
        bool isDead = (playerHealth != null && playerHealth.HasDied) || (botController != null && botController.HasDied);

        if (isDead || !playerTransform.gameObject.activeInHierarchy)
        {
            Debug.Log($"EnemyMovement: {gameObject.name} SetTarget skipped, HasDied={(playerHealth?.HasDied ?? botController?.HasDied)}, active={playerTransform.gameObject.activeInHierarchy}.");
            return;
        }

        player = playerTransform;
        transform.position = GetRandomSpawnPosition();
        lastPosition = transform.position;
        Debug.Log($"EnemyMovement: Target set for {gameObject.name} to {(player != null ? player.name : "null")} at {playerTransform.position}, spawned at {transform.position}, moveSpeed={moveSpeed}");
    }

    private void FindInitialTarget()
    {
        GameObject[] players = GameObject.FindGameObjectsWithTag("Player");
        GameObject[] bots = GameObject.FindGameObjectsWithTag("Bot");
        Transform closestTarget = null;
        float minDistance = float.MaxValue;

        foreach (GameObject candidate in players.Concat(bots))
        {
            if (!candidate.activeInHierarchy) continue;

            PlayerHealth playerHealth = candidate.GetComponent<PlayerHealth>();
            BotController botController = candidate.GetComponent<BotController>();
            bool isDead = (playerHealth != null && playerHealth.HasDied) || (botController != null && botController.HasDied);

            if (isDead) continue;

            float distance = Vector3.Distance(transform.position, candidate.transform.position);
            if (distance < minDistance)
            {
                minDistance = distance;
                closestTarget = candidate.transform;
            }
        }

        if (closestTarget != null)
        {
            SetTarget(closestTarget);
        }
        else
        {
            Debug.LogWarning($"EnemyMovement: No valid target found for {gameObject.name} in FindInitialTarget.");
        }
    }

    Vector3 GetRandomSpawnPosition()
    {
        if (player == null)
        {
            Debug.LogWarning($"EnemyMovement: No player set for {gameObject.name} in GetRandomSpawnPosition, using fallback spawn.");
            return new Vector3(
                Random.Range(-spawnRadius, spawnRadius),
                Random.Range(-spawnRadius, spawnRadius),
                0f
            );
        }

        Vector3 spawnPosition;
        int attempts = 0;
        const int maxAttempts = 10;

        do
        {
            float randomAngle = Random.Range(0f, 360f);
            float randomRadius = Random.Range(minSpawnDistance, spawnRadius);
            spawnPosition = new Vector3(
                Mathf.Cos(randomAngle * Mathf.Deg2Rad) * randomRadius,
                Mathf.Sin(randomAngle * Mathf.Deg2Rad) * randomRadius,
                0f
            );
            attempts++;
        } while (Vector3.Distance(player.position, spawnPosition) < minSpawnDistance && attempts < maxAttempts);

        if (attempts >= maxAttempts)
        {
            Debug.LogWarning($"EnemyMovement: Failed to find valid spawn position for {gameObject.name} after max attempts.");
            spawnPosition = new Vector3(spawnRadius, 0, 0);
        }

        return player.position + spawnPosition;
    }

    void OnCollisionEnter2D(Collision2D collision)
    {
        if (collision.gameObject.CompareTag("Player") || collision.gameObject.CompareTag("Bot"))
        {
            PlayerHealth playerHealth = collision.gameObject.GetComponent<PlayerHealth>();
            BotController botController = collision.gameObject.GetComponent<BotController>();
            bool isDead = (playerHealth != null && playerHealth.HasDied) || (botController != null && botController.HasDied);

            if (!isDead)
            {
                if (playerHealth != null)
                {
                    playerHealth.TakeDamage(10f, false, -1, PlayerHealth.DeathCause.EnemyCollision);
                }
                else if (botController != null)
                {
                    botController.TakeDamage(10f, false, -1, PlayerHealth.DeathCause.EnemyCollision);
                }
                Destroy(gameObject);
                Debug.Log($"EnemyMovement: Destroyed {gameObject.name} after colliding with {collision.gameObject.name}");
            }
            else
            {
                Debug.Log($"EnemyMovement: Ignored collision with {collision.gameObject.name}, HasDied={(playerHealth?.HasDied ?? botController?.HasDied)}");
            }
        }
    }
}