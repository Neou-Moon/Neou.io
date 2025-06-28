using UnityEngine;
using TMPro;
using Photon.Pun;
using System.Collections;

public class DamageCounter : MonoBehaviourPun
{
    // Inspector References
    [Header("Component References")]
    [SerializeField] private TextMeshProUGUI damageText;
    [SerializeField] private RectTransform rectTransform;
    [SerializeField] private Canvas canvas;

    // Animation Settings
    [Header("Animation Settings")]
    [SerializeField] private float moveSpeed = 3f;
    [SerializeField] private float fadeDuration = 1f;
    [SerializeField] private float lifetime = 1.2f;
    [SerializeField] private Vector3 positionOffset = new Vector3(-86f, 0.2f, 0); // Adjusted: lower (y: 0.5 -> 0.3), more left (x: -0.6 -> -0.8)

    private Camera mainCamera;
    private Vector3 startPosition;
    private Color whiteColor = Color.white;
    private Color redColor = new Color(1f, 0.3f, 0.3f);
    private Transform targetTransform;

    void Awake()
    {
        mainCamera = Camera.main;

        if (canvas == null) canvas = GetComponentInChildren<Canvas>(true);
        if (damageText == null) damageText = GetComponentInChildren<TextMeshProUGUI>(true);
        if (rectTransform == null && damageText != null) rectTransform = damageText.rectTransform;

        if (canvas == null || damageText == null || rectTransform == null)
        {
            Debug.LogError("Missing required components!", gameObject);
            if (photonView.IsMine)
            {
                PhotonNetwork.Destroy(gameObject);
            }
            return;
        }

        canvas.renderMode = RenderMode.WorldSpace;
        canvas.worldCamera = mainCamera;
        rectTransform.sizeDelta = new Vector2(200, 100);
        transform.localScale = Vector3.one * 0.02f;
    }

    void Update()
    {
        if (mainCamera != null)
        {
            transform.LookAt(transform.position + mainCamera.transform.forward);
        }
    }

    public void Initialize(int damage, Transform target, int ownerViewID)
    {
        if (damageText == null) return;

        targetTransform = target;
        // Calculate position with offset relative to target's orientation
        Vector3 targetRight = target.right;
        startPosition = target.position +
                       (targetRight * positionOffset.x) +
                       (Vector3.up * positionOffset.y);

        transform.position = startPosition;
        damageText.text = damage.ToString();

        // Hide damage text for the owner
        if (PhotonNetwork.IsConnected && ownerViewID != -1)
        {
            PhotonView localPlayerView = PhotonNetwork.LocalPlayer.TagObject is GameObject playerObj
                ? playerObj.GetComponent<PhotonView>()
                : null;
            if (localPlayerView != null && localPlayerView.ViewID == ownerViewID)
            {
                damageText.enabled = false;
                Debug.Log($"DamageCounter: Hid damage text for local player (ViewID={ownerViewID})");
            }
            else
            {
                damageText.enabled = true;
                Debug.Log($"DamageCounter: Showing damage text for non-owner (local ViewID={(localPlayerView != null ? localPlayerView.ViewID.ToString() : "null")}, ownerViewID={ownerViewID})");
            }
        }
        else
        {
            damageText.enabled = true; // Show in offline mode or if no ownerViewID
        }

        StartCoroutine(AnimateAndDestroy());
    }

    [PunRPC]
    public void InitializeRPC(int damage, int ownerViewID)
    {
        // Ensure the targetTransform is still valid
        if (transform.parent != null)
        {
            Initialize(damage, transform.parent, ownerViewID);
        }
        else
        {
            Debug.LogWarning("DamageCounter: Parent transform is null, destroying counter");
            if (photonView.IsMine)
            {
                PhotonNetwork.Destroy(gameObject);
            }
        }
    }

    private IEnumerator AnimateAndDestroy()
    {
        float elapsed = 0f;
        float flashInterval = 0.1f;
        float nextFlashTime = 0f;
        bool isRed = false;

        while (elapsed < lifetime)
        {
            elapsed += Time.deltaTime;
            float t = elapsed / fadeDuration;

            // Move upward relative to target if still parented
            if (targetTransform != null)
            {
                Vector3 targetRight = targetTransform.right;
                startPosition = targetTransform.position +
                               (targetRight * positionOffset.x) +
                               (Vector3.up * positionOffset.y);
            }
            transform.position = startPosition + Vector3.up * (moveSpeed * elapsed);

            // Flash between red and white
            if (elapsed >= nextFlashTime && damageText.enabled)
            {
                isRed = !isRed;
                damageText.color = isRed ? redColor : whiteColor;
                nextFlashTime = elapsed + flashInterval;
            }

            // Fade out
            if (damageText.enabled)
            {
                damageText.color = new Color(
                    damageText.color.r,
                    damageText.color.g,
                    damageText.color.b,
                    Mathf.Lerp(1f, 0f, Mathf.Clamp01(t))
                );
            }

            yield return null;
        }

        if (photonView.IsMine)
        {
            PhotonNetwork.Destroy(gameObject);
        }
    }
}