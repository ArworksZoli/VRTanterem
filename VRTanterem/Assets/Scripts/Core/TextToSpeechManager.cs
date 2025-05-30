using UnityEngine;
using UnityEngine.Networking;
using System;
using System.Text;
using System.Collections;
using System.Collections.Generic;

// Osztály a TTS API kérés payloadjának reprezentálására
[System.Serializable]
public class TTSRequestPayload
{
    public string model;
    public string input;
    public string voice;
    public string response_format;
    public float speed = 1.0f;
}

public class TextToSpeechManager : MonoBehaviour
{
    [Header("TTS Configuration")]
    [SerializeField] private string ttsModel = "tts-1";
    private string currentTtsVoice;
    [SerializeField] private string ttsResponseFormat = "mp3";
    [SerializeField] private string ttsApiUrl = "https://api.openai.com/v1/audio/speech";
    [Tooltip("Playback speed. 1.0 is normal speed. Range: 0.25 to 4.0")]
    [Range(0.25f, 4.0f)]
    [SerializeField] private float ttsSpeed = 1.0f;
    [Tooltip("Maximum number of audio clips to keep ready for playback.")]
    [SerializeField] private int maxPlaybackQueueSize = 3;

    [Header("Components")]
    [Tooltip("The AudioSource used for playing the main lecture audio.")]
    [SerializeField] private AudioSource audioSource;
    [Tooltip("The AudioSource used for playing prompts and immediate answers.")]
    [SerializeField] private AudioSource promptAudioSource;

    public AudioSource MainAudioSource => audioSource;
    public AudioSource PromptAudioSource => promptAudioSource;

    // --- ESEMÉNYEK ---
    /// <summary>Fired when a regular lecture sentence starts playing.</summary>
    public event Action<int> OnTTSPlaybackStart;
    /// <summary>Fired when a regular lecture sentence finishes playing.</summary>
    public event Action<int> OnTTSPlaybackEnd;
    /// <summary>Fired when a TTS error occurs.</summary>
    public event Action<string> OnTTSError;
    // Nincs külön prompt vége esemény, a SpeakSingleSentence korutinja kezeli a végét.
    public event Action OnPlaybackQueueCompleted;

    public bool IsPlaying => audioSource != null && audioSource.isPlaying;

    // --- Belső állapotok és várólisták ---
    private string apiKey;
    private StringBuilder sentenceBuffer = new StringBuilder();
    private Queue<SentenceData> pendingSentencesQueue = new Queue<SentenceData>(); // Mondatok, amikre még nincs hang generálva
    private Queue<SentenceData> playbackQueue = new Queue<SentenceData>();         // Mondatok, amikre van hang, lejátszásra várnak
    private int sentenceCounter = 0; // Folyamatosan növekvő index a mondatokhoz

    private bool isTTSRequestInProgress = false;
    private Coroutine manageTTSCoroutine;
    private Coroutine managePlaybackCoroutine;
    private Coroutine currentPlaybackMonitor = null;
    private Coroutine currentPromptCoroutine = null; // Korutin a prompt lejátszásához

    // --- ÁLLAPOTJELZŐK ---
    private bool isPausedForQuestion = false;
    private int resumeFromSentenceIndex = -1;

    // --- ÚJ VÁLTOZÓK A VÁLASZ KEZELÉSÉHEZ ---
    private StringBuilder answerSentenceBuffer = new StringBuilder();

    // Szekvenciális audió generálás
    private Coroutine currentAnswerSentenceProcessingCoroutine = null;

    // Struktúra az index, szöveg és klip tárolásához
    private struct SentenceData
    {
        public int Index;
        public string Text;
        public AudioClip Clip;
    }

    // --- UNITY ÉLETCKLUS ---

    void Start()
    {
        // Ellenőrizzük mindkét AudioSource meglétét
        if (audioSource == null)
        {
            Debug.LogError("[TextToSpeechManager_LOG] Main AudioSource (audioSource) is not assigned in the Inspector! TTS will not work correctly.", this);
            enabled = false;
            return;
        }
        if (promptAudioSource == null) // <<< ÚJ ELLENŐRZÉS
        {
            Debug.LogError("[TextToSpeechManager_LOG] Prompt AudioSource (promptAudioSource) is not assigned in the Inspector! Prompts/Answers will not play.", this);
            // Dönthetsz úgy, hogy itt is letiltod (enabled = false;), vagy csak figyelmeztetsz.
            // Ha a promptok kritikusak, tiltsd le.
            enabled = false;
            return;
        }
    }

    void OnDestroy()
    {
        Debug.Log("[TextToSpeechManager_LOG] OnDestroy called. Stopping coroutines and clearing queues.");
        StopAllCoroutines(); // Leállítunk minden futó korutint ebben a scriptben

        // Töröljük a memóriából a még lejátszásra váró klipeket is
        ClearQueuesAndClips();
    }

    // --- INICIALIZÁLÁS ÉS RESET ---

    public void Initialize(string key, string voiceId)
    {
        Debug.Log($"[TextToSpeechManager_LOG] Initialize called. Voice ID: {voiceId}");
        // ... (A meglévő ellenőrzések és API kulcs/hang beállítás változatlan) ...
        if (string.IsNullOrEmpty(key)) { /*...*/ enabled = false; return; }
        if (string.IsNullOrEmpty(voiceId)) { /*...*/ }
        if (audioSource == null) { /*...*/ enabled = false; return; }

        this.apiKey = key;
        this.currentTtsVoice = voiceId;
        Debug.Log($"[TextToSpeechManager_LOG] API Key stored. Current TTS Voice set to: {this.currentTtsVoice}");

        // Korutinok indítása (ha még nem futnak)
        if (manageTTSCoroutine == null)
        {
            manageTTSCoroutine = StartCoroutine(ManageTTSRequests());
            Debug.Log("[TextToSpeechManager_LOG] ManageTTSRequests coroutine started.");
        }
        if (managePlaybackCoroutine == null)
        {
            managePlaybackCoroutine = StartCoroutine(ManagePlayback());
            Debug.Log("[TextToSpeechManager_LOG] ManagePlayback coroutine started.");
        }

        Debug.Log("[TextToSpeechManager_LOG] Initialization complete.");
    }

    public void ResetManager()
    {
        Debug.LogWarning("[TextToSpeechManager_LOG] Resetting state called...");

        // 1. Lejátszás leállítása mindkét AudioSource-on
        if (audioSource != null && audioSource.isPlaying)
        {
            audioSource.Stop();
            Debug.Log("[TextToSpeechManager_LOG] Stopped active audio playback on main AudioSource (lecture).");
        }
        if (promptAudioSource != null && promptAudioSource.isPlaying)
        {
            promptAudioSource.Stop();
            Debug.LogWarning("[TextToSpeechManager_LOG] Stopped active audio playback on promptAudioSource (AI answer/prompt)."); // Fontos log!
        }

        // 2. Futó korutinok leállítása
        // Fő előadás (lecture) korutinjai
        if (currentPlaybackMonitor != null)
        {
            StopCoroutine(currentPlaybackMonitor);
            currentPlaybackMonitor = null;
            Debug.Log("[TextToSpeechManager_LOG] Stopped current playback monitor coroutine (lecture).");
        }
        // Egyszeri promptok korutinja (SpeakSingleSentence)
        if (currentPromptCoroutine != null)
        {
            StopCoroutine(currentPromptCoroutine);
            currentPromptCoroutine = null;
            Debug.Log("[TextToSpeechManager_LOG] Stopped current prompt coroutine (SpeakSingleSentence).");
        }
        // AI válaszok szekvenciális feldolgozását végző korutin
        if (currentAnswerSentenceProcessingCoroutine != null)
        {
            StopCoroutine(currentAnswerSentenceProcessingCoroutine);
            currentAnswerSentenceProcessingCoroutine = null;
            Debug.LogWarning("[TextToSpeechManager_LOG] Stopped current answer sentence processing coroutine."); // Fontos log!
        }

        // 3. Állapotjelzők és pufferek/várakozási sorok törlése
        // Fő előadás (lecture) állapotai
        isTTSRequestInProgress = false;
        isPausedForQuestion = false;
        resumeFromSentenceIndex = -1;
        sentenceBuffer.Clear();
        sentenceCounter = 0;

        // AI válasz szövegpufferének törlése
        answerSentenceBuffer.Clear();
        Debug.Log("[TextToSpeechManager_LOG] Cleared answerSentenceBuffer.");

        // Fő előadás (lecture) várakozási sorainak törlése
        ClearQueuesAndClips(); // Ez a pendingSentencesQueue és playbackQueue-t törli

        Debug.LogWarning($"[TextToSpeechManager_LOG] Reset completed.");
    }

    /// <summary>
    /// Appends text chunks for an immediate AI answer (played on promptAudioSource).
    /// </summary>
    public void AppendAnswerText(string textDelta)
    {
        if (!enabled || string.IsNullOrEmpty(apiKey) || promptAudioSource == null) return;
        Debug.Log($"[TTS LOG Answer] AppendAnswerText Received: '{textDelta}'. Buffer before append: '{answerSentenceBuffer.ToString(0, Math.Min(answerSentenceBuffer.Length, 50))}'. Current Time: {Time.time}");
        answerSentenceBuffer.Append(textDelta);
        if (currentAnswerSentenceProcessingCoroutine == null)
        {
            ProcessNextFullSentenceFromAnswerBuffer();
        }
    }

    /// <summary>
    /// Flushes any remaining text in the answer buffer as a final sentence.
    /// </summary>
    public void FlushAnswerBuffer()
    {
        if (!enabled || string.IsNullOrEmpty(apiKey)) return;
        Debug.LogWarning($"[TTS LOG Answer] FlushAnswerBuffer called. Buffer content before flush: '{answerSentenceBuffer.ToString(0, Math.Min(answerSentenceBuffer.Length, 50))}'. Current Time: {Time.time}");

        if (currentAnswerSentenceProcessingCoroutine == null)
        {
            string remainingText = answerSentenceBuffer.ToString().Trim();
            answerSentenceBuffer.Clear();
            if (!string.IsNullOrEmpty(remainingText))
            {
                Debug.LogWarning($"[TTS LOG Answer] Flush: Processing remaining buffer as LAST sentence: '{remainingText.Substring(0, Math.Min(remainingText.Length, 50))}'. Current Time: {Time.time}");
                currentAnswerSentenceProcessingCoroutine = StartCoroutine(GenerateAndPlaySingleAnswerPiece(remainingText, true));
            }
            else
            {
                Debug.LogWarning($"[TTS LOG Answer] Flush: Buffer was empty and no processing active. Considering answer completed. Current Time: {Time.time}");
                InteractionFlowManager.Instance?.HandleAnswerPlaybackCompleted();
            }
        }
        else
        {
            Debug.LogWarning($"[TTS LOG Answer] Flush: Sentence processing already in progress. The current sentence will finish, then check buffer. Current Time: {Time.time}");
        }
    }

    private void ProcessNextFullSentenceFromAnswerBuffer()
    {
        if (currentAnswerSentenceProcessingCoroutine != null) // Már fut valami
        {
            Debug.LogWarning($"[TTS LOG Answer] ProcessNextFullSentenceFromAnswerBuffer SKIPPED: currentAnswerSentenceProcessingCoroutine is active. Current Time: {Time.time}");
            return;
        }

        if (answerSentenceBuffer.Length == 0)
        {
            Debug.LogWarning($"[TTS LOG Answer] ProcessNextFullSentenceFromAnswerBuffer: Buffer is empty. Nothing to process. Current Time: {Time.time}");
            return;
        }

        int searchStartIndex = 0;
        int potentialEndIndex = FindPotentialSentenceEnd(answerSentenceBuffer, searchStartIndex);

        if (potentialEndIndex != -1) // Találtunk mondatvéget
        {
            string sentence = answerSentenceBuffer.ToString(0, potentialEndIndex + 1).Trim();
            answerSentenceBuffer.Remove(0, potentialEndIndex + 1); // Kivesszük a bufferből

            if (!string.IsNullOrWhiteSpace(sentence))
            {
                Debug.LogWarning($"[TTS LOG Answer] ProcessNextFullSentenceFromAnswerBuffer: Extracted sentence: '{sentence.Substring(0, Math.Min(sentence.Length, 50))}...'. Starting generation. Current Time: {Time.time}");
                currentAnswerSentenceProcessingCoroutine = StartCoroutine(GenerateAndPlaySingleAnswerPiece(sentence, false)); // false: nem tudjuk még, hogy ez-e az utolsó
            }
            else if (answerSentenceBuffer.Length > 0) // Ha üres mondatot szedtünk ki, de van még a bufferben
            {
                Debug.LogWarning($"[TTS LOG Answer] ProcessNextFullSentenceFromAnswerBuffer: Extracted empty sentence, but buffer has content. Re-processing. Current Time: {Time.time}");
                ProcessNextFullSentenceFromAnswerBuffer(); // Rekurzív hívás a következő darabra
            }
            // Ha üres mondat volt és a buffer is kiürült, a FlushBuffer vagy a korutin vége kezeli.
        }
        else
        {
            // Nincs mondatvég a bufferben. Ilyenkor várunk a FlushBuffer-re, ami majd az egészet feldolgozza
            // vagy több AppendAnswerText hívásra, ami kiegészíti a mondatot.
            Debug.LogWarning($"[TTS LOG Answer] ProcessNextFullSentenceFromAnswerBuffer: No sentence end found in buffer yet. Buffer: '{answerSentenceBuffer.ToString(0, Math.Min(answerSentenceBuffer.Length, 50))}'. Current Time: {Time.time}");
        }
    }

    private IEnumerator GenerateAndPlaySingleAnswerPiece(string textPiece, bool explicitlyIsLastPiece)
    {
        // Biztonsági ellenőrzés, hogy ne fusson üres szöveggel, bár a hívó helyeknek ezt már szűrnie kellene.
        if (string.IsNullOrWhiteSpace(textPiece))
        {
            Debug.LogWarning($"[TTS LOG Answer] GenerateAndPlaySingleAnswerPiece called with empty or whitespace text. Skipping. ExplicitlyLast: {explicitlyIsLastPiece}. Current Time: {Time.time}");

            // Ha ez expliciten az utolsó darab lett volna (pl. Flush egy üres bufferrel), akkor befejezettnek tekintjük.
            if (explicitlyIsLastPiece)
            {
                InteractionFlowManager.Instance?.HandleAnswerPlaybackCompleted();
            }
            currentAnswerSentenceProcessingCoroutine = null; // Fontos, hogy nullázzuk, hogy a következő ProcessNext... elindulhasson.

            // Ha a bufferben még van valami, megpróbáljuk feldolgozni (bár elvileg a hívó már üres mondatot nem adna át)
            if (!explicitlyIsLastPiece && answerSentenceBuffer.Length > 0)
            {
                ProcessNextFullSentenceFromAnswerBuffer();
            }
            yield break; // Kilépés a korutinból
        }

        Debug.LogWarning($"[TTS LOG Answer] GenerateAndPlaySingleAnswerPiece START for: \"{textPiece.Substring(0, Math.Min(textPiece.Length, 70))}...\". ExplicitlyLast: {explicitlyIsLastPiece}. Current Time: {Time.time}");

        // Várakozás, ha a promptAudioSource foglalt (pl. egy előző darab, vagy egy SpeakSingleSentence játszik)
        if (promptAudioSource.isPlaying)
        {
            Debug.LogWarning($"[TTS LOG Answer] GenerateAndPlaySingleAnswerPiece: promptAudioSource is busy (Clip: {promptAudioSource.clip?.name}). Waiting... For: \"{textPiece.Substring(0, Math.Min(textPiece.Length, 30))}...\". Current Time: {Time.time}");
            yield return new WaitUntil(() => !promptAudioSource.isPlaying);
            Debug.LogWarning($"[TTS LOG Answer] GenerateAndPlaySingleAnswerPiece: promptAudioSource is NOW FREE. For: \"{textPiece.Substring(0, Math.Min(textPiece.Length, 30))}...\". Current Time: {Time.time}");
        }

        // --- 1. Logolás a TranscriptLogger-be (ha használod) ---
        if (TranscriptLogger.Instance != null)
        {
            TranscriptLogger.Instance.AddEntry("AI", textPiece); // "AI" vagy a megfelelő azonosító
            Debug.Log($"[TextToSpeechManager_LOG] Logged AI answer piece to TranscriptLogger: \"{textPiece.Substring(0, Math.Min(textPiece.Length, 70))}...\"");
        }
        else
        {
            Debug.LogWarning("[TextToSpeechManager_LOG] TranscriptLogger.Instance is null. Cannot log AI answer piece.");
        }

        // --- 2. TTS API Kérés Előkészítése és Küldése ---
        AudioClip generatedClip = null; // Ebben tároljuk a sikeresen generált AudioClip-et

        TTSRequestPayload payload = new TTSRequestPayload
        {
            model = this.ttsModel,         // Osztályszintű változó
            input = textPiece,             // A kapott szövegdarab
            voice = this.currentTtsVoice,  // Osztályszintű változó (Initialize-ben beállítva)
            response_format = this.ttsResponseFormat, // Osztályszintű változó
            speed = this.ttsSpeed          // Osztályszintű változó
        };
        string jsonPayload = JsonUtility.ToJson(payload);
        byte[] bodyRaw = Encoding.UTF8.GetBytes(jsonPayload);

        Debug.Log($"[TTS LOG Answer] Sending TTS API request for piece: \"{textPiece.Substring(0, Math.Min(textPiece.Length, 70))}...\". Voice: {this.currentTtsVoice}, Speed: {this.ttsSpeed}. Current Time: {Time.time}");

        using (UnityWebRequest request = new UnityWebRequest(ttsApiUrl, "POST")) // ttsApiUrl osztályszintű változó
        {
            request.uploadHandler = new UploadHandlerRaw(bodyRaw);
            request.uploadHandler.contentType = "application/json"; // Fontos a tartalomtípus

            AudioType audioType = GetAudioTypeFromFormat(this.ttsResponseFormat); // Segédfüggvény
                                                                                  // Az URI itt a DownloadHandlerAudioClip konstruktorához szükséges.
            request.downloadHandler = new DownloadHandlerAudioClip(new Uri(ttsApiUrl), audioType);

            request.SetRequestHeader("Authorization", $"Bearer {this.apiKey}"); // apiKey osztályszintű változó
            request.SetRequestHeader("Content-Type", "application/json");      // Szokásos header
            request.timeout = 60; // Timeout másodpercben (állítsd be igény szerint)

            yield return request.SendWebRequest(); // Várakozás a válaszra

            // Válasz feldolgozása
            if (request.result == UnityWebRequest.Result.Success)
            {
                AudioClip receivedClip = DownloadHandlerAudioClip.GetContent(request);
                if (receivedClip != null && receivedClip.loadState == AudioDataLoadState.Loaded && receivedClip.length > 0)
                {
                    generatedClip = receivedClip; // Sikeres klip
                    Debug.LogWarning($"[TTS LOG Answer] Generate SUCCESS for piece. Clip Length: {generatedClip.length:F2}s. API Response Code: {request.responseCode}. For: \"{textPiece.Substring(0, Math.Min(textPiece.Length, 70))}...\". Current Time: {Time.time}");
                }
                else
                {
                    // Ha a klip nem érvényes
                    string errorReason = "Unknown reason for invalid clip after successful request.";
                    if (receivedClip == null) errorReason = "Received clip is null.";
                    else if (receivedClip.loadState != AudioDataLoadState.Loaded) errorReason = $"Clip LoadState is {receivedClip.loadState}.";
                    else if (receivedClip.length <= 0) errorReason = "Clip length is 0 or less.";

                    Debug.LogError($"[TTS LOG Answer] Generate FAILED (Invalid Clip). Reason: {errorReason}. For: \"{textPiece.Substring(0, Math.Min(textPiece.Length, 70))}...\". API Response Code: {request.responseCode}. Error from request: {request.error}. Current Time: {Time.time}");
                    OnTTSError?.Invoke($"AudioClip generation failed (invalid clip) for: {textPiece.Substring(0, Math.Min(textPiece.Length, 30))}");
                    if (receivedClip != null) Destroy(receivedClip); // Takarítás
                }
            }
            else
            {
                // Sikertelen API kérés
                string responseBody = request.downloadHandler?.text ?? "No response body from server.";
                Debug.LogError($"[TTS LOG Answer] Generate FAILED (API Error). For: \"{textPiece.Substring(0, Math.Min(textPiece.Length, 70))}...\". Code: {request.responseCode} - Error: {request.error}\nServer Response: {responseBody}. Current Time: {Time.time}");
                OnTTSError?.Invoke($"TTS API Error for: {textPiece.Substring(0, Math.Min(textPiece.Length, 30))}: {request.error}");
            }
        } // A 'using' blokk itt véget ér, a 'request' erőforrásai felszabadulnak.

        // --- 3. Lejátszás a promptAudioSource-on (ha sikeres volt a generálás) ---
        if (generatedClip != null) // Csak akkor próbáljuk lejátszani, ha van érvényes klipünk
        {
            Debug.LogWarning($"[TTS LOG Answer] Playing generated clip on promptAudioSource for piece: \"{textPiece.Substring(0, Math.Min(textPiece.Length, 70))}...\". Clip Length: {generatedClip.length:F2}s. Current Time: {Time.time}");
            promptAudioSource.clip = generatedClip;
            promptAudioSource.Play();

            // Várjuk meg, amíg ez a konkrét klip lejátszódik
            float playbackStartTime = Time.time;
            // Biztonsági timeout hozzáadása a WaitWhile-hoz, ha a lejátszás valamiért beragadna
            float safetyTimeout = generatedClip.length + 5.0f; // Klip hossza + 5 másodperc tulerancia

            // Várakozás, amíg a klip játszódik, vagy a klip megváltozik, vagy timeout
            while (promptAudioSource.isPlaying &&
                   promptAudioSource.clip == generatedClip &&
                   (Time.time - playbackStartTime < safetyTimeout))
            {
                yield return null;
            }

            float actualPlaybackDuration = Time.time - playbackStartTime;
            if (promptAudioSource.isPlaying && promptAudioSource.clip == generatedClip) // Ha a timeout miatt léptünk ki
            {
                Debug.LogWarning($"[TTS LOG Answer] Playback TIMEOUT for piece: \"{textPiece.Substring(0, Math.Min(textPiece.Length, 70))}...\". Stopping audio. Played for {actualPlaybackDuration:F2}s. Clip Length: {generatedClip.length:F2}s Current Time: {Time.time}");
                promptAudioSource.Stop();
            }
            else
            {
                Debug.LogWarning($"[TTS LOG Answer] Finished playing or was interrupted for piece: \"{textPiece.Substring(0, Math.Min(textPiece.Length, 70))}...\". Played for {actualPlaybackDuration:F2}s. Clip Length: {generatedClip.length:F2}s. Current Time: {Time.time}");
            }

            Destroy(generatedClip); // Klip törlése használat után, hogy ne szivárogjon a memória
        }
        else
        {
            // Ha nem sikerült klipet generálni, ezt már fentebb logoltuk, de itt is jelezhetjük, hogy a lejátszás kimarad.
            Debug.LogError($"[TTS LOG Answer] Skipping playback for piece: \"{textPiece.Substring(0, Math.Min(textPiece.Length, 70))}...\" due to generation failure. Current Time: {Time.time}");
        }

        // --- 4. Következő lépés kezelése ---
        currentAnswerSentenceProcessingCoroutine = null; // Jelzi, hogy ez a darab feldolgozása befejeződött

        // Itt döntjük el, mi legyen a következő lépés
        if (explicitlyIsLastPiece)
        {
            // Ha a FlushBuffer jelezte, hogy ez volt az utolsó darab, akkor az AI válasz véget ért.
            Debug.LogWarning($"[TTS LOG Answer] ExplicitlyIsLastPiece was TRUE for piece: \"{textPiece.Substring(0, Math.Min(textPiece.Length, 70))}...\". Calling InteractionFlowManager.HandleAnswerPlaybackCompleted. Current Time: {Time.time}");
            InteractionFlowManager.Instance?.HandleAnswerPlaybackCompleted();
        }
        else if (answerSentenceBuffer.Length > 0)
        {
            // Ha nem ez volt expliciten az utolsó, de van még szöveg a globális bufferben, dolgozzuk fel a következőt.
            Debug.LogWarning($"[TTS LOG Answer] More text found in answerSentenceBuffer after playing piece: \"{textPiece.Substring(0, Math.Min(textPiece.Length, 70))}...\". Triggering ProcessNextFullSentenceFromAnswerBuffer. Current Time: {Time.time}");
            ProcessNextFullSentenceFromAnswerBuffer();
        }
        else // Nincs több a bufferben, ÉS nem volt expliciten az utolsóként jelölve.
        {
            // Ebben az esetben feltételezzük, hogy a mondat természetes módon ért véget, és a buffer kiürült.
            // Ez is azt jelenti, hogy az AI válasz (erre a körre) befejeződött.
            Debug.LogWarning($"[TTS LOG Answer] answerSentenceBuffer is empty after playing piece: \"{textPiece.Substring(0, Math.Min(textPiece.Length, 70))}...\" (and not explicitly last). Assuming answer completed. Calling InteractionFlowManager.HandleAnswerPlaybackCompleted. Current Time: {Time.time}");
            InteractionFlowManager.Instance?.HandleAnswerPlaybackCompleted();
        }
    }

    /// <summary>
    /// Clears both pending and playback queues, destroying any associated AudioClips.
    /// </summary>
    private void ClearQueuesAndClips()
    {
        pendingSentencesQueue.Clear(); // Itt nincsenek klipek

        int clearedPlayback = 0;
        while (playbackQueue.Count > 0)
        {
            SentenceData data = playbackQueue.Dequeue();
            if (data.Clip != null)
            {
                Destroy(data.Clip);
                clearedPlayback++;
            }
        }
        playbackQueue.Clear();
        if (clearedPlayback > 0)
        {
            Debug.Log($"[TextToSpeechManager_LOG] Cleared {clearedPlayback} clips from playback queue.");
        }
    }

    // --- SZÖVEG FELDOLGOZÁS (Append, Flush, Mondat Detektálás) ---
    // Ezek a részek (AppendText, FlushBuffer, ProcessSentenceBuffer, FindPotentialSentenceEnd)
    // változatlanok maradnak, továbbra is a pendingSentencesQueue-t töltik SentenceData objektumokkal.
    // ... (VÁLTOZATLAN KÓD IDE) ...
    public void AppendText(string textDelta)
    {
        if (!enabled || string.IsNullOrEmpty(apiKey)) return;
        sentenceBuffer.Append(textDelta);
        ProcessSentenceBuffer();
    }

    public void FlushBuffer()
    {
        if (!enabled || string.IsNullOrEmpty(apiKey)) return;
        string remainingText = sentenceBuffer.ToString().Trim();
        if (!string.IsNullOrEmpty(remainingText))
        {
            pendingSentencesQueue.Enqueue(new SentenceData { Index = sentenceCounter, Text = remainingText });
            sentenceCounter++;
        }
        sentenceBuffer.Clear();
    }

    private void ProcessSentenceBuffer()
    {
        int searchStartIndex = 0;
        while (true)
        {
            int potentialEndIndex = FindPotentialSentenceEnd(sentenceBuffer, searchStartIndex);
            if (potentialEndIndex == -1) break;
            char punctuation = sentenceBuffer[potentialEndIndex];
            bool isLikelyEndOfSentence = true; // Kezdetben igaz

            // --- MÓDOSÍTOTT RÉSZ (SentenceHighlighterből áthozva, ha kell) ---
            // Számjegy ellenőrzés pont esetén
            if (punctuation == '.' && potentialEndIndex > 0 && char.IsDigit(sentenceBuffer[potentialEndIndex - 1]))
            {
                // Jelenleg nem teszünk semmit, a pont utáni karakter dönt
            }
            // --- MÓDOSÍTÁS VÉGE ---

            // Következő karakter ellenőrzés
            if (isLikelyEndOfSentence && potentialEndIndex < sentenceBuffer.Length - 1)
            {
                char nextChar = sentenceBuffer[potentialEndIndex + 1];
                // Egyszerűsített ellenőrzés: Ha pont után nem szóköz/idézőjel/zárójel van, nem mondatvég
                if (punctuation == '.' && !char.IsWhiteSpace(nextChar) && nextChar != '"' && nextChar != '\'' && nextChar != ')')
                {
                    isLikelyEndOfSentence = false;
                }
                // TODO: Finomabb logikát igényelhet rövidítésekhez (pl. "stb.") vagy speciális esetekhez.
            }

            if (isLikelyEndOfSentence)
            {
                string sentence = sentenceBuffer.ToString(0, potentialEndIndex + 1).Trim();
                if (!string.IsNullOrWhiteSpace(sentence))
                {
                    pendingSentencesQueue.Enqueue(new SentenceData { Index = sentenceCounter, Text = sentence });
                    // Debug.Log($"[TTS Sentence Detected] Enqueued (Index: {sentenceCounter}): '{sentence.Substring(0, Math.Min(sentence.Length, 30))}'");
                    sentenceCounter++;
                }
                sentenceBuffer.Remove(0, potentialEndIndex + 1);
                searchStartIndex = 0;
            }
            else
            {
                searchStartIndex = potentialEndIndex + 1;
                if (searchStartIndex >= sentenceBuffer.Length) break;
            }
        }
    }

    private int FindPotentialSentenceEnd(StringBuilder buffer, int startIndex)
    {
        for (int i = startIndex; i < buffer.Length; i++)
        {
            char c = buffer[i];
            if (c == '.' || c == '?' || c == '!')
            {
                // Opcionális: Ellenőrizhetnénk, hogy nem rövidítés része-e (pl. Mr., Dr., stb.)
                // if (c == '.' && i > 0 && char.IsUpper(buffer[i-1])) { /* ... */ }
                return i;
            }
        }
        return -1;
    }


    // --- TTS KÉRÉS KEZELÉSE (ManageTTSRequests, GenerateSpeechCoroutine) ---
    // Ez a rész változatlan marad, továbbra is a pendingSentencesQueue-ból dolgozik
    // és a playbackQueue-ba teszi az elkészült SentenceData-kat (klippel együtt).
    // ... (VÁLTOZATLAN KÓD IDE) ...
    private IEnumerator ManageTTSRequests()
    {
        while (true)
        {
            // Vár, amíg nincs folyamatban TTS kérés, van feldolgozandó mondat, ÉS van hely a lejátszási sorban
            yield return new WaitUntil(() => !isTTSRequestInProgress &&
                                             pendingSentencesQueue.Count > 0 &&
                                             playbackQueue.Count < maxPlaybackQueueSize);

            SentenceData sentenceData = pendingSentencesQueue.Dequeue();
            // Debug.Log($"[TTS Manager] Dequeued sentence (Index: {sentenceData.Index}) for TTS generation.");

            // Elindítjuk a hang generálását az adott mondathoz
            StartCoroutine(GenerateSpeechCoroutine(sentenceData));
        }
    }

    private IEnumerator GenerateSpeechCoroutine(SentenceData data)
    {
        isTTSRequestInProgress = true;
        // Debug.Log($"[TTS API Call] Sending text (Index: {data.Index}): '{data.Text.Substring(0, Math.Min(data.Text.Length, 50))}...'");

        TTSRequestPayload payload = new TTSRequestPayload
        {
            model = this.ttsModel,
            input = data.Text,
            voice = this.currentTtsVoice,
            response_format = this.ttsResponseFormat,
            speed = this.ttsSpeed
        };
        string jsonPayload = JsonUtility.ToJson(payload);
        byte[] bodyRaw = Encoding.UTF8.GetBytes(jsonPayload);

        using (UnityWebRequest request = new UnityWebRequest(ttsApiUrl, "POST"))
        {
            request.uploadHandler = new UploadHandlerRaw(bodyRaw);
            AudioType audioType = GetAudioTypeFromFormat(ttsResponseFormat);
            // Fontos: Az URI itt csak a DownloadHandlerAudioClip konstruktorához kell,
            // a tényleges cél URL a UnityWebRequest-ben van megadva.
            request.downloadHandler = new DownloadHandlerAudioClip(new Uri(ttsApiUrl), audioType);
            request.SetRequestHeader("Authorization", $"Bearer {apiKey}");
            request.SetRequestHeader("Content-Type", "application/json");
            request.timeout = 60; // 60 másodperc timeout

            yield return request.SendWebRequest();

            if (request.result == UnityWebRequest.Result.Success)
            {
                AudioClip receivedClip = DownloadHandlerAudioClip.GetContent(request);
                // Ellenőrizzük, hogy a klip érvényes és betöltődött-e
                if (receivedClip != null && receivedClip.loadState == AudioDataLoadState.Loaded && receivedClip.length > 0)
                {
                    data.Clip = receivedClip; // Klip hozzáadása az adathoz
                    playbackQueue.Enqueue(data); // Teljes adat a lejátszási sorba
                    // Debug.Log($"[TTS Success] Received AudioClip for sentence {data.Index} (Length: {receivedClip.length}s). Playback Queue size: {playbackQueue.Count}");
                }
                else
                {
                    string errorReason = receivedClip == null ? "Clip is null" :
                                         receivedClip.loadState != AudioDataLoadState.Loaded ? $"LoadState is {receivedClip.loadState}" :
                                         "Clip length is 0";
                    Debug.LogError($"[TTS Error] Failed to get valid AudioClip for sentence {data.Index}. Reason: {errorReason}. Error: {request.error}");
                    OnTTSError?.Invoke($"AudioClip generation failed for sentence {data.Index}.");
                    // Ha a klip létrejött, de nem jó, töröljük
                    if (receivedClip != null) Destroy(receivedClip);
                }
            }
            else
            {
                string errorDetails = request.downloadHandler?.text ?? "No response body";
                Debug.LogError($"[TTS API Error] Sentence {data.Index} - Code: {request.responseCode} - Error: {request.error}\nResponse: {errorDetails}");
                OnTTSError?.Invoke($"TTS API Error for sentence {data.Index}: {request.error}");
            }
        }
        isTTSRequestInProgress = false;
    }

    private AudioType GetAudioTypeFromFormat(string format)
    {
        switch (format.ToLower())
        {
            case "mp3": return AudioType.MPEG;
            // case "opus": return AudioType.OPUS; // Opus támogatás hozzáadva
            // case "aac": return AudioType.AAC;   // AAC támogatás hozzáadva
            // case "flac": return AudioType.FLAC; // FLAC támogatás hozzáadva
            // A WAV és OGG Vorbis általában nem támogatott kimenet az OpenAI TTS-nél
            // case "wav": return AudioType.WAV;
            // case "ogg": return AudioType.OGGVORBIS;
            default:
                Debug.LogWarning($"[TTS] Unsupported audio format '{format}' specified or potentially unsupported by Unity's DownloadHandlerAudioClip. Falling back to MPEG (MP3).");
                return AudioType.MPEG;
        }
    }


    // --- LEJÁTSZÁS VEZÉRLÉSE (Pause, Resume, SpeakSingle) ---

    /// <summary>
    /// Pauses the playback of the main lecture queue. Stops any currently playing audio.
    /// </summary>
    public void PausePlayback()
    {
        Debug.Log("[TextToSpeechManager_LOG] PausePlayback called.");
        isPausedForQuestion = true;

        // Ha éppen játszik le valamit az AudioSource, állítsuk le
        if (audioSource != null && audioSource.isPlaying)
        {
            audioSource.Stop();
            Debug.Log("[TextToSpeechManager_LOG] Stopped currently playing audio due to pause request.");
        }

        // Ha van aktív lejátszás monitor korutin, állítsuk le,
        // mert a lejátszás megszakadt, nem természetesen ért véget.
        if (currentPlaybackMonitor != null)
        {
            StopCoroutine(currentPlaybackMonitor);
            currentPlaybackMonitor = null; // Fontos nullázni!
            Debug.Log("[TextToSpeechManager_LOG] Stopped playback monitor due to pause.");
            // A monitor által figyelt klipet nem töröljük itt, mert lehet, hogy még kell a folytatáshoz.
        }

        // A ManagePlayback korutint nem kell leállítani, az ellenőrzi az isPausedForQuestion flag-et.
    }

    /// <summary>
    /// Resumes the playback of the main lecture queue from the specified sentence index.
    /// </summary>
    /// <param name="startFromIndex">The index of the sentence to resume playback from.</param>
    public void ResumePlayback(int startFromIndex)
    {
        Debug.Log($"[TextToSpeechManager_LOG] ResumePlayback called. Resuming from index: {startFromIndex}");
        resumeFromSentenceIndex = startFromIndex; // Beállítjuk, honnan kell folytatni
        isPausedForQuestion = false; // Engedélyezzük a lejátszást

        // A ManagePlayback korutin automatikusan észreveszi, hogy isPausedForQuestion már false,
        // és a resumeFromSentenceIndex alapján fogja folytatni. Nincs szükség a korutin újraindítására.
        Debug.Log("[TextToSpeechManager_LOG] Playback queue processing will resume.");
    }

    /// <summary>
    /// Generates and speaks a single sentence immediately (e.g., a prompt).
    /// Waits for the AudioSource to be free before playing.
    /// Calls InteractionFlowManager.Instance.EnableSpeakButton() upon completion.
    /// </summary>
    /// <param name="text">The sentence to speak.</param>
    public void SpeakSingleSentence(string text)
    {
        if (!enabled || string.IsNullOrEmpty(apiKey) || audioSource == null)
        {
            Debug.LogError("[TextToSpeechManager_LOG] Cannot SpeakSingleSentence: Component disabled, API key missing, or AudioSource missing.");
            // Ha hiba van, lehet, hogy mégis engedélyezni kell a gombot, hogy a user tudjon reagálni?
            InteractionFlowManager.Instance?.EnableSpeakButton(); // Próbáljuk meg engedélyezni
            return;
        }

        // Ha már fut egy prompt korutin, állítsuk le az előzőt
        if (currentPromptCoroutine != null)
        {
            Debug.LogWarning("[TextToSpeechManager_LOG] SpeakSingleSentence called while another prompt was potentially processing. Stopping previous prompt coroutine.");
            StopCoroutine(currentPromptCoroutine);
        }

        // Indítjuk az új prompt korutint
        currentPromptCoroutine = StartCoroutine(SpeakSingleSentenceCoroutine(text));
    }

    private IEnumerator SpeakSingleSentenceCoroutine(string text)
    {
        Debug.Log($"[TextToSpeechManager_LOG] SpeakSingleSentenceCoroutine started for: '{text}'");

        // Ellenőrizzük a promptAudioSource meglétét a biztonság kedvéért
        if (promptAudioSource == null)
        {
            Debug.LogError("[SpeakSingleSentenceCoroutine] promptAudioSource is null! Cannot play prompt.");
            // Ha hiba van, próbáljuk meg engedélyezni a gombot, hogy a user tudjon reagálni
            InteractionFlowManager.Instance?.EnableSpeakButton();
            currentPromptCoroutine = null; // Nullázzuk a korutin referenciát, jelezve a végét
            yield break; // Kilépünk a korutinból
        }

        // 1. Várjunk, amíg a PROMPT AudioSource nem játszik le mást
        if (promptAudioSource.isPlaying) // <<< MÓDOSÍTVA: promptAudioSource
        {
            Debug.Log("[SpeakSingleSentenceCoroutine] Prompt AudioSource is busy. Waiting...");
            // Várakozás, amíg a promptAudioSource befejezi az előző lejátszást
            yield return new WaitUntil(() => !promptAudioSource.isPlaying); // <<< MÓDOSÍTVA: promptAudioSource
            Debug.Log("[SpeakSingleSentenceCoroutine] Prompt AudioSource became free.");
        }

        // 2. Generáljuk le a hangot ehhez az egy mondathoz
        AudioClip promptClip = null;
        bool generationError = false;

        TTSRequestPayload payload = new TTSRequestPayload
        {
            model = this.ttsModel,
            input = text,
            voice = this.currentTtsVoice,
            response_format = this.ttsResponseFormat,
            speed = this.ttsSpeed // Használhatunk kicsit gyorsabb sebességet a promptokhoz? Pl. 1.1f
        };
        string jsonPayload = JsonUtility.ToJson(payload);
        byte[] bodyRaw = Encoding.UTF8.GetBytes(jsonPayload);

        Debug.Log("[SpeakSingleSentenceCoroutine_LOG] Sending TTS request for prompt...");
        using (UnityWebRequest request = new UnityWebRequest(ttsApiUrl, "POST"))
        {
            request.uploadHandler = new UploadHandlerRaw(bodyRaw);
            AudioType audioType = GetAudioTypeFromFormat(ttsResponseFormat);

            request.downloadHandler = new DownloadHandlerAudioClip(new Uri(ttsApiUrl), audioType);
            request.SetRequestHeader("Authorization", $"Bearer {apiKey}");
            request.SetRequestHeader("Content-Type", "application/json");
            request.timeout = 30; // Rövidebb timeout a promptokhoz?

            yield return request.SendWebRequest();

            if (request.result == UnityWebRequest.Result.Success)
            {
                promptClip = DownloadHandlerAudioClip.GetContent(request);
                // Ellenőrizzük, hogy a klip érvényes és betöltődött-e
                if (promptClip != null && promptClip.loadState == AudioDataLoadState.Loaded && promptClip.length > 0)
                {
                    Debug.Log($"[SpeakSingleSentenceCoroutine_LOG] Prompt AudioClip generated successfully (Length: {promptClip.length}s).");
                }
                else
                {
                    string errorReason = promptClip == null ? "Clip is null" :
                                         promptClip.loadState != AudioDataLoadState.Loaded ? $"LoadState is {promptClip.loadState}" :
                                         "Clip length is 0";
                    Debug.LogError($"[SpeakSingleSentenceCoroutine_LOG] Failed to get valid AudioClip for prompt. Reason: {errorReason}. Error: {request.error}");
                    generationError = true;
                    if (promptClip != null) Destroy(promptClip); // Takarítás
                    promptClip = null;
                }
            }
            else
            {
                string errorDetails = request.downloadHandler?.text ?? "No response body";
                Debug.LogError($"[SpeakSingleSentenceCoroutine_LOG] TTS API Error for prompt: {request.error} - Code: {request.responseCode}\nResponse: {errorDetails}");
                generationError = true;
            }
        }

        // 3. Játsszuk le a generált klipet a PROMPT AudioSource-on (ha sikerült)
        if (!generationError && promptClip != null)
        {
            // Itt logoljuk az eredeti 'text' paramétert, amit a metódus kapott
            TranscriptLogger.Instance?.AddEntry("AI", text);

            Debug.Log("[SpeakSingleSentenceCoroutine_LOG] Playing prompt clip on promptAudioSource...");
            promptAudioSource.clip = promptClip;
            promptAudioSource.Play(); 

            // 4. Várjuk meg, amíg a lejátszás befejeződik a PROMPT AudioSource-on
            float startTime = Time.time;

            while (promptAudioSource.isPlaying && promptAudioSource.clip == promptClip && currentPromptCoroutine != null) // <<< MÓDOSÍTVA: promptAudioSource
            {
                // Biztonsági timeout, ha valamiért beragadna
                if (Time.time - startTime > promptClip.length + 5.0f) // 5 mp ráhagyás
                {
                    Debug.LogError("[SpeakSingleSentenceCoroutine_LOG] Playback timeout reached for prompt! Stopping playback on promptAudioSource.");
                    promptAudioSource.Stop(); // <<< MÓDOSÍTVA: promptAudioSource
                    break;
                }
                yield return null;
            }
            Debug.Log("[SpeakSingleSentenceCoroutine_LOG] Prompt playback finished or was interrupted on promptAudioSource.");
        }
        else
        {
            Debug.LogError("[SpeakSingleSentenceCoroutine_LOG] Skipping prompt playback due to generation error.");
        }

        // 5. Hívjuk meg az InteractionFlowManager-t a beszéd gomb engedélyezéséhez
        if (InteractionFlowManager.Instance != null)
        {
            Debug.Log("[SpeakSingleSentenceCoroutine_LOG] Enabling Speak Button via InteractionFlowManager.");
            InteractionFlowManager.Instance.EnableSpeakButton();
        }
        else
        {
            Debug.LogError("[SpeakSingleSentenceCoroutine_LOG] Cannot enable speak button: InteractionFlowManager.Instance is null!");
        }

        // 6. Takarítás: Töröljük a prompt klipet, amit ehhez a korutinhoz generáltunk
        if (promptClip != null)
        {
            Destroy(promptClip);
            // Debug.Log("[SpeakSingleSentenceCoroutine_LOG] Destroyed prompt clip.");
        }

        // 7. Nullázzuk a korutin referenciát, jelezve, hogy befejeződött
        currentPromptCoroutine = null;
        Debug.Log("[SpeakSingleSentenceCoroutine_LOG] Finished.");
    }


    // --- LEJÁTSZÁS CIKLUS (ManagePlayback, MonitorPlaybackEnd) ---

    private IEnumerator ManagePlayback()
    {
        while (true)
        {
            yield return new WaitUntil(() => !isPausedForQuestion &&
                                             audioSource != null &&
                                             !audioSource.isPlaying &&
                                             playbackQueue.Count > 0);

            SentenceData dataToPlay = playbackQueue.Peek();

            if (resumeFromSentenceIndex != -1 && dataToPlay.Index < resumeFromSentenceIndex)
            {
                playbackQueue.Dequeue(); // Kivesszük a sorból
                Debug.Log($"[TTS Playback_LOG] Skipping sentence {dataToPlay.Index} (Resuming from {resumeFromSentenceIndex}).");
                if (dataToPlay.Clip != null) Destroy(dataToPlay.Clip); // Töröljük a felesleges klipet
                continue;
            }

            dataToPlay = playbackQueue.Dequeue();
            resumeFromSentenceIndex = -1;

            if (dataToPlay.Clip != null)
            {
                TranscriptLogger.Instance?.AddEntry("AI", dataToPlay.Text);

                // Debug.Log($"[TTS Playback_LOG] Playing sentence {dataToPlay.Index}. Length: {dataToPlay.Clip.length}s. Remaining in queue: {playbackQueue.Count}");

                // Esemény kiváltása a lejátszás kezdetéről
                try { OnTTSPlaybackStart?.Invoke(dataToPlay.Index); }
                catch (Exception ex) { Debug.LogError($"Error in OnTTSPlaybackStart handler: {ex.Message}"); }

                audioSource.clip = dataToPlay.Clip;
                audioSource.Play();

                // Indítjuk a monitor korutint
                if (currentPlaybackMonitor != null) StopCoroutine(currentPlaybackMonitor); // Biztonsági leállítás, ha valamiért maradt volna
                currentPlaybackMonitor = StartCoroutine(MonitorPlaybackEnd(dataToPlay));
            }
            else
            {
                Debug.LogWarning($"[TTS Playback_LOG] Dequeued data for sentence {dataToPlay.Index} with a null AudioClip. Skipping playback.");

                try { OnTTSPlaybackEnd?.Invoke(dataToPlay.Index); }
                catch (Exception ex) { Debug.LogError($"Error invoking OnTTSPlaybackEnd for skipped null clip {dataToPlay.Index}: {ex.Message}"); }
            }
        }
    }

    // A MonitorPlaybackEnd metódus változatlan marad, az kezeli a lejátszás végét
    // és az OnTTSPlaybackEnd esemény kiváltását, valamint a klip törlését.
    // ... (VÁLTOZATLAN MonitorPlaybackEnd KÓD IDE) ...
    private IEnumerator MonitorPlaybackEnd(SentenceData playedData)
    {
        // ... (korutin eleje, null ellenőrzések) ...

        // Várakozás, amíg a lejátszás véget ér VAGY a klip megváltozik VAGY pause van
        yield return new WaitUntil(() =>
            isPausedForQuestion ||
            audioSource == null ||
            audioSource.clip != playedData.Clip ||
            !audioSource.isPlaying
        );

        // ... (ellenőrzés, hogy a korutint nem állították-e le külsőleg) ...

        // Ellenőrizzük a leállás okát
        bool stoppedForPause = isPausedForQuestion;
        bool clipIsCorrect = (audioSource != null && audioSource.clip == playedData.Clip);
        // Fontos: Itt ellenőrizzük, hogy tényleg leállt-e a lejátszás *és* nem pause miatt
        bool stoppedPlayingNaturally = (audioSource != null && !audioSource.isPlaying && !stoppedForPause);

        // --- ITT VAN A LÉNYEG ---
        if (clipIsCorrect && stoppedPlayingNaturally) // Csak akkor, ha természetesen fejeződött be
        {
            // Természetes befejezés
            Debug.Log($"[TTS Playback Monitor_LOG] Playback finished naturally for sentence {playedData.Index}. Firing End event."); // <<< MÓDOSÍTOTT LOG
            try
            {
                // Először jelezzük az EGY mondat végét
                OnTTSPlaybackEnd?.Invoke(playedData.Index);
            }
            catch (Exception ex) { Debug.LogError($"[TTS Playback_LOG] Error invoking OnTTSPlaybackEnd event handler for index {playedData.Index}: {ex.Message}\n{ex.StackTrace}"); }

            // --- ÚJ RÉSZ: Ellenőrizzük, hogy a lejátszási sor kiürült-e ---
            // Ezt azután ellenőrizzük, hogy az OnTTSPlaybackEnd lefutott,
            // mert lehet, hogy annak a kezelője (pl. Highlighter) még használja az indexet.
            // Fontos: Itt csak a playbackQueue-t nézzük. Ha közben érkezik új szöveg
            // a pendingSentencesQueue-ba, az egy új "ciklust" fog indítani.
            if (playbackQueue.Count == 0)
            {
                Debug.LogWarning($"[TTS Playback Monitor_LOG] Playback queue is now empty after sentence {playedData.Index}. Firing OnPlaybackQueueCompleted event.");
                try
                {
                    OnPlaybackQueueCompleted?.Invoke(); // Kiváltjuk az új eseményt
                }
                catch (Exception ex) { Debug.LogError($"[TTS Playback_LOG] Error invoking OnPlaybackQueueCompleted event handler: {ex.Message}\n{ex.StackTrace}"); }
            }
            // --- ÚJ RÉSZ VÉGE ---


            // Klip törlése a természetes befejezés után
            if (playedData.Clip != null) Destroy(playedData.Clip);
        }
        else
        {
            // Megszakadt lejátszás (pause, klipváltás, hiba)
            // ... (a meglévő logika változatlan) ...
        }

        currentPlaybackMonitor = null; // Jelzi, hogy ez a monitor végzett
    }

} // <-- Osztály vége
