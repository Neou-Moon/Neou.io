using UnityEngine;
using Photon.Pun;
using Photon.Realtime;

public class SpaceshipRelocationManager : MonoBehaviourPunCallbacks
{
    private const float RELOCATION_INTERVAL = 120f; // 2 minutes in seconds
    private float timer = 0f;
    private BoundaryManager boundaryManager;

    void Start()
    {
        boundaryManager = FindFirstObjectByType<BoundaryManager>();
        if (boundaryManager == null)
        {
            CustomLogger.LogError("SpaceshipRelocationManager: BoundaryManager not found in scene. Disabling script.");
            enabled = false;
            return;
        }

        if (!PhotonNetwork.IsConnected)
        {
            CustomLogger.LogError("SpaceshipRelocationManager: Not connected to Photon. Disabling script.");
            enabled = false;
            return;
        }

        CustomLogger.Log($"SpaceshipRelocationManager: Initialized successfully. IsMasterClient={PhotonNetwork.IsMasterClient}");
    }

    void Update()
    {
        if (!PhotonNetwork.IsConnected || !PhotonNetwork.IsMasterClient)
        {
            return; // Only Master Client runs the timer
        }

        timer += Time.deltaTime;
        CustomLogger.Log($"SpaceshipRelocationManager: Timer={timer:F2}/{RELOCATION_INTERVAL:F2}");
        if (timer >= RELOCATION_INTERVAL)
        {
            TriggerRelocation();
            timer = 0f; // Reset timer
        }
    }

    private void TriggerRelocation()
    {
        if (boundaryManager != null)
        {
            CustomLogger.Log("SpaceshipRelocationManager: Triggering spaceship relocation via BoundaryManager.ResetBoundary.");
            boundaryManager.ResetBoundary();
            if (photonView != null)
            {
                photonView.RPC("NotifyRelocation", RpcTarget.All, Time.time);
            }
            else
            {
                CustomLogger.LogError("SpaceshipRelocationManager: PhotonView missing, cannot send NotifyRelocation RPC.");
            }
        }
        else
        {
            CustomLogger.LogError("SpaceshipRelocationManager: BoundaryManager reference lost, cannot trigger relocation.");
        }
    }

    [PunRPC]
    private void NotifyRelocation(float relocationTime)
    {
        CustomLogger.Log($"SpaceshipRelocationManager: Spaceships relocated at time {relocationTime:F2} seconds.");
        // Update compass for local player
        if (PhotonNetwork.LocalPlayer != null && PhotonNetwork.LocalPlayer.CustomProperties.TryGetValue("CompassViewID", out object compassViewID))
        {
            PhotonView compassView = PhotonView.Find((int)compassViewID);
            if (compassView != null)
            {
                if (PhotonNetwork.LocalPlayer.CustomProperties.TryGetValue("SpaceshipViewID", out object spaceshipViewID))
                {
                    compassView.RPC("UpdateSpaceshipTransform", RpcTarget.AllBuffered, (int)spaceshipViewID);
                    CustomLogger.Log($"SpaceshipRelocationManager: Sent UpdateSpaceshipTransform RPC to Compass ViewID={compassViewID} with SpaceshipViewID={spaceshipViewID}");
                }
                else
                {
                    CustomLogger.LogWarning("SpaceshipRelocationManager: SpaceshipViewID not found in CustomProperties after relocation.");
                }
            }
            else
            {
                CustomLogger.LogWarning($"SpaceshipRelocationManager: CompassViewID={compassViewID} not found after relocation.");
            }
        }
    }

    public override void OnMasterClientSwitched(Player newMasterClient)
    {
        base.OnMasterClientSwitched(newMasterClient);
        CustomLogger.Log($"SpaceshipRelocationManager: Master Client switched to {newMasterClient.NickName}. Resetting timer.");
        timer = 0f; // Reset timer to ensure continuity
    }
}