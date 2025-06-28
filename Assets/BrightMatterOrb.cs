using UnityEngine;
using Photon.Pun;
using System.Linq; // Added for Concat method

public class BrightMatterOrb : MonoBehaviourPun
{
    private int brightMatterAmount;
    private float despawnTime = 30f;
    private bool hasBeenCollected = false;
    private Vector3 initialPosition;
    private float hoverAmplitude = 0.5f; // Hover height
    private float hoverFrequency = 1f; // Hover speed
    private float attractionSpeed = 5f; // Speed toward player/bot
    private float attractionDistance = 30f; // Distance to start attraction
    private GameObject target; // Player or bot to move toward
    private int teamID = -1; // Add teamID field
    public int TeamID => teamID;

    void Start()
    {
        if (photonView.IsMine)
        {
            Invoke(nameof(DestroyOrb), despawnTime);
        }
        initialPosition = transform.position; // Store initial position for hover
    }

    public void SetBrightMatter(int amount)
    {
        brightMatterAmount = amount;
    }

    public int GetAmount()
    {
        return brightMatterAmount;
    }

    void Update()
    {
        if (!photonView.IsMine) return;

        // Hover effect
        float hoverOffset = Mathf.Sin(Time.time * hoverFrequency) * hoverAmplitude;
        transform.position = new Vector3(initialPosition.x, initialPosition.y + hoverOffset, initialPosition.z);

        // Find nearest player or bot within 30 units
        if (!hasBeenCollected)
        {
            GameObject[] players = GameObject.FindGameObjectsWithTag("Player");
            GameObject[] bots = GameObject.FindGameObjectsWithTag("Bot");
            GameObject nearest = null;
            float minDistance = attractionDistance;

            foreach (GameObject entity in players.Concat(bots))
            {
                if (!entity.activeInHierarchy) continue;
                float distance = Vector3.Distance(transform.position, entity.transform.position);
                if (distance < minDistance)
                {
                    minDistance = distance;
                    nearest = entity;
                }
            }

            target = nearest;

            // Move toward target if within range
            if (target != null)
            {
                Vector3 direction = (target.transform.position - transform.position).normalized;
                transform.position += direction * attractionSpeed * Time.deltaTime;
                CustomLogger.Log($"BrightMatterOrb: Moving toward {target.name}, distance={minDistance:F2}, ViewID={photonView.ViewID}");
            }
        }
    }

    void OnTriggerEnter2D(Collider2D other)
    {
        if (!photonView.IsMine || hasBeenCollected) return;

        if (other.CompareTag("Player"))
        {
            PlayerController player = other.GetComponent<PlayerController>();
            if (player != null && player.photonView.IsMine)
            {
                hasBeenCollected = true;
                CustomLogger.Log($"BrightMatterOrb: Player ViewID={player.photonView.ViewID} collecting {brightMatterAmount} BrightMatter, Orb ViewID={photonView.ViewID}");
                player.AddBrightMatter(brightMatterAmount);
                photonView.RPC("DestroyOrbRPC", RpcTarget.All);
            }
        }
        else if (other.CompareTag("Bot"))
        {
            BotController bot = other.GetComponent<BotController>();
            if (bot != null)
            {
                hasBeenCollected = true;
                CustomLogger.Log($"BrightMatterOrb: Bot ViewID={bot.photonView.ViewID} collecting {brightMatterAmount} BrightMatter, Orb ViewID={photonView.ViewID}");
                bot.AddBrightMatter(brightMatterAmount);
                photonView.RPC("DestroyOrbRPC", RpcTarget.All);
            }
        }
    }
    [PunRPC]
    public void SetTeamID(int newTeamID)
    {
        teamID = newTeamID;
        CustomLogger.Log($"BrightMatterOrb: Set TeamID={teamID}, ViewID={photonView.ViewID}");
    }
    [PunRPC]
    void DestroyOrbRPC()
    {
        if (photonView.IsMine)
        {
            PhotonNetwork.Destroy(gameObject);
        }
    }

    void DestroyOrb()
    {
        if (photonView.IsMine)
        {
            PhotonNetwork.Destroy(gameObject);
        }
    }

    public void Interact(PlayerController player)
    {
        if (!photonView.IsMine || hasBeenCollected || player == null || !player.photonView.IsMine) return;

        hasBeenCollected = true;
        CustomLogger.Log($"BrightMatterOrb: Interact called by player ViewID={player.photonView.ViewID}, collecting {brightMatterAmount} BrightMatter, Orb ViewID={photonView.ViewID}");
        player.AddBrightMatter(brightMatterAmount);
        photonView.RPC("DestroyOrbRPC", RpcTarget.All);
    }
}