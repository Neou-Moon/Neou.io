using UnityEngine;
using Photon.Pun;
using Photon.Realtime;
using System.Collections.Generic;
using System.Collections;

[RequireComponent(typeof(PhotonView))]
public class BotManager : MonoBehaviourPunCallbacks
{
    public static BotManager Instance { get; private set; }
    [SerializeField] private string botPrefabPath = "Prefabs/Bot";
    private const int maxBots = 9;
    private const int maxTotalEntities = 10; // Players + Bots
    private readonly Dictionary<int, GameObject> bots = new Dictionary<int, GameObject>();
    private readonly List<int> botActorNumbers = new List<int>();
    private readonly List<Vector2> botPositions = new List<Vector2>();
    private bool isSpawningBots = false;
    private int nextBotActorNumber = -1; // Negative to avoid conflict with player ActorNumbers

    void Awake()
    {
        if (Instance == null)
        {
            Instance = this;
            DontDestroyOnLoad(gameObject);
            CustomLogger.Log("BotManager: Singleton instance set");
        }
        else
        {
            CustomLogger.LogWarning("BotManager: Duplicate instance detected, destroying this one");
            Destroy(gameObject);
            return;
        }

        if (!GetComponent<PhotonView>().IsRoomView)
        {
            CustomLogger.LogError("BotManager: PhotonView is not set as RoomView");
            enabled = false;
        }
    }

    void Start()
    {
        if (!PhotonNetwork.IsConnected)
        {
            CustomLogger.LogError("BotManager: Not connected to Photon, disabling");
            enabled = false;
            return;
        }

        if (!Resources.Load<GameObject>(botPrefabPath))
        {
            CustomLogger.LogError($"BotManager: Bot prefab not found at Assets/Resources/{botPrefabPath}.prefab");
            enabled = false;
            return;
        }

        StartCoroutine(InitializeBotsWithRetry());
    }

    private IEnumerator InitializeBotsWithRetry()
    {
        yield return new WaitForSeconds(5f);
        if (PhotonNetwork.IsMasterClient)
        {
            AdjustBotCount();
        }
    }

    private void AdjustBotCount()
    {
        if (!PhotonNetwork.IsMasterClient || isSpawningBots) return;

        int humanCount = PhotonNetwork.PlayerList.Length;
        int desiredBotCount = Mathf.Max(0, maxTotalEntities - humanCount);
        int currentBotCount = bots.Count;

        CustomLogger.Log($"BotManager: Adjusting bots. Humans={humanCount}, CurrentBots={currentBotCount}, DesiredBots={desiredBotCount}");

        if (desiredBotCount > currentBotCount)
        {
            StartCoroutine(SpawnBots(desiredBotCount - currentBotCount));
        }
        else if (desiredBotCount < currentBotCount)
        {
            RemoveBots(currentBotCount - desiredBotCount);
        }
    }

    private IEnumerator SpawnBots(int count)
    {
        isSpawningBots = true;
        BoundaryManager boundaryManager = FindFirstObjectByType<BoundaryManager>();
        RandomPlanetGenerator planetGenerator = FindFirstObjectByType<RandomPlanetGenerator>();
        ScoreboardManager scoreboardManager = FindFirstObjectByType<ScoreboardManager>();

        for (int i = 0; i < count; i++)
        {
            if (bots.Count >= maxBots || (PhotonNetwork.PlayerList.Length + bots.Count) >= maxTotalEntities)
            {
                CustomLogger.Log("BotManager: Reached max bots or total entities, stopping spawn");
                break;
            }

            Vector3 spawnPosition = GetValidSpawnPosition(boundaryManager);
            if (spawnPosition == Vector3.zero)
            {
                CustomLogger.LogWarning("BotManager: Could not find valid spawn position, retrying");
                yield return new WaitForSeconds(2f);
                i--;
                continue;
            }

            GameObject bot = PhotonNetwork.InstantiateRoomObject(botPrefabPath, spawnPosition, Quaternion.identity);
            if (bot == null)
            {
                CustomLogger.LogError($"BotManager: Failed to instantiate bot at {botPrefabPath}");
                yield return new WaitForSeconds(2f);
                i--;
                continue;
            }

            PhotonView botView = bot.GetComponent<PhotonView>();
            BotController botController = bot.GetComponent<BotController>();
            if (botView == null || botController == null)
            {
                CustomLogger.LogError("BotManager: Bot missing PhotonView or BotController");
                PhotonNetwork.Destroy(bot);
                yield return new WaitForSeconds(2f);
                i--;
                continue;
            }

            int actorNumber = nextBotActorNumber--;
            botController.SetActorNumber(actorNumber);
            botController.CustomProperties["BotViewID"] = botView.ViewID;
            botController.CustomProperties["Username"] = botController.NickName;
            botController.CustomProperties["Points"] = 0;
            if (boundaryManager != null)
            {
                boundaryManager.SpawnPlayerAndSpaceship(botController);
                CustomLogger.Log($"BotManager: Spawned spaceship for bot {botController.NickName} (ActorNumber={actorNumber})");
            }
            else
            {
                CustomLogger.LogError($"BotManager: BoundaryManager not found, cannot spawn spaceship for bot {botController.NickName}");
            }

            bots.Add(actorNumber, bot);
            botActorNumbers.Add(actorNumber);
            botPositions.Add(spawnPosition);
            photonView.RPC("AddBotToClients", RpcTarget.AllBuffered, actorNumber, botView.ViewID, botController.NickName, spawnPosition);

            if (planetGenerator != null)
            {
                planetGenerator.AddPlayer(actorNumber, bot);
                CustomLogger.Log($"BotManager: Added bot {botController.NickName} (ActorNumber={actorNumber}) to RandomPlanetGenerator");
            }
            if (scoreboardManager != null)
            {
                scoreboardManager.UpdateScoreboard();
                CustomLogger.Log($"BotManager: Updated ScoreboardManager for bot {botController.NickName}");
            }

            CustomLogger.Log($"BotManager: Spawned bot {botController.NickName} (ActorNumber={actorNumber}) at {spawnPosition}, ViewID={botView.ViewID}");
            yield return new WaitForSeconds(2f); // Increased delay for sequential spawning
        }

        isSpawningBots = false;
    }

    private void RemoveBots(int count)
    {
        RandomPlanetGenerator planetGenerator = FindFirstObjectByType<RandomPlanetGenerator>();
        ScoreboardManager scoreboardManager = FindFirstObjectByType<ScoreboardManager>();

        for (int i = 0; i < count && bots.Count > 0; i++)
        {
            int actorNumber = botActorNumbers[botActorNumbers.Count - 1];
            if (bots.TryGetValue(actorNumber, out GameObject bot))
            {
                if (planetGenerator != null)
                {
                    planetGenerator.RemovePlayer(actorNumber);
                    CustomLogger.Log($"BotManager: Removed bot ActorNumber={actorNumber} from RandomPlanetGenerator");
                }

                botPositions.Remove(bot.transform.position);
                bots.Remove(actorNumber);
                botActorNumbers.Remove(actorNumber);
                photonView.RPC("RemoveBotFromClients", RpcTarget.AllBuffered, actorNumber);

                if (bot != null && bot.GetComponent<PhotonView>() != null)
                {
                    PhotonNetwork.Destroy(bot);
                    CustomLogger.Log($"BotManager: Destroyed bot ActorNumber={actorNumber}");
                }

                if (scoreboardManager != null)
                {
                    scoreboardManager.UpdateScoreboard();
                    CustomLogger.Log($"BotManager: Updated ScoreboardManager after removing bot ActorNumber={actorNumber}");
                }
            }
        }
    }

    [PunRPC]
    private void AddBotToClients(int actorNumber, int botViewID, string nickName, Vector3 position)
    {
        PhotonView botView = PhotonView.Find(botViewID);
        if (botView == null || !botView.gameObject.activeInHierarchy)
        {
            CustomLogger.LogWarning($"BotManager: Bot ViewID={botViewID} not found or inactive on client");
            return;
        }

        BotController botController = botView.GetComponent<BotController>();
        if (botController == null)
        {
            CustomLogger.LogError($"BotManager: Bot ViewID={botViewID} missing BotController");
            return;
        }

        botController.SetActorNumber(actorNumber);
        botController.CustomProperties["BotViewID"] = botViewID;
        botController.CustomProperties["Username"] = nickName;
        botController.CustomProperties["Points"] = 0;

        if (!bots.ContainsKey(actorNumber))
        {
            bots.Add(actorNumber, botView.gameObject);
            botActorNumbers.Add(actorNumber);
            botPositions.Add(position);
            CustomLogger.Log($"BotManager: Added bot {nickName} (ActorNumber={actorNumber}, ViewID={botViewID}) to client at {position}");

            RandomPlanetGenerator planetGenerator = FindFirstObjectByType<RandomPlanetGenerator>();
            if (planetGenerator != null)
            {
                planetGenerator.AddPlayer(actorNumber, botView.gameObject);
                CustomLogger.Log($"BotManager: Added bot {nickName} to RandomPlanetGenerator on client");
            }

            ScoreboardManager scoreboardManager = FindFirstObjectByType<ScoreboardManager>();
            if (scoreboardManager != null)
            {
                scoreboardManager.UpdateScoreboard();
                CustomLogger.Log($"BotManager: Updated ScoreboardManager for bot {nickName} on client");
            }
        }
    }

    [PunRPC]
    private void RemoveBotFromClients(int actorNumber)
    {
        if (bots.TryGetValue(actorNumber, out GameObject bot))
        {
            RandomPlanetGenerator planetGenerator = FindFirstObjectByType<RandomPlanetGenerator>();
            if (planetGenerator != null)
            {
                planetGenerator.RemovePlayer(actorNumber);
                CustomLogger.Log($"BotManager: Removed bot ActorNumber={actorNumber} from RandomPlanetGenerator on client");
            }

            botPositions.Remove(bot.transform.position);
            bots.Remove(actorNumber);
            botActorNumbers.Remove(actorNumber);

            if (bot != null && bot.GetComponent<PhotonView>() != null)
            {
                PhotonNetwork.Destroy(bot);
                CustomLogger.Log($"BotManager: Destroyed bot ActorNumber={actorNumber} on client");
            }

            ScoreboardManager scoreboardManager = FindFirstObjectByType<ScoreboardManager>();
            if (scoreboardManager != null)
            {
                scoreboardManager.UpdateScoreboard();
                CustomLogger.Log($"BotManager: Updated ScoreboardManager after removing bot ActorNumber={actorNumber} on client");
            }
        }
    }

    private Vector3 GetValidSpawnPosition(BoundaryManager boundaryManager)
    {
        if (boundaryManager == null)
        {
            CustomLogger.LogError("BotManager: BoundaryManager not found");
            return Vector3.zero;
        }

        float safeBoundary = boundaryManager.BoundarySize / 2 - 50f;
        const int maxAttempts = 30; // Increased attempts for robustness
        for (int i = 0; i < maxAttempts; i++)
        {
            Vector3 position = new Vector3(
                Random.Range(-safeBoundary, safeBoundary),
                Random.Range(-safeBoundary, safeBoundary),
                0f
            );
            if (boundaryManager.IsValidBotPosition(position, nextBotActorNumber, boundaryManager.minBotSpacing) &&
                RandomPlanetGenerator.Instance.CheckSpawnDistance(position, nextBotActorNumber, boundaryManager.minBotSpacing))
            {
                return position;
            }
            CustomLogger.Log($"BotManager: Spawn attempt {i + 1}/{maxAttempts} failed for position {position}");
            if (i == maxAttempts - 1)
            {
                CustomLogger.LogWarning("BotManager: Retrying with relaxed constraints");
                for (int j = 0; j < 10; j++)
                {
                    position = new Vector3(
                        Random.Range(-safeBoundary / 2, safeBoundary / 2), // Narrower range
                        Random.Range(-safeBoundary / 2, safeBoundary / 2),
                        0f
                    );
                    if (boundaryManager.IsValidBotPosition(position, nextBotActorNumber, boundaryManager.minBotSpacing / 2))
                    {
                        return position;
                    }
                }
            }
        }
        return Vector3.zero;
    }

    public override void OnPlayerEnteredRoom(Player newPlayer)
    {
        if (PhotonNetwork.IsMasterClient)
        {
            CustomLogger.Log($"BotManager: Player {newPlayer.NickName} (ActorNumber={newPlayer.ActorNumber}) entered room");
            AdjustBotCount();
        }
    }

    public override void OnPlayerLeftRoom(Player otherPlayer)
    {
        if (PhotonNetwork.IsMasterClient)
        {
            CustomLogger.Log($"BotManager: Player {otherPlayer.NickName} (ActorNumber={otherPlayer.ActorNumber}) left room");
            AdjustBotCount();
        }
    }

    public void ResetBots()
    {
        if (!PhotonNetwork.IsMasterClient)
        {
            CustomLogger.Log("BotManager: ResetBots ignored, not Master Client");
            return;
        }

        foreach (var bot in bots.Values)
        {
            if (bot != null && bot.GetComponent<PhotonView>() != null)
            {
                PhotonNetwork.Destroy(bot);
            }
        }
        bots.Clear();
        botActorNumbers.Clear();
        botPositions.Clear();
        nextBotActorNumber = -1;
        CustomLogger.Log("BotManager: Cleared all bots and reset state");

        AdjustBotCount();
    }

    void OnDestroy()
    {
        if (Instance == this)
        {
            Instance = null;
            CustomLogger.Log("BotManager: Singleton instance cleared");
        }
    }
}