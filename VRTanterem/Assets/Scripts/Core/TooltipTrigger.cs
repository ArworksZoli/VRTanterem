using UnityEngine;
using UnityEngine.EventSystems; // Ez a n�vt�r elengedhetetlen az esem�nyekhez!

public class TooltipTrigger : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
{
    [Header("Tooltip Content")]
    [Tooltip("A sz�veg, ami megjelenik a tooltipben, ha erre az elemre mutat a felhaszn�l�.")]
    [TextArea(3, 5)] // Nagyobb sz�vegdoboz az Inspectorban
    public string tooltipMessage;

    /// <summary>
    /// Ez a met�dus automatikusan megh�v�dik, amikor a VR kontroller pointere
    /// bel�p az elem ter�let�re.
    /// </summary>
    public void OnPointerEnter(PointerEventData eventData)
    {
        // Megh�vjuk a k�zponti rendszer Show() met�dus�t a saj�t �zenet�nkkel.
        TooltipSystem.Instance.Show(tooltipMessage);
    }

    /// <summary>
    /// Ez a met�dus automatikusan megh�v�dik, amikor a VR kontroller pointere
    /// elhagyja az elem ter�let�t.
    /// </summary>
    public void OnPointerExit(PointerEventData eventData)
    {
        // Megh�vjuk a k�zponti rendszer Hide() met�dus�t.
        TooltipSystem.Instance.Hide();
    }
}