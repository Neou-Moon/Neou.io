using UnityEngine;
using UnityEngine.EventSystems; // For IPointerDownHandler, IPointerUpHandler, IDragHandler, PointerEventData
using UnityEngine.UI; // For RectTransformUtility (optional, but included for completeness)
using Photon.Pun; // For PhotonView and PunRPC

public class Joystick : MonoBehaviour, IPointerDownHandler, IPointerUpHandler, IDragHandler
{
    [SerializeField] private RectTransform background;
    [SerializeField] private RectTransform handle;
    [SerializeField] private float handleRange = 50f;
    [SerializeField] private float deadZone = 0.1f;

    private Vector2 inputVector;
    private Vector2 startPos;
    private bool isActive;
    private PhotonView photonView;

    public Vector2 InputVector => inputVector;
    public bool IsActive => isActive; // Added to allow PlayerController to check state

    void Start()
    {
        photonView = GetComponentInParent<PhotonView>();
        if (background == null || handle == null)
        {
            Debug.LogError($"Joystick: Background or Handle not assigned on {gameObject.name}");
            enabled = false;
            return;
        }
        startPos = handle.anchoredPosition;
        inputVector = Vector2.zero;
        isActive = false;
        ForceReset();
    }

    void Update()
    {
        // Only update handle position if active to prevent drift
        if (!isActive && handle.anchoredPosition != startPos)
        {
            handle.anchoredPosition = startPos;
            inputVector = Vector2.zero;
            if (photonView != null && photonView.IsMine)
            {
                photonView.RPC("SyncJoystick", RpcTarget.Others, handle.anchoredPosition, inputVector);
            }
            Debug.Log($"Joystick: {gameObject.name} snapped to center, InputVector={inputVector}");
        }
    }

    public void OnPointerDown(PointerEventData eventData)
    {
        isActive = true;
        OnDrag(eventData);
        Debug.Log($"Joystick: {gameObject.name} pointer down, isActive={isActive}");
    }

    public void OnPointerUp(PointerEventData eventData)
    {
        ForceReset();
        Debug.Log($"Joystick: {gameObject.name} pointer up, reset to center");
    }

    public void OnDrag(PointerEventData eventData)
    {
        if (!isActive) return;

        RectTransformUtility.ScreenPointToLocalPointInRectangle(
            background,
            eventData.position,
            eventData.pressEventCamera,
            out Vector2 localPoint);

        localPoint = Vector2.ClampMagnitude(localPoint, handleRange);
        handle.anchoredPosition = startPos + localPoint;

        inputVector = localPoint / handleRange;
        if (inputVector.magnitude < deadZone)
        {
            inputVector = Vector2.zero;
            handle.anchoredPosition = startPos;
        }
        else
        {
            if (photonView != null && photonView.IsMine)
            {
                photonView.RPC("SyncJoystick", RpcTarget.Others, handle.anchoredPosition, inputVector);
            }
        }
        Debug.Log($"Joystick: {gameObject.name} dragged, InputVector={inputVector}, HandlePos={handle.anchoredPosition}");
    }

    public void SetInput(Vector2 input)
    {
        if (input.magnitude <= deadZone)
        {
            ForceReset();
            Debug.Log($"Joystick: {gameObject.name} set input to zero, force reset");
        }
        else
        {
            isActive = true;
            inputVector = Vector2.ClampMagnitude(input, 1f);
            handle.anchoredPosition = startPos + (inputVector * handleRange);
            if (photonView != null && photonView.IsMine)
            {
                photonView.RPC("SyncJoystick", RpcTarget.Others, handle.anchoredPosition, inputVector);
            }
            Debug.Log($"Joystick: {gameObject.name} set input={inputVector}, handlePos={handle.anchoredPosition}");
        }
    }

    [PunRPC]
    void SyncJoystick(Vector2 handlePos, Vector2 input)
    {
        handle.anchoredPosition = handlePos;
        inputVector = input;
        isActive = input.magnitude > 0;
        Debug.Log($"Joystick: {gameObject.name} synced, handlePos={handlePos}, inputVector={input}");
    }

    public void ForceReset()
    {
        isActive = false;
        inputVector = Vector2.zero;
        handle.anchoredPosition = startPos;
        if (photonView != null && photonView.IsMine)
        {
            photonView.RPC("SyncJoystick", RpcTarget.Others, handle.anchoredPosition, inputVector);
        }
        Debug.Log($"Joystick: {gameObject.name} force reset to center, inputVector={inputVector}");
    }
}