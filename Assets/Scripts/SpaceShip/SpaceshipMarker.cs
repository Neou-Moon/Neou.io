using UnityEngine;
using Photon.Pun;
using System.Collections;

[RequireComponent(typeof(SpriteRenderer), typeof(PhotonView))]
public class SpaceshipMarker : MonoBehaviourPun
{
    public int ownerId = -1;
    private SpriteRenderer spriteRenderer;

    void Awake()
    {
        spriteRenderer = GetComponent<SpriteRenderer>();
        if (!spriteRenderer)
        {
            CustomLogger.LogError($"SpaceshipMarker: SpriteRenderer component is missing on {gameObject.name}, ViewID={photonView.ViewID}");
        }
        CustomLogger.Log($"SpaceshipMarker: Awake for ViewID={photonView.ViewID}, HasPhotonView={photonView != null}");
    }

    void Start()
    {
        StartCoroutine(UpdateColorWhenInitialized());
        CustomLogger.Log($"SpaceshipMarker: Started for ViewID={photonView.ViewID}, ownerId={ownerId}");
    }

    private IEnumerator UpdateColorWhenInitialized()
    {
        int maxRetries = 10;
        int retryCount = 0;
        float retryDelay = 0.5f;

        while (retryCount < maxRetries && ownerId == -1)
        {
            retryCount++;
            CustomLogger.Log($"SpaceshipMarker: Waiting for ownerId initialization, retry {retryCount}/{maxRetries} for ViewID={photonView.ViewID}, IsMine={photonView.IsMine}, CurrentRoomPlayers={(PhotonNetwork.CurrentRoom != null ? PhotonNetwork.CurrentRoom.PlayerCount : 0)}");
            yield return new WaitForSeconds(retryDelay);
        }

        if (ownerId == -1 && photonView.IsMine && PhotonNetwork.LocalPlayer != null)
        {
            ownerId = PhotonNetwork.LocalPlayer.ActorNumber;
            CustomLogger.Log($"SpaceshipMarker: Fallback set ownerId={ownerId} for local spaceship ViewID={photonView.ViewID}, coloring green");
            UpdateShipColor();
        }
        else if (ownerId == -1)
        {
            CustomLogger.LogError($"SpaceshipMarker: Failed to initialize ownerId after {maxRetries} retries for ViewID={photonView.ViewID}, IsMine={photonView.IsMine}, setting default color to red");
            if (spriteRenderer != null)
                spriteRenderer.color = Color.red;
            var player = PhotonNetwork.CurrentRoom?.GetPlayer(ownerId);
            CustomLogger.Log($"SpaceshipMarker: Owner check for ViewID={photonView.ViewID}, ownerId={ownerId}, PlayerFound={(player != null ? player.NickName : "null")}");
        }
        else
        {
            UpdateShipColor();
        }
    }

    [PunRPC]
    public void InitializeShip(int newOwnerId)
    {
        if (ownerId != -1 && ownerId != newOwnerId)
        {
            CustomLogger.LogWarning($"SpaceshipMarker: Attempt to change ownerId from {ownerId} to {newOwnerId} for ViewID={photonView.ViewID}, Ignoring to prevent overwrite.");
            return;
        }

        ownerId = newOwnerId;
        CustomLogger.Log($"SpaceshipMarker: Initialized with ownerId={ownerId} for ViewID={photonView.ViewID}");
        UpdateShipColor();

        if (PhotonNetwork.CurrentRoom == null)
        {
            CustomLogger.Log($"SpaceshipMarker: CurrentRoom is null for ViewID={photonView.ViewID}, ownerId={ownerId}, cannot check player, likely during disconnection.");
        }
        else
        {
            var player = PhotonNetwork.CurrentRoom.GetPlayer(ownerId);
            if (player == null)
            {
                CustomLogger.Log($"SpaceshipMarker: Player with ownerId={ownerId} not found in room for ViewID={photonView.ViewID}, likely a bot or disconnected player.");
            }
        }
    }

    [PunRPC]
    public void SyncSpaceshipPosition(Vector3 newPosition)
    {
        if (gameObject != null)
        {
            transform.position = newPosition;
            CustomLogger.Log($"SpaceshipMarker: Synced position to {newPosition} for ViewID={photonView.ViewID}");
        }
    }

    void UpdateShipColor()
    {
        if (!spriteRenderer)
        {
            CustomLogger.LogError($"SpaceshipMarker: Cannot update color for ViewID={photonView.ViewID}, SpriteRenderer is null.");
            return;
        }

        if (PhotonNetwork.LocalPlayer != null)
        {
            bool isOwnShip = ownerId == PhotonNetwork.LocalPlayer.ActorNumber;
            spriteRenderer.color = isOwnShip ? Color.green : Color.red;
            CustomLogger.Log($"SpaceshipMarker: Updated color to {(isOwnShip ? "green" : "red")} for ViewID={photonView.ViewID}, ownerId={ownerId}, LocalPlayer ActorNumber={PhotonNetwork.LocalPlayer.ActorNumber}, IsOwnShip={isOwnShip}");
        }
        else
        {
            CustomLogger.LogWarning($"SpaceshipMarker: LocalPlayer is null for ViewID={photonView.ViewID}, setting default color to red");
            spriteRenderer.color = Color.red;
        }
    }
    public void TriggerColorUpdate()
    {
        UpdateShipColor();
    }
    void OnDestroy()
    {
        if (photonView.IsMine)
        {
            CustomLogger.Log($"SpaceshipMarker: Destroyed ViewID={photonView.ViewID}");
        }
    }
}