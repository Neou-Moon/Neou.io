using UnityEngine;
using UnityEngine.Video;
using TMPro;
using System.Collections;

public class RankDisplay : MonoBehaviour
{
    [Header("UI Elements")]
    [SerializeField] private TextMeshProUGUI usernameText; // Assign in Inspector, larger font size (e.g., 31.2f)
    [SerializeField] private TextMeshProUGUI ranksText;    // Assign in Inspector, base font size (e.g., 24f)
    [SerializeField] private VideoPlayer videoPlayer;      // Assign in Inspector

    void Start()
    {
        // Validate references
        if (usernameText == null)
        {
            CustomLogger.LogError("RankDisplay: usernameText not assigned in Inspector.");
            return;
        }
        if (ranksText == null)
        {
            CustomLogger.LogError("RankDisplay: ranksText not assigned in Inspector.");
            return;
        }
        if (videoPlayer == null)
        {
            CustomLogger.LogError("RankDisplay: videoPlayer not assigned in Inspector.");
            return;
        }

        // Retrieve username and ranks from PlayerPrefs
        string username = PlayerPrefs.GetString("PlayerUsername", "Unknown");
        int moonRanRank = PlayerPrefs.GetInt("MoonRanRank", 0);
        int teamMoonRanRank = PlayerPrefs.GetInt("TeamMoonRanRank", 0);

        // Format rank display
        string moonRanDisplay = moonRanRank > 0 ? moonRanRank.ToString() : "Unranked";
        string teamMoonRanDisplay = teamMoonRanRank > 0 ? teamMoonRanRank.ToString() : "Unranked";

        // Set text for username and ranks with extra line spacing
        usernameText.text = username;
        ranksText.text = $"Moon Ran Rank: {moonRanDisplay}\n\nTeam Moon Ran Rank: {teamMoonRanDisplay}";

        // Initialize text as invisible
        Color usernameColor = usernameText.color;
        usernameColor.a = 0f;
        usernameText.color = usernameColor;
        Color ranksColor = ranksText.color;
        ranksColor.a = 0f;
        ranksText.color = ranksColor;

        CustomLogger.Log($"RankDisplay: Displaying username: {username}, ranks - MoonRan: {moonRanDisplay}, TeamMoonRan: {teamMoonRanDisplay}");

        // Start coroutine for video delay and fade
        StartCoroutine(WaitForVideoAndFade());
    }

    private IEnumerator WaitForVideoAndFade()
    {
        // Wait until video reaches 22 seconds
        while (videoPlayer.time < 22f)
        {
            yield return null;
        }

        CustomLogger.Log("RankDisplay: Video reached 22 seconds, starting fade animation.");

        // Start fading animation
        while (true)
        {
            // Fade in over 1.0 seconds
            float elapsed = 0f;
            while (elapsed < 1.0f)
            {
                elapsed += Time.deltaTime;
                float alpha = Mathf.Lerp(0f, 1f, elapsed / 1.0f);
                SetTextAlpha(alpha);
                yield return null;
            }
            SetTextAlpha(1f);

            // Stay visible for 1.0 seconds
            yield return new WaitForSeconds(1.0f);

            // Fade out over 1.0 seconds
            elapsed = 0f;
            while (elapsed < 1.0f)
            {
                elapsed += Time.deltaTime;
                float alpha = Mathf.Lerp(1f, 0f, elapsed / 1.0f);
                SetTextAlpha(alpha);
                yield return null;
            }
            SetTextAlpha(0f);

            // Small delay before next cycle
            yield return new WaitForSeconds(0.2f);
        }
    }

    private void SetTextAlpha(float alpha)
    {
        Color usernameColor = usernameText.color;
        usernameColor.a = alpha;
        usernameText.color = usernameColor;
        Color ranksColor = ranksText.color;
        ranksColor.a = alpha;
        ranksText.color = ranksColor;
    }
}