using UnityEngine;
using UnityEngine.UI;
using TMPro;
using Photon.Pun;
using System.Collections;
using Photon.Realtime;
using UnityEngine.SceneManagement;

public class PlayerHealth : MonoBehaviourPun
{
    public enum Team
    {
        None,
        Red,
        Cyan
    }

    private Team team = Team.None;
    public Team CurrentTeam => team;
    public int maxHealth = 100;
    public Image healthBarFill;
    public TextMeshProUGUI healthText;
    public int damageReductionLevel = 0;

    private int currentHealth;
    private PlayerController playerController;
    public bool hasDied = false;
    private Canvas playerCanvas;
    private int lastKillerViewID = -1;
    private DeathCause lastDeathCause = DeathCause.None;
    private SpriteRenderer spriteRenderer;
    private bool isInvulnerable = false;
    private float invulnerabilityDuration = 5f;

    public bool HasDied => hasDied;

    public enum DeathCause
    {
        None,
        Projectile,
        ElephantBomb,
        PlanetDebris,
        OutOfBounds,
        EnemyCollision,
        EnemyBlast,
        SelfDebris,
        OutOfRange
    }

    public void SetTeam(Team newTeam)
    {
        team = newTeam;
        Debug.Log($"PlayerHealth: Set team for {gameObject.name} to {team}");
    }

    public Team GetTeam()
    {
        return team;
    }

    void Start()
    {
        if (!photonView.IsMine)
        {
            enabled = false;
            Debug.Log($"PlayerHealth: Disabled for {gameObject.name}, photonView.IsMine=false");
            return;
        }

        playerCanvas = GetComponentInChildren<Canvas>();
        if (playerCanvas == null || playerCanvas.name != "Player Canvas")
        {
            Debug.LogError("PlayerHealth: Player Canvas not found on Player");
            return;
        }

        if (healthBarFill == null)
        {
            Image[] images = playerCanvas.GetComponentsInChildren<Image>(true);
            foreach (var img in images)
            {
                if (img.name.Contains("HealthBarFill"))
                {
                    healthBarFill = img;
                    Debug.Log($"PlayerHealth: Found HealthBarFill ({img.name})");
                    break;
                }
            }
            if (healthBarFill == null)
            {
                Debug.LogError("PlayerHealth: Could not find HealthBarFill");
            }
        }

        if (healthText == null)
        {
            TextMeshProUGUI[] texts = playerCanvas.GetComponentsInChildren<TextMeshProUGUI>(true);
            foreach (var text in texts)
            {
                if (text.name.Contains("HealthText"))
                {
                    healthText = text;
                    Debug.Log($"PlayerHealth: Found HealthText ({text.name})");
                    break;
                }
            }
            if (healthText == null)
            {
                Debug.LogError("PlayerHealth: Could not find HealthText");
            }
        }

        Image healthBarBackground = null;
        foreach (var img in playerCanvas.GetComponentsInChildren<Image>(true))
        {
            if (img.name.Contains("HealthBarBackground"))
            {
                healthBarBackground = img;
                healthBarBackground.gameObject.SetActive(true);
                healthBarBackground.color = new Color(1f, 0f, 0f, 1f);
                Debug.Log($"PlayerHealth: Activated HealthBarBackground ({img.name}), Color={healthBarBackground.color}, RectTransform={healthBarBackground.rectTransform.rect}, SiblingIndex={healthBarBackground.transform.GetSiblingIndex()}, Parent={healthBarBackground.transform.parent?.name}");
                if (healthBarFill != null)
                {
                    int fillIndex = healthBarFill.transform.GetSiblingIndex();
                    int bgIndex = healthBarBackground.transform.GetSiblingIndex();
                    if (fillIndex <= bgIndex)
                    {
                        Debug.LogWarning($"PlayerHealth: HealthBarFill (SiblingIndex={fillIndex}) is behind or equal to HealthBarBackground (SiblingIndex={bgIndex}). Adjusting...");
                        healthBarFill.transform.SetSiblingIndex(bgIndex + 1);
                    }
                }
                break;
            }
        }
        if (healthBarBackground == null)
        {
            Debug.LogWarning("PlayerHealth: HealthBarBackground not found in PlayerCanvas. Ensure an Image named 'HealthBarBackground' exists as a child of PlayerCanvas.");
        }

        spriteRenderer = GetComponentInChildren<SpriteRenderer>();
        if (spriteRenderer == null)
        {
            Debug.LogWarning($"PlayerHealth: SpriteRenderer not found on {gameObject.name} or its children for damage flash");
        }

        // Add click event listeners for HealthBarBackground and HealthBarFill
        if (healthBarBackground != null)
        {
            Button bgButton = healthBarBackground.GetComponent<Button>();
            if (bgButton == null)
                bgButton = healthBarBackground.gameObject.AddComponent<Button>();
            bgButton.onClick.AddListener(() => GetComponent<SpaceShipInteraction>()?.ConvertBrightMatterToHealth());
            Debug.Log($"PlayerHealth: Added click listener to HealthBarBackground ({healthBarBackground.name})");
        }
        if (healthBarFill != null)
        {
            Button fillButton = healthBarFill.GetComponent<Button>();
            if (fillButton == null)
                fillButton = healthBarFill.gameObject.AddComponent<Button>();
            fillButton.onClick.AddListener(() => GetComponent<SpaceShipInteraction>()?.ConvertBrightMatterToHealth());
            Debug.Log($"PlayerHealth: Added click listener to HealthBarFill ({healthBarFill.name})");
        }

        currentHealth = maxHealth;
        UpdateHealthUI();
        playerController = GetComponent<PlayerController>();
        if (playerController == null)
            Debug.LogError("PlayerHealth: PlayerController not found");

        isInvulnerable = true;
        StartCoroutine(EndInvulnerability());
        Debug.Log($"PlayerHealth: Initialized {gameObject.name}, maxHealth={maxHealth}, currentHealth={currentHealth}, damageReductionLevel={damageReductionLevel}, isInvulnerable={isInvulnerable}");
    }

    public int GetCurrentHealth()
    {
        return currentHealth;
    }

    public void ResetHealth()
    {
        currentHealth = maxHealth;
        hasDied = false;
        isInvulnerable = true;
        StartCoroutine(EndInvulnerability());
        UpdateHealthUI();
        photonView.RPC("SyncDeathState", RpcTarget.AllBuffered, "None", -1);
        Debug.Log($"PlayerHealth: Reset health for {gameObject.name}, currentHealth={currentHealth}, maxHealth={maxHealth}, hasDied={hasDied}, isInvulnerable={isInvulnerable}");
    }

    private IPlayer FindPlayerByActorNumber(int actorNumber)
    {
        // Check real players
        foreach (Player photonPlayer in PhotonNetwork.PlayerList)
        {
            if (photonPlayer.ActorNumber == actorNumber)
            {
                GameObject playerObj = photonPlayer.TagObject as GameObject;
                if (playerObj != null)
                {
                    return playerObj.GetComponent<PlayerController>();
                }
            }
        }
        // Check bots
        BotController[] bots = Object.FindObjectsByType<BotController>(FindObjectsSortMode.None);
        foreach (BotController bot in bots)
        {
            if (bot.ActorNumber == actorNumber)
            {
                return bot;
            }
        }
        CustomLogger.LogWarning($"PlayerHealth: No player or bot found for ActorNumber={actorNumber}");
        return null;
    }

    public int GetMaxHealth()
    {
        return maxHealth;
    }

    public void TakeDamage(float damage, bool ignoreShield, int killerActorNumber, DeathCause cause = DeathCause.None)
    {
        if (hasDied || currentHealth <= 0) return;

        // Check if attacker is on the same team in TeamMoonRan
        if (SceneManager.GetActiveScene().name == "TeamMoonRan" && killerActorNumber != -1)
        {
            IPlayer attacker = FindPlayerByActorNumber(killerActorNumber);
            if (attacker != null && attacker.CustomProperties.ContainsKey("Team") && playerController.CustomProperties.ContainsKey("Team"))
            {
                string attackerTeam = attacker.CustomProperties["Team"].ToString();
                string playerTeam = playerController.CustomProperties["Team"].ToString();
                if (attackerTeam == playerTeam)
                {
                    CustomLogger.Log($"PlayerHealth: {playerController.NickName} (Team {playerTeam}) ignored damage from ActorNumber={killerActorNumber} (Team {attackerTeam}) due to same team in TeamMoonRan");
                    return;
                }
            }
        }

        if (!ignoreShield && playerController.isShieldActive)
        {
            ShockShield shield = GetComponent<ShockShield>();
            if (shield != null && shield.GetEnergy() > 0)
            {
                float shieldAbsorption = Mathf.Min(shield.GetEnergy(), damage);
                shield.DrainEnergy(shieldAbsorption); // Changed from ConsumeEnergy
                damage -= shieldAbsorption;
                CustomLogger.Log($"PlayerHealth: Shield absorbed {shieldAbsorption} damage for {playerController.NickName}, remaining shield energy={shield.GetEnergy()}, remaining damage={damage}");
                if (shield.GetEnergy() <= 0)
                {
                    playerController.isShieldActive = false;
                    playerController.photonView.RPC("SyncShieldState", RpcTarget.All, false);
                    CustomLogger.Log($"PlayerHealth: Shield depleted for {playerController.NickName}, ViewID={playerController.photonView.ViewID}");
                }
            }
        }

        if (damage > 0)
        {
            if (!isInvulnerable)
            {
                currentHealth = (int)Mathf.Max(0, currentHealth - damage); // Explicit cast to int
                CustomLogger.Log($"PlayerHealth: {playerController.NickName} took {damage} damage, currentHealth={currentHealth}, killerActorNumber={killerActorNumber}, cause={cause}");
                UpdateHealthUI();
                if (currentHealth <= 0)
                {
                    hasDied = true;
                    lastDeathCause = cause;
                    lastKillerViewID = killerActorNumber;
                    playerController.Die();
                    if (killerActorNumber != -1)
                    {
                        IPlayer killer = FindPlayerByActorNumber(killerActorNumber);
                        if (killer != null)
                        {
                            killer.AddPoints(100);
                            if (killer is PlayerController pc && pc.photonView.IsMine)
                            {
                                pc.OnPlayerKilled(playerController.NickName);
                            }
                            CustomLogger.Log($"PlayerHealth: {killer.NickName} (ActorNumber={killerActorNumber}) killed {playerController.NickName}, awarded 100 points");
                        }
                    }
                }
                StartCoroutine(FlashDamage());
            }
            else
            {
                CustomLogger.Log($"PlayerHealth: {playerController.NickName} ignored {damage} damage due to invulnerability");
            }
        }

    }

    private IEnumerator EndInvulnerability()
    {
        yield return new WaitForSeconds(invulnerabilityDuration);
        isInvulnerable = false;
        Debug.Log($"PlayerHealth: Invulnerability ended for {gameObject.name}, isInvulnerable={isInvulnerable}");
    }

    private IEnumerator FlashDamage()
    {
        if (spriteRenderer == null) yield break;
        Color originalColor = spriteRenderer.color;
        spriteRenderer.color = Color.red;
        yield return new WaitForSeconds(0.1f);
        spriteRenderer.color = originalColor;
        yield return new WaitForSeconds(0.1f);
        spriteRenderer.color = Color.red;
        yield return new WaitForSeconds(0.1f);
        spriteRenderer.color = originalColor;
    }

    public void Heal(int amount)
    {
        if (hasDied || !photonView.IsMine)
        {
            Debug.Log($"PlayerHealth: Heal skipped for {gameObject.name}, hasDied={hasDied}, photonView.IsMine={photonView.IsMine}");
            return;
        }
        currentHealth = Mathf.Min(maxHealth, currentHealth + amount);
        UpdateHealthUI();
        Debug.Log($"PlayerHealth: Healed {gameObject.name}, amount={amount}, currentHealth={currentHealth}");
    }

    void UpdateHealthUI()
    {
        if (healthBarFill != null)
        {
            float newFillAmount = (float)currentHealth / maxHealth;
            healthBarFill.fillAmount = newFillAmount;
            Debug.Log($"PlayerHealth: Updated HealthBarFill, fillAmount={newFillAmount:F2}, CurrentHealth={currentHealth}, MaxHealth={maxHealth}, ImageType={healthBarFill.type}, FillMethod={healthBarFill.fillMethod}");
        }
        else
        {
            Debug.LogWarning("PlayerHealth: HealthBarFill is null in UpdateHealthUI");
        }
        if (healthText != null)
        {
            healthText.text = currentHealth.ToString();
            Debug.Log($"PlayerHealth: Updated HealthText to {currentHealth}, textObject={healthText.gameObject.name}");
        }
        else
        {
            Debug.LogWarning("PlayerHealth: HealthText is null in UpdateHealthUI");
        }
    }

    void Die()
    {
        if (hasDied || !photonView.IsMine)
        {
            Debug.Log($"PlayerHealth: Die skipped for {gameObject.name}, hasDied={hasDied}, photonView.IsMine={photonView.IsMine}");
            return;
        }
        hasDied = true;

        ExitGames.Client.Photon.Hashtable props = new ExitGames.Client.Photon.Hashtable
        {
            { "DeathCause", lastDeathCause.ToString() },
            { "KillerViewID", lastKillerViewID }
        };
        PhotonNetwork.LocalPlayer.SetCustomProperties(props);
        photonView.RPC("SyncDeathState", RpcTarget.AllBuffered, lastDeathCause.ToString(), lastKillerViewID);
        Debug.Log($"PlayerHealth: Set DeathCause={lastDeathCause}, KillerViewID={lastKillerViewID} for {gameObject.name}");

        bool isPlayerKill = lastDeathCause == DeathCause.Projectile || lastDeathCause == DeathCause.ElephantBomb || lastDeathCause == DeathCause.PlanetDebris;
        if (!isPlayerKill && playerController != null)
        {
            playerController.AddPoints(-50);
            Debug.Log($"PlayerHealth: Deducted 50 points for {gameObject.name} (cause={lastDeathCause})");
        }

        if (isPlayerKill && lastKillerViewID != -1 && lastKillerViewID != photonView.ViewID)
        {
            PhotonView killerView = PhotonView.Find(lastKillerViewID);
            if (killerView != null && killerView.gameObject != null)
            {
                PlayerController killerPlayer = killerView.GetComponentInParent<PlayerController>();
                BotController killerBot = killerView.GetComponentInParent<BotController>();
                ScoreboardManager scoreboard = FindFirstObjectByType<ScoreboardManager>();
                int pointsToAward = (scoreboard != null && scoreboard.IsTopPlayer(photonView.ControllerActorNr)) ? 200 : 100;

                if (killerPlayer != null)
                {
                    killerPlayer.AddPoints(pointsToAward);
                    killerPlayer.OnPlayerKilled(playerController.NickName);
                    Debug.Log($"PlayerHealth: Awarded {pointsToAward} points and notified killer {killerPlayer.NickName} (ViewID={lastKillerViewID}) for killing {gameObject.name}");
                }
                else if (killerBot != null)
                {
                    killerBot.AddPoints(pointsToAward);
                    killerBot.OnPlayerKilled(playerController.NickName);
                    Debug.Log($"PlayerHealth: Awarded {pointsToAward} points and notified killer {killerBot.NickName} (ViewID={lastKillerViewID}) for killing {gameObject.name}");
                }
                else
                {
                    Debug.LogWarning($"PlayerHealth: Killer not found for ViewID={lastKillerViewID}, no PlayerController or BotController on object {killerView.gameObject.name} or its parents");
                }
            }
            else
            {
                Debug.LogWarning($"PlayerHealth: Killer PhotonView not found for ViewID={lastKillerViewID} or GameObject is null");
            }
        }
        else
        {
            Debug.Log($"PlayerHealth: No points awarded, isPlayerKill={isPlayerKill}, lastKillerViewID={lastKillerViewID}, selfViewID={photonView.ViewID}");
        }

        if (playerController != null)
        {
            playerController.Die();
        }
        Debug.Log($"PlayerHealth: {gameObject.name} died, cause={lastDeathCause}, killerViewID={lastKillerViewID}");
    }

    [PunRPC]
    private void SyncDeathState(string deathCause, int killerViewID)
    {
        PhotonNetwork.LocalPlayer.CustomProperties["DeathCause"] = deathCause;
        PhotonNetwork.LocalPlayer.CustomProperties["KillerViewID"] = killerViewID;
        hasDied = deathCause != "None";
        Debug.Log($"PlayerHealth: Synced death state for {gameObject.name}, DeathCause={deathCause}, KillerViewID={killerViewID}, hasDied={hasDied}");
    }
}