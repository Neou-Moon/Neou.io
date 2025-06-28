using UnityEngine;

public class PlayerPrefsReset : MonoBehaviour
{
    void Start()
    {
        PlayerPrefs.DeleteKey("BrightMatter");
        PlayerPrefs.DeleteKey("InsideSpaceShip");
        PlayerPrefs.DeleteKey("UpgradeLevels");
        PlayerPrefs.DeleteKey("PlayerUID");
        PlayerPrefs.Save();
        Debug.Log("PlayerPrefs reset: BrightMatter, InsideSpaceShip, UpgradeLevels, PlayerUID cleared.");
        Destroy(this); // Remove script after reset
    }
}