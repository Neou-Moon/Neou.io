using UnityEngine;

public class Attractable : MonoBehaviour
{
    [SerializeField] private bool rotateToCenter = true;
    [SerializeField] private Attractor currentAttractor;
    [SerializeField] private float gravityStrength = 100;
    [SerializeField] private float searchRadius = 10f;

    private Transform m_transform;
    private Collider2D m_collider;
    private Rigidbody2D m_rigidbody;

    private void Start()
    {
        m_transform = transform;
        m_collider = GetComponent<Collider2D>();
        m_rigidbody = GetComponent<Rigidbody2D>();
    }

    private void Update()
    {
        if (currentAttractor == null || !currentAttractor.gameObject.activeInHierarchy)
        {
            currentAttractor = FindClosestAttractor();
        }
        if (currentAttractor != null)
        {
            if (!currentAttractor.AttractedObjects.Contains(m_collider))
            {
                currentAttractor = FindClosestAttractor();
                if (currentAttractor == null)
                {
                    m_rigidbody.gravityScale = 1;
                    return;
                }
            }
            if (rotateToCenter)
            {
                RotateToCenter();
            }
            m_rigidbody.gravityScale = 0;
        }
        else
        {
            m_rigidbody.gravityScale = 1;
        }
    }

    public void Attract(Attractor attractorObj)
    {
        Vector2 attractionDir = ((Vector2)attractorObj.attractorTransform.position - m_rigidbody.position).normalized;
        m_rigidbody.AddForce(attractionDir * -attractorObj.gravity * gravityStrength * Time.fixedDeltaTime);
        currentAttractor ??= attractorObj;
    }

    private void RotateToCenter()
    {
        if (currentAttractor == null) return;
        Vector2 distanceVector = (Vector2)currentAttractor.attractorTransform.position - (Vector2)m_transform.position;
        float angle = Mathf.Atan2(distanceVector.y, distanceVector.x) * Mathf.Rad2Deg;
        m_transform.rotation = Quaternion.AngleAxis(angle + 90, Vector3.forward);
    }

    private Attractor FindClosestAttractor()
    {
        Collider2D[] colliders = Physics2D.OverlapCircleAll(m_transform.position, searchRadius, LayerMask.GetMask("Planet", "halfPlanet", "quarterPlanet"));
        Attractor closestAttractor = null;
        float minDistance = float.MaxValue;
        foreach (Collider2D collider in colliders)
        {
            if (collider.CompareTag("Planet") || collider.CompareTag("halfPlanet") || collider.CompareTag("quarterPlanet"))
            {
                Attractor attractor = collider.GetComponent<Attractor>();
                if (attractor != null && attractor.gameObject.activeInHierarchy)
                {
                    float distance = Vector2.Distance(m_transform.position, attractor.attractorTransform.position);
                    if (distance < minDistance)
                    {
                        minDistance = distance;
                        closestAttractor = attractor;
                    }
                }
            }
        }
        return closestAttractor;
    }
}