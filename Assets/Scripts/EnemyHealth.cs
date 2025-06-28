using UnityEngine;
using Photon.Pun;

public class EnemyHealth : MonoBehaviourPun
{
    public float health = 50f;
    public float maxHealth = 50f;
    private bool isHitByShockShield;

    void Start()
    {
        health = maxHealth;
        CustomLogger.Log($"EnemyHealth: Initialized for {gameObject.name}, health={health}, ViewID={photonView.ViewID}, IsMine={photonView.IsMine}");
    }

    public void TakeDamage(float damage)
    {
        if (!photonView.IsMine)
        {
            CustomLogger.Log($"EnemyHealth: TakeDamage skipped for {gameObject.name}, not mine, ViewID={photonView.ViewID}, requesting damage via RPC");
            photonView.RPC("ApplyDamageRPC", RpcTarget.MasterClient, damage);
            return;
        }

        ApplyDamage(damage);
    }

    [PunRPC]
    private void ApplyDamageRPC(float damage)
    {
        if (!photonView.IsMine)
        {
            CustomLogger.Log($"EnemyHealth: ApplyDamageRPC skipped for {gameObject.name}, not mine, ViewID={photonView.ViewID}");
            return;
        }

        ApplyDamage(damage);
    }

    private void ApplyDamage(float damage)
    {
        health -= damage;
        CustomLogger.Log($"EnemyHealth: {gameObject.name} took {damage} damage, health={health}, ViewID={photonView.ViewID}, frame={Time.frameCount}");

        // Sync health to all clients
        photonView.RPC("SyncHealthRPC", RpcTarget.All, health);

        // Spawn damage counter for all clients
        if (DamageCounterManager.Instance != null && damage > 0)
        {
            DamageCounterManager.Instance.SpawnDamageCounter(Mathf.FloorToInt(damage), transform, photonView);
        }
        else if (DamageCounterManager.Instance == null)
        {
            Debug.LogWarning("EnemyHealth: DamageCounterManager instance not found");
        }

        if (health <= 0)
        {
            Die();
        }
    }

    [PunRPC]
    private void SyncHealthRPC(float newHealth)
    {
        health = newHealth;
        CustomLogger.Log($"EnemyHealth: Synced health for {gameObject.name} to {health}, ViewID={photonView.ViewID}, frame={Time.frameCount}");
        if (health <= 0 && !photonView.IsMine)
        {
            // Ensure non-master clients reflect destruction
            CustomLogger.Log($"EnemyHealth: Health <= 0 on non-master client for {gameObject.name}, expecting destruction, ViewID={photonView.ViewID}");
        }
    }

    public void HitByShockShield()
    {
        isHitByShockShield = true;
        CustomLogger.Log($"EnemyHealth: {gameObject.name} marked as hit by ShockShield, ViewID={photonView.ViewID}");
    }

    private void Die()
    {
        if (!photonView.IsMine)
        {
            CustomLogger.Log($"EnemyHealth: Die skipped for {gameObject.name}, not mine, ViewID={photonView.ViewID}");
            return;
        }

        CustomLogger.Log($"EnemyHealth: {gameObject.name} died, health={health}, isHitByShockShield={isHitByShockShield}, ViewID={photonView.ViewID}, frame={Time.frameCount}");
        PhotonNetwork.Destroy(gameObject);
    }
}