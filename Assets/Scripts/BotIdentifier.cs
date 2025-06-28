using UnityEngine;
using Photon.Pun;

[RequireComponent(typeof(PhotonView))]
public class BotIdentifier : MonoBehaviourPun
{
    public string botName { get; private set; }

    public void Initialize(string name)
    {
        botName = name;
        gameObject.name = name;
        if (photonView.IsMine)
        {
            photonView.RPC("SyncBotName", RpcTarget.AllBuffered, name);
        }
        Debug.Log($"BotIdentifier: Initialized {name}, ViewID={photonView.ViewID}");
    }

    [PunRPC]
    void SyncBotName(string name)
    {
        botName = name;
        gameObject.name = name;
        Debug.Log($"BotIdentifier: Synced name {name}, ViewID={photonView.ViewID}");
    }
}