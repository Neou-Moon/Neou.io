using UnityEngine;
using Photon.Pun;
using System.Collections;

[RequireComponent(typeof(PhotonView))]
public class PhasingTeleportation : MonoBehaviourPunCallbacks
{
    [SerializeField] private float teleportDistance = 60f; [SerializeField] private float teleportDelay = 0.5f; [SerializeField] public float fuelCostPerTeleport = 10f; [SerializeField] private ParticleSystem teleportInEffect; [SerializeField] private ParticleSystem teleportOutEffect;

    private bool canTeleport = true;
    public bool CanTeleport => canTeleport; // Public getter for canTeleport
    private Vector2 movementDirection;
    private PlayerFuel playerFuel;
    private BotFuel botFuel;
    private bool spacePressedThisFrame = false;

    void Start()
    {
        if (gameObject.CompareTag("Player"))
        {
            playerFuel = GetComponent<PlayerFuel>();
            if (playerFuel == null)
            {
                Debug.LogError($"PhasingTeleportation: PlayerFuel component not found on {gameObject.name} (tagged as Player). Teleportation disabled.");
                enabled = false;
                return;
            }
        }
        else if (gameObject.CompareTag("Bot"))
        {
            botFuel = GetComponent<BotFuel>();
            if (botFuel == null)
            {
                Debug.LogError($"PhasingTeleportation: BotFuel component not found on {gameObject.name} (tagged as Bot). Teleportation disabled.");
                enabled = false;
                return;
            }
        }
        else
        {
            Debug.LogError($"PhasingTeleportation: GameObject {gameObject.name} has invalid tag (not Player or Bot). Teleportation disabled.");
            enabled = false;
            return;
        }

        // Preload effects
        StartCoroutine(PreloadEffects());

        Debug.Log($"PhasingTeleportation: Initialized for {gameObject.name}, tag={gameObject.tag}, playerFuel={(playerFuel != null ? "present" : "null")}, botFuel={(botFuel != null ? "present" : "null")}");
    }

    private IEnumerator PreloadEffects()
    {
        if (teleportOutEffect == null)
        {
            GameObject outEffectObj = Resources.Load<GameObject>("Prefabs/TeleportOutEffect");
            if (outEffectObj != null)
            {
                teleportOutEffect = outEffectObj.GetComponent<ParticleSystem>();
                CustomLogger.Log($"PhasingTeleportation: Loaded teleportOutEffect from Resources for {gameObject.name}");
            }
            else
            {
                Debug.LogWarning($"PhasingTeleportation: teleportOutEffect not assigned and not found in Resources for {gameObject.name}.");
            }
        }
        if (teleportInEffect == null)
        {
            GameObject inEffectObj = Resources.Load<GameObject>("Prefabs/TeleportInEffect");
            if (inEffectObj != null)
            {
                teleportInEffect = inEffectObj.GetComponent<ParticleSystem>();
                CustomLogger.Log($"PhasingTeleportation: Loaded teleportInEffect from Resources for {gameObject.name}");
            }
            else
            {
                Debug.LogWarning($"PhasingTeleportation: teleportInEffect not assigned and not found in Resources for {gameObject.name}.");
            }
        }
        yield return null;
    }

    void Update()
    {
        if (!photonView.IsMine) return;

        if (gameObject.CompareTag("Player") && playerFuel != null)
        {
            float horizontalInput = 0f;
            float verticalInput = 0f;
            if (Input.GetKey(KeyCode.W)) verticalInput += 1f;
            if (Input.GetKey(KeyCode.S)) verticalInput -= 1f;
            if (Input.GetKey(KeyCode.A)) horizontalInput -= 1f;
            if (Input.GetKey(KeyCode.D)) horizontalInput += 1f;
            movementDirection = new Vector2(horizontalInput, verticalInput).normalized;

            if (Input.GetKeyDown(KeyCode.Space) && CanTeleport && !spacePressedThisFrame)
            {
                spacePressedThisFrame = true;
                bool canAfford = playerFuel.CanAffordFuel(fuelCostPerTeleport);
                Debug.Log($"PhasingTeleportation: Teleport attempt for {gameObject.name}, canTeleport={CanTeleport}, movementDirection={movementDirection}, canAffordFuel={canAfford}, currentFuel={playerFuel.CurrentFuel}");
                if (canAfford)
                {
                    Teleport();
                }
                else
                {
                    // Warning removed to suppress log
                }
            }
            else if (!Input.GetKey(KeyCode.Space))
            {
                spacePressedThisFrame = false;
            }
        }
    }

    public void Teleport()
    {
        if (!photonView.IsMine)
        {
            Debug.LogWarning($"PhasingTeleportation: Teleport called on {gameObject.name} but photonView.IsMine=false.");
            return;
        }

        Vector2 teleportTarget = (Vector2)transform.position + (movementDirection * teleportDistance);
        Debug.Log($"PhasingTeleportation: Initiating teleport for {gameObject.name}, movementDirection={movementDirection}, teleportTarget={teleportTarget}");
        PerformTeleport(teleportTarget, true);
    }

    public void Teleport(Vector2 newPosition)
    {
        if (!photonView.IsMine)
        {
            Debug.LogWarning($"PhasingTeleportation: Teleport(Vector2) called on {gameObject.name} but photonView.IsMine=false.");
            return;
        }

        if (botFuel != null && !botFuel.CanAffordFuel(fuelCostPerTeleport))
        {
            Debug.LogWarning($"PhasingTeleportation: Bot cannot teleport for {gameObject.name}, insufficient fuel (required={fuelCostPerTeleport}, current={botFuel.CurrentFuel})");
            return;
        }

        PerformTeleport(newPosition, false);
    }

    private void PerformTeleport(Vector2 teleportTarget, bool isPlayerTeleport)
    {
        if (isPlayerTeleport && playerFuel != null && !playerFuel.CanAffordFuel(fuelCostPerTeleport))
        {
            // Warning removed to suppress log
            return;
        }
        if (!isPlayerTeleport && botFuel != null && !botFuel.CanAffordFuel(fuelCostPerTeleport))
        {
            // Warning removed to suppress log
            return;
        }

        if (teleportOutEffect != null)
        {
            if (photonView.IsMine)
            {
                PhotonNetwork.Instantiate("Prefabs/" + teleportOutEffect.name, transform.position, Quaternion.identity);
            }
        }

        transform.position = teleportTarget;

        if (teleportInEffect != null)
        {
            if (photonView.IsMine)
            {
                PhotonNetwork.Instantiate("Prefabs/" + teleportInEffect.name, transform.position, Quaternion.identity);
            }
        }

        if (isPlayerTeleport && playerFuel != null)
        {
            playerFuel.DrainFuel(fuelCostPerTeleport);
        }
        else if (botFuel != null)
        {
            botFuel.DrainFuel(fuelCostPerTeleport);
        }

        photonView.RPC("SyncTeleport", RpcTarget.AllBuffered, teleportTarget);

        StartCoroutine(PhaseDelay());

        string fuelLog = isPlayerTeleport ? $"playerFuel={playerFuel?.CurrentFuel}" : $"botFuel={botFuel?.CurrentFuel}";
        Debug.Log($"PhasingTeleportation: Teleported {gameObject.name} to {teleportTarget}, isPlayerTeleport={isPlayerTeleport}, fuelCost={fuelCostPerTeleport}, {fuelLog}");
    }

    [PunRPC]
    private void SyncTeleport(Vector2 newPosition)
    {
        transform.position = newPosition;
        Debug.Log($"PhasingTeleportation: Synced teleport for {gameObject.name} to {newPosition}");
    }

    private IEnumerator PhaseDelay()
    {
        canTeleport = false;
        yield return new WaitForSeconds(teleportDelay);
        canTeleport = true;
    }

}