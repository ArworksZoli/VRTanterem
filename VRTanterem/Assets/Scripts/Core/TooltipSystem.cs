using UnityEngine;
using TMPro;

public class TooltipSystem : MonoBehaviour
{
    public static TooltipSystem Instance { get; private set; }

    [Header("UI References")]
    [Tooltip("A tooltip panel, ami a szöveget tartalmazza.")]
    [SerializeField] private GameObject tooltipPanel;

    [Tooltip("A TextMeshPro komponens, amibe a szöveget írjuk.")]
    [SerializeField] private TextMeshProUGUI tooltipText;

    void Awake()
    {
        // Singleton minta beállítása
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
        }
        else
        {
            Instance = this;
        }

        // Biztosítjuk, hogy a panel rejtve legyen az elején
        if (tooltipPanel != null)
        {
            tooltipPanel.SetActive(false);
        }
    }

    /// <summary>
    /// Megjeleníti a tooltip panelt a megadott szöveggel.
    /// </summary>
    /// <param name="content">A megjelenítendő szöveg.</param>
    public void Show(string content)
    {
        if (tooltipPanel == null || tooltipText == null) return;

        tooltipText.text = content;
        tooltipPanel.SetActive(true);
    }

    /// <summary>
    /// Elrejti a tooltip panelt.
    /// </summary>
    public void Hide()
    {
        if (tooltipPanel == null) return;

        tooltipPanel.SetActive(false);
    }
}