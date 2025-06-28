using UnityEngine;

public class LaserBeamCollision : MonoBehaviour
{
    private void OnTriggerEnter2D(Collider2D collision)
    {
        Debug.Log("Laser hit: " + collision.gameObject.name);
        if (collision.CompareTag("Planet"))
        {
            Planet planet = collision.GetComponent<Planet>();
            if (planet != null)
            {
                planet.SplitIntoHalves();
            }
        }
        else if (collision.CompareTag("halfPlanet"))
        {
            PlanetHalf planetHalf = collision.GetComponent<PlanetHalf>();
            if (planetHalf != null)
            {
                planetHalf.SplitIntoQuarters();
            }
        }
        else if (collision.CompareTag("Enemy"))
        {
            EnemyHealth enemyHealth = collision.GetComponent<EnemyHealth>();
            if (enemyHealth != null)
            {
                enemyHealth.TakeDamage(100);
            }
        }
        else if (collision.CompareTag("ElephantBomb"))
        {
            ElephantBomb bomb = collision.GetComponent<ElephantBomb>();
            if (bomb != null)
            {
                bomb.Explode();
            }
        }
    }
}
