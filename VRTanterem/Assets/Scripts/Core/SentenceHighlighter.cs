using UnityEngine;
using TMPro;
using System.Collections.Generic;
using System.Text;
using System;

public class SentenceHighlighter : MonoBehaviour
{
    [Header("Dependencies")]
    [Tooltip("Reference to the TextToSpeechManager component.")]
    [SerializeField] private TextToSpeechManager ttsManager;
    [Tooltip("The TextMeshProUGUI component to display and highlight text.")]
    [SerializeField] private TextMeshProUGUI textDisplay;

    [Header("Highlighting Colors")]
    [Tooltip("The Rich Text color tag for default text (e.g., 'black', '#000000').")]
    [SerializeField] private string defaultColorTag = "black"; // Alapértelmezett szín
    [Tooltip("The Rich Text color tag for the currently spoken sentence (e.g., 'red', '#FF0000').")]
    [SerializeField] private string highlightColorTag = "red"; // Kiemelés színe

    // --- Belső Állapotok ---
    private List<string> allSentences = new List<string>();
    private StringBuilder sentenceBuffer = new StringBuilder();
    private int currentHighlightIndex = -1;
    private bool isInitialized = false;
    private bool isStreamComplete = false;

    void Start()
    {
        InitializeHighlighter();
    }

    void OnDestroy()
    {
        // Leiratkozás az eseményekről a memóriaszivárgás elkerülése érdekében
        if (ttsManager != null && isInitialized)
        {
            ttsManager.OnTTSPlaybackStart -= HandleTTSPlaybackStart;
            ttsManager.OnTTSPlaybackEnd -= HandleTTSPlaybackEnd;
            // Debug.Log("[SentenceHighlighter] Unsubscribed from TTS Manager events.");
        }
    }

    /// <summary>
    /// Initializes the highlighter, checks dependencies, and subscribes to TTS events.
    /// </summary>
    private void InitializeHighlighter()
    {
        if (isInitialized) return; // Ne inicializáljunk többször

        // Függőségek ellenőrzése
        if (ttsManager == null)
        {
            Debug.LogError("[SentenceHighlighter] TextToSpeechManager reference is not set! Highlighting disabled.", this);
            enabled = false; // Letiltjuk a komponenst
            return;
        }
        if (textDisplay == null)
        {
            Debug.LogError("[SentenceHighlighter] TextMeshProUGUI reference (textDisplay) is not set! Highlighting disabled.", this);
            enabled = false;
            return;
        }

        // Feliratkozás a TTS Manager eseményeire
        ttsManager.OnTTSPlaybackStart += HandleTTSPlaybackStart;
        ttsManager.OnTTSPlaybackEnd += HandleTTSPlaybackEnd;
        // Opcionális: Feliratkozás a hibára is, ha törölni akarjuk a szöveget hiba esetén
        // ttsManager.OnTTSError += HandleTTSError;

        isInitialized = true;
        textDisplay.text = ""; // Kezdetben üres a kijelző
        Debug.Log("[SentenceHighlighter] Initialized and subscribed to TTS Manager events.");
    }

    /// <summary>
    /// Resets the highlighter state, clearing sentences and the display.
    /// Should be called before processing a new stream of text, typically after ttsManager.ResetManager().
    /// </summary>
    public void ResetHighlighter()
    {
        if (!isInitialized) InitializeHighlighter(); // Biztosítjuk az inicializálást
        if (!enabled) return; // Ha le van tiltva, ne csináljon semmit

        // Debug.Log("[SentenceHighlighter] Resetting state.");
        sentenceBuffer.Clear();
        allSentences.Clear();
        currentHighlightIndex = -1;
        isStreamComplete = false;
        if (textDisplay != null)
        {
            textDisplay.text = ""; // Töröljük a kijelzőt
        }
        // Az UpdateTextDisplay()-t nem kell hívni, mert a text üres lett.
    }

    /// <summary>
    /// Appends incoming text delta to the internal buffer and processes it for sentences.
    /// Call this method from your streaming source (e.g., OpenAIWebRequest).
    /// </summary>
    /// <param name="textDelta">The piece of text received from the stream.</param>
    public void AppendText(string textDelta)
    {
        if (!enabled || !isInitialized) return; // Ne csináljon semmit, ha nincs inicializálva vagy le van tiltva

        sentenceBuffer.Append(textDelta);
        ProcessSentenceBuffer();
    }

    /// <summary>
    /// Processes any remaining text in the buffer as a final sentence.
    /// Call this method when the text stream ends.
    /// </summary>
    public void FlushBuffer()
    {
        if (!enabled || !isInitialized) return;

        string remainingText = sentenceBuffer.ToString().Trim();
        if (!string.IsNullOrEmpty(remainingText))
        {
            allSentences.Add(remainingText);
            // UpdateTextDisplay(); // <<< EZT A HÍVÁST INNEN KIVETTÜK
        }
        sentenceBuffer.Clear();
        isStreamComplete = true;
        Debug.Log($"[Highlighter Flush] Stream marked complete. Total sentences: {allSentences.Count}");

        // Ellenőrizzük, hogy a TTS Manager létezik-e és befejezte-e a lejátszást
        bool ttsFinished = ttsManager != null && !ttsManager.IsPlaying;

        // Ha a stream véget ért ÉS a TTS már nem játszik ÉS még van aktív kiemelés
        if (ttsFinished && currentHighlightIndex != -1)
        {
            Debug.Log("[Highlighter Flush] Stream complete and TTS is not playing. Forcing highlight removal.");
            currentHighlightIndex = -1;
            UpdateTextDisplay(); // Frissítjük a UI-t, hogy a teljes szöveg alap színnel jelenjen meg
        }
        // Ha a TTS még játszik, akkor a HandleTTSPlaybackEnd fogja (remélhetőleg) kezelni a kiemelés törlését.
        else if (!ttsFinished)
        {
            Debug.Log("[Highlighter Flush] Stream complete, but TTS is still playing. Highlight removal deferred to HandleTTSPlaybackEnd.");
        }
    }

    /// <summary>
    /// Processes the internal sentence buffer, extracts complete sentences,
    /// adds them to the allSentences list, and updates the text display.
    /// </summary>
    private void ProcessSentenceBuffer()
    {
        int searchStartIndex = 0;
        bool sentenceFound = false;

        while (true)
        {
            int potentialEndIndex = FindPotentialSentenceEnd(sentenceBuffer, searchStartIndex);
            if (potentialEndIndex == -1) break;

            char punctuation = sentenceBuffer[potentialEndIndex];
            bool isLikelyEndOfSentence = true; // Kezdetben igaz

            // --- MÓDOSÍTOTT RÉSZ ---
            // 1. Számjegy ellenőrzés pont esetén (MOST MÁR NEM ÁLLÍTJA HAMISRA)
            if (punctuation == '.' && potentialEndIndex > 0 && char.IsDigit(sentenceBuffer[potentialEndIndex - 1]))
            {
                // Kivettük a belső if-et és az isLikelyEndOfSentence = false; sort.
                // Most már nem tekintjük automatikusan NEM mondatvégnek, ha számjegy van előtte.
                // A DÖNTÉST a pont UTÁNI karakterek ellenőrzésére bízzuk (lásd alább).
                // Ide később visszatérhetünk, ha a listákat vagy rövidítéseket (pl. Mr.)
                // pontosabban kell kezelni.
            }
            // --- MÓDOSÍTÁS VÉGE ---

            // 2. Következő karakter ellenőrzés (ez változatlan és FONTOS)
            // Ez a rész ellenőrzi, hogy mi van a pont UTÁN. Ha pl. nincs utána szóköz
            // (mint a "v1.2" vagy "e.g." esetén), akkor itt állítódik hamisra az isLikelyEndOfSentence.
            // Ez segít elkerülni a helytelen vágásokat.
            if (isLikelyEndOfSentence && potentialEndIndex < sentenceBuffer.Length - 1)
            {
                char nextChar = sentenceBuffer[potentialEndIndex + 1];
                bool isFollowedByWhitespace = char.IsWhiteSpace(nextChar);
                bool isFollowedByQuote = nextChar == '"' || nextChar == '\'';
                bool isFollowedByBracket = nextChar == ')' || nextChar == ']';

                // Ha pont van, és utána NINCS szóköz/idézőjel/zárójel, akkor valószínűleg nem mondatvég
                if (punctuation == '.' && !isFollowedByWhitespace && !isFollowedByQuote && !isFollowedByBracket)
                {
                    isLikelyEndOfSentence = false;
                }
                // Ha idézőjel/zárójel van, és az UTÁN sincs szóköz, akkor sem mondatvég
                else if ((isFollowedByQuote || isFollowedByBracket) && potentialEndIndex < sentenceBuffer.Length - 2)
                {
                    char afterNextChar = sentenceBuffer[potentialEndIndex + 2];
                    if (!char.IsWhiteSpace(afterNextChar))
                    {
                        isLikelyEndOfSentence = false;
                    }
                }
            }
            // --- Következő karakter ellenőrzés vége ---

            if (isLikelyEndOfSentence)
            {
                // Megvan a mondat!
                string sentence = sentenceBuffer.ToString(0, potentialEndIndex + 1).Trim();
                if (!string.IsNullOrWhiteSpace(sentence))
                {
                    allSentences.Add(sentence);
                    sentenceFound = true;
                }
                sentenceBuffer.Remove(0, potentialEndIndex + 1);
                searchStartIndex = 0;
            }
            else
            {
                // Nem volt igazi mondatvég, keressünk tovább
                searchStartIndex = potentialEndIndex + 1;
                if (searchStartIndex >= sentenceBuffer.Length) break;
            }
        }

        if (sentenceFound)
        {
            UpdateTextDisplay();
        }
    }


    /// <summary>
    /// Finds the index of the first potential sentence-ending punctuation mark (., ?, !)
    /// starting from the given index in the buffer.
    /// </summary>
    /// <returns>The index of the punctuation, or -1 if none found.</returns>
    private int FindPotentialSentenceEnd(StringBuilder buffer, int startIndex)
    {
        for (int i = startIndex; i < buffer.Length; i++)
        {
            char c = buffer[i];
            if (c == '.' || c == '?' || c == '!')
            {
                return i;
            }
        }
        return -1; // Nem találtunk
    }

    // --- KIJELZŐ FRISSÍTÉSE ---

    /// <summary>
    /// Rebuilds and updates the TextMeshProUGUI display based on the
    /// current list of sentences (allSentences) and the highlighted index (currentHighlightIndex).
    /// </summary>
    private void UpdateTextDisplay()
    {
        if (textDisplay == null) return;
        StringBuilder displayTextBuilder = new StringBuilder();

        int displayUntilIndex = -1; // Alapértelmezés: semmit nem jelenítünk meg

        if (currentHighlightIndex != -1)
        {
            // Ha van aktív kiemelés, addig az indexig jelenítünk meg
            displayUntilIndex = currentHighlightIndex;
        }
        else if (isStreamComplete) // <<< ÚJ FELTÉTEL
        {
            // Ha nincs aktív kiemelés, DE a stream már befejeződött,
            // akkor az összes mondatot megjelenítjük alap színnel.
            displayUntilIndex = allSentences.Count - 1;
        }

        // Ha displayUntilIndex továbbra is -1 (mert a stream még nem ért véget ÉS nincs kiemelés),
        // akkor a ciklus nem fut le, és a textDisplay.text üres lesz.
        if (displayUntilIndex >= 0) // Csak akkor építünk szöveget, ha van mit megjeleníteni
        {
            for (int i = 0; i <= displayUntilIndex && i < allSentences.Count; i++)
            {
                string escapedSentence = EscapeRichText(allSentences[i]);

                // A kiemelés csak akkor aktív, ha i == currentHighlightIndex (ami nem -1)
                if (i == currentHighlightIndex)
                {
                    displayTextBuilder.Append($"<color={highlightColorTag}>{escapedSentence}</color>");
                }
                else
                {
                    displayTextBuilder.Append($"<color={defaultColorTag}>{escapedSentence}</color>");
                }

                if (i < displayUntilIndex)
                {
                    displayTextBuilder.Append("\n");
                }
            }
        }

        textDisplay.text = displayTextBuilder.ToString();
        // Debug.Log($"[UpdateTextDisplay] Updated. HighlightIdx: {currentHighlightIndex}, StreamComplete: {isStreamComplete}, DisplayUntil: {displayUntilIndex}. Text: '{textDisplay.text.Substring(0, Math.Min(textDisplay.text.Length, 50))}...'");
    }


    /// <summary>
    /// Escapes characters that could interfere with TextMeshPro's Rich Text parsing.
    /// Adds zero-width spaces around '<' and '>' to prevent them from being interpreted as tags.
    /// </summary>
    /// <param name="input">The raw sentence text.</param>
    /// <returns>The escaped text suitable for Rich Text display.</returns>
    private string EscapeRichText(string input)
    {
        if (string.IsNullOrEmpty(input)) return input;

        // Alapvető escape-elés: Zero-width space (<ZWSP> = \u200B) beszúrása
        // Ez megakadályozza, hogy a TMP a '<' és '>' karaktereket tageknek nézze.
        // Finomítható lenne, ha tudjuk, milyen tageket használunk (pl. color, b, i, size).
        input = input.Replace("<", "<\u200B");
        input = input.Replace(">", "\u200B>");

        // További potenciális escape-elendő karakterek (ha szükséges):
        // input = input.Replace("&", "&amp;"); // Ha & karakterek is gondot okoznak

        return input;
    }

    // --- ESEMÉNYKEZELŐK (TTS Manager eseményeire) ---

    /// <summary>
    /// Handles the OnTTSPlaybackStart event from the TextToSpeechManager.
    /// Sets the current highlight index and updates the display.
    /// </summary>
    /// <param name="sentenceIndex">The index of the sentence that started playing.</param>
    private void HandleTTSPlaybackStart(int sentenceIndex)
    {
        if (!enabled || !isInitialized) return; // Ellenőrzés

        // Debug.Log($"[SentenceHighlighter] Received PlaybackStart for index: {sentenceIndex}");

        // Ellenőrizzük, hogy az index érvényes-e a listánkhoz képest
        if (sentenceIndex >= 0 && sentenceIndex < allSentences.Count)
        {
            // Beállítjuk az aktuális kiemelési indexet
            currentHighlightIndex = sentenceIndex;
            // Frissítjük a kijelzőt, hogy az új kiemelés megjelenjen
            UpdateTextDisplay();
        }
        else
        {
            // Ez nem fordulhatna elő normál működés esetén, de logoljuk, ha mégis
            Debug.LogWarning($"[SentenceHighlighter] Received PlaybackStart with invalid index: {sentenceIndex}. Current sentence count: {allSentences.Count}. Resetting highlight.");
            // Ha érvénytelen index jön, töröljük a kiemelést
            if (currentHighlightIndex != -1)
            {
                currentHighlightIndex = -1;
                UpdateTextDisplay();
            }
        }
    }

    /// <summary>
    /// Handles the OnTTSPlaybackEnd event from the TextToSpeechManager.
    /// Typically, we don't need to do anything here, as the highlight will be
    /// updated when the *next* sentence starts playing.
    /// Optionally, could reset the highlight here if desired.
    /// </summary>
    /// <param name="sentenceIndex">The index of the sentence that finished playing.</param>
    private void HandleTTSPlaybackEnd(int sentenceIndex)
    {
        if (!enabled || !isInitialized) return;
        // Debug logolás maradhat, ha segít a követésben:
        // Debug.Log($"[HandleTTSPlaybackEnd] Received for index: {sentenceIndex}. CurrentHighlight: {currentHighlightIndex}. TotalSentences: {allSentences.Count}");

        // NINCS SZÜKSÉG A KIEMELÉS TÖRLÉSÉRE ITT.
        // A kiemelés akkor fog frissülni (vagy eltűnni, ha nincs több mondat),
        // amikor a KÖVETKEZŐ mondat elindul (HandleTTSPlaybackStart),
        // vagy amikor a stream véget ér és a FlushBuffer lefut.
        // Az itteni idő előtti törlés okozza a random villogást/törlést.
    }

    // Opcionális: Hibakezelő
    /*
    private void HandleTTSError(string errorMessage)
    {
        if (!enabled || !isInitialized) return;

        Debug.LogError($"[SentenceHighlighter] Received TTS Error: {errorMessage}. Resetting highlight.");
        // Hiba esetén érdemes lehet törölni a kiemelést
        if (currentHighlightIndex != -1)
        {
            currentHighlightIndex = -1;
            UpdateTextDisplay();
        }
        // Esetleg a kijelzőt is törölhetnénk vagy hibaüzenetet írhatnánk ki:
        // textDisplay.text = $"<color=red>TTS Error: {errorMessage}</color>";
    }
    */
}
