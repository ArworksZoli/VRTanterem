using UnityEngine;
using System.Collections.Generic;
using System.Text;
using System;


[System.Serializable]
public struct LogEntry
{
    public DateTime Timestamp;
    public string Speaker;
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
    public List<LogEntry> Transcript { get; private set; } = new List<LogEntry>();

    public event Action<string> OnNewAIEntryAdded;

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

    public void AddEntry(string speaker, string text)
    {
        if (string.IsNullOrEmpty(speaker) || string.IsNullOrWhiteSpace(text))
        {
            Debug.LogWarning($"[TranscriptLogger] Attempted to add empty entry (Speaker: '{speaker}', Text: '{text}'). Entry skipped.");
            return;
        }

        LogEntry newEntry = new LogEntry(speaker, text.Trim()); // Trim whitespace
        Transcript.Add(newEntry);

        Debug.Log($"[Transcript_LOG] {newEntry}");

        if (speaker.Equals("AI", StringComparison.OrdinalIgnoreCase))
        {
            OnNewAIEntryAdded?.Invoke(newEntry.Text);
            // Debug.Log("[TranscriptLogger] OnNewAIEntryAdded event invoked."); // Opcionális logolás
        }

        // Opcionális: Frissítjük a UI kijelzőt
        // UpdateTranscriptUI();
    }

    public string GetFormattedTranscript()
    {
        StringBuilder sb = new StringBuilder();
        foreach (var entry in Transcript)
        {
            sb.AppendLine(entry.ToString());
        }
        return sb.ToString();
    }

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
        
        for (int i = Transcript.Count - 1; i >= 0; i--)
        {
            if (Transcript[i].Speaker.Equals("AI", StringComparison.OrdinalIgnoreCase)) // Kis-nagybetű érzéketlen összehasonlítás
            {
                return Transcript[i].Text; // Visszaadjuk a megtalált AI üzenet szövegét
            }
        }
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
