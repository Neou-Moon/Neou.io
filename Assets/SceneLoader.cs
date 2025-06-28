using Photon.Pun;
using UnityEngine.SceneManagement;

public class SceneLoader : MonoBehaviourPun
{
    private static SceneLoader _instance;

    void Awake()
    {
        if (_instance != null) Destroy(gameObject);
        else
        {
            _instance = this;
            DontDestroyOnLoad(gameObject);
        }
    }

    public static void LoadNetworkScene(string sceneName)
    {
        if (PhotonNetwork.IsConnected) PhotonNetwork.LoadLevel(sceneName);
        else SceneManager.LoadScene(sceneName);
    }
}