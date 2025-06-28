using System.Collections.Generic;
using UnityEngine;
using Photon.Pun;
using System.Collections;
using System.Linq;

public class RandomPlanetGenerator : MonoBehaviourPunCallbacks
{
    [SerializeField] private string planetPrefabPath = "Prefabs/Planets";
    [SerializeField] private string orePrefabPath = "Prefabs/Ore";
    [Range(1, 50)] public int activePlanetCountPerPlayer = 6;
    [Range(10f, 10000f)] public float spawnDistanceMin = 500f;
    [Range(10f, 10000f)] public float spawnDistanceMax = 2500f;
    [Range(1f, 300f)] public float planetSpacing = 30f;
    public float despawnDistance = 450f;
    private float lastSpawnTime;
    private float lastOreSpawnTime;
    private const float spawnCooldown = 5f;
    private const int maxPlanets = 140;
    private const float brightMatterChance = 0.50f;

    private Dictionary<int, GameObject> players = new Dictionary<int, GameObject>();
    private Dictionary<int, List<GameObject>> playerPlanets = new Dictionary<int, List<GameObject>>();
    private Dictionary<int, Vector3> lastPlayerPositions = new Dictionary<int, Vector3>();
    private HashSet<int> respawningPlayers = new HashSet<int>();

    public static RandomPlanetGenerator Instance { get; private set; }

    void Awake()
    {
        if (Instance == null)
        {
            Instance = this;
            DontDestroyOnLoad(gameObject);
            CustomLogger.Log("RandomPlanetGenerator: Singleton instance set for this scene");
        }
        else
        {
            CustomLogger.LogWarning("RandomPlanetGenerator: Another instance exists, destroying this one");
            Destroy(gameObject);
            return;
        }

        PhotonView pv = GetComponent<PhotonView>();
        if (pv != null && pv.IsRoomView)
        {
            CustomLogger.Log($"RandomPlanetGenerator: PhotonView registered, ViewID={pv.ViewID}, IsRoomView={pv.IsRoomView}");
        }
        else
        {
            CustomLogger.LogError("RandomPlanetGenerator: Missing or invalid PhotonView component");
            enabled = false;
        }
    }

    void OnDestroy()
    {
        if (Instance == this)
        {
            Instance = null;
            CustomLogger.Log("RandomPlanetGenerator: Singleton instance cleared on destroy");
        }
    }

    void Start()
    {
        if (!PhotonNetwork.IsConnected)
        {
            CustomLogger.LogError("RandomPlanetGenerator: Not connected to Photon, disabling.");
            enabled = false;
            return;
        }

        if (!Resources.Load<GameObject>(planetPrefabPath))
        {
            CustomLogger.LogError($"RandomPlanetGenerator: Planets prefab not found at Assets/Resources/{planetPrefabPath}.prefab.");
            enabled = false;
            return;
        }
        if (!Resources.Load<GameObject>(orePrefabPath))
        {
            CustomLogger.LogError($"RandomPlanetGenerator: Ore prefab not found at Assets/Resources/{orePrefabPath}.prefab.");
            enabled = false;
            return;
        }

        if (!gameObject.CompareTag("PlanetGenerator"))
        {
            CustomLogger.LogWarning("RandomPlanetGenerator: GameObject is not tagged as 'PlanetGenerator'. Adding tag.");
            gameObject.tag = "PlanetGenerator";
        }

        CustomLogger.Log($"RandomPlanetGenerator: Initialized with planetPrefabPath={planetPrefabPath}, orePrefabPath={orePrefabPath}, activePlanetCountPerPlayer={activePlanetCountPerPlayer}, spawnDistanceMin={spawnDistanceMin}, spawnDistanceMax={spawnDistanceMax}, planetSpacing={planetSpacing}, despawnDistance={despawnDistance}, brightMatterChance={brightMatterChance}");
        StartCoroutine(DelayedInitializePlayers());
    }

    private IEnumerator DelayedInitializePlayers()
    {
        yield return new WaitForSeconds(10f);
        CustomLogger.Log("RandomPlanetGenerator: Starting player initialization after 10-second delay");
        StartCoroutine(InitializePlayersWithRetry());
    }

    private IEnumerator InitializePlayersWithRetry()
    {
        int maxRetries = 30;
        int retries = 0;
        while (retries < maxRetries)
        {
            if (PhotonNetwork.IsConnectedAndReady)
            {
                foreach (var player in PhotonNetwork.PlayerList)
                {
                    if (player.CustomProperties.TryGetValue("PlayerViewID", out object viewID))
                    {
                        PhotonView pv = PhotonView.Find((int)viewID);
                        if (pv != null && pv.gameObject != null && pv.gameObject.CompareTag("Player") && pv.gameObject.activeInHierarchy)
                        {
                            AddPlayer(player.ActorNumber, pv.gameObject);
                            CustomLogger.Log($"RandomPlanetGenerator: Added player ActorNumber={player.ActorNumber}, Name={pv.gameObject.name}, ViewID={viewID}");
                        }
                    }
                }
                foreach (BotController bot in Object.FindObjectsByType<BotController>(FindObjectsSortMode.None))
                {
                    if (bot.CustomProperties.TryGetValue("BotViewID", out object viewID) && bot.gameObject.activeInHierarchy)
                    {
                        AddPlayer(bot.ActorNumber, bot.gameObject);
                        CustomLogger.Log($"RandomPlanetGenerator: Added bot ActorNumber={bot.ActorNumber}, Name={bot.NickName}, ViewID={viewID}");
                    }
                }
                if (players.Count > 0)
                {
                    CustomLogger.Log($"RandomPlanetGenerator: Initialized {players.Count} players/bots.");
                    StartCoroutine(SpawnPlanetsForPlayers());
                    yield break;
                }
            }
            retries++;
            CustomLogger.Log($"RandomPlanetGenerator: Retry {retries}/{maxRetries} to find players/bots.");
            yield return new WaitForSeconds(2f);
        }
        CustomLogger.LogError("RandomPlanetGenerator: Failed to find players/bots after retries.");
        enabled = false;
    }

    private IEnumerator SpawnPlanetsForPlayers()
    {
        int totalPlanets = 0;
        List<int> actorNumbers = new List<int>(players.Keys);
        foreach (var actorNumber in actorNumbers)
        {
            if (!players.ContainsKey(actorNumber)) continue;
            for (int i = 0; i < activePlanetCountPerPlayer; i++)
            {
                if (totalPlanets >= maxPlanets)
                {
                    CustomLogger.Log($"RandomPlanetGenerator: Reached max planet limit ({maxPlanets}), stopping spawn.");
                    yield break;
                }
                SpawnPlanetForPlayer(actorNumber, false);
                totalPlanets++;
                yield return new WaitForSeconds(0.2f);
            }
        }
    }

    public void AddPlayer(int actorNumber, GameObject playerObj)
    {
        if (playerObj == null || !playerObj.activeInHierarchy)
        {
            CustomLogger.LogError($"RandomPlanetGenerator: Cannot add player ActorNumber={actorNumber}, playerObj is null or inactive");
            return;
        }

        if (!players.ContainsKey(actorNumber))
        {
            players[actorNumber] = playerObj;
            playerPlanets[actorNumber] = new List<GameObject>();
            lastPlayerPositions[actorNumber] = playerObj.transform.position;
            CustomLogger.Log($"RandomPlanetGenerator: Added player/bot ActorNumber={actorNumber}, Name={playerObj.name}");
            if (PhotonNetwork.IsMasterClient)
            {
                StartCoroutine(SpawnInitialPlanetsForPlayer(actorNumber));
            }
        }
        else
        {
            players[actorNumber] = playerObj;
            lastPlayerPositions[actorNumber] = playerObj.transform.position;
            CustomLogger.Log($"RandomPlanetGenerator: Updated player/bot ActorNumber={actorNumber}, Name={playerObj.name}");
        }
    }

    public void ReAddPlayer(int actorNumber, GameObject playerObj)
    {
        if (!PhotonNetwork.IsMasterClient)
        {
            CustomLogger.Log("RandomPlanetGenerator: ReAddPlayer ignored, not Master Client.");
            return;
        }
        if (playerObj == null || !playerObj.activeInHierarchy)
        {
            CustomLogger.LogError($"RandomPlanetGenerator: Cannot re-add player ActorNumber={actorNumber}, playerObj is null or inactive");
            return;
        }

        respawningPlayers.Add(actorNumber);
        if (players.ContainsKey(actorNumber))
        {
            players[actorNumber] = playerObj;
            lastPlayerPositions[actorNumber] = playerObj.transform.position;
            CustomLogger.Log($"RandomPlanetGenerator: Re-added player/bot ActorNumber={actorNumber} after respawn");
        }
        else
        {
            AddPlayer(actorNumber, playerObj);
            CustomLogger.Log($"RandomPlanetGenerator: Player/bot ActorNumber={actorNumber} not found for re-add, treated as new player.");
        }
        StartCoroutine(ClearRespawningStatus(actorNumber));
    }

    private IEnumerator ClearRespawningStatus(int actorNumber)
    {
        yield return new WaitForSeconds(2f);
        respawningPlayers.Remove(actorNumber);
        CustomLogger.Log($"RandomPlanetGenerator: Cleared respawning status for ActorNumber={actorNumber}");
    }

    public void RemovePlayer(int actorNumber)
    {
        if (!PhotonNetwork.IsMasterClient)
        {
            CustomLogger.Log("RandomPlanetGenerator: RemovePlayer ignored, not Master Client.");
            return;
        }
        if (PhotonNetwork.CurrentRoom != null && PhotonNetwork.CurrentRoom.Players.ContainsKey(actorNumber))
        {
            CustomLogger.Log($"RandomPlanetGenerator: Player/bot ActorNumber={actorNumber} still in room, not removing.");
            return;
        }
        if (players.ContainsKey(actorNumber))
        {
            foreach (var planet in playerPlanets[actorNumber].ToList())
            {
                if (planet != null && planet.GetComponent<PhotonView>() != null)
                {
                    Planet planetScript = planet.GetComponent<Planet>();
                    if (planetScript != null)
                    {
                        planetScript.SplitIntoQuarters();
                        CustomLogger.Log($"RandomPlanetGenerator: Split planet {planet.name} into quarters for ActorNumber={actorNumber} on player removal");
                    }
                }
            }
            players.Remove(actorNumber);
            playerPlanets.Remove(actorNumber);
            lastPlayerPositions.Remove(actorNumber);
            respawningPlayers.Remove(actorNumber);
            CustomLogger.Log($"RandomPlanetGenerator: Removed player/bot ActorNumber={actorNumber} and split all associated planets into quarters");
        }
    }

    public List<GameObject> GetPlayerPlanets(int actorNumber)
    {
        if (playerPlanets.ContainsKey(actorNumber))
        {
            return playerPlanets[actorNumber];
        }
        return new List<GameObject>();
    }

    public Vector3 GetSpawnPosition()
    {
        GameObject player = players.Count > 0 ? players[players.Keys.ToList()[Random.Range(0, players.Count)]] : null;
        return player != null ? GetRandomPositionAroundPlayer(player) : Vector3.zero;
    }

    private IEnumerator SpawnInitialPlanetsForPlayer(int actorNumber)
    {
        int totalPlanets = playerPlanets.Values.Sum(list => list.Count);
        for (int i = 0; i < activePlanetCountPerPlayer; i++)
        {
            if (totalPlanets >= maxPlanets)
            {
                CustomLogger.Log($"RandomPlanetGenerator: Reached max planet limit ({maxPlanets}) for ActorNumber={actorNumber}, stopping spawn.");
                yield break;
            }
            if (Time.time - lastSpawnTime < spawnCooldown) yield return new WaitForSeconds(spawnCooldown - (Time.time - lastSpawnTime));
            SpawnPlanetForPlayer(actorNumber, false);
            totalPlanets++;
            lastSpawnTime = Time.time;
            yield return new WaitForSeconds(0.2f);
        }
    }

    void Update()
    {
        if (!PhotonNetwork.IsConnected)
        {
            CustomLogger.LogWarning("RandomPlanetGenerator: Photon disconnected, attempting to reconnect.");
            PhotonNetwork.ConnectUsingSettings();
            return;
        }

        if (PhotonNetwork.IsMasterClient)
        {
            List<int> keysToRemove = new List<int>();
            foreach (var kvp in players)
            {
                if (respawningPlayers.Contains(kvp.Key))
                {
                    CustomLogger.Log($"RandomPlanetGenerator: Skipping cleanup for ActorNumber={kvp.Key} (respawning)");
                    continue;
                }
                if (kvp.Value == null || !kvp.Value.activeInHierarchy)
                {
                    keysToRemove.Add(kvp.Key);
                    CustomLogger.LogWarning($"RandomPlanetGenerator: Detected null/inactive player ActorNumber={kvp.Key}, marking for removal");
                }
            }
            foreach (int key in keysToRemove)
            {
                RemovePlayer(key);
            }

            List<int> actorNumbers = new List<int>(players.Keys);
            foreach (var actorNumber in actorNumbers)
            {
                GameObject player = players[actorNumber];
                if (player == null || !player.activeInHierarchy)
                {
                    if (!respawningPlayers.Contains(actorNumber))
                    {
                        CustomLogger.LogWarning($"RandomPlanetGenerator: Player/bot ActorNumber={actorNumber} is null or inactive, skipping.");
                    }
                    continue;
                }

                bool isBot = player.GetComponent<BotController>() != null;
                if (!isBot && (PhotonNetwork.CurrentRoom == null || !PhotonNetwork.CurrentRoom.Players.ContainsKey(actorNumber)))
                {
                    CustomLogger.LogWarning($"RandomPlanetGenerator: Player ActorNumber={actorNumber} no longer in room, removing.");
                    RemovePlayer(actorNumber);
                    continue;
                }

                CustomLogger.Log($"RandomPlanetGenerator: Managing planets for ActorNumber={actorNumber}, Position={player.transform.position}, Planets={playerPlanets[actorNumber].Count}, IsBot={isBot}");
                ManagePlanetsForPlayer(actorNumber);

                if (playerPlanets[actorNumber].Count == 0)
                {
                    CustomLogger.Log($"RandomPlanetGenerator: No planets for ActorNumber={actorNumber}, triggering initial spawn.");
                    StartCoroutine(SpawnInitialPlanetsForPlayer(actorNumber));
                }

                lastPlayerPositions[actorNumber] = player.transform.position;
            }
        }
    }

    void ManagePlanetsForPlayer(int actorNumber)
    {
        if (PhotonNetwork.IsMasterClient)
        {
            if (!playerPlanets.ContainsKey(actorNumber)) playerPlanets[actorNumber] = new List<GameObject>();
            List<int> planetViewIDsToRemove = new List<int>();
            GameObject player = players[actorNumber];

            if (player == null || !player.activeInHierarchy)
            {
                if (!respawningPlayers.Contains(actorNumber))
                {
                    CustomLogger.Log($"RandomPlanetGenerator: Player ActorNumber={actorNumber} is null or inactive, skipping planet management.");
                }
                return;
            }

            foreach (GameObject planet in playerPlanets[actorNumber].ToList())
            {
                if (planet == null || planet.gameObject == null || !planet.activeInHierarchy)
                {
                    CustomLogger.Log($"RandomPlanetGenerator: Planet is null, destroyed, or inactive for ActorNumber={actorNumber}, marking for removal.");
                    PhotonView planetView = planet != null ? planet.GetComponent<PhotonView>() : null;
                    if (planetView != null)
                        planetViewIDsToRemove.Add(planetView.ViewID);
                    continue;
                }

                float distance = Vector3.Distance(planet.transform.position, player.transform.position);
                CustomLogger.Log($"RandomPlanetGenerator: Planet {planet.name} for ActorNumber={actorNumber} at {planet.transform.position}, Distance={distance:F2}, DespawnDistance={despawnDistance}");
                Planet planetScript = planet.GetComponent<Planet>();
                if (planetScript != null && distance > despawnDistance)
                {
                    CustomLogger.Log($"RandomPlanetGenerator: Shrinking and destroying planet {planet.name} for ActorNumber={actorNumber} at distance {distance:F2}");
                    planetScript.ShrinkAndDestroy();
                    planetViewIDsToRemove.Add(planet.GetComponent<PhotonView>().ViewID);
                }
            }

            foreach (int viewID in planetViewIDsToRemove)
            {
                photonView.RPC("RemovePlanetFromList", RpcTarget.AllBuffered, viewID);
                CustomLogger.Log($"RandomPlanetGenerator: Sent RPC to remove planet ViewID={viewID} for ActorNumber={actorNumber}");
            }

            int totalPlanets = playerPlanets.Values.Sum(list => list.Count);
            int planetsNeeded = activePlanetCountPerPlayer - playerPlanets[actorNumber].Count;
            for (int i = 0; i < planetsNeeded; i++)
            {
                if (totalPlanets >= maxPlanets)
                {
                    CustomLogger.Log($"RandomPlanetGenerator: Reached max planet limit ({maxPlanets}) for ActorNumber={actorNumber}, stopping spawn.");
                    break;
                }
                CustomLogger.Log($"RandomPlanetGenerator: Spawning new planet for ActorNumber={actorNumber}, current count={playerPlanets[actorNumber].Count}, needed={planetsNeeded}");
                SpawnPlanetForPlayer(actorNumber, true);
                totalPlanets++;
            }
        }
    }

    void SpawnPlanetForPlayer(int actorNumber, bool forceSpawn = false)
    {
        if (!forceSpawn && Time.time - lastSpawnTime < spawnCooldown)
        {
            CustomLogger.Log($"RandomPlanetGenerator: Spawn on cooldown for ActorNumber={actorNumber}, wait {spawnCooldown - (Time.time - lastSpawnTime):F1}s.");
            return;
        }

        GameObject player = players[actorNumber];
        if (player == null || !player.activeInHierarchy)
        {
            if (!respawningPlayers.Contains(actorNumber))
                CustomLogger.LogWarning($"RandomPlanetGenerator: Player/bot ActorNumber={actorNumber} is null or inactive in SpawnPlanetForPlayer.");
            return;
        }

        Vector3 spawnPosition = GetRandomPositionAroundPlayer(player);
        if (!CheckForOverlap(spawnPosition))
        {
            CustomLogger.Log($"RandomPlanetGenerator: Spawn position {spawnPosition} overlaps with another planet, skipping for ActorNumber={actorNumber}.");
            return;
        }

        if (PhotonNetwork.IsMasterClient)
        {
            try
            {
                GameObject planet = PhotonNetwork.Instantiate(planetPrefabPath, spawnPosition, Quaternion.identity);
                if (planet == null)
                {
                    CustomLogger.LogError($"RandomPlanetGenerator: Failed to instantiate Planets at Assets/Resources/{planetPrefabPath}.prefab");
                    return;
                }

                PhotonView planetView = planet.GetComponent<PhotonView>();
                if (planetView == null)
                {
                    CustomLogger.LogError($"RandomPlanetGenerator: No PhotonView on planet {planet.name} for ActorNumber={actorNumber}.");
                    PhotonNetwork.Destroy(planet);
                    return;
                }

                CustomLogger.Log($"RandomPlanetGenerator: Spawned planet {planet.name} for ActorNumber={actorNumber} at {spawnPosition}, ViewID={planetView.ViewID}");
                photonView.RPC("AddPlanetToList", RpcTarget.AllBuffered, actorNumber, planetView.ViewID);

                if (Random.value < brightMatterChance)
                    SpawnOreOnPlanet(planet, actorNumber);

                lastSpawnTime = Time.time;
            }
            catch (System.Exception e)
            {
                CustomLogger.LogError($"RandomPlanetGenerator: Failed to spawn planet for ActorNumber={actorNumber}: {e.Message}");
            }
        }
    }

    [PunRPC]
    void AddPlanetToList(int actorNumber, int planetViewID)
    {
        PhotonView planetView = PhotonView.Find(planetViewID);
        if (planetView != null && planetView.gameObject != null && planetView.gameObject.activeInHierarchy)
        {
            if (!playerPlanets.ContainsKey(actorNumber))
                playerPlanets[actorNumber] = new List<GameObject>();
            if (!playerPlanets[actorNumber].Contains(planetView.gameObject))
            {
                playerPlanets[actorNumber].Add(planetView.gameObject);
                CustomLogger.Log($"RandomPlanetGenerator: Added planet {planetView.gameObject.name} for ActorNumber={actorNumber}, ViewID={planetViewID}");
            }
        }
        else
        {
            CustomLogger.LogWarning($"RandomPlanetGenerator: Could not find planet with ViewID={planetViewID} for ActorNumber={actorNumber} or it is inactive.");
        }
    }

    [PunRPC]
    void RemovePlanetFromList(int planetViewID)
    {
        foreach (var actorNumber in playerPlanets.Keys.ToList())
        {
            int removed = playerPlanets[actorNumber].RemoveAll(p =>
                p == null || p.GetComponent<PhotonView>() == null || p.GetComponent<PhotonView>().ViewID == planetViewID);
            if (removed > 0)
            {
                CustomLogger.Log($"RandomPlanetGenerator: Removed {removed} planet(s) with ViewID={planetViewID} for ActorNumber={actorNumber}");
                if (PhotonNetwork.IsMasterClient)
                {
                    CustomLogger.Log($"RandomPlanetGenerator: Planet removed (ViewID={planetViewID}) for ActorNumber={actorNumber}, triggering ManagePlanets.");
                    ManagePlanetsForPlayer(actorNumber);
                }
                return;
            }
        }
        CustomLogger.Log($"RandomPlanetGenerator: Planet with ViewID={planetViewID} not found in any player's planet list.");
    }

    void SpawnOreOnPlanet(GameObject planet, int actorNumber)
    {
        if (Time.time - lastOreSpawnTime < spawnCooldown)
        {
            CustomLogger.Log($"RandomPlanetGenerator: Ore spawn on cooldown for ActorNumber={actorNumber}, wait {spawnCooldown - (Time.time - lastOreSpawnTime):F1}s.");
            return;
        }

        if (planet == null || planet.gameObject == null || !planet.activeInHierarchy)
        {
            CustomLogger.LogError($"RandomPlanetGenerator: Cannot spawn ore for ActorNumber={actorNumber}, planet is null, destroyed, or inactive.");
            return;
        }

        Collider2D planetCollider = planet.GetComponent<Collider2D>();
        if (planetCollider == null)
        {
            CustomLogger.LogWarning($"RandomPlanetGenerator: No Collider2D on planet {planet.name} for ActorNumber={actorNumber}. Cannot spawn ore.");
            return;
        }

        float planetRadius = planetCollider.bounds.extents.x;
        float oreOffset = Resources.Load<GameObject>(orePrefabPath)?.transform.localScale.x ?? 1f;
        Vector3 randomOffset = (Vector3)(Random.insideUnitCircle.normalized * (planetRadius + oreOffset));
        Vector3 spawnPosition = planet.transform.position + randomOffset;

        if (PhotonNetwork.IsMasterClient)
        {
            try
            {
                GameObject ore = PhotonNetwork.Instantiate(orePrefabPath, spawnPosition, Quaternion.identity);
                if (ore == null)
                {
                    CustomLogger.LogError($"RandomPlanetGenerator: Failed to instantiate Ore at Assets/Resources/{orePrefabPath}.prefab");
                    return;
                }
                ore.transform.SetParent(planet.transform);
                ore.transform.up = ore.transform.position - planet.transform.position;

                PhotonView orePhotonView = ore.GetComponent<PhotonView>();
                if (orePhotonView != null)
                {
                    PhotonView planetPhotonView = planet.GetComponent<PhotonView>();
                    if (planetPhotonView != null)
                    {
                        orePhotonView.RPC("SetOreParentAndOrientation", RpcTarget.AllBuffered, planetPhotonView.ViewID, randomOffset);
                        CustomLogger.Log($"RandomPlanetGenerator: Synced ore parenting for ActorNumber={actorNumber} on planet {planet.name}, OreViewID={orePhotonView.ViewID}");
                    }
                }
                lastOreSpawnTime = Time.time;
                Planet planetScript = planet.GetComponent<Planet>();
                if (planetScript != null)
                {
                    planetScript.SetHasOre(true);
                    CustomLogger.Log($"RandomPlanetGenerator: Marked planet {planet.name} as having ore for ActorNumber={actorNumber}");
                }
                CustomLogger.Log($"RandomPlanetGenerator: Spawned ore for ActorNumber={actorNumber} on planet {planet.name} at {spawnPosition}, OreViewID={(orePhotonView != null ? orePhotonView.ViewID : -1)}");
            }
            catch (System.Exception e)
            {
                CustomLogger.LogError($"RandomPlanetGenerator: Failed to spawn ore for ActorNumber={actorNumber}: {e.Message}");
            }
        }
    }

    Vector3 GetRandomPositionAroundPlayer(GameObject player)
    {
        if (player == null)
        {
            CustomLogger.LogWarning("RandomPlanetGenerator: Player is null in GetRandomPositionAroundPlayer.");
            return Vector3.zero;
        }

        int maxAttempts = 10;
        Vector3 spawnPosition = Vector3.zero;
        bool validPositionFound = false;

        for (int i = 0; i < maxAttempts; i++)
        {
            Vector3 randomDirection = new Vector3(Random.Range(-1f, 1f), Random.Range(-1f, 1f)).normalized;
            float spawnDistance = Random.Range(spawnDistanceMin, spawnDistanceMax);
            spawnPosition = player.transform.position + randomDirection * spawnDistance;

            // Check distance from other players and bots
            bool isValidDistance = true;
            foreach (var otherPlayer in players)
            {
                if (otherPlayer.Value != null && otherPlayer.Value != player && otherPlayer.Value.activeInHierarchy)
                {
                    float distance = Vector3.Distance(spawnPosition, otherPlayer.Value.transform.position);
                    if (distance < planetSpacing * 2f) // Increased spacing for other players
                    {
                        isValidDistance = false;
                        CustomLogger.Log($"RandomPlanetGenerator: Spawn position {spawnPosition} too close to player/bot ActorNumber={otherPlayer.Key}, distance={distance:F2}, attempt={i + 1}");
                        break;
                    }
                }
            }

            // Check overlap with all planets
            if (isValidDistance && CheckForOverlap(spawnPosition))
            {
                validPositionFound = true;
                break;
            }

            CustomLogger.Log($"RandomPlanetGenerator: Invalid spawn position {spawnPosition} on attempt {i + 1}, retrying.");
        }

        if (!validPositionFound)
        {
            CustomLogger.LogWarning($"RandomPlanetGenerator: Failed to find valid spawn position after {maxAttempts} attempts, using fallback position.");
            spawnPosition = player.transform.position + new Vector3(Random.Range(-1f, 1f), Random.Range(-1f, 1f)).normalized * spawnDistanceMin;
        }

        return spawnPosition;
    }

    bool CheckForOverlap(Vector3 spawnPosition)
    {
        // Check against all planets in the scene, not just those for a specific actor
        GameObject[] allPlanets = GameObject.FindGameObjectsWithTag("Planet");
        foreach (GameObject planet in allPlanets)
        {
            if (planet != null && planet.activeInHierarchy)
            {
                float distance = Vector3.Distance(planet.transform.position, spawnPosition);
                if (distance < planetSpacing)
                {
                    CustomLogger.Log($"RandomPlanetGenerator: Spawn position {spawnPosition} overlaps with planet {planet.name} at {planet.transform.position}, distance={distance:F2}");
                    return false;
                }
            }
        }
        return true;
    }

    public bool CheckSpawnDistance(Vector3 position, int actorNumber, float minDistance)
    {
        GameObject[] bots = GameObject.FindGameObjectsWithTag("Bot");
        foreach (GameObject bot in bots)
        {
            if (bot.activeInHierarchy)
            {
                BotController botController = bot.GetComponent<BotController>();
                if (botController != null && botController.ActorNumber != actorNumber)
                {
                    float distance = Vector3.Distance(position, bot.transform.position);
                    if (distance < minDistance)
                    {
                        CustomLogger.Log($"RandomPlanetGenerator: Spawn position {position} for ActorNumber={actorNumber} is {distance:F2} units from bot {botController.NickName}, less than {minDistance}");
                        return false;
                    }
                }
            }
        }

        GameObject[] spaceships = GameObject.FindGameObjectsWithTag("SpaceShip");
        foreach (GameObject ship in spaceships)
        {
            if (ship.activeInHierarchy)
            {
                SpaceshipMarker marker = ship.GetComponent<SpaceshipMarker>();
                if (marker != null && marker.ownerId != actorNumber)
                {
                    float distance = Vector3.Distance(position, ship.transform.position);
                    if (distance < minDistance)
                    {
                        CustomLogger.Log($"RandomPlanetGenerator: Spawn position {position} for ActorNumber={actorNumber} is {distance:F2} units from spaceship (ownerId={marker.ownerId}), less than {minDistance}");
                        return false;
                    }
                }
            }
        }

        return true;
    }

    public override void OnPlayerLeftRoom(Photon.Realtime.Player otherPlayer)
    {
        if (!PhotonNetwork.IsMasterClient)
            return;
        RemovePlayer(otherPlayer.ActorNumber);
        CustomLogger.Log($"RandomPlanetGenerator: Player {otherPlayer.NickName} (ActorNumber={otherPlayer.ActorNumber}) left room.");
    }

    public void ResetPlanets()
    {
        if (!PhotonNetwork.IsMasterClient)
        {
            CustomLogger.Log("RandomPlanetGenerator: ResetPlanets ignored, not Master Client.");
            return;
        }

        photonView.RPC("PerformReset", RpcTarget.AllBuffered);
        CustomLogger.Log("RandomPlanetGenerator: Sent PerformReset RPC to all clients");
    }

    [PunRPC]
    private void PerformReset()
    {
        // Destroy all planets and their ores
        foreach (var planetList in playerPlanets.Values)
        {
            foreach (GameObject planet in planetList.ToList())
            {
                if (planet != null && planet.GetComponent<PhotonView>() != null)
                {
                    // Destroy any ores attached to the planet
                    foreach (Transform child in planet.transform)
                    {
                        if (child.CompareTag("Ore") && child.GetComponent<PhotonView>() != null)
                        {
                            PhotonNetwork.Destroy(child.gameObject);
                            CustomLogger.Log($"RandomPlanetGenerator: Destroyed ore on planet {planet.name}");
                        }
                    }
                    PhotonNetwork.Destroy(planet);
                    CustomLogger.Log($"RandomPlanetGenerator: Destroyed planet {planet.name}");
                }
            }
        }

        // Destroy all standalone ores
        GameObject[] ores = GameObject.FindGameObjectsWithTag("Ore");
        foreach (GameObject ore in ores)
        {
            if (ore.GetComponent<PhotonView>() != null)
            {
                PhotonNetwork.Destroy(ore);
                CustomLogger.Log($"RandomPlanetGenerator: Destroyed standalone ore {ore.name}");
            }
        }

        // Destroy all BrightMatterOrbs
        GameObject[] orbs = GameObject.FindGameObjectsWithTag("BrightMatterOrb");
        foreach (GameObject orb in orbs)
        {
            if (orb.GetComponent<PhotonView>() != null)
            {
                PhotonNetwork.Destroy(orb);
                CustomLogger.Log($"RandomPlanetGenerator: Destroyed BrightMatterOrb {orb.name}");
            }
        }

        // Destroy all spaceships
        GameObject[] spaceships = GameObject.FindGameObjectsWithTag("SpaceShip");
        foreach (GameObject ship in spaceships)
        {
            if (ship.GetComponent<PhotonView>() != null)
            {
                PhotonNetwork.Destroy(ship);
                CustomLogger.Log($"RandomPlanetGenerator: Destroyed spaceship {ship.name}");
            }
        }

        // Destroy all compasses
        GameObject[] compasses = GameObject.FindGameObjectsWithTag("Compass");
        foreach (GameObject compass in compasses)
        {
            if (compass.GetComponent<PhotonView>() != null)
            {
                PhotonNetwork.Destroy(compass);
                CustomLogger.Log($"RandomPlanetGenerator: Destroyed compass {compass.name}");
            }
        }

        // Clear player data
        playerPlanets.Clear();
        players.Clear();
        lastPlayerPositions.Clear();
        respawningPlayers.Clear();
        CustomLogger.Log("RandomPlanetGenerator: Cleared players, playerPlanets, lastPlayerPositions, and respawningPlayers");

        // Reset player properties
        foreach (var player in PhotonNetwork.PlayerList)
        {
            var props = new ExitGames.Client.Photon.Hashtable
            {
                { "Points", 0 },
                { "DeathCause", PlayerHealth.DeathCause.None.ToString() },
                { "KillerViewID", null },
                { "PlayerViewID", null },
                { "SpaceshipViewID", null },
                { "CompassViewID", null }
            };
            player.SetCustomProperties(props);
            CustomLogger.Log($"RandomPlanetGenerator: Reset properties for player {player.NickName}, ActorNumber={player.ActorNumber}");
        }

        // Notify BoundaryManager to reset
        BoundaryManager boundaryManager = Object.FindFirstObjectByType<BoundaryManager>();
        if (boundaryManager != null)
        {
            boundaryManager.ResetBoundary();
            CustomLogger.Log("RandomPlanetGenerator: Triggered BoundaryManager.ResetBoundary");
        }

        // Notify ScoreboardManager to reset
        ScoreboardManager scoreboard = Object.FindFirstObjectByType<ScoreboardManager>();
        if (scoreboard != null)
        {
            scoreboard.ResetScoreboard();
            CustomLogger.Log("RandomPlanetGenerator: Triggered ScoreboardManager.ResetScoreboard");
        }

        // Notify MatchTimerManager to reset
        MatchTimerManager timerManager = Object.FindFirstObjectByType<MatchTimerManager>();
        if (timerManager != null)
        {
            timerManager.ResetTimer();
            CustomLogger.Log("RandomPlanetGenerator: Triggered MatchTimerManager.ResetTimer");
        }

        // Reset camera
        CameraFollow cameraFollow = Object.FindFirstObjectByType<CameraFollow>();
        if (cameraFollow != null)
        {
            cameraFollow.ResetCameraTarget();
            CustomLogger.Log("RandomPlanetGenerator: Triggered CameraFollow.ResetCameraTarget");
        }

        // Reset PlayerPrefs for BrightMatter and InsideSpaceShip
        PlayerPrefs.SetInt("BrightMatter", 50);
        PlayerPrefs.SetInt("InsideSpaceShip", 0);
        PlayerPrefs.Save();
        CustomLogger.Log("RandomPlanetGenerator: Reset PlayerPrefs (BrightMatter=50, InsideSpaceShip=0)");

        // Reinitialize for current players and bots
        foreach (Photon.Realtime.Player photonPlayer in PhotonNetwork.PlayerList)
        {
            if (photonPlayer.CustomProperties.TryGetValue("PlayerViewID", out object viewIDObj) && viewIDObj is int viewID)
            {
                PhotonView playerView = PhotonView.Find(viewID);
                if (playerView != null && playerView.gameObject != null && playerView.gameObject.activeInHierarchy)
                {
                    AddPlayer(photonPlayer.ActorNumber, playerView.gameObject);
                    CustomLogger.Log($"RandomPlanetGenerator: Re-added player ActorNumber={photonPlayer.ActorNumber} during reset");
                }
            }
        }
        foreach (BotController bot in Object.FindObjectsByType<BotController>(FindObjectsSortMode.None))
        {
            if (bot.gameObject.activeInHierarchy && !players.ContainsKey(bot.ActorNumber))
            {
                AddPlayer(bot.ActorNumber, bot.gameObject);
                CustomLogger.Log($"RandomPlanetGenerator: Re-added bot ActorNumber={bot.ActorNumber} during reset");
            }
        }

        // Trigger planet spawning
        StartCoroutine(DelayedInitializePlayers());
        CustomLogger.Log("RandomPlanetGenerator: Reinitialized players and triggered planet spawning");
    }
}