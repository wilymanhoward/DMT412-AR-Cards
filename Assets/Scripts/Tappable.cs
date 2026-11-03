using System;
using UnityEngine;

// Put on (or above) an object with a collider; the business card's tap handler calls onTap when it is hit.
public class Tappable : MonoBehaviour
{
    public Action onTap;
}
