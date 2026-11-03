using UnityEngine;
using UnityEngine.EventSystems;

// A tap (or mouse click) that began outside any UI element.
public static class TapInput
{
    public static bool Started(out Vector2 position)
    {
        position = default;
        if (Input.touchCount > 0)
        {
            var touch = Input.GetTouch(0);
            if (touch.phase != TouchPhase.Began || EventSystem.current.IsPointerOverGameObject(touch.fingerId)) return false;
            position = touch.position;
            return true;
        }
        if (!Input.GetMouseButtonDown(0) || EventSystem.current.IsPointerOverGameObject()) return false;
        position = Input.mousePosition;
        return true;
    }
}
