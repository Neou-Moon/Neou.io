using UnityEngine;
using Photon.Pun;
using Photon.Realtime;

public class NetworkManager : MonoBehaviourPunCallbacks
{
    public static NetworkManager Instance;
    public GameObject spaceshipMarkerPrefab;

    private bool isConnecting;

    void Awake()
    {
        if (Instance == null)
        {
            Instance = this;
            DontDestroyOnLoad(gameObject);
        }
        else
        {
            Destroy(gameObject);
        }
    }

    void Start()
    {
        ConnectToPhoton();
    }

    public void ConnectToPhoton()
    {
        if (!PhotonNetwork.IsConnected && !isConnecting)
        {
            isConnecting = true;
            PhotonNetwork.PhotonServerSettings.AppSettings.FixedRegion = "eu";
            string username = PlayerPrefs.GetString("PlayerUsername", "Guest" + Random.Range(1000, 9999));
            PhotonNetwork.NickName = username;
            PhotonNetwork.ConnectUsingSettings();
            Debug.Log($"Connecting to Photon with nickname: {username}");
        }
    }

    public override void OnConnectedToMaster()
    {
        Debug.Log("Connected to Photon Master Server");
        isConnecting = false;
        PhotonNetwork.JoinRandomOrCreateRoom();
    }

    public override void OnJoinedRoom()
    {
        Debug.Log($"Joined room: {PhotonNetwork.CurrentRoom.Name}");
        if (PhotonNetwork.IsConnectedAndReady)
        {
            Vector3 shipPosition = new Vector3(Random.Range(-10f, 10f), Random.Range(-10f, 10f), 0);
            GameObject ship = PhotonNetwork.Instantiate("SpaceshipMarker", shipPosition, Quaternion.identity);
            ship.GetComponent<PhotonView>().RPC("InitializeShip", RpcTarget.AllBuffered, PhotonNetwork.LocalPlayer.ActorNumber);

            Vector3 playerPosition = shipPosition + new Vector3(Random.Range(-2f, 2f), Random.Range(-2f, 2f), 0);
            PhotonNetwork.Instantiate("Player", playerPosition, Quaternion.identity);
        }
    }

    public override void OnJoinRandomFailed(short returnCode, string message)
    {
        Debug.Log("Failed to join random room, creating new room...");
        PhotonNetwork.CreateRoom(null, new RoomOptions { MaxPlayers = 20 });
    }

    public override void OnDisconnected(DisconnectCause cause)
    {
        Debug.LogWarning($"Disconnected: {cause}");
        isConnecting = false;
    }
}