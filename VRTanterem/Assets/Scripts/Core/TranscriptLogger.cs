using UnityEngine;
using System.Collections.Generic;
using System.Text;
using System;
using System.Linq;


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

    public string GetConcatenatedLastNAiEntries(int count, string separator = " ")
    {
        if (Transcript == null || count <= 0)
        {
            return string.Empty;
        }

        // Szűrjük az AI bejegyzéseket, megfordítjuk, hogy az utolsóaktól kezdjük,
        // vesszük a 'count' darabot, majd újra megfordítjuk, hogy az eredeti sorrendben legyenek,
        // végül kiválasztjuk a szövegeket.
        List<string> aiTexts = Transcript
            .Where(entry => entry.Speaker.Equals("AI", StringComparison.OrdinalIgnoreCase))
            .Reverse() // Hátulról kezdjük a keresést (legutóbbiak elöl)
            .Take(count) // Veszünk 'count' darabot
            .Reverse() // Visszafordítjuk az eredeti időrendi sorrendbe
            .Select(entry => entry.Text)
            .ToList();

        if (aiTexts.Count == 0)
        {
            Debug.LogWarning($"[TranscriptLogger] GetConcatenatedLastNAiEntries: No AI entries found to concatenate for the last {count} entries.");
            return string.Empty;
        }

        // Debug logolás az összefűzött elemekről
        // Debug.Log($"[TranscriptLogger] Concatenating {aiTexts.Count} AI entries: {string.Join(" | ", aiTexts)}");

        return string.Join(separator, aiTexts);
    }

    public List<LogEntry> GetLastNAiLogEntries(int count)
    {
        if (Transcript == null || count <= 0)
        {
            // Debug.LogWarning($"[TranscriptLogger] GetLastNAiLogEntries: Transcript is null or count is invalid ({count}). Returning empty list.");
            return new List<LogEntry>(); // Üres lista, ha nincs mit visszaadni
        }

        List<LogEntry> aiEntries = Transcript
            .Where(entry => entry.Speaker.Equals("AI", StringComparison.OrdinalIgnoreCase))
            .Reverse() // Hátulról kezdjük (legutóbbiak elöl)
            .Take(count) // Veszünk 'count' darabot
            .Reverse() // Visszafordítjuk az eredeti időrendi sorrendbe
            .ToList();

        // Opcionális debug log, hogy lásd, mit ad vissza
        // if (aiEntries.Count > 0)
        // {
        //    Debug.Log($"[TranscriptLogger] GetLastNAiLogEntries returning {aiEntries.Count} entries. Last one: '{aiEntries.Last().Text}'");
        // }
        // else
        // {
        //    Debug.LogWarning($"[TranscriptLogger] GetLastNAiLogEntries: No AI entries found for the last {count} entries.");
        // }
        return aiEntries;
    }
}
