using UnityEngine;
using UnityEngine.Video;

public class VideoLoopController : MonoBehaviour
{
    [SerializeField] private VideoPlayer videoPlayer;
    private const float LOOP_START_TIME = 10f; // Start looping from 10 seconds

    void Start()
    {
        // Validate VideoPlayer reference
        if (videoPlayer == null)
        {
            videoPlayer = GetComponent<VideoPlayer>();
            if (videoPlayer == null)
            {
                Debug.LogError("VideoLoopController: VideoPlayer is not assigned and not found on GameObject.");
                return;
            }
        }

        // Configure VideoPlayer
        videoPlayer.isLooping = true; // Enable looping
        videoPlayer.loopPointReached += OnLoopPointReached; // Subscribe to loop event
        videoPlayer.prepareCompleted += OnVideoPrepared; // Subscribe to prepare event

        // Start playing the video
        videoPlayer.Play();
    }

    private void OnVideoPrepared(VideoPlayer vp)
    {
        // Set initial time to 10 seconds when video is prepared
        vp.time = LOOP_START_TIME;
        Debug.Log($"VideoLoopController: Video prepared, starting at {LOOP_START_TIME} seconds.");
    }

    private void OnLoopPointReached(VideoPlayer vp)
    {
        // When video reaches the end, jump to 10 seconds and continue playing
        vp.time = LOOP_START_TIME;
        vp.Play();
        Debug.Log($"VideoLoopController: Video reached end, looping back to {LOOP_START_TIME} seconds.");
    }

    void OnDestroy()
    {
        // Unsubscribe from events to prevent memory leaks
        if (videoPlayer != null)
        {
            videoPlayer.loopPointReached -= OnLoopPointReached;
            videoPlayer.prepareCompleted -= OnVideoPrepared;
        }
    }
}