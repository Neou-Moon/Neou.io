using UnityEngine;
using Photon.Pun;
using System.Collections;
using Photon.Realtime;
using System.Collections.Generic;

public class TwinTurretManager : MonoBehaviourPunCallbacks
{
    public GameObject droidPrefab; public float spawnOffset = 2f; public float twinTurretDuration = 10f; public float cooldownDuration = 15f;

    private bool twinTurretActive;
    private bool onCooldown;
    private PlayerHealth playerHealth;
    private BotController botController;
    private GameObject activeDroid;
    private DroidShooting childDroidShooting; // Reference to original droid's DroidShooting
    private Joystick shootingJoystick; // Reference to the shooting joystick

    public bool TwinTurretActive => twinTurretActive;
    public bool OnCooldown => onCooldown;

    void Awake()
    {
        // Load droidPrefab if not assigned
        if (droidPrefab == null)
        {
            droidPrefab = Resources.Load<GameObject>("Prefabs/Droid");
            if (droidPrefab == null)
            {
                Debug.LogError($"TwinTurretManager: Failed to load droidPrefab from Assets/Resources/Prefabs/Droid.prefab on {gameObject.name}. Please ensure the prefab exists.");
            }
            else
            {
                Debug.Log($"TwinTurretManager: Loaded droidPrefab from Resources for {gameObject.name}");
            }
        }
    }

    void Start()
    {
        playerHealth = GetComponent<PlayerHealth>();
        botController = GetComponent<BotController>();
        childDroidShooting = GetComponentInChildren<DroidShooting>();

        if (photonView == null)
        {
            Debug.LogError($"TwinTurretManager: PhotonView component is missing on {gameObject.name}. Please add a PhotonView component in the Unity Inspector.");
            return;
        }

        if (droidPrefab == null)
        {
            Debug.LogError($"TwinTurretManager: droidPrefab is not assigned or failed to load for {gameObject.name}. Please assign the Droid prefab from Assets/Resources/Prefabs/Droid.prefab.");
            return;
        }

        if (!droidPrefab.GetComponent<DroidShooting>())
        {
            Debug.LogWarning($"TwinTurretManager: droidPrefab is missing a DroidShooting component for {gameObject.name}.");
        }
        if (!droidPrefab.GetComponent<PhotonView>())
        {
            Debug.LogWarning($"TwinTurretManager: droidPrefab is missing a PhotonView component for {gameObject.name}, required for PhotonNetwork.Instantiate.");
        }

        // Get the shooting joystick from PlayerController
        PlayerController playerController = GetComponent<PlayerController>();
        if (playerController != null)
        {
            shootingJoystick = playerController.GetComponentInChildren<Joystick>(true); // Assuming shootingJoystick is a child component
            if (shootingJoystick == null)
            {
                Debug.LogWarning($"TwinTurretManager: Shooting Joystick not found in PlayerController for {gameObject.name}.");
            }
        }

        Debug.Log($"TwinTurretManager: Initialized for {gameObject.name}, photonView.ViewID={photonView.ViewID}, IsMine={photonView.IsMine}, droidPrefab={(droidPrefab != null ? droidPrefab.name : "null")}");
    }

    public void ActivateTwinTurret()
    {
        if (!photonView.IsMine)
        {
            Debug.Log($"TwinTurretManager: ActivateTwinTurret skipped for {gameObject.name}, photonView.IsMine=false");
            return;
        }

        if (twinTurretActive || onCooldown)
        {
            Debug.Log($"TwinTurretManager: Cannot activate twin turret for {gameObject.name}, active={twinTurretActive}, onCooldown={onCooldown}");
            return;
        }

        if (botController != null && botController.HasDied)
        {
            Debug.Log($"TwinTurretManager: ActivateTwinTurret skipped for bot {gameObject.name}, HasDied={botController.HasDied}");
            return;
        }
        if (playerHealth != null && playerHealth.HasDied)
        {
            Debug.Log($"TwinTurretManager: ActivateTwinTurret skipped for player {gameObject.name}, HasDied={playerHealth.HasDied}");
            return;
        }

        if (!droidPrefab)
        {
            Debug.LogError($"TwinTurretManager: droidPrefab is not assigned for {gameObject.name}. Please assign the Droid prefab from Assets/Resources/Prefabs/Droid.prefab.");
            return;
        }

        if (childDroidShooting == null)
        {
            Debug.LogError($"TwinTurretManager: No DroidShooting component found in children of {gameObject.name}. Ensure Droid.prefab is a child with a DroidShooting component.");
            return;
        }

        // Log who is activating
        string activator = botController != null ? $"Bot {botController.NickName}" : (playerHealth != null ? $"Player {photonView.Owner.NickName}" : "Unknown");
        Debug.Log($"TwinTurretManager: ActivateTwinTurret called by {activator} on {gameObject.name}, ViewID={photonView.ViewID}");

        // Spawn 15 units to the left of the child Droid.prefab
        Vector3 spawnPosition = childDroidShooting.transform.position + new Vector3(-15f, 0f, 0f);

        GameObject cloneDroid;
        if (PhotonNetwork.IsConnectedAndReady)
        {
            Debug.Log($"TwinTurretManager: Instantiating clone Droid at {spawnPosition} via PhotonNetwork for {gameObject.name}, ViewID={photonView.ViewID}");
            try
            {
                cloneDroid = PhotonNetwork.Instantiate("Prefabs/Droid", spawnPosition, Quaternion.identity);
                if (cloneDroid == null)
                {
                    Debug.LogError($"TwinTurretManager: PhotonNetwork.Instantiate returned null for 'Prefabs/Droid' for {gameObject.name}. Ensure the 'Droid' prefab is in Assets/Resources/Prefabs/Droid.prefab and registered with Photon.");
                    return;
                }

                cloneDroid.transform.SetParent(transform, true);
                PhotonView cloneView = cloneDroid.GetComponent<PhotonView>();
                if (cloneView != null)
                {
                    cloneView.TransferOwnership(photonView.Owner);
                    Debug.Log($"TwinTurretManager: Set clone Droid ownership to {photonView.Owner.NickName}, clone ViewID={cloneView.ViewID}, parent={gameObject.name}");
                }
                else
                {
                    Debug.LogWarning($"TwinTurretManager: Clone Droid missing PhotonView for {gameObject.name}, may cause network issues.");
                }
            }
            catch (System.Exception e)
            {
                Debug.LogError($"TwinTurretManager: Failed to instantiate 'Prefabs/Droid' prefab via PhotonNetwork for {gameObject.name}: {e.Message}. Ensure the prefab is in Assets/Resources/Prefabs/Droid.prefab and properly configured for Photon.");
                return;
            }
        }
        else
        {
            Debug.Log($"TwinTurretManager: Instantiating clone Droid at {spawnPosition} locally for {gameObject.name}.");
            cloneDroid = Instantiate(droidPrefab, spawnPosition, Quaternion.identity, transform);
            if (cloneDroid == null)
            {
                Debug.LogError($"TwinTurretManager: Local instantiation of droidPrefab failed for {gameObject.name}. Ensure droidPrefab is assigned and valid.");
                return;
            }
        }

        activeDroid = cloneDroid;
        Debug.Log($"TwinTurretManager: Clone Droid instantiated successfully for {gameObject.name}, position={cloneDroid.transform.position}, parent={cloneDroid.transform.parent?.name ?? "none"}");

        Vector3 originalScale = transform.localScale;
        cloneDroid.transform.localScale = new Vector3(originalScale.x * 2.5f, originalScale.y * 2.5f, originalScale.z);

        if (cloneDroid.TryGetComponent(out SpriteRenderer renderer))
        {
            renderer.sortingOrder = 5;
            renderer.enabled = true;
            Debug.Log($"TwinTurretManager: Set clone Droid SpriteRenderer sortingOrder=5, enabled={renderer.enabled} for {gameObject.name}");
        }
        else
        {
            Debug.LogWarning($"TwinTurretManager: Clone Droid has no SpriteRenderer component for {gameObject.name}.");
        }

        DroidShooting cloneShooting = cloneDroid.GetComponent<DroidShooting>();
        if (cloneShooting != null)
        {
            UpgradeManager upgradeManager = GetComponent<UpgradeManager>();
            if (upgradeManager != null)
            {
                cloneShooting.projectileSpeed = 100f + 8f * upgradeManager.UpgradeLevels[4]; // Bullet Speed
                cloneShooting.bulletDamage = 5f + 5f * upgradeManager.UpgradeLevels[8]; // Bullet Damage
            }
            else
            {
                cloneShooting.projectileSpeed = 200f;
                cloneShooting.bulletDamage = 5f;
            }
            cloneShooting.fireRate = 0.2f;
            cloneShooting.canShoot = true;
            Debug.Log($"TwinTurretManager: Configured clone DroidShooting for {gameObject.name}, projectileSpeed={cloneShooting.projectileSpeed}, bulletDamage={cloneShooting.bulletDamage}, canShoot={cloneShooting.canShoot}");
        }
        else
        {
            Debug.LogWarning($"TwinTurretManager: DroidShooting component missing on clone Droid for {gameObject.name}.");
            return;
        }

        twinTurretActive = true;
        onCooldown = true;
        StartCoroutine(FollowPlayer(cloneDroid, childDroidShooting));
        StartCoroutine(DestroyCloneAfterDuration(cloneDroid));
        StartCoroutine(ResetCooldown());
    }

    IEnumerator FollowPlayer(GameObject clone, DroidShooting childDroid)
    {
        DroidShooting cloneShooting = clone.GetComponent<DroidShooting>();
        if (cloneShooting == null)
        {
            Debug.LogError($"TwinTurretManager: Clone DroidShooting component missing in FollowPlayer for {gameObject.name}.");
            yield break;
        }

        while (clone != null && childDroid != null && gameObject.activeInHierarchy)
        {
            // Keep clone 15 units to the left of the child Droid.prefab
            clone.transform.position = childDroid.transform.position + new Vector3(-15f, 0f, 0f);
            clone.transform.rotation = childDroid.transform.rotation;

            // Synchronize joystick input to the clone
            if (shootingJoystick != null && shootingJoystick.IsActive && shootingJoystick.InputVector.magnitude > 0.1f)
            {
                cloneShooting.SetJoystickInput(shootingJoystick.InputVector);
            }
            else
            {
                cloneShooting.SetJoystickInput(Vector2.zero);
            }

            yield return null;
        }

        if (clone != null && (childDroid == null || !gameObject.activeInHierarchy))
        {
            Debug.Log($"TwinTurretManager: {gameObject.name} is inactive or childDroid is null, destroying clone.");
            DestroyClone(clone);
        }
    }

    IEnumerator DestroyCloneAfterDuration(GameObject clone)
    {
        yield return new WaitForSeconds(twinTurretDuration);
        if (clone != null)
        {
            DestroyClone(clone);
        }
    }
    void DestroyClone(GameObject clone)
    {
        if (clone == null) return;

        // Clear active projectiles for this droid
        DroidShooting droidShooting = clone.GetComponent<DroidShooting>();
        if (droidShooting != null)
        {
            var projectilesToDestroy = new List<GameObject>();
            PhotonView cloneView = clone.GetComponent<PhotonView>();
            int cloneActorNumber = cloneView != null ? cloneView.ControllerActorNr : -1;

            foreach (var projectile in DroidShooting.activeProjectiles)
            {
                if (projectile != null)
                {
                    Projectile projScript = projectile.GetComponent<Projectile>();
                    if (projScript != null && projScript.OwnerActorNumber == cloneActorNumber)
                    {
                        projectilesToDestroy.Add(projectile);
                    }
                }
            }

            foreach (var projectile in projectilesToDestroy)
            {
                if (PhotonNetwork.IsConnected && projectile.GetComponent<PhotonView>() != null && photonView.IsMine)
                {
                    PhotonNetwork.Destroy(projectile);
                    DroidShooting.RemoveProjectile(projectile);
                    Debug.Log($"TwinTurretManager: Destroyed projectile ViewID={projectile.GetComponent<PhotonView>().ViewID} for clone {clone.name}");
                }
                else
                {
                    Destroy(projectile);
                    DroidShooting.RemoveProjectile(projectile);
                    Debug.Log($"TwinTurretManager: Locally destroyed projectile for clone {clone.name}");
                }
            }
            Debug.Log($"TwinTurretManager: Destroyed {projectilesToDestroy.Count} active projectiles for clone {clone.name}");
        }

        if (PhotonNetwork.IsConnected && clone.GetComponent<PhotonView>() != null && photonView.IsMine)
        {
            Debug.Log($"TwinTurretManager: Destroying clone Droid via PhotonNetwork for {gameObject.name}, clone position={clone.transform.position}");
            PhotonNetwork.Destroy(clone);
        }
        else
        {
            Debug.Log($"TwinTurretManager: Destroying clone Droid locally for {gameObject.name}, clone position={clone.transform.position}");
            Destroy(clone);
        }
        activeDroid = null;
        twinTurretActive = false;
    }

    IEnumerator ResetCooldown()
    {
        yield return new WaitForSeconds(twinTurretDuration + cooldownDuration);
        onCooldown = false;
        Debug.Log($"TwinTurretManager: Cooldown reset for {gameObject.name}, twinTurretActive={twinTurretActive}, onCooldown={onCooldown}");
    }

    public void DeactivateTwinTurret()
    {
        if (!twinTurretActive || activeDroid == null) return;
        DestroyClone(activeDroid);
        Debug.Log($"TwinTurretManager: Deactivated twin turret for {gameObject.name}");
    }

    public override void OnPlayerLeftRoom(Player otherPlayer)
    {
        base.OnPlayerLeftRoom(otherPlayer);
        if (PhotonNetwork.IsMasterClient)
        {
            Debug.Log($"TwinTurretManager: Player {otherPlayer.NickName} left, cleanup handled by Photon for {gameObject.name}.");
        }
    }

}