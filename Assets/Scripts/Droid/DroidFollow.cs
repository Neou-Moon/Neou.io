using UnityEngine;

public class DroidFollow : MonoBehaviour
{
    public Transform playerHead;
    public float followDistance = 2f;
    public float followHeight = 2f;
    public float followSpeed = 5f;
    public float shimmySpeed = 1f;
    public float shimmyAmount = 1f;
    public float hoverAmount = 0.5f;
    public float hoverSpeed = 1f;

    private void Update()
    {
        if (playerHead == null) return;

        Vector3 targetPosition = playerHead.position + new Vector3(followDistance, followHeight, 0f);
        targetPosition.x += Mathf.Sin(Time.time * shimmySpeed) * shimmyAmount;
        targetPosition.y += Mathf.Sin(Time.time * hoverSpeed) * hoverAmount;

        transform.position = Vector3.MoveTowards(transform.position, targetPosition, followSpeed * Time.deltaTime);
    }
}