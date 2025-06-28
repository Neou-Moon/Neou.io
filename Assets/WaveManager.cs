using UnityEngine;
using Photon.Pun;
using System.Collections;
using TMPro;
using UnityEngine.SceneManagement;

[RequireComponent(typeof(PhotonView))]
public class WaveManager : MonoBehaviourPunCallbacks
{
    [SerializeField] private TextMeshProUGUI timerText; // Assign WaveTimerText in Inspector
    private const float WAVE_INTERVAL = 120f; // Total cycle: 90s pre-wave + 30s wave
    private const float WAVE_DURATION = 30f; // Wave lasts 30 seconds
    private const float PRE_WAVE_DURATION = 90f; // Pre-wave countdown
    private const float SPAWN_INTERVAL = 5f; // Spawn 1 enemy every 5 seconds
    private const int ENEMIES_PER_WAVE = 6; // 30 ÷ 5 = 6 enemies
    private float timer = PRE_WAVE_DURATION; // Tracks countdown
    private bool isWavePhase = false; // False: pre-wave, True: wave
    private bool isInitialized = false; // Tracks if timer has started

    // Public getter for timerText
    public TextMeshProUGUI TimerText => timerText;

    void Start()
    {
        if (!PhotonNetwork.IsConnected && !PhotonNetwork.OfflineMode)
        {
            Debug.LogError($"WaveManager: Not connected to Photon and not in offline mode, disabling. State={PhotonNetwork.NetworkClientState}, frame={Time.frameCount}");
            enabled = false;
            return;
        }

        if (photonView == null)
        {
            Debug.LogError($"WaveManager: PhotonView missing on WaveManager, frame={Time.frameCount}");
            enabled = false;
            return;
        }

        if (timerText == null)
        {
            Debug.LogError($"WaveManager: timerText not assigned in Inspector on {gameObject.name}. Attempting to find WaveTimerText.");
            timerText = GameObject.Find("WaveTimerText")?.GetComponent<TextMeshProUGUI>();
            if (timerText == null)
            {
                Debug.LogError($"WaveManager: WaveTimerText not found in scene. Ensure it exists under Canvas.");
                enabled = false;
                return;
            }
        }

        timerText.raycastTarget = false;
        timerText.gameObject.SetActive(true);
        timerText.text = "Waiting for wave...";
        Debug.Log($"WaveManager: Initialized, timerText={timerText.name}, photonView.ID={photonView.ViewID}, IsConnected={PhotonNetwork.IsConnected}, IsMasterClient={PhotonNetwork.IsMasterClient}, frame={Time.frameCount}");

        StartCoroutine(InitializeWithRetry());
    }

    private IEnumerator InitializeWithRetry()
    {
        float timeout = 15f;
        float elapsed = 0f;
        MatchTimerManager matchTimerManager = null;

        // Wait for MatchTimerManager and match start
        while (elapsed < timeout)
        {
            matchTimerManager = MatchTimerManager.Instance;
            if (matchTimerManager != null && matchTimerManager.IsMatchStarted && !matchTimerManager.IsMatchEnded)
            {
                Debug.Log($"WaveManager: MatchTimerManager singleton found and match started, proceeding with initialization, Frame={Time.frameCount}");
                break;
            }
            if (matchTimerManager == null)
            {
                Debug.Log($"WaveManager: MatchTimerManager singleton not found, Elapsed={elapsed:F2}s, Frame={Time.frameCount}");
            }
            else
            {
                Debug.Log($"WaveManager: MatchTimerManager found but match not started, IsMatchStarted={matchTimerManager.IsMatchStarted}, IsMatchEnded={matchTimerManager.IsMatchEnded}, Elapsed={elapsed:F2}s, Frame={Time.frameCount}");
            }
            elapsed += Time.deltaTime;
            yield return null;
        }

        if (matchTimerManager == null)
        {
            Debug.LogWarning($"WaveManager: MatchTimerManager singleton not found after {elapsed:F2}s, proceeding in offline mode or as Master Client, Frame={Time.frameCount}");
        }

        // Wait for correct scene and network state
        elapsed = 0f;
        while (elapsed < timeout)
        {
            string sceneName = SceneManager.GetActiveScene().name;
            bool isCorrectScene = sceneName == "Moon Ran" || sceneName == "TeamMoonRan";
            bool isNetworkReady = PhotonNetwork.OfflineMode || PhotonNetwork.InRoom || PhotonNetwork.NetworkClientState == Photon.Realtime.ClientState.Joined || PhotonNetwork.NetworkClientState == Photon.Realtime.ClientState.ConnectingToGameServer;

            if (isCorrectScene && isNetworkReady)
            {
                Debug.Log($"WaveManager: Triggering InitializeWaveTimer, scene={sceneName}, PhotonState={PhotonNetwork.NetworkClientState}, Frame={Time.frameCount}");
                photonView.RPC("InitializeWaveTimer", RpcTarget.AllBuffered);
                yield break;
            }
            Debug.Log($"WaveManager: Waiting for correct scene or network state, Scene={sceneName}, State={PhotonNetwork.NetworkClientState}, Elapsed={elapsed:F2}s, Frame={Time.frameCount}");
            elapsed += Time.deltaTime;
            yield return null;
        }

        Debug.LogError($"WaveManager: Failed to initialize after {timeout}s, disabling. Scene={SceneManager.GetActiveScene().name}, State={PhotonNetwork.NetworkClientState}, Frame={Time.frameCount}");
        enabled = false;

        // Fallback: Request Master Client to initialize if not offline
        if (!PhotonNetwork.OfflineMode && PhotonNetwork.InRoom && !PhotonNetwork.IsMasterClient)
        {
            Debug.Log($"WaveManager: Requesting Master Client to initialize WaveManager, Frame={Time.frameCount}");
            photonView.RPC("RequestWaveInitialization", RpcTarget.MasterClient);
        }
    }

    [PunRPC]
    void RequestWaveInitialization()
    {
        if (PhotonNetwork.IsMasterClient)
        {
            Debug.Log($"WaveManager: Master Client received initialization request, re-triggering InitializeWaveTimer, Frame={Time.frameCount}");
            photonView.RPC("InitializeWaveTimer", RpcTarget.AllBuffered);
        }
    }

    public override void OnJoinedRoom()
    {
        Debug.Log($"WaveManager: Joined room {PhotonNetwork.CurrentRoom.Name}, IsMasterClient={PhotonNetwork.IsMasterClient}, frame={Time.frameCount}");
        // Trigger initialization for Master Client or offline mode
        if (PhotonNetwork.IsMasterClient || PhotonNetwork.OfflineMode)
        {
            StartCoroutine(InitializeWithRetry());
        }
    }

    void Update()
    {
        if (!PhotonNetwork.IsMasterClient || !isInitialized || !PhotonNetwork.InRoom)
        {
            Debug.Log($"WaveManager: Update skipped, IsMasterClient={PhotonNetwork.IsMasterClient}, isInitialized={isInitialized}, InRoom={PhotonNetwork.InRoom}, State={PhotonNetwork.NetworkClientState}, frame={Time.frameCount}");
            return;
        }

        if (timer > 0)
        {
            timer -= Time.deltaTime;
            string timerTextValue = isWavePhase ? $"Wave Active: {Mathf.CeilToInt(timer)}" : $"Next Wave: {Mathf.CeilToInt(timer)}";
            photonView.RPC("SyncWaveState", RpcTarget.All, timerTextValue);
            Debug.Log($"WaveManager: Sent SyncWaveState RPC, timer={timer:F2}, isWavePhase={isWavePhase}, text={timerTextValue}, frame={Time.frameCount}");
            if (timer <= 0)
            {
                timer = 0;
                string endText = isWavePhase ? "Wave Active: 0" : "Next Wave: 0";
                photonView.RPC("SyncWaveState", RpcTarget.All, endText);
                Debug.Log($"WaveManager: {(isWavePhase ? "Wave" : "Pre-wave")} ended, next phase in {(isWavePhase ? PRE_WAVE_DURATION : WAVE_DURATION)}s, sent text={endText}, frame={Time.frameCount}");
            }
        }
    }

    [PunRPC]
    void InitializeWaveTimer()
    {
        Debug.Log($"WaveManager: InitializeWaveTimer called, IsMasterClient={PhotonNetwork.IsMasterClient}, IsInitialized={isInitialized}, Scene={SceneManager.GetActiveScene().name}, PhotonState={PhotonNetwork.NetworkClientState}, Frame={Time.frameCount}");
        if (isInitialized)
        {
            Debug.Log($"WaveManager: InitializeWaveTimer skipped, already initialized, Frame={Time.frameCount}");
            return;
        }

        isInitialized = true;
        if (PhotonNetwork.OfflineMode || PhotonNetwork.IsMasterClient)
        {
            timer = PRE_WAVE_DURATION;
            isWavePhase = false;
            StartCoroutine(WaveCycle());
            string initialText = $"Next Wave: {Mathf.CeilToInt(timer)}";
            photonView.RPC("SyncWaveState", RpcTarget.AllBuffered, initialText);
            Debug.Log($"WaveManager: Initialized wave timer, timer={timer:F2}, sent text={initialText}, photonView.ID={photonView.ViewID}, Frame={Time.frameCount}");
        }
        else
        {
            Debug.Log($"WaveManager: Wave timer initialized on non-master client, waiting for SyncWaveState, Frame={Time.frameCount}");
        }
    }

    IEnumerator WaveCycle()
    {
        while (true)
        {
            timer = PRE_WAVE_DURATION;
            isWavePhase = false;
            string preWaveText = $"Next Wave: {Mathf.CeilToInt(timer)}";
            photonView.RPC("SyncWaveState", RpcTarget.AllBuffered, preWaveText);
            Debug.Log($"WaveManager: Pre-wave started, counting down {PRE_WAVE_DURATION}s, sent text={preWaveText}, frame={Time.frameCount}");

            yield return new WaitForSeconds(PRE_WAVE_DURATION);

            if (!PhotonNetwork.IsMasterClient)
            {
                Debug.LogWarning($"WaveManager: Master client changed, stopping WaveCycle, frame={Time.frameCount}");
                yield break;
            }
            timer = WAVE_DURATION;
            isWavePhase = true;
            string waveText = $"Wave Active: {Mathf.CeilToInt(timer)}";
            photonView.RPC("SyncWaveState", RpcTarget.AllBuffered, waveText);
            Debug.Log($"WaveManager: Wave started, triggering {ENEMIES_PER_WAVE} enemy spawns over {WAVE_DURATION}s, sent text={waveText}, frame={Time.frameCount}");

            for (int i = 0; i < ENEMIES_PER_WAVE; i++)
            {
                photonView.RPC("TriggerEnemySpawn", RpcTarget.All);
                Debug.Log($"WaveManager: Sent TriggerEnemySpawn RPC #{i + 1}/{ENEMIES_PER_WAVE}, frame={Time.frameCount}");
                yield return new WaitForSeconds(SPAWN_INTERVAL);
            }

            yield return new WaitForSeconds(WAVE_DURATION % SPAWN_INTERVAL);
        }
    }

    public override void OnDisconnected(Photon.Realtime.DisconnectCause cause)
    {
        Debug.Log($"WaveManager: Disconnected from Photon, cause={cause}, frame={Time.frameCount}");
        isInitialized = false; // Reset initialization state on disconnect
    }

    [PunRPC]
    void SyncWaveState(string timerTextValue)
    {
        if (timerText != null)
        {
            timerText.text = timerTextValue;
            timerText.gameObject.SetActive(true);
            Debug.Log($"WaveManager: Synced wave state, set text={timerTextValue}, text active={timerText.gameObject.activeSelf}, frame={Time.frameCount}");
        }
        else
        {
            Debug.LogError($"WaveManager: SyncWaveState failed, timerText is null, received text={timerTextValue}, frame={Time.frameCount}");
        }
    }

    [PunRPC]
    void TriggerEnemySpawn()
    {
        Debug.Log($"WaveManager: Received TriggerEnemySpawn call, Frame={Time.frameCount}");
    }
}