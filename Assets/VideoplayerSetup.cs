using UnityEngine;
using UnityEngine.Video;
using System.IO;

public class VideoPlayerSetup : MonoBehaviour
{
    [SerializeField] private VideoPlayer videoPlayer;
    [SerializeField] private string videoFileName = "TheStartVid.mp4"; // Set per instance
    [SerializeField] private RenderTexture renderTexture; // Optional: For UI display

    void Start()
    {
        if (videoPlayer == null)
        {
            Debug.LogError("VideoPlayerSetup: VideoPlayer component not assigned!");
            return;
        }

        // Set up video path
        string videoPath = Path.Combine(Application.streamingAssetsPath, "Videos", videoFileName);
#if UNITY_WEBGL && !UNITY_EDITOR
        // WebGL: Use relative path from build root
        videoPath = Path.Combine("StreamingAssets", "Videos", videoFileName);
#endif

        Debug.Log($"VideoPlayerSetup: Setting video URL to '{videoPath}' for {videoFileName}");

        // Configure VideoPlayer
        videoPlayer.url = videoPath;
        videoPlayer.isLooping = false;
        videoPlayer.playOnAwake = false;
        videoPlayer.skipOnDrop = true;

        // Assign render texture if provided (for UI)
        if (renderTexture != null)
        {
            videoPlayer.renderMode = VideoRenderMode.RenderTexture;
            videoPlayer.targetTexture = renderTexture;
            Debug.Log($"VideoPlayerSetup: Assigned RenderTexture for {videoFileName}");
        }
        else
        {
            videoPlayer.renderMode = VideoRenderMode.CameraNearPlane; // Fallback
            Debug.Log($"VideoPlayerSetup: Using CameraNearPlane render mode for {videoFileName}");
        }

        // Add error handling
        videoPlayer.errorReceived += (source, message) =>
        {
            Debug.LogError($"VideoPlayerSetup: Error for {videoFileName}: {message}");
        };

        videoPlayer.prepareCompleted += (source) =>
        {
            Debug.Log($"VideoPlayerSetup: Video {videoFileName} prepared successfully");
            videoPlayer.Play();
        };

        // Prepare and play
        videoPlayer.Prepare();
    }
}