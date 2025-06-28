using UnityEngine;
using Photon.Pun;

[RequireComponent(typeof(LineRenderer), typeof(PhotonView))]
public class BoundaryVisual : MonoBehaviourPun
{
    [SerializeField] private float boundarySize = 3000f;

    void Start()
    {
        SetupLineRenderer();
        if (photonView.IsMine)
        {
            photonView.RPC(nameof(SyncLineRenderer), RpcTarget.Others);
            CustomLogger.Log($"BoundaryVisual: Sent SyncLineRenderer RPC, ViewID={photonView.ViewID}");
        }
    }

    [PunRPC]
    private void SyncLineRenderer()
    {
        SetupLineRenderer();
        CustomLogger.Log($"BoundaryVisual: Received SyncLineRenderer RPC, ViewID={photonView.ViewID}");
    }

    private void SetupLineRenderer()
    {
        LineRenderer lr = GetComponent<LineRenderer>();
        lr.positionCount = 4;
        float halfSize = boundarySize / 2;
        lr.SetPositions(new Vector3[]
        {
            new Vector3(-halfSize, -halfSize, 0),
            new Vector3(halfSize, -halfSize, 0),
            new Vector3(halfSize, halfSize, 0),
            new Vector3(-halfSize, halfSize, 0)
        });
        lr.loop = true;
        lr.startWidth = 0.1f;
        lr.endWidth = 0.1f;
        lr.useWorldSpace = true;

        Material redMaterial = new Material(Shader.Find("Sprites/Default"));
        if (redMaterial == null)
        {
            CustomLogger.LogError("BoundaryVisual: Sprites/Default shader not found, falling back to Standard");
            redMaterial = new Material(Shader.Find("Standard"));
        }
        if (redMaterial != null)
        {
            redMaterial.color = Color.red;
            lr.material = redMaterial;
            CustomLogger.Log($"BoundaryVisual: Set material with shader={redMaterial.shader.name}, color={redMaterial.color}");
        }
        else
        {
            CustomLogger.LogError("BoundaryVisual: Failed to create material, no shader available");
        }

        lr.startColor = Color.red;
        lr.endColor = Color.red;

        CustomLogger.Log($"BoundaryVisual: Initialized boundary outline for {gameObject.name}, IsMine={photonView.IsMine}, PositionCount={lr.positionCount}, StartWidth={lr.startWidth}, Material={(lr.material != null ? lr.material.shader.name : "null")}, StartColor={lr.startColor}, EndColor={lr.endColor}");
    }
}