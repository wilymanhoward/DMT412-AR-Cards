using UnityEngine;

// What CardHub needs from a card's experience: which 3D content and UI belong to it, and how to show, pause
// and reset it. The greeting card and the business card both implement it.
public interface ICardExperience
{
    Transform Content { get; }   // under the card's image target
    GameObject UiRoot { get; }   // everything this card puts on screen
    void SetShown(bool shown);
    void OnCardFound();
    void OnCardLost();
    void ResetToStart();         // back to the beginning: another card took over
}
