using UnityEngine;
using Photon.Pun;
using System.Collections.Generic;
using System.Collections;

public class DroidShooting : MonoBehaviour
{
    public GameObject projectilePrefab;
    public Transform shootingPoint;
    public float fireRate = 0.2f;
    public float projectileSpeed = 10f;
    public float bulletDamage = 5f;
    public bool canShoot = true;
    public static int maxBullets = 50;

    private float nextFireTime;
    public Vector2 aimDirection = Vector2.right;
    private bool isShooting;
    public static List<GameObject> activeProjectiles = new List<GameObject>();
    private bool isScenePlacedDroid;
    private PhotonView photonView;
    private bool isBot;
    private bool firedThisFrame;
    private PlayerHealth playerHealth;
    private BotController botController;
    private bool isInitialized = false;

    public bool IsInitialized => isInitialized;

    void Awake()
    {
        StartCoroutine(InitializeDroid());
    }

    private IEnumerator InitializeDroid()
    {
        int maxRetries = 5;
        int retries = 0;

        while (retries < maxRetries)
        {
            photonView = GetComponent<PhotonView>();
            if (photonView == null)
            {
                Transform current = transform;
                while (current != null)
                {
                    photonView = current.GetComponent<PhotonView>();
                    if (photonView != null) break;
                    current = current.parent;
                }
            }

            if (photonView != null)
            {
                isScenePlacedDroid = !photonView.IsMine;
                isBot = GetComponentInParent<BotController>() != null;
                playerHealth = GetComponentInParent<PlayerHealth>();
                botController = GetComponentInParent<BotController>();

                if (shootingPoint == null || !shootingPoint.gameObject.activeInHierarchy)
                {
                    shootingPoint = transform.Find("ShootingPoint");
                    if (shootingPoint == null || !shootingPoint.gameObject.activeInHierarchy)
                    {
                        Debug.LogWarning($"DroidShooting: {gameObject.name} shootingPoint not found or inactive, using transform");
                        shootingPoint = transform;
                    }
                }

                if (projectilePrefab == null)
                {
                    Debug.LogError($"DroidShooting: projectilePrefab missing for {gameObject.name}");
                    enabled = false;
                    yield break;
                }

                gameObject.layer = LayerMask.NameToLayer("Ignore Raycast");
                if (projectilePrefab != null)
                {
                    projectilePrefab.layer = LayerMask.NameToLayer("Default");
                }

                isInitialized = true;
                Debug.Log($"DroidShooting: Initialized {gameObject.name}, IsBot={isBot}, IsMine={photonView.IsMine}, ViewID={(photonView != null ? photonView.ViewID.ToString() : "none")}, shootingPoint={shootingPoint.position}, bulletDamage={bulletDamage}");
                yield break;
            }

            Debug.LogWarning($"DroidShooting: PhotonView not found on {gameObject.name} or parents, retry {retries + 1}/{maxRetries}");
            retries++;
            yield return new WaitForSeconds(0.5f);
        }

        Debug.LogError($"DroidShooting: Failed to find PhotonView after {maxRetries} retries on {gameObject.name}. Disabling component.");
        enabled = false;
    }

    void Update()
    {
        firedThisFrame = false;
        if (!isInitialized || !canShoot || !photonView.IsMine)
        {
            Debug.Log($"DroidShooting: {gameObject.name} Update skipped, isInitialized={isInitialized}, canShoot={canShoot}, IsMine={(photonView != null ? photonView.IsMine : false)}");
            return;
        }

        if (isBot && botController != null && botController.HasDied)
        {
            Debug.Log($"DroidShooting: {gameObject.name} Update skipped, bot HasDied={botController.HasDied}");
            return;
        }
        if (!isBot && playerHealth != null && playerHealth.HasDied)
        {
            Debug.Log($"DroidShooting: {gameObject.name} Update skipped, player HasDied={playerHealth.HasDied}");
            return;
        }

        if (isBot)
        {
            if (aimDirection != Vector2.zero && Time.time >= nextFireTime && !firedThisFrame)
            {
                FireProjectile();
                nextFireTime = Time.time + fireRate;
                firedThisFrame = true;
            }
        }
        else
        {
            HandleAiming();
        }
    }

    void HandleAiming()
    {
        if (!isBot)
        {
            Vector2 newDirection = Vector2.zero;
            if (Input.GetKey(KeyCode.UpArrow)) newDirection += Vector2.up;
            if (Input.GetKey(KeyCode.DownArrow)) newDirection += Vector2.down;
            if (Input.GetKey(KeyCode.LeftArrow)) newDirection += Vector2.left;
            if (Input.GetKey(KeyCode.RightArrow)) newDirection += Vector2.right;

            isShooting = newDirection.magnitude > 0.1f;
            if (isShooting)
            {
                aimDirection = newDirection.normalized;
                float angle = Mathf.Atan2(aimDirection.y, aimDirection.x) * Mathf.Rad2Deg;
                transform.rotation = Quaternion.Euler(0, 0, angle);
                if (Time.time >= nextFireTime)
                {
                    FireProjectile();
                    nextFireTime = Time.time + fireRate;
                }
                Debug.Log($"DroidShooting: {gameObject.name} keyboard aim, direction={aimDirection}, isShooting={isShooting}");
            }
            else
            {
                aimDirection = Vector2.right;
                isShooting = false;
                transform.rotation = Quaternion.Euler(0, 0, 0);
                Debug.Log($"DroidShooting: {gameObject.name} no keyboard input, reset aimDirection to right, isShooting={isShooting}");
            }
        }
    }

    public void SetJoystickInput(Vector2 input)
    {
        if (!isInitialized || isBot || (photonView != null && !photonView.IsMine) || transform == null)
        {
            Debug.LogWarning($"DroidShooting: SetJoystickInput failed for {gameObject.name}: isInitialized={isInitialized}, isBot={isBot}, photonView={(photonView != null ? photonView.ViewID.ToString() : "null")}, input={input}, transform={(transform != null ? "present" : "null")}");
            return;
        }

        isShooting = input.magnitude > 0.1f;
        if (isShooting)
        {
            aimDirection = input.normalized;
            float angle = Mathf.Atan2(aimDirection.y, aimDirection.x) * Mathf.Rad2Deg;
            transform.rotation = Quaternion.Euler(0, 0, angle);
            if (Time.time >= nextFireTime)
            {
                FireProjectile();
                nextFireTime = Time.time + fireRate;
            }
            Debug.Log($"DroidShooting: {gameObject.name} joystick aim, direction={aimDirection}, angle={angle}, isShooting={isShooting}");
        }
        else
        {
            aimDirection = Vector2.right;
            isShooting = false;
            transform.rotation = Quaternion.Euler(0, 0, 0);
            Debug.Log($"DroidShooting: {gameObject.name} no joystick input, reset aimDirection to right, isShooting={isShooting}");
        }
    }

    public void FireProjectile()
    {
        if (firedThisFrame)
        {
            Debug.Log($"DroidShooting: {gameObject.name} skipped FireProjectile, already fired this frame");
            return;
        }

        if (activeProjectiles.Count >= maxBullets)
        {
            if (activeProjectiles[0] != null)
            {
                if (PhotonNetwork.IsConnected && activeProjectiles[0].GetComponent<PhotonView>() != null && PhotonNetwork.IsMasterClient)
                {
                    PhotonNetwork.Destroy(activeProjectiles[0]);
                }
                else
                {
                    Destroy(activeProjectiles[0]);
                }
            }
            activeProjectiles.RemoveAt(0);
            Debug.Log($"DroidShooting: {gameObject.name} removed oldest projectile, activeCount={activeProjectiles.Count}");
        }

        bool canSpawn = isScenePlacedDroid || (PhotonNetwork.IsConnectedAndReady && photonView.IsMine);
        if (!canSpawn)
        {
            Debug.Log($"DroidShooting: {gameObject.name} cannot spawn projectile, IsMine={(photonView != null && photonView.IsMine)}");
            return;
        }

        Vector3 spawnPosition = shootingPoint != null && shootingPoint.gameObject.activeInHierarchy ? shootingPoint.position : transform.position;
        Debug.Log($"Spawning projectile at {spawnPosition}, aimDirection={aimDirection}, projectileSpeed={projectileSpeed}, damage={bulletDamage}");

        if (projectilePrefab == null)
        {
            Debug.LogError($"DroidShooting: {gameObject.name} failed to instantiate projectile, prefab missing");
            return;
        }

        GameObject projectile;
        Quaternion rotation = Quaternion.Euler(0, 0, Mathf.Atan2(aimDirection.y, aimDirection.x) * Mathf.Rad2Deg);
        if (PhotonNetwork.IsConnectedAndReady && photonView.IsMine && !isScenePlacedDroid)
        {
            projectile = PhotonNetwork.Instantiate("Projectile", spawnPosition, rotation);
        }
        else
        {
            projectile = Instantiate(projectilePrefab, spawnPosition, rotation);
        }

        if (projectile == null)
        {
            Debug.LogError($"DroidShooting: {gameObject.name} failed to instantiate projectile, instantiation returned null");
            return;
        }

        Projectile projectileScript = projectile.GetComponent<Projectile>();
        Rigidbody2D rb = projectile.GetComponent<Rigidbody2D>();
        if (rb != null)
        {
            rb.linearVelocity = aimDirection * projectileSpeed;
            Debug.Log($"DroidShooting: {gameObject.name} projectile spawned with velocity={rb.linearVelocity}, position={rb.position}");
        }
        else
        {
            Debug.LogWarning($"DroidShooting: {gameObject.name} projectile missing Rigidbody2D, using transform movement");
            if (projectileScript != null)
            {
                projectileScript.UseTransformMovement(aimDirection, projectileSpeed);
            }
            else
            {
                Debug.LogError($"DroidShooting: {gameObject.name} projectile missing Projectile component, destroying");
                if (PhotonNetwork.IsConnected && projectile.GetComponent<PhotonView>() != null && PhotonNetwork.IsMasterClient)
                {
                    PhotonNetwork.Destroy(projectile);
                }
                else
                {
                    Destroy(projectile);
                }
                return;
            }
        }

        if (projectileScript != null)
        {
            projectileScript.SetSpawner(this);
            projectileScript.SetDamage(bulletDamage);
            int ownerActorNumber = -1;
            Transform current = transform;
            while (current != null)
            {
                PlayerController playerController = current.GetComponent<PlayerController>();
                BotController botController = current.GetComponent<BotController>();
                if (playerController != null)
                {
                    ownerActorNumber = playerController.ActorNumber;
                    Debug.Log($"DroidShooting: {gameObject.name} set projectile ownerActorNumber={ownerActorNumber} for player");
                    break;
                }
                else if (botController != null)
                {
                    ownerActorNumber = botController.ActorNumber;
                    Debug.Log($"DroidShooting: {gameObject.name} set projectile ownerActorNumber={ownerActorNumber} for bot");
                    break;
                }
                current = current.parent;
            }
            if (ownerActorNumber == -1)
            {
                Debug.LogWarning($"DroidShooting: {gameObject.name} no parent found for PlayerController or BotController, ownerActorNumber=-1");
            }
            projectileScript.SetOwnerActorNumber(ownerActorNumber);
            activeProjectiles.Add(projectile);
        }
        else
        {
            Debug.LogError($"DroidShooting: {gameObject.name} projectile missing Projectile component");
            if (PhotonNetwork.IsConnected && projectile.GetComponent<PhotonView>() != null && PhotonNetwork.IsMasterClient)
            {
                PhotonNetwork.Destroy(projectile);
            }
            else
            {
                Destroy(projectile);
            }
        }
    }

    public static void RemoveProjectile(GameObject projectile)
    {
        activeProjectiles.Remove(projectile);
    }

    public Vector2 GetAimingDirection()
    {
        return aimDirection;
    }
}