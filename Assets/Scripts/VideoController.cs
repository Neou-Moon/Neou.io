using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Video;

public class VideoController : MonoBehaviour
{
    public VideoPlayer videoPlayer;
    public GameObject menuUI;
    public GameObject socialNetworkUI;
    public double seatTime = 5.0;
    public double dashboardTime = 10.0;
    public double socialNetworkTime = 20.0;

    void Update()
    {
        if (Input.GetKeyDown(KeyCode.S))
            videoPlayer.time = seatTime;

        if (videoPlayer.time >= seatTime && videoPlayer.time < seatTime + 0.1)
            PauseVideo(menuUI);
        else if (videoPlayer.time >= dashboardTime && videoPlayer.time < dashboardTime + 0.1)
            PauseVideo(socialNetworkUI);
    }

    void PauseVideo(GameObject menu)
    {
        videoPlayer.Pause();
        menu.SetActive(true);
    }

    public void ResumeVideo()
    {
        menuUI.SetActive(false);
        socialNetworkUI.SetActive(false);
        videoPlayer.Play();
    }
}