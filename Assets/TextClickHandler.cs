using UnityEngine;
using UnityEngine.EventSystems;

public class TextClickHandler : MonoBehaviour, IPointerDownHandler
{
    public System.Action OnClick; // Event to invoke when clicked

    public void OnPointerDown(PointerEventData eventData)
    {
        OnClick?.Invoke();
    }
}