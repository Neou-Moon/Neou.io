using UnityEngine;
using Photon.Pun;
using TMPro;
using System.Linq; // Added for Concat

[RequireComponent(typeof(PhotonView))]
public class EnemySpawner : MonoBehaviourPunCallbacks
{
    [SerializeField] private GameObject enemyPrefab; // Drag Assets/Resources/Prefabs/Enemy.prefab
    [SerializeField] private float spawnDistance = 10f; // Distance from player/bot
    private float nextSpawnTime;
    private const float SPAWN_INTERVAL = 5f; // Spawn every 5 seconds during wave
    private TextMeshProUGUI waveTimerText; // Reference to WaveManager's timerText
    private bool isWaveActive; // Tracks if in "Wave Active:" phase

    void Start()
    {
        if (!photonView.IsMine)
        {
            Debug.Log($"EnemySpawner: Start skipped on {gameObject.name}, photonView.IsMine=false, OwnerActorNr={photonView.OwnerActorNr}, frame={Time.frameCount}");
            enabled = false;
            return;
        }

        if (enemyPrefab == null)
        {
            Debug.LogError($"EnemySpawner: enemyPrefab is not assigned in Inspector on {gameObject.name}.");
            enabled = false;
            return;
        }

        // Find WaveManager's timerText
        WaveManager waveManager = Object.FindFirstObjectByType<WaveManager>();
        if (waveManager != null)
        {
            waveTimerText = waveManager.TimerText;
            if (waveTimerText == null)
            {
                Debug.LogError($"EnemySpawner: WaveManager found but timerText is null on {gameObject.name}.");
                enabled = false;
                return;
            }
        }
        else
        {
            Debug.LogError($"EnemySpawner: WaveManager not found in scene on {gameObject.name}.");
            enabled = false;
            return;
        }

        nextSpawnTime = Time.time + SPAWN_INTERVAL;
        Debug.Log($"EnemySpawner: Initialized on {gameObject.name}, photonView.ID={photonView.ViewID}, OwnerActorNr={photonView.OwnerActorNr}, first spawn check at {nextSpawnTime:F2}, frame={Time.frameCount}");
    }

    void Update()
    {
        if (!photonView.IsMine) return;

        // Check if in "Wave Active:" phase
        isWaveActive = waveTimerText != null && waveTimerText.text.StartsWith("Wave Active:");
        if (!isWaveActive)
        {
            // Reset spawn time to align with next wave
            nextSpawnTime = Time.time + SPAWN_INTERVAL;
            return;
        }

        if (Time.time >= nextSpawnTime)
        {
            SpawnEnemy();
            nextSpawnTime = Time.time + SPAWN_INTERVAL;
            Debug.Log($"EnemySpawner: Scheduled next spawn on {gameObject.name} at {nextSpawnTime:F2}, frame={Time.frameCount}");
        }
    }

    void SpawnEnemy()
    {
        if (!PhotonNetwork.IsConnected && !PhotonNetwork.OfflineMode)
        {
            Debug.LogError($"EnemySpawner: Cannot spawn enemy on {gameObject.name}, not connected to Photon, frame={Time.frameCount}");
            return;
        }

        // Find a valid target (player or bot)
        GameObject[] players = GameObject.FindGameObjectsWithTag("Player");
        GameObject[] bots = GameObject.FindGameObjectsWithTag("Bot");
        GameObject target = null;
        float minDistance = float.MaxValue;
        Vector3 referencePosition = Vector3.zero;

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
                target = candidate;
                referencePosition = candidate.transform.position;
            }
        }

        if (target == null)
        {
            Debug.LogWarning($"EnemySpawner: No valid target found for spawning enemy on {gameObject.name}, frame={Time.frameCount}");
            return;
        }

        // Calculate spawn position relative to the target
        Vector2 randomDirection = Random.insideUnitCircle.normalized;
        Vector3 spawnPosition = referencePosition + (Vector3)(randomDirection * spawnDistance);
        spawnPosition.z = 0f;

        try
        {
            GameObject enemy = PhotonNetwork.Instantiate("Prefabs/Enemy", spawnPosition, Quaternion.identity);
            Debug.Log($"EnemySpawner: Spawned {enemy.name} for {gameObject.name} (OwnerActorNr={photonView.OwnerActorNr}) at {spawnPosition}, targeting {target.name}, frame={Time.frameCount}");

            EnemyMovement enemyMovement = enemy.GetComponent<EnemyMovement>();
            if (enemyMovement != null)
            {
                enemyMovement.SetTarget(target.transform);
                Debug.Log($"EnemySpawner: Assigned {target.name} as target for {enemy.name}, frame={Time.frameCount}");
            }
            else
            {
                Debug.LogWarning($"EnemySpawner: Enemy prefab missing EnemyMovement component on {gameObject.name}, frame={Time.frameCount}");
            }
        }
        catch (System.Exception e)
        {
            Debug.LogError($"EnemySpawner: Failed to spawn enemy on {gameObject.name}: {e.Message}, frame={Time.frameCount}");
        }
    }

    public override void OnJoinedRoom()
    {
        Debug.Log($"EnemySpawner: Joined room {PhotonNetwork.CurrentRoom.Name} on {gameObject.name}, photonView.ID={photonView.ViewID}, OwnerActorNr={photonView.OwnerActorNr}, frame={Time.frameCount}");
    }
}