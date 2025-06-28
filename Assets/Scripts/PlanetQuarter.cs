using UnityEngine;
using System.Collections.Generic;
using Photon.Pun;
using System.Collections;

public class PlanetQuarter : MonoBehaviourPun
{
    private bool isLaunched;
    private bool canDamage;
    private float launchTime;
    private float damageWindow = 2f;
    private HashSet<GameObject> hitTargets = new HashSet<GameObject>();
    private int enemyDamage = 100;
    private readonly float playerDamagePercent = 0.2f; // 20% of maxHealth
    private Rigidbody2D rb;
    private float spawnTime;
    private float despawnDelay = 15f;
    private int lastKnockedByViewID = -1;

    void Start()
    {
        spawnTime = Time.time;
        rb = GetComponent<Rigidbody2D>();
        if (rb != null)
        {
            rb.mass = 100f;
            rb.linearDamping = 2f;
            rb.linearVelocity = Vector2.zero;
            Debug.Log($"PlanetQuarter: {gameObject.name} spawned, mass={rb.mass}, drag={rb.linearDamping}, velocity={rb.linearVelocity}, photonView.IsMine={photonView.IsMine}");
        }
        else
        {
            Debug.LogError($"PlanetQuarter: {gameObject.name} missing Rigidbody2D.");
        }
    }

    public void DestroyQuarterPlanet()
    {
        if (PhotonNetwork.IsConnected && photonView != null && photonView.IsMine)
        {
            PhotonNetwork.Destroy(gameObject);
            Debug.Log($"PlanetQuarter: Destroyed {gameObject.name} via PhotonNetwork.Destroy.");
        }
        else
        {
            Destroy(gameObject);
            Debug.Log($"PlanetQuarter: Destroyed {gameObject.name} via Destroy.");
        }
    }

    public void Launch(int knockedByViewID = -1)
    {
        isLaunched = true;
        canDamage = true;
        launchTime = Time.time;
        hitTargets.Clear();
        lastKnockedByViewID = knockedByViewID;
        Debug.Log($"PlanetQuarter: {gameObject.name} launched, damaging for {damageWindow}s, knocked by ViewID={lastKnockedByViewID}, photonView.IsMine={photonView.IsMine}.");
    }

    private void OnCollisionEnter2D(Collision2D other)
    {
        Debug.Log($"PlanetQuarter: Collision with {other.gameObject.name}, tag={other.gameObject.tag}, isLaunched={isLaunched}, canDamage={canDamage}, timeSinceLaunch={Time.time - launchTime:F2}, hit={hitTargets.Contains(other.gameObject)}, photonView.IsMine={photonView.IsMine}, otherViewID={other.gameObject.GetPhotonView()?.ViewID}");

        if (!isLaunched || !canDamage || Time.time > launchTime + damageWindow || hitTargets.Contains(other.gameObject))
        {
            Debug.Log($"PlanetQuarter: Collision skipped for {other.gameObject.name}: isLaunched={isLaunched}, canDamage={canDamage}, timeSinceLaunch={Time.time - launchTime:F2}, hit={hitTargets.Contains(other.gameObject)}");
            return;
        }

        if (other.gameObject.CompareTag("Blast"))
        {
            Debug.Log($"PlanetQuarter: Collision with {other.gameObject.name} (tag=Blast), skipping damage processing.");
            return;
        }

        if (photonView.IsMine)
        {
            PhotonView otherView = other.gameObject.GetPhotonView();
            if (otherView == null)
            {
                Debug.LogWarning($"PlanetQuarter: No PhotonView on {other.gameObject.name}, cannot apply damage.");
                return;
            }

            if (other.gameObject.CompareTag("Enemy"))
            {
                photonView.RPC("ApplyDamageRPC", RpcTarget.All, otherView.ViewID, enemyDamage, true);
                Debug.Log($"PlanetQuarter: Sent ApplyDamageRPC for Enemy {other.gameObject.name}, ViewID={otherView.ViewID}, damage={enemyDamage}.");
            }
            else if (other.gameObject.CompareTag("Player"))
            {
                photonView.RPC("ApplyDamageRPC", RpcTarget.All, otherView.ViewID, 0, false);
                Debug.Log($"PlanetQuarter: Sent ApplyDamageRPC for Player {other.gameObject.name}, ViewID={otherView.ViewID}, damage=20% of maxHealth.");
            }
        }
        else
        {
            Debug.LogWarning($"PlanetQuarter: Collision with {other.gameObject.name} ignored, photonView.IsMine=false.");
        }
    }

    [PunRPC]
    private void ApplyDamageRPC(int targetViewID, int damage, bool isEnemy)
    {
        PhotonView targetView = PhotonView.Find(targetViewID);
        if (targetView == null || targetView.gameObject == null)
        {
            Debug.LogWarning($"PlanetQuarter: ApplyDamageRPC failed, target ViewID={targetViewID} not found.");
            return;
        }

        GameObject target = targetView.gameObject;
        if (hitTargets.Contains(target))
        {
            Debug.Log($"PlanetQuarter: ApplyDamageRPC skipped for {target.name}, already hit.");
            return;
        }

        bool isInstigator = targetView.ControllerActorNr == lastKnockedByViewID && lastKnockedByViewID != -1;
        Debug.Log($"PlanetQuarter: ApplyDamageRPC processing for {target.name}, isEnemy={isEnemy}, damage={(isEnemy ? damage : "20% of maxHealth")}, isInstigator={isInstigator}, targetViewID={targetViewID}, targetActorNr={targetView.ControllerActorNr}, knockedBy={lastKnockedByViewID}");

        if (isEnemy)
        {
            EnemyHealth health = target.GetComponent<EnemyHealth>();
            if (health != null)
            {
                health.TakeDamage(health.health); // Instant kill
                hitTargets.Add(target);
                Debug.Log($"PlanetQuarter: Dealt {health.health} (instant kill) to {target.name}.");
            }
            else
            {
                Debug.LogWarning($"PlanetQuarter: No EnemyHealth on {target.name}.");
            }
        }
        else
        {
            PlayerController player = target.GetComponent<PlayerController>();
            PlayerHealth health = target.GetComponent<PlayerHealth>();
            if (player == null)
            {
                Debug.LogWarning($"PlanetQuarter: No PlayerController on {target.name}.");
                return;
            }
            if (health == null)
            {
                Debug.LogWarning($"PlanetQuarter: No PlayerHealth on {target.name}.");
                return;
            }
            if (player.isShieldActive)
            {
                Debug.Log($"PlanetQuarter: {target.name} shielded (isShieldActive=true), no damage.");
                return;
            }

            int playerDamage = Mathf.FloorToInt(health.maxHealth * playerDamagePercent);
            health.TakeDamage(playerDamage, true, lastKnockedByViewID); // Pass killerViewID
            hitTargets.Add(target);
            Debug.Log($"PlanetQuarter: Dealt {playerDamage} (20% of maxHealth={health.maxHealth}) to {target.name} (isInstigator={isInstigator}, ViewID={targetView.ControllerActorNr}, knockedBy={lastKnockedByViewID}).");
        }
    }

    private void Update()
    {
        if (isLaunched && Time.time > launchTime + damageWindow)
        {
            isLaunched = false;
            canDamage = false;
            Debug.Log($"PlanetQuarter: {gameObject.name} damage window expired.");
        }

        if (Time.time > spawnTime + despawnDelay)
        {
            DestroyQuarterPlanet();
        }
    }
}