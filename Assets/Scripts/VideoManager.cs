using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Video;

public class VideoManager : MonoBehaviour
{
    public VideoPlayer videoPlayer;
    public GameObject mainMenuUI;
    public GameObject socialUI;
    public GameObject healingUI;
    public double introEndTime = 5.0;
    public double menuPauseTime = 10.0;
    public double socialPauseTime = 20.0;
    public double healingStartTime = 30.0;

    void Update()
    {
        if (Input.GetKeyDown(KeyCode.S))
            videoPlayer.time = introEndTime;

        if (videoPlayer.time >= menuPauseTime && videoPlayer.time < menuPauseTime + 0.1)
            PauseVideo(mainMenuUI);
        else if (videoPlayer.time >= socialPauseTime && videoPlayer.time < socialPauseTime + 0.1)
            PauseVideo(socialUI);
    }

    void PauseVideo(GameObject uiToShow)
    {
        videoPlayer.Pause();
        uiToShow.SetActive(true);
    }

    public void ResumeVideo()
    {
        mainMenuUI.SetActive(false);
        socialUI.SetActive(false);
        healingUI.SetActive(false);
        videoPlayer.Play();
    }

    public void GoToSocial()
    {
        mainMenuUI.SetActive(false);
        videoPlayer.time = socialPauseTime;
        videoPlayer.Play();
    }

    public void StartHealing()
    {
        mainMenuUI.SetActive(false);
        videoPlayer.time = healingStartTime;
        videoPlayer.Play();
    }
}