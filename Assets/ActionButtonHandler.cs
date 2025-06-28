using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using Photon.Pun;
using System.Collections;
using System.Collections.Generic;
using UnityEngine.SceneManagement;

public class ActionButtonHandler : MonoBehaviour, IPointerDownHandler
{
    private PlayerController playerController; private Button button; private string buttonTag; private float lastClickTime = 0f; private bool isOnCooldown = false; private Coroutine resetCoroutine = null; private static Dictionary<string, int> buttonClickCounts = new Dictionary<string, int> { { "TeleportButton", 0 }, { "ShieldButton", 0 }, { "LaserButton", 0 }, { "TwinButton", 0 }, { "BombButton", 0 } };

    void Awake()
    {
        string sceneName = SceneManager.GetActiveScene().name;
        if (sceneName != "Moon Ran" && sceneName != "TeamMoonRan")
        {
            CustomLogger.Log($"ActionButtonHandler: Skipping initialization for non-gameplay scene {sceneName} on {gameObject.name}");
            enabled = false;
            return;
        }

        button = GetComponent<Button>();
        if (button == null)
        {
            CustomLogger.LogError($"ActionButtonHandler: Button component missing on {gameObject.name}. Disabling script.");
            enabled = false;
            return;
        }

        buttonTag = gameObject.tag;
        if (!IsValidTag(buttonTag))
        {
            CustomLogger.LogError($"ActionButtonHandler: Invalid tag '{buttonTag}' on {gameObject.name}. Expected 'TeleportButton', 'ShieldButton', 'LaserButton', 'TwinButton', or 'BombButton'. Disabling script.");
            enabled = false;
            return;
        }

        button.interactable = true;
        StartCoroutine(FindPlayerController());
        CustomLogger.Log($"ActionButtonHandler: Initialized on {gameObject.name}, Tag: {buttonTag}, Interactable: {button.interactable}, Scene: {sceneName}");
    }

    private IEnumerator FindPlayerController()
    {
        int maxRetries = 30;
        int retries = 0;
        float initialDelay = 0.5f;

        while (retries < maxRetries)
        {
            if (PhotonNetwork.LocalPlayer == null)
            {
                CustomLogger.LogWarning($"ActionButtonHandler: PhotonNetwork.LocalPlayer is null on retry {retries + 1}/{maxRetries} for {gameObject.name}, waiting...");
                retries++;
                yield return new WaitForSeconds(initialDelay);
                continue;
            }

            if (PhotonNetwork.LocalPlayer.TagObject != null)
            {
                GameObject playerObj = PhotonNetwork.LocalPlayer.TagObject as GameObject;
                if (playerObj != null && playerObj.CompareTag("Player"))
                {
                    var photonView = playerObj.GetComponent<PhotonView>();
                    if (photonView != null && photonView.IsMine)
                    {
                        playerController = playerObj.GetComponent<PlayerController>();
                        if (playerController != null)
                        {
                            CustomLogger.Log($"ActionButtonHandler: Found local PlayerController via TagObject on {playerObj.name}, ViewID: {photonView.ViewID}, NickName: {playerController.NickName} for {gameObject.name}");
                            button.interactable = true;
                            yield break;
                        }
                    }
                }
            }

            if (PhotonNetwork.LocalPlayer.CustomProperties.TryGetValue("PlayerViewID", out object viewIDObj) && viewIDObj is int viewID)
            {
                PhotonView playerView = PhotonView.Find(viewID);
                if (playerView != null && playerView.IsMine && playerView.gameObject.CompareTag("Player"))
                {
                    playerController = playerView.GetComponent<PlayerController>();
                    if (playerController != null)
                    {
                        PhotonNetwork.LocalPlayer.TagObject = playerView.gameObject;
                        CustomLogger.Log($"ActionButtonHandler: Found local PlayerController via PlayerViewID on {playerView.gameObject.name}, ViewID: {viewID}, NickName: {playerController.NickName} for {gameObject.name}");
                        button.interactable = true;
                        yield break;
                    }
                }
                else
                {
                    CustomLogger.LogWarning($"ActionButtonHandler: PlayerViewID {viewID} invalid or not mine on retry {retries + 1}/{maxRetries} for {gameObject.name}");
                }
            }

            GameObject[] players = GameObject.FindGameObjectsWithTag("Player");
            foreach (var playerObj in players)
            {
                var photonView = playerObj.GetComponent<PhotonView>();
                if (photonView != null && photonView.IsMine)
                {
                    playerController = playerObj.GetComponent<PlayerController>();
                    if (playerController != null)
                    {
                        PhotonNetwork.LocalPlayer.TagObject = playerObj;
                        PhotonNetwork.LocalPlayer.SetCustomProperties(new ExitGames.Client.Photon.Hashtable { { "PlayerViewID", photonView.ViewID } });
                        CustomLogger.Log($"ActionButtonHandler: Found local PlayerController via tag on {playerObj.name}, ViewID: {photonView.ViewID}, NickName: {playerController.NickName} for {gameObject.name}");
                        button.interactable = true;
                        yield break;
                    }
                }
            }

            retries++;
            CustomLogger.Log($"ActionButtonHandler: Retry {retries}/{maxRetries} to find local PlayerController for {gameObject.name}, TagObject={(PhotonNetwork.LocalPlayer.TagObject != null ? "exists" : "null")}, PlayerViewID={(PhotonNetwork.LocalPlayer.CustomProperties.ContainsKey("PlayerViewID") ? "set" : "unset")}, PlayersFound={players.Length}");
            yield return new WaitForSeconds(initialDelay);
            initialDelay = Mathf.Min(initialDelay * 1.2f, 2f);
        }

        CustomLogger.LogError($"ActionButtonHandler: Failed to find local PlayerController after {maxRetries} retries for {gameObject.name}. Disabling button.");
        button.interactable = false;
    }
    public void OnPointerDown(PointerEventData eventData)
    {
        CustomLogger.Log($"ActionButtonHandler: Click detected on {gameObject.name}, Tag: {buttonTag}, Position: {eventData.position}, Time: {Time.time:F2}, Interactable: {button.interactable}, Scene: {SceneManager.GetActiveScene().name}");

        if (!button.interactable)
        {
            CustomLogger.LogWarning($"ActionButtonHandler: Ignored click on {gameObject.name}, button is not interactable.");
            return;
        }

        if (playerController == null || playerController.photonView == null || !playerController.photonView.IsMine)
        {
            CustomLogger.LogError($"ActionButtonHandler: Ignored click on {gameObject.name}, playerController={(playerController == null ? "null" : "exists")}, photonView={(playerController?.photonView == null ? "null" : "exists")}, IsMine={(playerController?.photonView != null ? playerController.photonView.IsMine.ToString() : "N/A")}");
            return;
        }

        if (isOnCooldown)
        {
            CustomLogger.Log($"ActionButtonHandler: Ignored click on {gameObject.name}, isOnCooldown={isOnCooldown}, time={Time.time:F2}.");
            return;
        }

        if (!IsActionReady(buttonTag))
        {
            CustomLogger.Log($"ActionButtonHandler: Ignored click on {gameObject.name}, action {buttonTag} not ready, time={Time.time:F2}.");
            return;
        }

        buttonClickCounts[buttonTag]++;
        CustomLogger.Log($"ActionButtonHandler: Processing click on {gameObject.name}, Tag: {buttonTag}, ClickCount: {buttonClickCounts[buttonTag]}");

        bool actionTriggered = false;
        switch (buttonTag)
        {
            case "TeleportButton":
                var teleport = playerController.GetComponent<PhasingTeleportation>();
                if (teleport != null)
                {
                    playerController.TriggerTeleport();
                    actionTriggered = true;
                    CustomLogger.Log($"ActionButtonHandler: Triggered Teleport for {playerController.NickName}, TotalClicks: {buttonClickCounts[buttonTag]}");
                }
                else
                {
                    CustomLogger.LogError($"ActionButtonHandler: PhasingTeleportation missing for {playerController.NickName} on TeleportButton click");
                }
                break;
            case "ShieldButton":
                var shield = playerController.GetComponent<ShockShield>();
                if (shield != null)
                {
                    playerController.TriggerShield();
                    actionTriggered = true;
                    CustomLogger.Log($"ActionButtonHandler: Triggered Shield for {playerController.NickName}, TotalClicks: {buttonClickCounts[buttonTag]}");
                }
                else
                {
                    CustomLogger.LogError($"ActionButtonHandler: ShockShield missing for {playerController.NickName} on ShieldButton click");
                }
                break;
            case "LaserButton":
                var laser = playerController.GetComponentInChildren<LaserBeam>();
                if (laser != null)
                {
                    playerController.TriggerLaser();
                    actionTriggered = true;
                    CustomLogger.Log($"ActionButtonHandler: Triggered Laser for {playerController.NickName}, TotalClicks: {buttonClickCounts[buttonTag]}");
                }
                else
                {
                    CustomLogger.LogError($"ActionButtonHandler: LaserBeam missing for {playerController.NickName} on LaserButton click");
                }
                break;
            case "TwinButton":
                var twinTurret = playerController.GetComponent<TwinTurretManager>();
                if (twinTurret != null)
                {
                    twinTurret.ActivateTwinTurret();
                    actionTriggered = true;
                    CustomLogger.Log($"ActionButtonHandler: Triggered TwinTurret for {playerController.NickName}, TotalClicks: {buttonClickCounts[buttonTag]}");
                }
                else
                {
                    CustomLogger.LogError($"ActionButtonHandler: TwinTurretManager missing for {playerController.NickName} on TwinButton click");
                }
                break;
            case "BombButton":
                var bombManager = playerController.GetComponentInChildren<BombManager>();
                if (bombManager != null)
                {
                    bombManager.TryDeployBomb();
                    actionTriggered = true;
                    CustomLogger.Log($"ActionButtonHandler: Triggered Bomb for {playerController.NickName}, TotalClicks: {buttonClickCounts[buttonTag]}");
                }
                else
                {
                    CustomLogger.LogError($"ActionButtonHandler: BombManager missing for {playerController.NickName} on BombButton click");
                }
                break;
            default:
                CustomLogger.LogError($"ActionButtonHandler: Unknown tag '{buttonTag}' on {gameObject.name}");
                return;
        }

        if (actionTriggered)
        {
            lastClickTime = Time.time;
            isOnCooldown = true;
            button.interactable = false;
            if (resetCoroutine != null)
            {
                StopCoroutine(resetCoroutine);
            }
            resetCoroutine = StartCoroutine(FullReset());
        }
    }

    private bool IsActionReady(string tag)
    {
        switch (tag)
        {
            case "TeleportButton":
                var teleport = playerController.GetComponent<PhasingTeleportation>();
                if (teleport == null)
                {
                    CustomLogger.LogError($"IsActionReady: PhasingTeleportation missing for {playerController.NickName}");
                    return false;
                }
                var playerFuel = playerController.GetComponent<PlayerFuel>();
                bool canTeleport = teleport.CanTeleport && (playerFuel != null && playerFuel.CanAffordFuel(teleport.fuelCostPerTeleport));
                CustomLogger.Log($"IsActionReady: Teleport ready={canTeleport}, canTeleport={teleport.CanTeleport}, fuelAffordable={(playerFuel != null ? playerFuel.CanAffordFuel(teleport.fuelCostPerTeleport) : false)}");
                return canTeleport;

            case "ShieldButton":
                var shield = playerController.GetComponent<ShockShield>();
                if (shield == null)
                {
                    CustomLogger.LogError($"IsActionReady: ShockShield missing for {playerController.NickName}");
                    return false;
                }
                bool canToggleShield = shield.isShieldActive || (!shield.isShieldActive && shield.GetEnergy() >= 2f && !(shield.WasFullyDepleted && shield.GetEnergy() <= 50f));
                CustomLogger.Log($"IsActionReady: Shield ready={canToggleShield}, isShieldActive={shield.isShieldActive}, energy={shield.GetEnergy():F2}, wasFullyDepleted={shield.WasFullyDepleted}");
                return canToggleShield;

            case "LaserButton":
                var laser = playerController.GetComponentInChildren<LaserBeam>();
                if (laser == null)
                {
                    CustomLogger.LogError($"IsActionReady: LaserBeam missing for {playerController.NickName}");
                    return false;
                }
                bool canFireLaser = !laser.IsLaserFiring && !laser.IsJammed && laser.energyBar1 != null && laser.energyBar1.value >= 1f;
                CustomLogger.Log($"IsActionReady: Laser ready={canFireLaser}, isFiring={laser.IsLaserFiring}, isJammed={laser.IsJammed}, energy={(laser.energyBar1 != null ? laser.energyBar1.value : 0):F2}");
                return canFireLaser;

            case "TwinButton":
                var twinTurret = playerController.GetComponent<TwinTurretManager>();
                if (twinTurret == null)
                {
                    CustomLogger.LogError($"IsActionReady: TwinTurretManager missing for {playerController.NickName}");
                    return false;
                }
                bool canActivateTwinTurret = !twinTurret.TwinTurretActive && !twinTurret.OnCooldown;
                CustomLogger.Log($"IsActionReady: TwinTurret ready={canActivateTwinTurret}, isActive={twinTurret.TwinTurretActive}, onCooldown={twinTurret.OnCooldown}");
                return canActivateTwinTurret;

            case "BombButton":
                var bombManager = playerController.GetComponentInChildren<BombManager>();
                if (bombManager == null)
                {
                    CustomLogger.LogError($"IsActionReady: BombManager missing for {playerController.NickName}");
                    return false;
                }
                // Allow detonation if an active bomb exists, or deployment if cooldown has passed
                bool hasActiveBomb = bombManager.HasActiveBomb();
                bool canDeployBomb = Time.time - bombManager.GetLastBombTime() >= 5f;
                bool canUseBomb = hasActiveBomb || canDeployBomb;
                CustomLogger.Log($"IsActionReady: Bomb ready={canUseBomb}, hasActiveBomb={hasActiveBomb}, canDeployBomb={canDeployBomb}, timeSinceLastBomb={Time.time - bombManager.GetLastBombTime():F2}");
                return canUseBomb;

            default:
                CustomLogger.LogError($"IsActionReady: Unknown tag '{tag}'");
                return false;
        }
    }

    private IEnumerator FullReset()
    {
        yield return new WaitForSeconds(0.5f);
        isOnCooldown = false;
        lastClickTime = 0f;
        buttonClickCounts[buttonTag] = 0;
        button.interactable = true;
        resetCoroutine = null;
        CustomLogger.Log($"ActionButtonHandler: Full reset completed for {gameObject.name}, Tag: {buttonTag}, isOnCooldown={isOnCooldown}, clickCount={buttonClickCounts[buttonTag]}, time={Time.time:F2}");
    }

    private bool IsValidTag(string tag)
    {
        return tag == "TeleportButton" || tag == "ShieldButton" || tag == "LaserButton" || tag == "TwinButton" || tag == "BombButton";
    }

    public static void ResetClickCounts()
    {
        buttonClickCounts["TeleportButton"] = 0;
        buttonClickCounts["ShieldButton"] = 0;
        buttonClickCounts["LaserButton"] = 0;
        buttonClickCounts["TwinButton"] = 0;
        buttonClickCounts["BombButton"] = 0;
        CustomLogger.Log("ActionButtonHandler: Reset all button click counts to 0.");
    }

    public void ForceResetButton()
    {
        if (resetCoroutine != null)
        {
            StopCoroutine(resetCoroutine);
            resetCoroutine = null;
        }
        isOnCooldown = false;
        lastClickTime = 0f;
        buttonClickCounts[buttonTag] = 0;
        button.interactable = true;
        CustomLogger.Log($"ActionButtonHandler: Force reset for {gameObject.name}, Tag: {buttonTag}, time={Time.time:F2}");
    }

}