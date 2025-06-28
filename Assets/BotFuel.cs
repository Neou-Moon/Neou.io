using UnityEngine;
using Photon.Pun;

[RequireComponent(typeof(PhotonView))]
public class BotFuel : MonoBehaviourPun
{
    [SerializeField] private float maxFuel = 100f;

    private float currentFuel;
    private bool isRefueling;
    private BotUpgradeManager upgradeManager;

    public float CurrentFuel => currentFuel;

    void Start()
    {
        if (!photonView.IsMine)
        {
            enabled = false;
            Debug.Log($"BotFuel: Disabled for {gameObject.name}, photonView.IsMine=false.");
            return;
        }

        upgradeManager = GetComponent<BotUpgradeManager>();
        if (upgradeManager == null)
        {
            Debug.LogError("BotFuel: BotUpgradeManager not found on Bot.");
        }

        currentFuel = maxFuel;
        Debug.Log($"BotFuel: Initialized with currentFuel={currentFuel}, maxFuel={maxFuel}");
    }

    void Update()
    {
        if (!photonView.IsMine) return;

        if (isRefueling)
        {
            float refuelPercentage = GetRefuelRate();
            float refuelAmount = maxFuel * refuelPercentage * Time.deltaTime;
            RefuelFuel(refuelAmount);
        }
    }

    private float GetRefuelRate()
    {
        if (upgradeManager == null)
        {
            Debug.LogWarning("BotFuel: BotUpgradeManager is null, using default refuel rate of 10% per second.");
            return 0.10f;
        }

        int fuelAbsorptionLevel = upgradeManager.UpgradeLevels[3]; // Fuel Absorption is index 3
        switch (fuelAbsorptionLevel)
        {
            case 0: return 0.10f; // 10% per second
            case 1: return 0.15f; // 15% per second
            case 2: return 0.20f; // 20% per second
            case 3: return 0.25f; // 25% per second
            case 4: return 0.30f; // 30% per second
            default:
                Debug.LogWarning($"BotFuel: Invalid Fuel Absorption level {fuelAbsorptionLevel}, using default 10% per second.");
                return 0.10f;
        }
    }

    public void DrainFuel(float amount)
    {
        currentFuel = Mathf.Max(0, currentFuel - amount);
        if (photonView.IsMine && PhotonNetwork.IsConnectedAndReady)
        {
            photonView.RPC("SyncFuel", RpcTarget.Others, currentFuel);
        }
    }

    public void RefuelFuel(float amount)
    {
        currentFuel = Mathf.Min(maxFuel, currentFuel + amount);
        if (photonView.IsMine && PhotonNetwork.IsConnectedAndReady)
        {
            photonView.RPC("SyncFuel", RpcTarget.Others, currentFuel);
        }
    }

    public void SetFuel(float value)
    {
        currentFuel = Mathf.Clamp(value, 0, maxFuel);
        if (photonView.IsMine && PhotonNetwork.IsConnectedAndReady)
        {
            photonView.RPC("SyncFuel", RpcTarget.Others, currentFuel);
        }
    }

    [PunRPC]
    private void SyncFuel(float fuel)
    {
        currentFuel = fuel;
        Debug.Log($"BotFuel: Synced fuel for {gameObject.name}, currentFuel={currentFuel}");
    }

    public bool CanAffordFuel(float cost) => currentFuel >= cost;

    void OnTriggerEnter2D(Collider2D other)
    {
        if (other.CompareTag("Planet"))
        {
            isRefueling = true;
            Debug.Log($"BotFuel: Started refueling on Planet trigger for {gameObject.name}.");
        }
    }

    void OnTriggerExit2D(Collider2D other)
    {
        if (other.CompareTag("Planet"))
        {
            isRefueling = false;
            Debug.Log($"BotFuel: Stopped refueling on Planet trigger exit for {gameObject.name}.");
        }
    }
}