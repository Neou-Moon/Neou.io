using UnityEngine;
using System.Collections;
using Photon.Pun;

public class PlayerMovement : MonoBehaviourPun
{
    [Header("Movement Settings")]
    public float moveSpeed = 10f;
    public float jetThrust = 10f;
    public float gravityScale = 2f;
    public float airControlFactor = 0.5f;
    public float airDrag = 1f;

    private Rigidbody2D rb;
    private Animator animator;
    private bool isFlying;
    private bool isFacingRight = true;
    private bool isNearOre;
    private OrePrefab currentOre;
    private bool isDepleting;
    private PlayerController playerController;

    void Start()
    {
        if (!photonView.IsMine)
        {
            enabled = false;
            return;
        }

        rb = GetComponent<Rigidbody2D>();
        animator = GetComponent<Animator>();
        playerController = GetComponent<PlayerController>();

        if (rb == null)
        {
            Debug.LogError("PlayerMovement: Rigidbody2D not found on GameObject.");
            enabled = false;
            return;
        }

        if (animator == null)
        {
            Debug.LogError("PlayerMovement: Animator not found on GameObject.");
            enabled = false;
            return;
        }

        if (playerController == null)
        {
            Debug.LogError("PlayerMovement: PlayerController not found on GameObject.");
            enabled = false;
            return;
        }

        rb.gravityScale = gravityScale;
    }

    public void Move(float horizontalInput, float verticalInput)
    {
        if (!photonView.IsMine || rb == null || animator == null)
            return;

        Vector2 movement = new Vector2(horizontalInput, verticalInput).normalized * moveSpeed;
        rb.linearVelocity = movement;

        animator.SetFloat("xVelocity", Mathf.Abs(rb.linearVelocity.x));
        animator.SetFloat("yVelocity", rb.linearVelocity.y);
    }

    public void FlipSprite(float horizontalInput)
    {
        if (!photonView.IsMine)
            return;

        if ((isFacingRight && horizontalInput < 0f) || (!isFacingRight && horizontalInput > 0f))
        {
            isFacingRight = !isFacingRight;
            Vector3 newScale = new Vector3(-transform.localScale.x, transform.localScale.y, transform.localScale.z);
            transform.localScale = newScale;
            photonView.RPC("SyncFlipSprite", RpcTarget.Others, newScale.x);
        }
    }

    [PunRPC]
    private void SyncFlipSprite(float scaleX)
    {
        transform.localScale = new Vector3(scaleX, transform.localScale.y, transform.localScale.z);
        isFacingRight = scaleX > 0;
        Debug.Log($"PlayerMovement: Synced flip, scaleX={scaleX}, isFacingRight={isFacingRight}");
    }

    void OnCollisionEnter2D(Collision2D collision)
    {
        if (!photonView.IsMine)
            return;

        if (collision.gameObject.CompareTag("Planet") || collision.gameObject.CompareTag("quarterPlanet") || collision.gameObject.CompareTag("halfPlanet"))
        {
            AlignWithSurface(collision.contacts[0].point);
        }
    }

    void AlignWithSurface(Vector2 contactPoint)
    {
        RaycastHit2D hit = Physics2D.Raycast(contactPoint, (transform.position - (Vector3)contactPoint).normalized, 2f, LayerMask.GetMask("Planet", "quarterPlanet", "halfPlanet"));

        if (hit.collider != null)
        {
            Vector2 normal = hit.normal;
            float angle = Mathf.Atan2(normal.y, normal.x) * Mathf.Rad2Deg - 90f;
            Quaternion newRotation = Quaternion.Euler(0, 0, angle);
            transform.rotation = newRotation;
            photonView.RPC("SyncRotation", RpcTarget.Others, angle);

            rb.linearVelocity = Vector2.zero;
            rb.angularVelocity = 0;
        }
    }

    [PunRPC]
    private void SyncRotation(float angle)
    {
        transform.rotation = Quaternion.Euler(0, 0, angle);
        Debug.Log($"PlayerMovement: Synced rotation to angle={angle}");
    }

    public void HandleOreMining()
    {
        if (!photonView.IsMine)
            return;

        if (isNearOre && !isDepleting && currentOre != null && playerController != null)
        {
            isDepleting = true;
            animator.SetBool("NearOre", true);
            currentOre.StartDepleting(playerController);
            Debug.Log($"PlayerMovement: Started mining {currentOre.gameObject.name}, NearOre animation set.");
            StartCoroutine(DepleteOre());
        }
    }

    private IEnumerator DepleteOre()
    {
        while (isNearOre && currentOre != null && currentOre.Health > 0)
        {
            yield return null;
        }

        if (currentOre != null && currentOre.Health <= 0)
        {
            isNearOre = false;
            isDepleting = false;
            animator.SetBool("NearOre", false);
        }
        else if (!isNearOre)
        {
            isDepleting = false;
            animator.SetBool("NearOre", false);
            if (currentOre != null)
            {
                currentOre.StopDepleting();
            }
        }
    }

    private void OnTriggerEnter2D(Collider2D collision)
    {
        if (!photonView.IsMine || !collision.CompareTag("Ore"))
            return;

        isNearOre = true;
        currentOre = collision.GetComponent<OrePrefab>();
        if (currentOre != null)
        {
            currentOre.OnOreDestroyed += HandleOreDestroyed;
            Debug.Log($"PlayerMovement: Entered ore trigger, currentOre={currentOre.gameObject.name}");
            HandleOreMining();
        }
    }

    private void OnTriggerExit2D(Collider2D collision)
    {
        if (!photonView.IsMine || !collision.CompareTag("Ore"))
            return;

        isNearOre = false;
        isDepleting = false;
        if (currentOre != null)
        {
            currentOre.OnOreDestroyed -= HandleOreDestroyed;
            currentOre.StopDepleting();
            Debug.Log($"PlayerMovement: Exited ore trigger, currentOre={currentOre.gameObject.name}, stopped depleting");
            currentOre = null;
        }
        animator.SetBool("NearOre", false);
    }

    private void HandleOreDestroyed()
    {
        if (!photonView.IsMine)
            return;

        if (currentOre != null)
        {
            currentOre.OnOreDestroyed -= HandleOreDestroyed;
            Debug.Log($"PlayerMovement: Handled ore destruction, currentOre={currentOre.gameObject.name}, unsubscribed OnOreDestroyed");
        }
        isNearOre = false;
        isDepleting = false;
        animator.SetBool("NearOre", false);
        Debug.Log("PlayerMovement: Ore destroyed, visual effects triggered.");
        currentOre = null;
    }
}