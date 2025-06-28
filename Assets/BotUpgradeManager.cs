using UnityEngine;
using Photon.Pun;
using System.Collections;

public class BotUpgradeManager : MonoBehaviourPunCallbacks
{
    [SerializeField] private BotController botController;

    private readonly int[] upgradeLevels = new int[9];
    private readonly int[] upgradeCosts = { 5, 15, 40, 70, 100 };
    private readonly string[] upgradeNames = {
        "Shield Recharge", "Shield Duration", "Laser Recharge",
        "Fuel Absorption", "Bullet Speed", "Turret Duration",
        "Damage Resist", "Teleport Fuel", "Bullet Damage"
    };
    private readonly int maxLevel = 5;
    private bool isAddingBrightMatter;

    public int[] UpgradeLevels => upgradeLevels;
    public int[] UpgradeCosts => upgradeCosts;

    public void InitializeForBot(BotController bot)
    {
        botController = bot;
        Start();
    }

    void Start()
    {
        if (!photonView.IsMine)
        {
            enabled = false;
            Debug.Log($"BotUpgradeManager: Disabled for {gameObject.name}, photonView.IsMine=false.");
            return;
        }

        if (botController == null)
        {
            botController = GetComponent<BotController>();
            if (botController == null)
            {
                Debug.LogError("BotUpgradeManager: botController not assigned and no BotController found.");
                return;
            }
        }

        for (int i = 0; i < 9; i++)
        {
            upgradeLevels[i] = 0; // Reset upgrades for simplicity
            if (i == 5)
            {
                StartCoroutine(ApplyTurretDurationUpgradeDelayed());
            }
            else
            {
                ApplyUpgradeEffect(i);
            }
        }
        botController.SyncBrightMatter(50);
        Debug.Log($"BotUpgradeManager: Initialized for {gameObject.name}, reset upgrades to 0, set BrightMatter to 50.");
    }

    private IEnumerator ApplyTurretDurationUpgradeDelayed()
    {
        yield return new WaitForEndOfFrame();
        TwinTurretManager turret = GetComponent<TwinTurretManager>();
        if (turret != null)
        {
            turret.twinTurretDuration = 10f + 2f * upgradeLevels[5];
            Debug.Log($"BotUpgradeManager: Applied Turret Duration delayed, level={upgradeLevels[5]}, twinTurretDuration={turret.twinTurretDuration}, GameObject={gameObject.name}");
        }
        else
        {
            Debug.LogWarning($"BotUpgradeManager: TwinTurretManager component not found for Turret Duration upgrade. Ensure TwinTurretManager is attached to {gameObject.name}.");
        }
    }

    public void TryPurchaseUpgrade(int index)
    {
        if (upgradeLevels[index] >= maxLevel) return;

        int cost = upgradeCosts[upgradeLevels[index]];
        int currentBrightMatter = botController?.GetBrightMatter() ?? 0;
        if (currentBrightMatter >= cost)
        {
            botController.SyncBrightMatter(currentBrightMatter - cost);
            upgradeLevels[index]++;
            ApplyUpgradeEffect(index);

            if (PhotonNetwork.IsConnectedAndReady)
            {
                photonView.RPC("SyncUpgradeRPC", RpcTarget.All, index, upgradeLevels[index]);
            }
            Debug.Log($"BotUpgradeManager: Purchased {upgradeNames[index]} to level {upgradeLevels[index]}, cost={cost}, new BrightMatter={currentBrightMatter - cost}");
        }
    }

    [PunRPC]
    void SyncUpgradeRPC(int index, int level)
    {
        upgradeLevels[index] = level;
        ApplyUpgradeEffect(index);
        Debug.Log($"BotUpgradeManager: Synced {upgradeNames[index]} to level {level} for {gameObject.name}.");
    }

    void ApplyUpgradeEffect(int index)
    {
        switch (index)
        {
            case 0: // Shield Recharge
                ShockShield shield = GetComponent<ShockShield>();
                if (shield != null)
                {
                    shield.rechargeTime = 10f - upgradeLevels[0];
                    Debug.Log($"BotUpgradeManager: Applied Shield Recharge, level={upgradeLevels[0]}, rechargeTime={shield.rechargeTime}");
                }
                else
                {
                    Debug.LogWarning($"BotUpgradeManager: ShockShield component not found for Shield Recharge upgrade on {gameObject.name}. Skipping.");
                }
                break;
            case 1: // Shield Duration
                shield = GetComponent<ShockShield>();
                if (shield != null)
                {
                    shield.shieldDuration = 5f + upgradeLevels[1];
                    Debug.Log($"BotUpgradeManager: Applied Shield Duration, level={upgradeLevels[1]}, shieldDuration={shield.shieldDuration}");
                }
                else
                {
                    Debug.LogWarning($"BotUpgradeManager: ShockShield component not found for Shield Duration upgrade on {gameObject.name}. Skipping.");
                }
                break;
            case 2: // Laser Recharge
                LaserBeam laser = GetComponentInChildren<LaserBeam>();
                if (laser != null)
                {
                    laser.SetRechargeTime(10f - upgradeLevels[2]);
                    Debug.Log($"BotUpgradeManager: Applied Laser Recharge, level={upgradeLevels[2]}, rechargeTime={10f - upgradeLevels[2]}");
                }
                else
                {
                    Debug.LogWarning($"BotUpgradeManager: LaserBeam component not found for Laser Recharge upgrade on {gameObject.name}. Skipping.");
                }
                break;
            case 3: // Fuel Absorption
                BotFuel fuel = GetComponent<BotFuel>();
                if (fuel != null)
                {
                    Debug.Log($"BotUpgradeManager: Applied Fuel Absorption, level={upgradeLevels[3]}");
                }
                else
                {
                    Debug.LogWarning($"BotUpgradeManager: BotFuel component not found for Fuel Absorption upgrade on {gameObject.name}. Skipping.");
                }
                break;
            case 4: // Bullet Speed
                DroidShooting shooting = GetComponentInChildren<DroidShooting>();
                if (shooting != null)
                {
                    shooting.projectileSpeed = 100f + 8f * upgradeLevels[4];
                    Debug.Log($"BotUpgradeManager: Applied Bullet Speed, level={upgradeLevels[4]}, projectileSpeed={shooting.projectileSpeed}");
                }
                else
                {
                    Debug.LogWarning($"BotUpgradeManager: DroidShooting component not found for Bullet Speed upgrade on {gameObject.name}. Skipping.");
                }
                break;
            case 5: // Turret Duration (Handled in ApplyTurretDurationUpgradeDelayed)
                break;
            case 6: // Damage Resist
                BotController botHealth = GetComponent<BotController>();
                if (botHealth != null)
                {
                    botHealth.damageReductionLevel = upgradeLevels[6];
                    Debug.Log($"BotUpgradeManager: Applied Damage Resist, level={upgradeLevels[6]}, damageReductionLevel={botHealth.damageReductionLevel} on {gameObject.name}");
                }
                else
                {
                    Debug.LogWarning($"BotUpgradeManager: BotController component not found for Damage Resist upgrade on {gameObject.name}. Skipping.");
                }
                break;
            case 7: // Teleport Fuel
                PhasingTeleportation teleport = GetComponent<PhasingTeleportation>();
                if (teleport != null)
                {
                    teleport.fuelCostPerTeleport = 10f - upgradeLevels[7]; // 10f at level 0, 9f at level 1, ..., 6f at level 4
                    Debug.Log($"BotUpgradeManager: Applied Teleport Fuel, level={upgradeLevels[7]}, fuelCostPerTeleport={teleport.fuelCostPerTeleport}");
                }
                else
                {
                    Debug.LogWarning($"BotUpgradeManager: PhasingTeleportation component not found for Teleport Fuel upgrade on {gameObject.name}. Skipping.");
                }
                break;
            case 8: // Bullet Damage
                shooting = GetComponentInChildren<DroidShooting>();
                if (shooting != null)
                {
                    shooting.bulletDamage = 5f + 5f * upgradeLevels[8];
                    Debug.Log($"BotUpgradeManager: Applied Bullet Damage, level={upgradeLevels[8]}, bulletDamage={shooting.bulletDamage}");
                }
                else
                {
                    Debug.LogWarning($"BotUpgradeManager: DroidShooting component not found for Bullet Damage upgrade on {gameObject.name}. Skipping.");
                }
                break;
        }
    }

    public void SyncBrightMatter(int amount)
    {
        if (isAddingBrightMatter) return;
        isAddingBrightMatter = true;
        Debug.Log($"BotUpgradeManager: SyncBrightMatter({amount}), Current={botController?.GetBrightMatter() ?? 0}, ViewID={photonView.ViewID}");
        botController.SyncBrightMatter(amount);
        isAddingBrightMatter = false;
    }

    public void AddBrightMatter(int amount)
    {
        if (amount == 0 || isAddingBrightMatter) return;
        isAddingBrightMatter = true;
        Debug.Log($"BotUpgradeManager: AddBrightMatter({amount}), Current={botController?.GetBrightMatter() ?? 0}, ViewID={photonView.ViewID}");
        botController.AddBrightMatter(amount);
        isAddingBrightMatter = false;
    }
}