using UnityEngine;
using UnityEngine.Video;

public class VideoPlayerVolumeSync : MonoBehaviour
{
    private VideoPlayer videoPlayer;

    void Awake()
    {
        videoPlayer = GetComponent<VideoPlayer>();
        if (videoPlayer == null)
        {
            Debug.LogWarning("VideoPlayerVolumeSync: No VideoPlayer found on this GameObject.");
            return;
        }

        // Apply saved volume
        float savedVolume = PlayerPrefs.GetFloat("VideoPlayerVolume", 0.5f);
        videoPlayer.SetDirectAudioVolume(0, savedVolume);

        // Subscribe to volume changes
        SettingsManager.OnVideoPlayerVolumeChanged += UpdateVolume;
    }

    void OnDestroy()
    {
        SettingsManager.OnVideoPlayerVolumeChanged -= UpdateVolume;
    }

    void UpdateVolume(float value)
    {
        if (videoPlayer != null)
        {
            videoPlayer.SetDirectAudioVolume(0, value);
        }
    }
}