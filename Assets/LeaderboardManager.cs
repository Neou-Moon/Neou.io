using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System.Collections.Generic;
using UnityEngine.SceneManagement;

public class LeaderboardManager : MonoBehaviour
{
    [SerializeField] private Transform moonRanContent;
    [SerializeField] private Transform teamMoonRanContent;
    [SerializeField] private GameObject leaderboardEntryPrefab;
    [SerializeField] private Button backButton;
    private List<GameObject> moonRanEntries = new List<GameObject>();
    private List<GameObject> teamMoonRanEntries = new List<GameObject>();

    void Start()
    {
        if (backButton != null)
        {
            backButton.onClick.AddListener(OnBackButtonClicked);
            CustomLogger.Log("LeaderboardManager: Back button listener added.");
        }
        else
        {
            CustomLogger.LogError("LeaderboardManager: BackButton not assigned in Inspector.");
        }

        // Load leaderboards for both game modes with top 100 players
        LoadLeaderboard("MoonRan", 100);
        LoadLeaderboard("TeamMoonRan", 100);

        // Load player ranks for both game modes
        LoadPlayerRank("MoonRan");
        LoadPlayerRank("TeamMoonRan");
    }

    void LoadLeaderboard(string gameMode, int limit)
    {
        string uid = PlayerPrefs.GetString("PlayerUID", "");
        if (string.IsNullOrEmpty(uid))
        {
            CustomLogger.LogError($"LeaderboardManager: No PlayerUID found, cannot load {gameMode} leaderboard.");
            return;
        }

        if (Application.platform == RuntimePlatform.WebGLPlayer)
        {
            // Trigger JavaScript function via Unity's WebGL bridge
            gameObject.SendMessage("TriggerLoadLeaderboard", new object[] { gameMode, limit });
            CustomLogger.Log($"LeaderboardManager: Requested leaderboard for {gameMode} with limit {limit} via JavaScript.");
        }
        else
        {
            // Editor simulation: Mock leaderboard data
            List<LeaderboardEntry> mockEntries = new List<LeaderboardEntry>
            {
                new LeaderboardEntry { Username = "testuser1", Crowns = 10 },
                new LeaderboardEntry { Username = "testuser2", Crowns = 8 },
                new LeaderboardEntry { Username = "testuser3", Crowns = 5 }
            };
            UpdateLeaderboardUI(gameMode, mockEntries);
            CustomLogger.Log($"LeaderboardManager: Editor simulation, loaded mock {gameMode} leaderboard with {mockEntries.Count} entries.");
        }
    }

    void LoadPlayerRank(string gameMode)
    {
        string uid = PlayerPrefs.GetString("PlayerUID", "");
        if (string.IsNullOrEmpty(uid))
        {
            CustomLogger.LogError($"LeaderboardManager: No PlayerUID found, cannot load {gameMode} rank.");
            return;
        }

        if (Application.platform == RuntimePlatform.WebGLPlayer)
        {
            // Trigger JavaScript function via Unity's WebGL bridge
            gameObject.SendMessage("TriggerGetPlayerRank", new object[] { uid, gameMode, gameObject.name });
            CustomLogger.Log($"LeaderboardManager: Requested rank for UID {uid} in {gameMode} via JavaScript.");
        }
        else
        {
            // Editor simulation: Mock rank
            int mockRank = Random.Range(1, 1000);
            OnPlayerRankLoaded($"{gameMode}|{mockRank}");
            CustomLogger.Log($"LeaderboardManager: Editor simulation, assigned mock rank {mockRank} for {gameMode}.");
        }
    }

    public void OnLeaderboardLoaded(string data)
    {
        try
        {
            string[] parts = data.Split('|');
            if (parts.Length != 2)
            {
                CustomLogger.LogError($"LeaderboardManager: Invalid leaderboard data format: {data}");
                return;
            }

            string gameMode = parts[0];
            string[] entries = parts[1].Split(';');
            List<LeaderboardEntry> leaderboardEntries = new List<LeaderboardEntry>();

            for (int i = 0; i < Mathf.Min(entries.Length, 100); i++)
            {
                if (string.IsNullOrEmpty(entries[i])) continue;
                string[] fields = entries[i].Split(',');
                if (fields.Length == 2 && int.TryParse(fields[1], out int crowns))
                {
                    leaderboardEntries.Add(new LeaderboardEntry { Username = fields[0], Crowns = crowns });
                }
            }

            UpdateLeaderboardUI(gameMode, leaderboardEntries);
        }
        catch (System.Exception e)
        {
            CustomLogger.LogError($"LeaderboardManager: Error parsing leaderboard data: {e.Message}");
        }
    }

    public void OnPlayerRankLoaded(string data)
    {
        try
        {
            string[] parts = data.Split('|');
            if (parts.Length != 2)
            {
                CustomLogger.LogError($"LeaderboardManager: Invalid rank data format: {data}");
                return;
            }

            string gameMode = parts[0];
            if (!int.TryParse(parts[1], out int rank))
            {
                CustomLogger.LogError($"LeaderboardManager: Invalid rank value in data: {data}");
                return;
            }

            string prefKey = gameMode == "MoonRan" ? "MoonRanRank" : "TeamMoonRanRank";
            PlayerPrefs.SetInt(prefKey, rank);
            PlayerPrefs.Save();
            CustomLogger.Log($"LeaderboardManager: Saved {gameMode} rank {rank} to PlayerPrefs.");
        }
        catch (System.Exception e)
        {
            CustomLogger.LogError($"LeaderboardManager: Error parsing rank data: {e.Message}");
        }
    }

    private void UpdateLeaderboardUI(string gameMode, List<LeaderboardEntry> entries)
    {
        Transform content = gameMode == "MoonRan" ? moonRanContent : teamMoonRanContent;
        List<GameObject> entryObjects = gameMode == "MoonRan" ? moonRanEntries : teamMoonRanEntries;

        foreach (GameObject obj in entryObjects)
        {
            Destroy(obj);
        }
        entryObjects.Clear();

        foreach (LeaderboardEntry entry in entries)
        {
            GameObject entryObj = Instantiate(leaderboardEntryPrefab, content);
            TextMeshProUGUI[] texts = entryObj.GetComponentsInChildren<TextMeshProUGUI>();
            if (texts.Length >= 2)
            {
                texts[0].text = entry.Username;
                texts[1].text = entry.Crowns.ToString();
            }
            else
            {
                CustomLogger.LogWarning($"LeaderboardManager: Leaderboard entry prefab missing TextMeshProUGUI components for {gameMode}");
            }
            entryObjects.Add(entryObj);
        }

        CustomLogger.Log($"LeaderboardManager: Updated {gameMode} leaderboard with {entries.Count} entries.");
    }

    private void OnBackButtonClicked()
    {
        SceneManager.LoadScene("InsideSpaceShip");
        CustomLogger.Log("LeaderboardManager: Back button clicked, loading InsideSpaceShip scene.");
    }

    private struct LeaderboardEntry
    {
        public string Username;
        public int Crowns;
    }
}