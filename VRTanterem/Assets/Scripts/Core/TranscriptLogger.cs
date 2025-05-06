using UnityEngine;
using System.Collections.Generic;
using System.Text; // Szükséges a StringBuilderhez
using System; // Szükséges a DateTime-hoz

// Struktúra egy naplóbejegyzés tárolásához
[System.Serializable] // Láthatóvá teszi az Inspectorban (ha szükséges)
public struct LogEntry
{
    public DateTime Timestamp;
    public string Speaker; // Pl. "User", "AI", "System"
    public string Text;

    public LogEntry(string speaker, string text)
    {
        Timestamp = DateTime.Now;
        Speaker = speaker;
        Text = text;
    }

    public override string ToString()
    {
        // Egyszerű formázás a könnyebb olvashatóságért
        return $"[{Timestamp:HH:mm:ss}] {Speaker}: {Text}";
    }
}

public class TranscriptLogger : MonoBehaviour
{
    // --- Singleton Minta ---
    public static TranscriptLogger Instance { get; private set; }

    // --- Napló Tárolása ---
    // A private set biztosítja, hogy kívülről csak olvasni lehessen a listát,
    // de módosítani csak ezen az osztályon belül lehet az AddEntry metódussal.
    public List<LogEntry> Transcript { get; private set; } = new List<LogEntry>();

    // --- Opcionális: UI megjelenítéshez ---
    // Ha szeretnéd valós időben látni a logot egy UI elemen
    // [Header("Optional UI Display")]
    // [SerializeField] private TMPro.TextMeshProUGUI transcriptDisplayUI;
    // [SerializeField] private int maxDisplayedLines = 50; // Korlát a UI-on megjelenő sorokra

    void Awake()
    {
        // Singleton implementáció
        if (Instance != null && Instance != this)
        {
            Debug.LogWarning("[TranscriptLogger] Duplicate instance found. Destroying self.", gameObject);
            Destroy(gameObject);
            return;
        }
        Instance = this;

        // Opcionális: Ne semmisüljön meg jelenetváltáskor, ha a logot meg akarod őrizni
        // DontDestroyOnLoad(gameObject);

        Debug.Log("[TranscriptLogger] Instance initialized.");
        // Kezdeti rendszerüzenet (opcionális)
        // AddEntry("System", "Transcript logger started.");
    }

    /// <summary>
    /// Adds a new entry to the transcript log.
    /// </summary>
    /// <param name="speaker">Who said it (e.g., "User", "AI", "System").</param>
    /// <param name="text">The content of the message.</param>
    public void AddEntry(string speaker, string text)
    {
        if (string.IsNullOrEmpty(speaker) || string.IsNullOrWhiteSpace(text))
        {
            Debug.LogWarning($"[TranscriptLogger] Attempted to add empty entry (Speaker: '{speaker}', Text: '{text}'). Entry skipped.");
            return;
        }

        LogEntry newEntry = new LogEntry(speaker, text.Trim()); // Trim whitespace
        Transcript.Add(newEntry);

        // Logoljuk a konzolra is a könnyebb követhetőségért
        Debug.Log($"[Transcript_LOG] {newEntry}");

        // Opcionális: Frissítjük a UI kijelzőt
        // UpdateTranscriptUI();
    }

    /// <summary>
    /// Returns the entire transcript as a single formatted string.
    /// </summary>
    public string GetFormattedTranscript()
    {
        StringBuilder sb = new StringBuilder();
        foreach (var entry in Transcript)
        {
            sb.AppendLine(entry.ToString());
        }
        return sb.ToString();
    }

    /// <summary>
    /// Clears the current transcript log.
    /// </summary>
    public void ClearTranscript()
    {
        Transcript.Clear();
        Debug.Log("[TranscriptLogger] Transcript cleared.");
        // Opcionális: UI törlése
        // if (transcriptDisplayUI != null) transcriptDisplayUI.text = "";
        // AddEntry("System", "Transcript cleared."); // Opcionális rendszerüzenet
    }

    public string GetLastAIEntryText()
    {
        // Visszafelé iterálunk a listán, hogy a legutóbbit találjuk meg először
        for (int i = Transcript.Count - 1; i >= 0; i--)
        {
            // Fontos: A "Speaker" stringnek pontosan egyeznie kell azzal,
            // amit az AddEntry-ben használsz az AI üzeneteihez.
            // Ha pl. "AI Assistant" a speaker, akkor itt is azt kell keresni.
            // Javasolt konstansokat használni a speaker nevekhez a hibák elkerülése érdekében.
            if (Transcript[i].Speaker.Equals("AI", StringComparison.OrdinalIgnoreCase)) // Kis-nagybetű érzéketlen összehasonlítás
            {
                return Transcript[i].Text; // Visszaadjuk a megtalált AI üzenet szövegét
            }
        }
        // Ha nem találtunk "AI" speakert, üres stringet adunk vissza
        Debug.LogWarning("[TranscriptLogger] GetLastAIEntryText: No entry found with speaker 'AI'.");
        return string.Empty;
    }

    // --- Opcionális UI Frissítő Metódus ---
    /*
    private void UpdateTranscriptUI()
    {
        if (transcriptDisplayUI == null) return;

        StringBuilder uiText = new StringBuilder();
        int startIndex = Mathf.Max(0, Transcript.Count - maxDisplayedLines); // Csak az utolsó X sort mutatjuk

        for (int i = startIndex; i < Transcript.Count; i++)
        {
            uiText.AppendLine(Transcript[i].ToString());
        }
        transcriptDisplayUI.text = uiText.ToString();

        // Opcionális: Automatikus görgetés az aljára (ha ScrollRect-ben van)
        // Canvas.ForceUpdateCanvases(); // Biztosítja a frissítést
        // scrollRect?.verticalNormalizedPosition = 0f; // Ha van scrollRect referencia
    }
    */
}
