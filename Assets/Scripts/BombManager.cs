using UnityEngine;
using UnityEngine.SceneManagement;
using Photon.Pun;
using System.Collections;
using ExitGames.Client.Photon;

[RequireComponent(typeof(PhotonView))]
public class BombManager : MonoBehaviourPunCallbacks
{
    private IPlayer owner; private GameObject activeBomb; private float lastBombTime = -5f; private const float bombCooldown = 5f; private PlayerCanvasController canvasController;

    void Awake()
    {
        // Find the parent IPlayer (PlayerController or BotController)
        owner = GetComponentInParent<IPlayer>();
        if (owner == null)
        {
            CustomLogger.LogError($"BombManager: No IPlayer found in parent of {gameObject.name}, destroying.");
            Destroy(gameObject);
            return;
        }

        // For PlayerController, get the canvas controller
        if (owner is PlayerController playerController)
        {
            Canvas playerCanvas = playerController.GetComponentInChildren<Canvas>();
            if (playerCanvas != null)
            {
                canvasController = playerCanvas.GetComponent<PlayerCanvasController>();
                if (canvasController == null)
                {
                    CustomLogger.LogWarning($"BombManager: PlayerCanvasController not found for {owner.NickName}.");
                }
            }
        }

        CustomLogger.Log($"BombManager: Initialized for {owner.NickName}, ActorNumber={owner.ActorNumber}, ViewID={photonView.ViewID}");
    }

    public void TryDeployBomb()
    {
        if (!photonView.IsMine)
        {
            CustomLogger.Log($"BombManager: TryDeployBomb skipped for {owner.NickName}, photonView.IsMine=false");
            return;
        }

        if (!PhotonNetwork.IsConnected)
        {
            CustomLogger.LogError($"BombManager: Cannot deploy bomb, not connected to Photon for {owner.NickName}.");
            return;
        }

        if (owner.ActorNumber <= 0)
        {
            CustomLogger.LogError($"BombManager: Invalid ActorNumber={owner.ActorNumber} for {owner.NickName}. Cannot deploy bomb.");
            return;
        }

        GameObject bombPrefab = Resources.Load<GameObject>("Prefabs/ElephantBomb");
        if (bombPrefab == null)
        {
            CustomLogger.LogError($"BombManager: ElephantBomb prefab not found at Assets/Resources/Prefabs/ElephantBomb.prefab for {owner.NickName}.");
            return;
        }

        // If there's an active bomb, explode it but don't reset the icon
        if (activeBomb != null)
        {
            ElephantBomb bombScript = activeBomb.GetComponent<ElephantBomb>();
            if (bombScript != null)
            {
                bombScript.ForceExplode();
                CustomLogger.Log($"BombManager: Detonated existing bomb for {owner.NickName}, ViewID={bombScript.photonView.ViewID}");
            }
            else
            {
                CustomLogger.LogError($"BombManager: Active bomb missing ElephantBomb script for {owner.NickName}.");
            }
            activeBomb = null;
            return; // Exit after detonation to prevent immediate redeployment
        }

        // Deploy a new bomb if cooldown allows
        if (Time.time - lastBombTime >= bombCooldown)
        {
            // Ensure owner has a Team property
            if (owner.CustomProperties == null || !owner.CustomProperties.ContainsKey("Team"))
            {
                ExitGames.Client.Photon.Hashtable props = new ExitGames.Client.Photon.Hashtable();
                if (owner.CustomProperties != null)
                {
                    foreach (var entry in owner.CustomProperties)
                    {
                        props[entry.Key] = entry.Value;
                    }
                }
                string defaultTeam = SceneManager.GetActiveScene().name == "TeamMoonRan" ? "Red" : "None"; // Default to Red in TeamMoonRan
                props["Team"] = defaultTeam;
                if (owner is PlayerController playerController && playerController.photonView != null)
                {
                    playerController.photonView.Owner.SetCustomProperties(props);
                    CustomLogger.Log($"BombManager: Set default Team={defaultTeam} for player {owner.NickName}, ActorNumber={owner.ActorNumber}");
                }
                else if (owner is BotController botController && botController.photonView != null)
                {
                    botController.photonView.RPC("UpdateCustomProperties", RpcTarget.AllBuffered, props);
                    CustomLogger.Log($"BombManager: Set default Team={defaultTeam} for bot {owner.NickName}, ActorNumber={owner.ActorNumber}");
                }
            }

            Vector3 bombPosition = owner is PlayerController
                ? transform.position + transform.up * 2f
                : transform.position + transform.up * 2f; // Same for BotController
            try
            {
                activeBomb = PhotonNetwork.Instantiate("Prefabs/ElephantBomb", bombPosition, Quaternion.identity);
                PhotonView bombView = activeBomb.GetComponent<PhotonView>();
                if (bombView != null)
                {
                    // Set ownership to the BombManager's PhotonView to track killer
                    bombView.TransferOwnership(PhotonNetwork.LocalPlayer);
                    // Store owner information
                    bombView.RPC("SetOwner", RpcTarget.AllBuffered, owner.ActorNumber, photonView.ViewID);
                    lastBombTime = Time.time;
                    if (canvasController != null)
                    {
                        canvasController.StartBombIconReveal(bombCooldown); // Start red fade animation
                        CustomLogger.Log($"BombManager: Started BombIcon gradient reveal for {owner.NickName}");
                    }
                    CustomLogger.Log($"BombManager: Deployed bomb for {owner.NickName}, BombViewID={bombView.ViewID}, Position={bombPosition}");
                }
                else
                {
                    CustomLogger.LogError($"BombManager: Bomb PhotonView missing after instantiation for {owner.NickName}.");
                    PhotonNetwork.Destroy(activeBomb);
                    activeBomb = null;
                }
            }
            catch (System.Exception e)
            {
                CustomLogger.LogError($"BombManager: Failed to instantiate bomb for {owner.NickName}: {e.Message}");
                activeBomb = null;
            }
        }
        else
        {
            CustomLogger.Log($"BombManager: Bomb on cooldown for {owner.NickName}, time since last={Time.time - lastBombTime:F2}");
        }
    }

    public void OnBombExploded()
    {
        activeBomb = null;
        CustomLogger.Log($"BombManager: Bomb exploded, cleared activeBomb for {owner.NickName}");
    }

    public float GetLastBombTime()
    {
        return lastBombTime;
    }

    public bool HasActiveBomb()
    {
        return activeBomb != null;
    }
}