using System.Collections.Generic;
using UnityEngine;

public class Attractor : MonoBehaviour
{
    public LayerMask AttractionLayer;
    public float gravity = 10;
    [SerializeField] private float Radius = 10;
    public List<Collider2D> AttractedObjects = new List<Collider2D>();
    [HideInInspector] public Transform attractorTransform;

    private void Awake() => attractorTransform = transform;

    private void Update() => SetAttractedObjects();

    private void FixedUpdate() => AttractObjects();

    private void SetAttractedObjects() =>
        AttractedObjects = new List<Collider2D>(Physics2D.OverlapCircleAll(attractorTransform.position, Radius, AttractionLayer));

    private void AttractObjects()
    {
        for (int i = 0; i < AttractedObjects.Count; i++)
        {
            if (AttractedObjects[i] == null) continue;
            AttractedObjects[i].GetComponent<Attractable>()?.Attract(this);
        }
    }

    private void OnDrawGizmosSelected()
    {
        Gizmos.color = Color.magenta;
        Gizmos.DrawWireSphere(transform.position, Radius);
    }
}