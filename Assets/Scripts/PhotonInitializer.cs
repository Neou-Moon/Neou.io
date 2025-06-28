using UnityEngine;
using Photon.Pun;
using Photon.Realtime;
using System.Collections;
using UnityEngine.SceneManagement;

public class PhotonInitializer : MonoBehaviourPunCallbacks
{
    [SerializeField] private string gameVersion = "1.0";
    [SerializeField] private string roomName = "MoonRanRoom";
    [SerializeField] private byte maxPlayers = 20;

    void Awake()
    {
        DontDestroyOnLoad(gameObject);
    }

    void Start()
    {
        if (SceneManager.GetActiveScene().name == "InsideSpaceShip")
        {
            Debug.Log("PhotonInitializer: Skipping initialization in InsideSpaceShip to ensure clean slate.");
            return;
        }

        // Check if LoadingSceneManager is handling connection
        // Changed from FindObjectOfType to FindFirstObjectByType to resolve CS0618 warning
        if (Object.FindFirstObjectByType<LoadingSceneManager>() != null)
        {
            Debug.Log("PhotonInitializer: LoadingSceneManager detected, skipping connection to avoid conflicts.");
            return;
        }

        if (!PhotonNetwork.IsConnected)
        {
            Debug.Log("PhotonInitializer: Connecting to Photon...");
            PhotonNetwork.GameVersion = gameVersion;

            if (string.IsNullOrEmpty(PhotonNetwork.NickName))
            {
                string defaultNickName = "Player_" + Random.Range(1000, 9999);
                PhotonNetwork.NickName = defaultNickName;
                Debug.Log($"PhotonInitializer: Set NickName to {defaultNickName}");
            }

            PhotonNetwork.ConnectUsingSettings();
        }
        else
        {
            Debug.Log($"PhotonInitializer: Already connected, NickName={PhotonNetwork.NickName}");
            if (string.IsNullOrEmpty(PhotonNetwork.NickName))
            {
                PhotonNetwork.NickName = "Player_" + Random.Range(1000, 9999);
                Debug.Log($"PhotonInitializer: Set NickName to {PhotonNetwork.NickName}");
            }
        }
    }

    public override void OnConnectedToMaster()
    {
        Debug.Log($"PhotonInitializer: Connected to Master Server, Region={PhotonNetwork.CloudRegion}");
        // Room joining handled by LoadingSceneManager
    }

    private void JoinOrCreateRoom()
    {
        if (PhotonNetwork.InRoom)
        {
            Debug.Log($"PhotonInitializer: Already in room {PhotonNetwork.CurrentRoom.Name}");
            return;
        }

        if (PhotonNetwork.NetworkClientState != ClientState.ConnectedToMasterServer)
        {
            Debug.LogWarning($"PhotonInitializer: Cannot join or create room, client not ready, State: {PhotonNetwork.NetworkClientState}");
            return;
        }

        RoomOptions roomOptions = new RoomOptions
        {
            MaxPlayers = maxPlayers,
            IsVisible = true,
            IsOpen = true
        };

        Debug.Log($"PhotonInitializer: Joining or creating room {roomName} with MaxPlayers={maxPlayers}");
        PhotonNetwork.JoinOrCreateRoom(roomName, roomOptions, TypedLobby.Default);
    }

    public override void OnJoinedRoom()
    {
        Debug.Log($"PhotonInitializer: Joined room {PhotonNetwork.CurrentRoom.Name}, PlayerCount={PhotonNetwork.CurrentRoom.PlayerCount}, MaxPlayers={PhotonNetwork.CurrentRoom.MaxPlayers}");
    }

    public override void OnJoinRoomFailed(short returnCode, string message)
    {
        Debug.LogWarning($"PhotonInitializer: Failed to join room: {message} (Code: {returnCode})");
        StartCoroutine(RetryJoinRoom());
    }

    private IEnumerator RetryJoinRoom()
    {
        yield return new WaitForSeconds(2f);
        if (SceneManager.GetActiveScene().name != "InsideSpaceShip" && PhotonNetwork.NetworkClientState == ClientState.ConnectedToMasterServer)
        {
            JoinOrCreateRoom();
        }
    }

    public override void OnDisconnected(DisconnectCause cause)
    {
        if (cause == DisconnectCause.ApplicationQuit || cause == DisconnectCause.DisconnectByClientLogic)
        {
            Debug.Log($"PhotonInitializer: Disconnected due to {cause}, no reconnection attempted.");
            return;
        }
        Debug.LogWarning($"PhotonInitializer: Disconnected from Photon: {cause}");
        StartCoroutine(Reconnect());
    }

    private IEnumerator Reconnect()
    {
        int maxRetries = 3;
        int retryCount = 0;
        while (retryCount < maxRetries && !PhotonNetwork.IsConnected && !PhotonNetwork.OfflineMode && SceneManager.GetActiveScene().name != "InsideSpaceShip")
        {
            retryCount++;
            Debug.Log($"PhotonInitializer: Reconnect attempt {retryCount}/{maxRetries}");
            PhotonNetwork.ConnectUsingSettings();
            float timeout = 10f;
            float elapsed = 0f;
            while (elapsed < timeout && !PhotonNetwork.IsConnected && PhotonNetwork.NetworkClientState != ClientState.Disconnected)
            {
                elapsed += Time.deltaTime;
                yield return null;
            }
            if (!PhotonNetwork.IsConnected)
            {
                yield return new WaitForSeconds(2f);
            }
        }
        if (!PhotonNetwork.IsConnected)
        {
            Debug.LogWarning("PhotonInitializer: Failed to reconnect, switching to offline mode.");
            PhotonNetwork.OfflineMode = true;
            JoinOrCreateRoom();
        }
    }
}