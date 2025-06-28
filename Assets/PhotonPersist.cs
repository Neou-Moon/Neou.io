using UnityEngine;
using Photon.Pun;

public class PhotonPersist : MonoBehaviour
{
    void Awake()
    {
        DontDestroyOnLoad(gameObject);
        CustomLogger.Log("PhotonPersist: Set DontDestroyOnLoad to maintain Photon connection across scenes.");
    }
}