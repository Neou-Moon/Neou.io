using UnityEngine;
using System.Collections.Generic;

public class Blast : MonoBehaviour
{
    public float speed = 85f; // Blasts move at 85 units/second
    public int damage = 10; // Damage to Player
    public float lifetime = 5f;
    public int deflectedDamage = 30; // Damage to Enemy when deflected

    private Rigidbody2D rb;
    private Vector3 lastPosition; // For speed debug
    private Collider2D blastCollider;
    private HashSet<Collider2D> ignoredColliders = new HashSet<Collider2D>();
    private bool isDeflected;
    private float deflectionTime;

    private void Start()
    {
        rb = GetComponent<Rigidbody2D>();
        blastCollider = GetComponent<Collider2D>();
        if (rb == null)
        {
            Debug.LogError($"Blast: Missing Rigidbody2D on {gameObject.name}, destroying.");
            Destroy(gameObject);
            return;
        }
        if (blastCollider == null)
        {
            Debug.LogError($"Blast: Missing Collider2D on {gameObject.name}, destroying.");
            Destroy(gameObject);
            return;
        }
        if (!blastCollider.enabled)
        {
            Debug.LogWarning($"Blast: Collider2D on {gameObject.name} is disabled, enabling it.");
            blastCollider.enabled = true;
        }

        // Rigidbody2D settings
        rb.mass = 1f;
        rb.linearDamping = 0f;
        rb.angularDamping = 0f;
        rb.gravityScale = 0f;
        rb.bodyType = RigidbodyType2D.Dynamic;
        rb.constraints = RigidbodyConstraints2D.None;
        rb.simulated = true;

        // Set layer
        int blastLayer = LayerMask.NameToLayer("Blast");
        if (blastLayer >= 0 && blastLayer <= 31)
        {
            gameObject.layer = blastLayer;
            Debug.Log($"Blast: Set layer to 'Blast' (index {blastLayer}) for {gameObject.name}");
        }
        else
        {
            gameObject.layer = 0;
            Debug.LogWarning($"Blast: 'Blast' layer not found for {gameObject.name}. Using 'Default' layer.");
        }

        // Ignore collisions
        IgnoreCollisionsWithTag("Droid");
        IgnoreCollisionsWithTag("SpaceShip");
        IgnoreCollisionsWithTag("Laser");
        IgnoreCollisionsWithTag("Enemy"); // Ignore enemies unless deflected

        // Set velocity
        rb.linearVelocity = transform.right * speed;
        lastPosition = transform.position;

        Destroy(gameObject, lifetime);

        Debug.Log($"Blast: Fired {gameObject.name}, speed={speed}, velocity={rb.linearVelocity}, magnitude={rb.linearVelocity.magnitude:F2}, position={transform.position}, rotation={transform.rotation.eulerAngles}, localScale={transform.localScale}, parent={(transform.parent != null ? transform.parent.name : "none")}, layer={LayerMask.LayerToName(gameObject.layer)}, hasRigidbody2D={(rb != null)}, hasCollider2D={(blastCollider != null)}");
    }

    private void IgnoreCollisionsWithTag(string tag)
    {
        GameObject[] objects = GameObject.FindGameObjectsWithTag(tag);
        if (objects.Length == 0)
        {
            return; // Silently skip if no objects found
        }

        foreach (GameObject obj in objects)
        {
            Collider2D otherCollider = obj.GetComponent<Collider2D>();
            if (otherCollider != null && !ignoredColliders.Contains(otherCollider))
            {
                Physics2D.IgnoreCollision(blastCollider, otherCollider, true);
                ignoredColliders.Add(otherCollider);
                Debug.Log($"Blast: {gameObject.name} ignoring collision with {obj.name} (Tag: {tag}, Layer: {LayerMask.LayerToName(obj.layer)})");
            }
            else if (otherCollider == null)
            {
                Debug.LogWarning($"Blast: Object {obj.name} with tag '{tag}' has no Collider2D for {gameObject.name}.");
            }
        }
    }

    private void FixedUpdate()
    {
        if (rb != null)
        {
            float actualSpeed = Vector3.Distance(transform.position, lastPosition) / Time.fixedDeltaTime;
            Debug.Log($"Blast: {gameObject.name} moving, speed={speed}, actualSpeed={actualSpeed:F2}, position={transform.position}, velocity={rb.linearVelocity}, isDeflected={isDeflected}");
            lastPosition = transform.position;
        }
    }

    private void OnCollisionEnter2D(Collision2D collision)
    {
        Debug.Log($"Blast: Collided with {collision.gameObject.name} (Tag: {collision.gameObject.tag}, Layer: {LayerMask.LayerToName(collision.gameObject.layer)}), isDeflected={isDeflected}, timeSinceDeflection={(isDeflected ? Time.time - deflectionTime : 0):F2}s");

        if (collision.gameObject.CompareTag("Player"))
        {
            PlayerHealth health = collision.gameObject.GetComponent<PlayerHealth>();
            if (health != null)
            {
                health.TakeDamage(damage, false, -1, PlayerHealth.DeathCause.EnemyBlast);
                Debug.Log($"Blast: Dealt {damage} damage to {collision.gameObject.name}, deathCause=EnemyBlast.");
            }
            Destroy(gameObject);
        }
        else if (collision.gameObject.CompareTag("Enemy") && isDeflected)
        {
            EnemyHealth enemyHealth = collision.gameObject.GetComponent<EnemyHealth>();
            if (enemyHealth != null)
            {
                float timeSinceDeflection = Time.time - deflectionTime;
                if (timeSinceDeflection <= 3f)
                {
                    enemyHealth.TakeDamage(enemyHealth.health);
                    Debug.Log($"Blast: Instantly destroyed {collision.gameObject.name} within {timeSinceDeflection:F2}s of deflection.");
                }
                else
                {
                    enemyHealth.TakeDamage(deflectedDamage);
                    Debug.Log($"Blast: Dealt {deflectedDamage} damage to {collision.gameObject.name} after {timeSinceDeflection:F2}s.");
                }
            }
            else
            {
                Debug.LogWarning($"Blast: Enemy {collision.gameObject.name} lacks EnemyHealth component.");
            }
            Destroy(gameObject);
        }
        else if (collision.gameObject.CompareTag("Planet") || collision.gameObject.CompareTag("quarterPlanet") || collision.gameObject.CompareTag("halfPlanet"))
        {
            Debug.Log($"Blast: Collided with {collision.gameObject.name} (Tag: {collision.gameObject.tag}), destroying blast.");
            Destroy(gameObject);
        }
    }

    public void Deflect(Vector3 shieldPosition)
    {
        if (rb == null) return;

        isDeflected = true;
        deflectionTime = Time.time;

        Vector2 deflectDirection = (transform.position - shieldPosition).normalized;
        rb.linearVelocity = deflectDirection * speed;
        float angle = Mathf.Atan2(deflectDirection.y, deflectDirection.x) * Mathf.Rad2Deg;
        transform.rotation = Quaternion.Euler(0, 0, angle);

        // Re-enable enemy collisions
        GameObject[] enemies = GameObject.FindGameObjectsWithTag("Enemy");
        foreach (GameObject enemy in enemies)
        {
            Collider2D enemyCollider = enemy.GetComponent<Collider2D>();
            if (enemyCollider != null && ignoredColliders.Contains(enemyCollider))
            {
                Physics2D.IgnoreCollision(blastCollider, enemyCollider, false);
                ignoredColliders.Remove(enemyCollider);
                Debug.Log($"Blast: Re-enabled collision with {enemy.name} for deflected {gameObject.name}");
            }
        }

        Debug.Log($"Blast: Deflected {gameObject.name}, new velocity={rb.linearVelocity}, direction={deflectDirection}, rotation={transform.rotation}, shieldPosition={shieldPosition}, deflectionTime={deflectionTime}");
    }
}