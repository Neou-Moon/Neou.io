using UnityEngine;
using UnityEngine.UI;
using Photon.Pun;

[RequireComponent(typeof(PhotonView))]
public class PlayerFuel : MonoBehaviourPun
{
    [SerializeField] private float maxFuel = 100f;
    [SerializeField] private Slider fuelBar;

    private float currentFuel;
    private bool isRefueling;
    private Canvas playerCanvas;
    private UpgradeManager upgradeManager;

    public float CurrentFuel => currentFuel;

    void Start()
    {
        if (!photonView.IsMine)
        {
            enabled = false;
            Debug.Log($"PlayerFuel: Disabled for {gameObject.name}, photonView.IsMine=false.");
            return;
        }

        playerCanvas = GetComponentInChildren<Canvas>();
        if (playerCanvas == null || playerCanvas.name != "Player Canvas")
        {
            Debug.LogError("PlayerFuel: Player Canvas not found on Player.");
            return;
        }

        if (fuelBar == null)
        {
            Slider[] sliders = playerCanvas.GetComponentsInChildren<Slider>(true);
            foreach (var slider in sliders)
            {
                if (slider.name.Contains("FuelBar"))
                {
                    fuelBar = slider;
                    Debug.Log($"PlayerFuel: Found FuelBar ({slider.name}) in Player Canvas.");
                    break;
                }
            }
            if (fuelBar == null)
            {
                Debug.LogError("PlayerFuel: Could not find FuelBar in Player Canvas. Ensure a Slider named 'FuelBar' exists in Player Canvas.");
            }
        }

        if (fuelBar != null)
        {
            fuelBar.maxValue = 1f;
            Debug.Log("PlayerFuel: Set fuelBar.maxValue to 1.");
        }

        upgradeManager = GetComponent<UpgradeManager>();
        if (upgradeManager == null)
        {
            Debug.LogError("PlayerFuel: UpgradeManager not found on Player.");
        }

        currentFuel = 100f;
        Debug.Log($"PlayerFuel: Hardcoded initial fuel, maxFuel={maxFuel}, currentFuel={currentFuel}, GameObject={gameObject.name}");
        UpdateFuelUI();
        Debug.Log($"PlayerFuel: Initialized with currentFuel={currentFuel}, maxFuel={maxFuel}, fuelBar={(fuelBar != null ? "assigned" : "null")}");
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
            Debug.LogWarning("PlayerFuel: UpgradeManager is null, using default refuel rate of 10% per second.");
            return 0.10f;
        }

        int fuelAbsorptionLevel = upgradeManager.UpgradeLevels[3];
        switch (fuelAbsorptionLevel)
        {
            case 0: return 0.10f;
            case 1: return 0.15f;
            case 2: return 0.20f;
            case 3: return 0.25f;
            case 4: return 0.30f;
            default:
                Debug.LogWarning($"PlayerFuel: Invalid Fuel Absorption level {fuelAbsorptionLevel}, using default 10% per second.");
                return 0.10f;
        }
    }

    public void DrainFuel(float amount)
    {
        if (amount < 0)
        {
            Debug.LogWarning($"PlayerFuel: Attempted to drain negative fuel ({amount}) for {gameObject.name}. Ignoring.");
            return;
        }
        float previousFuel = currentFuel;
        currentFuel = Mathf.Max(0, currentFuel - amount);
        UpdateFuelUI();
        Debug.Log($"PlayerFuel: Drained {amount} fuel for {gameObject.name}, previous={previousFuel}, current={currentFuel}, Caller={new System.Diagnostics.StackTrace().ToString()}");
    }

    public void RefuelFuel(float amount)
    {
        if (amount < 0)
        {
            Debug.LogWarning($"PlayerFuel: Attempted to refuel negative amount ({amount}) for {gameObject.name}. Ignoring.");
            return;
        }
        float previousFuel = currentFuel;
        currentFuel = Mathf.Min(maxFuel, currentFuel + amount);
        UpdateFuelUI();
        Debug.Log($"PlayerFuel: Refueled {amount} fuel for {gameObject.name}, previous={previousFuel}, current={currentFuel}");
    }

    public void SetFuel(float value)
    {
        float previousFuel = currentFuel;
        currentFuel = Mathf.Clamp(value, 0, maxFuel);
        UpdateFuelUI();
        Debug.Log($"PlayerFuel: Set fuel for {gameObject.name}, previous={previousFuel}, new={currentFuel}");
    }

    public void UpdateFuelUI()
    {
        if (fuelBar != null)
        {
            float fuelRatio = currentFuel / maxFuel;
            fuelBar.value = fuelRatio;
            Debug.Log($"PlayerFuel: Updated fuel UI for {gameObject.name}, fuelRatio={fuelRatio}, fuelBar.value={fuelBar.value}, currentFuel={currentFuel}, maxFuel={maxFuel}");
        }
        else
        {
            Debug.LogWarning($"PlayerFuel: fuelBar is null for {gameObject.name}, cannot update UI.");
        }
    }

    public bool CanAffordFuel(float cost)
    {
        bool canAfford = currentFuel >= cost;
        Debug.Log($"PlayerFuel: CanAffordFuel check for {gameObject.name}, cost={cost}, currentFuel={currentFuel}, canAfford={canAfford}");
        return canAfford;
    }

    void OnTriggerEnter2D(Collider2D other)
    {
        if (other.CompareTag("Planet"))
        {
            isRefueling = true;
            Debug.Log("PlayerFuel: Started refueling on Planet trigger.");
        }
    }

    void OnTriggerExit2D(Collider2D other)
    {
        if (other.CompareTag("Planet"))
        {
            isRefueling = false;
            Debug.Log("PlayerFuel: Stopped refueling on Planet trigger exit.");
        }
    }
}