using UnityEngine;
using UnityEngine.EventSystems; // Ez a névtér elengedhetetlen az eseményekhez!

public class TooltipTrigger : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
{
    [Header("Tooltip Content")]
    [Tooltip("A szöveg, ami megjelenik a tooltipben, ha erre az elemre mutat a felhasználó.")]
    [TextArea(3, 5)] // Nagyobb szövegdoboz az Inspectorban
    public string tooltipMessage;

    /// <summary>
    /// Ez a metódus automatikusan meghívódik, amikor a VR kontroller pointere
    /// belép az elem területére.
    /// </summary>
    public void OnPointerEnter(PointerEventData eventData)
    {
        // Meghívjuk a központi rendszer Show() metódusát a saját üzenetünkkel.
        TooltipSystem.Instance.Show(tooltipMessage);
    }

    /// <summary>
    /// Ez a metódus automatikusan meghívódik, amikor a VR kontroller pointere
    /// elhagyja az elem területét.
    /// </summary>
    public void OnPointerExit(PointerEventData eventData)
    {
        // Meghívjuk a központi rendszer Hide() metódusát.
        TooltipSystem.Instance.Hide();
    }
}