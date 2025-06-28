using UnityEngine;
using Photon.Pun;
using System.Collections;
using System.Collections.Generic;

public class PlanetHalf : MonoBehaviourPun
{
    public GameObject quarterPlanetPrefab;
    private bool isLaunched;
    private bool canDamage;
    private float launchTime;
    private float damageWindow = 2f;
    private HashSet<GameObject> hitTargets = new HashSet<GameObject>();
    private int enemyDamage = 100;
    private readonly float playerDamagePercent = 0.2f; // 20% of maxHealth
    private Rigidbody2D rb;
    private float splitDelay = 15f;
    private int lastKnockedByViewID = -1;
    private const string quarterPlanetPrefabPath = "Prefabs/quarterPlanet"; // Path relative to Resources

    void Start()
    {
        rb = GetComponent<Rigidbody2D>();
        if (rb != null)
        {
            rb.mass = 100f;
            rb.linearDamping = 2f;
            rb.linearVelocity = Vector2.zero;
            Debug.Log($"PlanetHalf: {gameObject.name} spawned, mass={rb.mass}, drag={rb.linearDamping}, velocity={rb.linearVelocity}, photonView.IsMine={photonView.IsMine}");
        }
        else
        {
            Debug.LogError($"PlanetHalf: {gameObject.name} missing Rigidbody2D.");
        }

        if (photonView != null && photonView.IsMine)
        {
            StartCoroutine(SplitTimer());
        }
        else
        {
            Debug.LogWarning($"PlanetHalf: {gameObject.name} missing PhotonView or not owned, skipping split timer.");
        }
    }

    private IEnumerator SplitTimer()
    {
        yield return new WaitForSeconds(splitDelay);
        if (photonView != null && photonView.IsMine)
        {
            photonView.RPC("SplitIntoQuarters", RpcTarget.All);
        }
        else
        {
            Debug.LogWarning($"PlanetHalf: {gameObject.name} cannot split, missing PhotonView or not owned.");
        }
    }

    [PunRPC]
    public void SplitIntoQuarters()
    {
        if (!PhotonNetwork.IsConnected)
        {
            float offsetDistance = 0.5f;
            float downwardOffset = 0.2f;

            Vector3[] quarterOffsets = new Vector3[]
            {
                new Vector3(-offsetDistance, 0, 0),
                new Vector3(offsetDistance, -downwardOffset, 0)
            };

            Quaternion[] quarterRotations = new Quaternion[]
            {
                Quaternion.Euler(0, 0, 90),
                Quaternion.Euler(0, 0, -90)
            };

            for (int i = 0; i < 2; i++)
            {
                GameObject quarter = Instantiate(quarterPlanetPrefab, transform.position + quarterOffsets[i], quarterRotations[i]);
                SetupQuarterRigidbody(quarter);
            }
        }
        else
        {
            // Load quarterPlanetPrefab dynamically if not assigned
            if (quarterPlanetPrefab == null)
            {
                quarterPlanetPrefab = Resources.Load<GameObject>(quarterPlanetPrefabPath);
                if (quarterPlanetPrefab == null)
                {
                    Debug.LogError($"PlanetHalf: {gameObject.name} failed to load quarterPlanetPrefab from Assets/Resources/{quarterPlanetPrefabPath}.prefab");
                    if (photonView != null && photonView.IsMine)
                        PhotonNetwork.Destroy(gameObject);
                    return;
                }
                else
                {
                    Debug.Log($"PlanetHalf: {gameObject.name} loaded quarterPlanetPrefab from Assets/Resources/{quarterPlanetPrefabPath}.prefab");
                }
            }

            float offsetDistance = 0.5f;
            float downwardOffset = 0.2f;

            Vector3[] quarterOffsets = new Vector3[]
            {
                new Vector3(-offsetDistance, 0, 0),
                new Vector3(offsetDistance, -downwardOffset, 0)
            };

            Quaternion[] quarterRotations = new Quaternion[]
            {
                Quaternion.Euler(0, 0, 90),
                Quaternion.Euler(0, 0, -90)
            };

            for (int i = 0; i < 2; i++)
            {
                GameObject quarter = PhotonNetwork.Instantiate(quarterPlanetPrefab.name, transform.position + quarterOffsets[i], quarterRotations[i]);
                SetupQuarterRigidbody(quarter);
            }
        }

        Debug.Log($"PlanetHalf: {gameObject.name} split into two quarters at {transform.position}.");
        if (PhotonNetwork.IsConnected && photonView != null && photonView.IsMine)
        {
            PhotonNetwork.Destroy(gameObject);
        }
        else
        {
            Destroy(gameObject);
        }
    }

    private void SetupQuarterRigidbody(GameObject quarter)
    {
        if (quarter == null)
        {
            Debug.LogError($"PlanetHalf: Attempted to set up Rigidbody2D for null quarterPlanet.");
            return;
        }

        Rigidbody2D quarterRb = quarter.GetComponent<Rigidbody2D>();
        if (quarterRb != null)
        {
            quarterRb.mass = 50f;
            quarterRb.linearDamping = 2f;
            quarterRb.linearVelocity = Vector2.zero;
            Debug.Log($"PlanetHalf: Spawned quarterPlanet {quarter.name}, mass={quarterRb.mass}, drag={quarterRb.linearDamping}, velocity={quarterRb.linearVelocity}.");
        }
        else
        {
            Debug.LogError($"PlanetHalf: quarterPlanet {quarter.name} missing Rigidbody2D.");
        }
    }

    public void Launch(int knockedByViewID = -1)
    {
        isLaunched = true;
        canDamage = true;
        launchTime = Time.time;
        hitTargets.Clear();
        lastKnockedByViewID = knockedByViewID;
        Debug.Log($"PlanetHalf: {gameObject.name} launched, damaging for {damageWindow}s, knocked by ViewID={lastKnockedByViewID}, photonView.IsMine={photonView.IsMine}.");
    }

    private void OnCollisionEnter2D(Collision2D other)
    {
        Debug.Log($"PlanetHalf: Collision with {other.gameObject.name}, tag={other.gameObject.tag}, isLaunched={isLaunched}, canDamage={canDamage}, timeSinceLaunch={Time.time - launchTime:F2}, hit={hitTargets.Contains(other.gameObject)}, photonView.IsMine={photonView.IsMine}, otherViewID={other.gameObject.GetPhotonView()?.ViewID}");

        if (!isLaunched || !canDamage || Time.time > launchTime + damageWindow || hitTargets.Contains(other.gameObject))
        {
            Debug.Log($"PlanetHalf: Collision skipped for {other.gameObject.name}: isLaunched={isLaunched}, canDamage={canDamage}, timeSinceLaunch={Time.time - launchTime:F2}, hit={hitTargets.Contains(other.gameObject)}");
            return;
        }

        if (other.gameObject.CompareTag("Blast"))
        {
            Debug.Log($"PlanetHalf: Collision with {other.gameObject.name} (tag=Blast), skipping damage processing.");
            return;
        }

        if (photonView.IsMine)
        {
            PhotonView otherView = other.gameObject.GetPhotonView();
            if (otherView == null)
            {
                Debug.LogWarning($"PlanetHalf: No PhotonView on {other.gameObject.name}, cannot apply damage.");
                return;
            }

            if (other.gameObject.CompareTag("Enemy"))
            {
                photonView.RPC("ApplyDamageRPC", RpcTarget.All, otherView.ViewID, enemyDamage, true);
                Debug.Log($"PlanetHalf: Sent ApplyDamageRPC for Enemy {other.gameObject.name}, ViewID={otherView.ViewID}, damage={enemyDamage}.");
            }
            else if (other.gameObject.CompareTag("Player"))
            {
                photonView.RPC("ApplyDamageRPC", RpcTarget.All, otherView.ViewID, 0, false);
                Debug.Log($"PlanetHalf: Sent ApplyDamageRPC for Player {other.gameObject.name}, ViewID={otherView.ViewID}, damage=20% of maxHealth.");
            }
        }
        else
        {
            Debug.LogWarning($"PlanetHalf: Collision with {other.gameObject.name} ignored, photonView.IsMine=false.");
        }
    }

    [PunRPC]
    private void ApplyDamageRPC(int targetViewID, int damage, bool isEnemy)
    {
        PhotonView targetView = PhotonView.Find(targetViewID);
        if (targetView == null || targetView.gameObject == null)
        {
            Debug.LogWarning($"PlanetHalf: ApplyDamageRPC failed, target ViewID={targetViewID} not found.");
            return;
        }

        GameObject target = targetView.gameObject;
        if (hitTargets.Contains(target))
        {
            Debug.Log($"PlanetHalf: ApplyDamageRPC skipped for {target.name}, already hit.");
            return;
        }

        bool isInstigator = targetView.ControllerActorNr == lastKnockedByViewID && lastKnockedByViewID != -1;
        Debug.Log($"PlanetHalf: ApplyDamageRPC processing for {target.name}, isEnemy={isEnemy}, damage={(isEnemy ? damage : "20% of maxHealth")}, isInstigator={isInstigator}, targetViewID={targetViewID}, targetActorNr={targetView.ControllerActorNr}, knockedBy={lastKnockedByViewID}");

        if (isEnemy)
        {
            EnemyHealth health = target.GetComponent<EnemyHealth>();
            if (health != null)
            {
                health.TakeDamage(health.health); // Instant kill
                hitTargets.Add(target);
                Debug.Log($"PlanetHalf: Dealt {health.health} (instant kill) to {target.name}.");
            }
            else
            {
                Debug.LogWarning($"PlanetHalf: No EnemyHealth on {target.name}.");
            }
        }
        else
        {
            PlayerController player = target.GetComponent<PlayerController>();
            PlayerHealth health = target.GetComponent<PlayerHealth>();
            if (player == null)
            {
                Debug.LogWarning($"PlanetHalf: No PlayerController on {target.name}.");
                return;
            }
            if (health == null)
            {
                Debug.LogWarning($"PlanetHalf: No PlayerHealth on {target.name}.");
                return;
            }
            if (player.isShieldActive)
            {
                Debug.Log($"PlanetHalf: {target.name} shielded (isShieldActive=true), no damage.");
                return;
            }

            int playerDamage = Mathf.FloorToInt(health.maxHealth * playerDamagePercent);
            health.TakeDamage(playerDamage, true, lastKnockedByViewID); // Pass killerViewID
            hitTargets.Add(target);
            Debug.Log($"PlanetHalf: Dealt {playerDamage} (20% of maxHealth={health.maxHealth}) to {target.name} (isInstigator={isInstigator}, ViewID={targetView.ControllerActorNr}, knockedBy={lastKnockedByViewID}).");
        }
    }

    private void Update()
    {
        if (isLaunched && Time.time > launchTime + damageWindow)
        {
            isLaunched = false;
            canDamage = false;
            Debug.Log($"PlanetHalf: {gameObject.name} damage window expired.");
        }
    }
}