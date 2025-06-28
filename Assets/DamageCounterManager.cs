using UnityEngine;
using Photon.Pun;

public class DamageCounterManager : MonoBehaviourPunCallbacks
{
    public static DamageCounterManager Instance { get; private set; }
    private GameObject damageCounterPrefab;

    void Awake()
    {
        if (Instance == null)
        {
            Instance = this;
            DontDestroyOnLoad(gameObject);
        }
        else
        {
            Destroy(gameObject);
            return;
        }

        damageCounterPrefab = Resources.Load<GameObject>("Prefabs/DamageCounter");
        if (damageCounterPrefab == null)
        {
            Debug.LogError("DamageCounterManager: Failed to load DamageCounter prefab from Resources/Prefabs/DamageCounter");
        }
    }

    public void SpawnDamageCounter(int damage, Transform targetTransform, PhotonView ownerView)
    {
        if (damageCounterPrefab == null)
        {
            Debug.LogError("DamageCounterManager: DamageCounter prefab not loaded");
            return;
        }

        if (!PhotonNetwork.IsConnected)
        {
            // Offline mode: Spawn locally for testing
            GameObject counter = Instantiate(damageCounterPrefab, targetTransform.position, Quaternion.identity);
            counter.transform.SetParent(targetTransform, true);
            DamageCounter damageCounter = counter.GetComponent<DamageCounter>();
            if (damageCounter != null)
            {
                damageCounter.Initialize(damage, targetTransform, -1); // No ownerViewID in offline mode
                Debug.Log($"DamageCounterManager: Spawned local damage counter (offline mode), damage={damage}, parent={targetTransform.name}");
            }
            else
            {
                Debug.LogError("DamageCounterManager: Spawned DamageCounter missing DamageCounter component");
                Destroy(counter);
            }
            return;
        }

        if (ownerView == null || ownerView.gameObject == null)
        {
            Debug.LogError("DamageCounterManager: Invalid ownerView or GameObject");
            return;
        }

        // Spawn for all clients and parent to the target transform
        GameObject localCounter = PhotonNetwork.Instantiate("Prefabs/DamageCounter", targetTransform.position, Quaternion.identity);
        localCounter.transform.SetParent(targetTransform, true);
        DamageCounter localDamageCounter = localCounter.GetComponent<DamageCounter>();
        if (localDamageCounter != null)
        {
            // Call RPC to initialize damage counter with ownerViewID
            localCounter.GetComponent<PhotonView>().RPC("InitializeRPC", RpcTarget.All, damage, ownerView.ViewID);
            Debug.Log($"DamageCounterManager: Spawned damage counter for ViewID={ownerView.ViewID}, damage={damage}, parent={targetTransform.name}, tag={targetTransform.tag}");
        }
        else
        {
            Debug.LogError("DamageCounterManager: Spawned DamageCounter missing DamageCounter component");
            PhotonNetwork.Destroy(localCounter);
        }
    }
}