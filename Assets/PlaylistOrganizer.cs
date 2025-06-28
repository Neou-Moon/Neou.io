using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System.Collections.Generic;
using UnityEngine.SceneManagement;
using System.Linq;
using System.Collections;

public class PlaylistOrganizer : MonoBehaviour
{
    [SerializeField] private SongDatabase songDatabase; // Reference to SongDatabase
    [SerializeField] private Button[] songButtons; // 17 buttons in a grid
    [SerializeField] private TMP_Text[] playlistSlots; // 5 slots for playlist display
    [SerializeField] private Button saveButton; // Save playlist button
    [SerializeField] private Button backButton; // Back to spaceship button
    [SerializeField] private Button addToPlaylistButton; // Add selected song to playlist
    [SerializeField] private Button removeFromPlaylistButton; // Remove selected song from playlist
    [SerializeField] private TMP_Text feedbackText; // Feedback messages
    [SerializeField] private AudioSource audioSource; // For playing song previews

    private List<int> playlistSongs = new List<int>(); // Indices of songs in playlist
    private int currentSelectedSongIndex = -1; // Index of currently selected song (-1 for none)
    private const int maxPlaylistSize = 5;
    private const float previewDuration = 30f; // 30-second preview
    private const float previewStartTime = 40f; // Start at 40 seconds

    private int lastPlayedSongIndex = -1; // Track last previewed song
    private Coroutine pulseCoroutine; // Track pulsing coroutine

    void Start()
    {
        Debug.Log($"PlaylistOrganizer started in scene: {SceneManager.GetActiveScene().name}");

        // Validate components
        if (!ValidateComponents()) return;

        // Setup button listeners
        for (int i = 0; i < songButtons.Length; i++)
        {
            int index = i;
            songButtons[i].onClick.AddListener(() => OnSongButtonClick(index));
            UpdateButtonAppearance(i); // Set initial appearance
        }

        saveButton.onClick.AddListener(SavePlaylist);
        backButton.onClick.AddListener(() => SceneManager.LoadScene("InsideSpaceShip"));
        addToPlaylistButton.onClick.AddListener(AddToPlaylist);
        removeFromPlaylistButton.onClick.AddListener(RemoveFromPlaylist);

        // Load existing playlist
        LoadPlaylist();
        UpdatePlaylistUI();

        // Start pulsing feedback text if no song is selected
        if (currentSelectedSongIndex < 0 && feedbackText != null)
        {
            feedbackText.text = "Select a song";
            pulseCoroutine = StartCoroutine(PulseFeedbackText());
        }
    }

    private bool ValidateComponents()
    {
        bool isValid = true;

        if (songDatabase == null)
        {
            Debug.LogError("SongDatabase is not assigned in Inspector.");
            isValid = false;
        }
        else if (songDatabase.SongClips == null || songDatabase.SongClips.Length != 17)
        {
            Debug.LogError($"SongDatabase.SongClips has {songDatabase?.SongClips.Length ?? 0} elements, expected 17.");
            isValid = false;
        }
        else
        {
            Debug.Log($"SongDatabase has {songDatabase.SongClips.Length} elements, names={string.Join(", ", songDatabase.SongClips.Select(c => c != null ? c.name : "null"))}");
            for (int i = 0; i < songDatabase.SongClips.Length; i++)
            {
                if (songDatabase.SongClips[i] == null)
                {
                    Debug.LogError($"SongDatabase.SongClips[{i}] is null.");
                    isValid = false;
                }
            }
        }

        if (songButtons == null || songButtons.Length != 17)
        {
            Debug.LogError($"SongButtons has {songButtons?.Length ?? 0} elements, expected 17.");
            isValid = false;
        }
        else
        {
            for (int i = 0; i < songButtons.Length; i++)
            {
                if (songButtons[i] == null)
                {
                    Debug.LogError($"SongButtons[{i}] is not assigned.");
                    isValid = false;
                }
                else
                {
                    TMP_Text text = songButtons[i].GetComponentInChildren<TMP_Text>();
                    if (text == null)
                    {
                        Debug.LogError($"SongButtons[{i}] ({songButtons[i].name}) has no TMP_Text child.");
                        isValid = false;
                    }
                    else
                    {
                        Debug.Log($"SongButton[{i}] ({songButtons[i].name}) text={text.text}, active={songButtons[i].gameObject.activeSelf}");
                    }
                }
            }
        }

        if (playlistSlots == null || playlistSlots.Length != 5)
        {
            Debug.LogError($"PlaylistSlots has {playlistSlots?.Length ?? 0} elements, expected 5.");
            isValid = false;
        }
        else
        {
            for (int i = 0; i < playlistSlots.Length; i++)
            {
                if (playlistSlots[i] == null)
                {
                    Debug.LogError($"PlaylistSlots[{i}] is not assigned.");
                    isValid = false;
                }
            }
        }

        if (saveButton == null)
        {
            Debug.LogError("SaveButton is not assigned in Inspector.");
            isValid = false;
        }
        if (backButton == null)
        {
            Debug.LogError("BackButton is not assigned in Inspector.");
            isValid = false;
        }
        if (addToPlaylistButton == null)
        {
            Debug.LogError("AddToPlaylistButton is not assigned in Inspector.");
            isValid = false;
        }
        if (removeFromPlaylistButton == null)
        {
            Debug.LogError("RemoveFromPlaylistButton is not assigned in Inspector.");
            isValid = false;
        }
        if (feedbackText == null)
        {
            Debug.LogError("FeedbackText is not assigned in Inspector.");
            isValid = false;
        }
        if (audioSource == null)
        {
            Debug.LogError("AudioSource is not assigned in Inspector.");
            isValid = false;
        }
        else
        {
            audioSource.playOnAwake = false;
            audioSource.loop = false;
            Debug.Log($"AudioSource assigned, playOnAwake={audioSource.playOnAwake}, loop={audioSource.loop}");
        }

        // Check for duplicate song names
        var duplicates = songDatabase?.SongClips?.GroupBy(c => c?.name).Where(g => g.Count() > 1).Select(g => g.Key) ?? new string[0];
        if (duplicates.Any())
        {
            Debug.LogError($"Duplicate song names in SongDatabase: {string.Join(", ", duplicates)}.");
            isValid = false;
        }

        // Verify button names match song clips
        for (int i = 0; i < Mathf.Min(songButtons?.Length ?? 0, songDatabase?.SongClips?.Length ?? 0); i++)
        {
            TMP_Text text = songButtons[i].GetComponentInChildren<TMP_Text>();
            if (text != null && songDatabase.SongClips[i] != null && text.text != songDatabase.SongClips[i].name)
            {
                Debug.LogWarning($"SongButton[{i}] text ({text.text}) does not match SongClips[{i}] name ({songDatabase.SongClips[i].name}).");
            }
        }

        if (!isValid)
        {
            Debug.LogError("Initialization aborted due to invalid setup.");
        }
        return isValid;
    }

    private void OnSongButtonClick(int index)
    {
        // Handle song selection
        if (currentSelectedSongIndex == index)
        {
            // Deselect if clicking the same song
            currentSelectedSongIndex = -1;
            feedbackText.text = "Song deselected.";
            Debug.Log($"Deselected song '{songDatabase.SongClips[index].name}'.");
            StopPreview();
            // Resume pulsing
            if (pulseCoroutine == null && feedbackText != null)
            {
                feedbackText.text = "Select a song";
                pulseCoroutine = StartCoroutine(PulseFeedbackText());
            }
        }
        else
        {
            // Select new song
            currentSelectedSongIndex = index;
            feedbackText.text = $"Selected '{songDatabase.SongClips[index].name}'.";
            Debug.Log($"Selected song '{songDatabase.SongClips[index].name}'.");
            // Stop pulsing and reset scale
            if (pulseCoroutine != null)
            {
                StopCoroutine(pulseCoroutine);
                pulseCoroutine = null;
                if (feedbackText != null)
                {
                    feedbackText.rectTransform.localScale = Vector3.one;
                }
            }
            PlaySongPreview(index);
        }

        // Update all button appearances
        for (int i = 0; i < songButtons.Length; i++)
        {
            UpdateButtonAppearance(i);
        }
    }

    private void AddToPlaylist()
    {
        if (currentSelectedSongIndex < 0)
        {
            feedbackText.text = "No song selected.";
            Debug.LogWarning("AddToPlaylist: No song selected.");
            return;
        }

        if (playlistSongs.Contains(currentSelectedSongIndex))
        {
            feedbackText.text = $"'{songDatabase.SongClips[currentSelectedSongIndex].name}' already in playlist.";
            Debug.LogWarning($"AddToPlaylist: '{songDatabase.SongClips[currentSelectedSongIndex].name}' already in playlist.");
            return;
        }

        if (playlistSongs.Count >= maxPlaylistSize)
        {
            feedbackText.text = "Playlist full! Remove a song first.";
            Debug.LogWarning("AddToPlaylist: Playlist is full.");
            return;
        }

        playlistSongs.Add(currentSelectedSongIndex);
        feedbackText.text = $"Added '{songDatabase.SongClips[currentSelectedSongIndex].name}' to playlist.";
        Debug.Log($"Added '{songDatabase.SongClips[currentSelectedSongIndex].name}' to playlist.");

        UpdatePlaylistUI();
        UpdateButtonAppearance(currentSelectedSongIndex);
    }

    private void RemoveFromPlaylist()
    {
        if (currentSelectedSongIndex < 0)
        {
            feedbackText.text = "No song selected.";
            Debug.LogWarning("RemoveFromPlaylist: No song selected.");
            return;
        }

        if (!playlistSongs.Contains(currentSelectedSongIndex))
        {
            feedbackText.text = $"'{songDatabase.SongClips[currentSelectedSongIndex].name}' not in playlist.";
            Debug.LogWarning($"RemoveFromPlaylist: '{songDatabase.SongClips[currentSelectedSongIndex].name}' not in playlist.");
            return;
        }

        playlistSongs.Remove(currentSelectedSongIndex);
        feedbackText.text = $"Removed '{songDatabase.SongClips[currentSelectedSongIndex].name}' from playlist.";
        Debug.Log($"Removed '{songDatabase.SongClips[currentSelectedSongIndex].name}' from playlist.");

        UpdatePlaylistUI();
        UpdateButtonAppearance(currentSelectedSongIndex);
    }

    private void PlaySongPreview(int index)
    {
        if (audioSource == null || songDatabase.SongClips[index] == null)
        {
            Debug.LogError($"Cannot play preview: audioSource={(audioSource == null ? "null" : "assigned")}, songClips[{index}]={(songDatabase.SongClips[index] == null ? "null" : songDatabase.SongClips[index].name)}");
            return;
        }

        // Stop any ongoing preview
        StopPreview();

        // Set and play preview
        audioSource.clip = songDatabase.SongClips[index];
        float startTime = Mathf.Min(previewStartTime, songDatabase.SongClips[index].length);
        audioSource.time = startTime;
        audioSource.Play();
        lastPlayedSongIndex = index;
        Debug.Log($"Playing preview for '{songDatabase.SongClips[index].name}' starting at {startTime}s, continuous until deselected or another song selected");
    }

    private void StopPreview()
    {
        if (audioSource != null && audioSource.isPlaying)
        {
            audioSource.Stop();
            audioSource.clip = null;
            Debug.Log($"Stopped preview{(lastPlayedSongIndex >= 0 ? $" for '{songDatabase.SongClips[lastPlayedSongIndex].name}'" : "")}");
            lastPlayedSongIndex = -1;
        }
    }

    private IEnumerator StopPreviewAfterDuration(float duration)
    {
        yield return new WaitForSeconds(duration);
        if (audioSource != null && audioSource.isPlaying)
        {
            audioSource.Stop();
            audioSource.clip = null;
            Debug.Log($"Preview stopped after {duration}s{(lastPlayedSongIndex >= 0 ? $" for '{songDatabase.SongClips[lastPlayedSongIndex].name}'" : "")}");
            lastPlayedSongIndex = -1;
        }
    }

    private IEnumerator PulseFeedbackText()
    {
        while (currentSelectedSongIndex < 0 && feedbackText != null)
        {
            float t = Mathf.PingPong(Time.time, 1f); // Oscillate between 0 and 1 over 1 second
            float scale = Mathf.Lerp(1f, 1.05f, t); // Subtle scale from 1 to 1.05
            feedbackText.rectTransform.localScale = new Vector3(scale, scale, 1f);
            yield return null; // Wait for next frame
        }

        // Ensure scale is reset when stopping
        if (feedbackText != null)
        {
            feedbackText.rectTransform.localScale = Vector3.one;
        }
    }

    private void UpdateButtonAppearance(int index)
    {
        Image buttonImage = songButtons[index].GetComponent<Image>();
        if (buttonImage != null)
        {
            // Green if in playlist or currently selected
            bool isHighlighted = playlistSongs.Contains(index) || currentSelectedSongIndex == index;
            buttonImage.color = isHighlighted ? Color.green : Color.white;
            Debug.Log($"Updated button '{songButtons[index].name}' color to {(isHighlighted ? "green (selected/in playlist)" : "white (deselected)")}");
        }
        else
        {
            Debug.LogError($"No Image component on SongButton[{index}] ({songButtons[index].name}).");
        }
    }

    private void UpdatePlaylistUI()
    {
        for (int i = 0; i < playlistSlots.Length; i++)
        {
            playlistSlots[i].text = i < playlistSongs.Count ? songDatabase.SongClips[playlistSongs[i]].name : "Empty";
        }
        Debug.Log($"Updated playlist UI: {string.Join(", ", playlistSongs.Select(i => songDatabase.SongClips[i].name))}");
    }

    private void SavePlaylist()
    {
        string uid = PlayerPrefs.GetString("PlayerUID", "");
        if (string.IsNullOrEmpty(uid))
        {
            feedbackText.text = "Error: Not logged in.";
            Debug.LogError("Cannot save playlist: No PlayerUID.");
            return;
        }

        PlayerPrefs.SetInt($"PlaylistCount_{uid}", playlistSongs.Count);
        for (int i = 0; i < maxPlaylistSize; i++)
        {
            PlayerPrefs.SetString($"PlaylistSong_{uid}_{i}", i < playlistSongs.Count ? songDatabase.SongClips[playlistSongs[i]].name : "");
        }
        PlayerPrefs.Save();

        feedbackText.text = "Playlist saved!";
        Debug.Log($"Saved playlist for UID={uid}: {string.Join(", ", playlistSongs.Select(i => songDatabase.SongClips[i].name))}");
    }

    private void LoadPlaylist()
    {
        string uid = PlayerPrefs.GetString("PlayerUID", "");
        if (string.IsNullOrEmpty(uid))
        {
            Debug.LogWarning("No PlayerUID, empty playlist.");
            return;
        }

        playlistSongs.Clear();
        int count = PlayerPrefs.GetInt($"PlaylistCount_{uid}", 0);
        for (int i = 0; i < count; i++)
        {
            string songName = PlayerPrefs.GetString($"PlaylistSong_{uid}_{i}", "");
            int index = System.Array.FindIndex(songDatabase.SongClips, c => c != null && c.name == songName);
            if (index >= 0)
            {
                playlistSongs.Add(index);
                UpdateButtonAppearance(index);
            }
            else
            {
                Debug.LogWarning($"Song '{songName}' not found in SongDatabase.");
            }
        }

        UpdatePlaylistUI();
        Debug.Log($"Loaded playlist for UID={uid}: {string.Join(", ", playlistSongs.Select(i => songDatabase.SongClips[i].name))}");
    }
}