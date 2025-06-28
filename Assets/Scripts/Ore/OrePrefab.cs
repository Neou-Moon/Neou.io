using UnityEngine;
using Photon.Pun;
using TMPro;
using System.Collections;
using System.Linq;

public class OrePrefab : MonoBehaviourPun
{
    public float Health = 100f;
    public int brightMatterAmount = 100; // Changed from 50 to 100
    public float depleteTime = 10f; // Changed from 20f to 10f
    private bool isDepleting;
    private float depleteTimer;
    private bool hasAwardedBrightMatter = false;
    private bool isFirstDepletion = true;
    public System.Action OnOreDestroyed;
    public TextMeshProUGUI percentageText;
    private Canvas percentageCanvas;
    private float lastPercentageUpdateTime;
    private Coroutine flashCoroutine;
    private IPlayer lastPlayer;
    private static int awardRPCCount = 0;

    void Start()
    {
        Debug.Log($"OrePrefab: Start {gameObject.name}, ViewID={photonView.ViewID}, IsConnected={PhotonNetwork.IsConnected}, NetworkState={PhotonNetwork.NetworkClientState}");
        percentageCanvas = GetComponentInChildren<Canvas>();
        if (percentageCanvas == null)
        {
            Debug.LogError($"OrePrefab: No Canvas found on {gameObject.name}. Check PercentageCanvas setup.");
            Destroy(gameObject);
            return;
        }
        if (percentageText == null)
        {
            Debug.LogError($"OrePrefab: percentageText not assigned on {gameObject.name}. Assign TextMeshProUGUI in Inspector.");
            Destroy(gameObject);
            return;
        }
        percentageText.text = "100%";
        percentageText.color = Color.white;
        depleteTimer = depleteTime; // Now using the new 10 second value
        if (PhotonNetwork.IsConnected && photonView.ViewID == 0)
        {
            Debug.LogError($"OrePrefab: Invalid PhotonView ID on {gameObject.name}. Destroying object.");
            Destroy(gameObject);
            return;
        }
        Debug.Log($"OrePrefab: Initialized {gameObject.name}, Canvas={percentageCanvas.name}, Text={percentageText.text}, Position={transform.position}");
    }

    void Update()
    {
        if (isDepleting && (!PhotonNetwork.IsConnected || photonView.IsMine) && lastPlayer != null)
        {
            depleteTimer -= Time.deltaTime;
            float percentage = Mathf.Clamp01(depleteTimer / depleteTime) * 100f;
            UpdatePercentage(percentage);
            Debug.Log($"OrePrefab: Depleting {gameObject.name}, timer={depleteTimer:F2}s, percentage={percentage:F1}%, ViewID={photonView.ViewID}, LastPlayer={lastPlayer.NickName}");
            if (depleteTimer <= 0)
            {
                if (PhotonNetwork.IsConnected)
                {
                    photonView.RPC("DestroyOreRPC", RpcTarget.All);
                }
                else
                {
                    DestroyOre();
                }
            }
        }

        // Check if parent planet is destroyed
        if (transform.parent == null && PhotonNetwork.IsMasterClient)
        {
            Debug.Log($"OrePrefab: Parent of {gameObject.name} is null, destroying ore.");
            photonView.RPC("DestroyOreRPC", RpcTarget.All);
        }
    }

    private void UpdatePercentage(float percentage)
    {
        if (percentageText == null)
        {
            Debug.LogWarning($"OrePrefab: percentageText is null in UpdatePercentage for {gameObject.name}");
            return;
        }
        percentageText.text = $"{Mathf.CeilToInt(percentage)}%";
        if (PhotonNetwork.IsConnected && photonView.ViewID != 0 && Time.time - lastPercentageUpdateTime >= 0.1f)
        {
            photonView.RPC("UpdatePercentageRPC", RpcTarget.Others, percentage);
            lastPercentageUpdateTime = Time.time;
            Debug.Log($"OrePrefab: Sent UpdatePercentageRPC for {gameObject.name}, percentage={percentage:F1}%");
        }
    }

    [PunRPC]
    void UpdatePercentageRPC(float percentage)
    {
        if (percentageText != null)
        {
            percentageText.text = $"{Mathf.CeilToInt(percentage)}%";
        }
        else
        {
            Debug.LogWarning($"OrePrefab: percentageText is null in UpdatePercentageRPC for {gameObject.name}");
        }
    }

    public void StartDepleting(IPlayer player)
    {
        if (!isDepleting)
        {
            isDepleting = true;
            if (isFirstDepletion)
            {
                depleteTimer = depleteTime;
                isFirstDepletion = false;
            }
            lastPlayer = player;
            UpdatePercentage(Mathf.Clamp01(depleteTimer / depleteTime) * 100f);
            if (flashCoroutine != null)
            {
                StopCoroutine(flashCoroutine);
            }
            flashCoroutine = StartCoroutine(FlashTextColor());
            Debug.Log($"OrePrefab: Started depleting {gameObject.name}, timer={depleteTimer:F2}s, ViewID={photonView.ViewID}, Player={player.NickName}");
        }
        else
        {
            lastPlayer = player;
            Debug.Log($"OrePrefab: Player={player.NickName} took over depleting {gameObject.name}, timer={depleteTimer:F2}s");
        }
    }

    public void StopDepleting()
    {
        if (isDepleting)
        {
            isDepleting = false;
            lastPlayer = null;
            if (percentageText != null)
            {
                float percentage = Mathf.Clamp01(depleteTimer / depleteTime) * 100f;
                percentageText.text = $"{Mathf.CeilToInt(percentage)}%";
                percentageText.color = Color.white;
            }
            if (flashCoroutine != null)
            {
                StopCoroutine(flashCoroutine);
                flashCoroutine = null;
            }
            if (PhotonNetwork.IsConnected && photonView.ViewID != 0)
            {
                photonView.RPC("StopFlashRPC", RpcTarget.Others);
            }
            Debug.Log($"OrePrefab: Stopped depleting {gameObject.name}, timer={depleteTimer:F2}s remaining");
        }
    }

    public void TakeDamage(float damage)
    {
        Health -= damage;
        if (Health <= 0)
        {
            if (PhotonNetwork.IsConnected)
            {
                photonView.RPC("DestroyOreRPC", RpcTarget.All);
            }
            else
            {
                DestroyOre();
            }
        }
    }

    [PunRPC]
    void DestroyOreRPC()
    {
        DestroyOre();
    }

    private void DestroyOre()
    {
        OnOreDestroyed?.Invoke();
        if (!hasAwardedBrightMatter && PhotonNetwork.IsMasterClient && lastPlayer != null && depleteTimer <= 0)
        {
            hasAwardedBrightMatter = true;
            awardRPCCount++;
            Debug.Log($"OrePrefab: Master awarding {brightMatterAmount} BrightMatter to player {lastPlayer.NickName}, ActorNumber={lastPlayer.ActorNumber}, Ore ViewID={photonView.ViewID}, RPC count={awardRPCCount}");
            photonView.RPC("AwardBrightMatterRPC", RpcTarget.All, brightMatterAmount, photonView.ViewID, lastPlayer.ActorNumber);
        }
        else if (!hasAwardedBrightMatter && PhotonNetwork.IsMasterClient)
        {
            Debug.Log($"OrePrefab: No BrightMatter awarded for {gameObject.name}, depleteTimer={depleteTimer:F2}s, lastPlayer={(lastPlayer != null ? lastPlayer.NickName : "null")}, Ore ViewID={photonView.ViewID}");
        }
        if (photonView.IsMine)
        {
            PhotonNetwork.Destroy(gameObject);
        }
        Debug.Log($"OrePrefab: Destroyed {gameObject.name}, awarded={hasAwardedBrightMatter}, ViewID={photonView.ViewID}, IsMaster={PhotonNetwork.IsMasterClient}");
    }

    [PunRPC]
    void AwardBrightMatterRPC(int amount, int oreViewID, int actorNumber)
    {
        IPlayer[] players = FindObjectsByType<MonoBehaviour>(FindObjectsSortMode.None).OfType<IPlayer>().ToArray();
        IPlayer targetPlayer = null;
        foreach (var player in players)
        {
            PhotonView pv = (player as MonoBehaviour)?.GetComponent<PhotonView>();
            if (pv != null && player.ActorNumber == actorNumber)
            {
                targetPlayer = player;
                break;
            }
        }
        if (targetPlayer != null)
        {
            awardRPCCount++;
            Debug.Log($"OrePrefab: RPC awarding {amount} BrightMatter to player {targetPlayer.NickName}, ActorNumber={actorNumber}, Ore ViewID={oreViewID}, total RPC calls={awardRPCCount}");
            targetPlayer.AddBrightMatter(amount);
        }
        else
        {
            Debug.LogWarning($"OrePrefab: No player found with ActorNumber={actorNumber} for AwardBrightMatterRPC, amount={amount}, Ore ViewID={oreViewID}");
        }
    }

    [PunRPC]
    void SetOreParentAndOrientation(int planetViewID, Vector3 randomOffset)
    {
        PhotonView planetPhotonView = PhotonView.Find(planetViewID);
        if (planetPhotonView != null && planetPhotonView.gameObject != null)
        {
            transform.SetParent(planetPhotonView.transform);
            transform.position = planetPhotonView.transform.position + randomOffset;
            transform.up = transform.position - planetPhotonView.transform.position;
            Debug.Log($"OrePrefab: Set parent for {gameObject.name} to {planetPhotonView.gameObject.name}, ViewID={photonView.ViewID}");
        }
        else
        {
            Debug.LogWarning($"OrePrefab: Could not find planet with ViewID {planetViewID} for {gameObject.name}, destroying ore.");
            if (PhotonNetwork.IsMasterClient)
            {
                photonView.RPC("DestroyOreRPC", RpcTarget.All);
            }
        }
    }

    private IEnumerator FlashTextColor()
    {
        float flashInterval = 0.5f;
        while (isDepleting)
        {
            if (percentageText != null)
            {
                percentageText.color = Color.red;
                if (PhotonNetwork.IsConnected && photonView.IsMine)
                {
                    photonView.RPC("SetTextColorRPC", RpcTarget.Others, 1f, 0f, 0f, 1f);
                }
                yield return new WaitForSeconds(flashInterval);

                if (percentageText != null)
                {
                    percentageText.color = Color.white;
                    if (PhotonNetwork.IsConnected && photonView.IsMine)
                    {
                        photonView.RPC("SetTextColorRPC", RpcTarget.Others, 1f, 1f, 1f, 1f);
                    }
                }
                yield return new WaitForSeconds(flashInterval);
            }
            else
            {
                Debug.LogWarning($"OrePrefab: percentageText is null in FlashTextColor for {gameObject.name}");
                yield break;
            }
        }
    }

    [PunRPC]
    void SetTextColorRPC(float r, float g, float b, float a)
    {
        if (percentageText != null)
        {
            percentageText.color = new Color(r, g, b, a);
        }
        else
        {
            Debug.LogWarning($"OrePrefab: percentageText is null in SetTextColorRPC for {gameObject.name}");
        }
    }

    [PunRPC]
    void StopFlashRPC()
    {
        if (flashCoroutine != null)
        {
            StopCoroutine(flashCoroutine);
            flashCoroutine = null;
        }
        if (percentageText != null)
        {
            percentageText.color = Color.white;
        }
    }

    public void Interact(IPlayer player)
    {
        StartDepleting(player);
        Debug.Log($"OrePrefab: Interact called by player {player.NickName}, starting depletion on {gameObject.name}");
    }
}