using System;
using UnityEngine;
using UnityEngine.EventSystems;

// Reports a left/right swipe over a UI element: -1 for a swipe right (previous), +1 for a swipe left (next).
public class SwipeArea : MonoBehaviour, IBeginDragHandler, IDragHandler, IEndDragHandler
{
    public Action<int> swiped;
    public float minDistance = 60f; // screen pixels

    public void OnBeginDrag(PointerEventData eventData) { }
    public void OnDrag(PointerEventData eventData) { }

    public void OnEndDrag(PointerEventData eventData)
    {
        float dx = eventData.position.x - eventData.pressPosition.x;
        if (Mathf.Abs(dx) >= minDistance) swiped?.Invoke(dx < 0 ? 1 : -1);
    }
}
