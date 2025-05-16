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
    private Coroutine currentAnswerPlaybackCoroutine = null;
    private Queue<AudioClip> answerAudioQueue = new Queue<AudioClip>();

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
        Debug.Log("[TextToSpeechManager_LOG] Resetting state...");

        // Lejátszás leállítása mindkét AudioSource-on
        if (audioSource != null && audioSource.isPlaying)
        {
            audioSource.Stop();
            Debug.Log("[TextToSpeechManager_LOG] Stopped active audio playback on main AudioSource.");
        }
        if (promptAudioSource != null && promptAudioSource.isPlaying) // <<< ÚJ BLOKK
        {
            promptAudioSource.Stop();
            Debug.Log("[TextToSpeechManager_LOG] Stopped active audio playback on prompt AudioSource.");
        }

        // Futó korutinok leállítása (különösen a monitor és prompt)
        if (currentPlaybackMonitor != null)
        {
            StopCoroutine(currentPlaybackMonitor);
            currentPlaybackMonitor = null;
            Debug.Log("[TextToSpeechManager_LOG] Stopped current playback monitor coroutine.");
        }
        if (currentPromptCoroutine != null)
        {
            StopCoroutine(currentPromptCoroutine);
            currentPromptCoroutine = null;
            Debug.Log("[TextToSpeechManager_LOG] Stopped current prompt coroutine.");
        }

        if (currentAnswerPlaybackCoroutine != null) // <<< ÚJ SOR >>>
        {
            StopCoroutine(currentAnswerPlaybackCoroutine);
            currentAnswerPlaybackCoroutine = null;
            Debug.Log("[TextToSpeechManager_LOG] Stopped current answer playback coroutine.");
        }

        // Állapotjelzők és várólisták törlése
        isTTSRequestInProgress = false;
        isPausedForQuestion = false;
        resumeFromSentenceIndex = -1;
        sentenceBuffer.Clear();
        sentenceCounter = 0;
        answerSentenceBuffer.Clear();

        ClearQueuesAndClips();
        ClearAnswerQueueAndClips();

        Debug.Log($"[TextToSpeechManager_LOG] Reset completed.");
    }

    private void ClearAnswerQueueAndClips()
    {
        int clearedAnswerClips = 0;
        while (answerAudioQueue.Count > 0)
        {
            AudioClip clip = answerAudioQueue.Dequeue();
            if (clip != null)
            {
                Destroy(clip);
                clearedAnswerClips++;
            }
        }
        if (clearedAnswerClips > 0)
        {
            Debug.Log($"[TextToSpeechManager_LOG] Cleared {clearedAnswerClips} clips from answer queue.");
        }
    }

    /// <summary>
    /// Appends text chunks for an immediate AI answer (played on promptAudioSource).
    /// </summary>
    public void AppendAnswerText(string textDelta)
    {
        if (!enabled || string.IsNullOrEmpty(apiKey) || promptAudioSource == null) return;
        Debug.Log($"[TTS LOG Answer] AppendAnswerText Received: '{textDelta}'");
        answerSentenceBuffer.Append(textDelta);
        ProcessAnswerSentenceBuffer();
    }

    /// <summary>
    /// Processes the answer buffer to find sentences and starts TTS generation for them.
    /// </summary>
    private void ProcessAnswerSentenceBuffer()
    {
        // Hasonló a ProcessSentenceBuffer-hez, de az answerSentenceBuffer-t használja
        // és a GenerateAndPlayAnswerSentence korutint hívja.
        int searchStartIndex = 0;
        while (true)
        {
            int potentialEndIndex = FindPotentialSentenceEnd(answerSentenceBuffer, searchStartIndex); // Ugyanaz a segédfüggvény jó
            if (potentialEndIndex == -1) break;

            // Itt most nem bonyolítjuk a mondatvégi logika finomításával, mint a fő buffernél
            string sentence = answerSentenceBuffer.ToString(0, potentialEndIndex + 1).Trim();
            if (!string.IsNullOrWhiteSpace(sentence))
            {
                Debug.LogWarning($"[TTS LOG Answer] Sentence Detected for Answer: '{sentence.Substring(0, Math.Min(sentence.Length, 50))}...'");
                // Közvetlenül indítjuk a generálást és lejátszást ehhez a mondathoz
                StartCoroutine(GenerateAndEnqueueAnswerAudio(sentence));
            }
            answerSentenceBuffer.Remove(0, potentialEndIndex + 1);
            searchStartIndex = 0; // Reset search index after removing part of the buffer
        }

        // Indítsuk el a lejátszási korutint, ha még nem fut és van hang a sorban
        if (answerAudioQueue.Count > 0 && currentAnswerPlaybackCoroutine == null)
        {
            currentAnswerPlaybackCoroutine = StartCoroutine(PlayAnswerAudioCoroutine());
        }
    }

    /// <summary>
    /// Flushes any remaining text in the answer buffer as a final sentence.
    /// </summary>
    public void FlushAnswerBuffer()
    {
        if (!enabled || string.IsNullOrEmpty(apiKey)) return;
        string remainingText = answerSentenceBuffer.ToString().Trim();
        if (!string.IsNullOrEmpty(remainingText))
        {
            Debug.LogWarning($"[TTS LOG Answer] Flushing remaining answer text: '{remainingText.Substring(0, Math.Min(remainingText.Length, 50))}...'");
            StartCoroutine(GenerateAndEnqueueAnswerAudio(remainingText));
        }
        answerSentenceBuffer.Clear();

        // Indítsuk el a lejátszási korutint, ha még nem fut és van hang a sorban
        if (answerAudioQueue.Count > 0 && currentAnswerPlaybackCoroutine == null)
        {
            currentAnswerPlaybackCoroutine = StartCoroutine(PlayAnswerAudioCoroutine());
        }
    }


    /// <summary>
    /// Generates audio for a single answer sentence and adds it to the answerAudioQueue.
    /// </summary>
    private IEnumerator GenerateAndEnqueueAnswerAudio(string sentence)
    {
        // Hasonló a GenerateSpeechCoroutine-hoz, de az answerAudioQueue-ba tesz
        Debug.Log($"[TTS LOG Answer] GenerateAndEnqueueAnswerAudio START: '{sentence.Substring(0, Math.Min(sentence.Length, 50))}...'");

        // TTS API hívás (ugyanaz a logika, mint GenerateSpeechCoroutine-ban)
        TTSRequestPayload payload = new TTSRequestPayload
        { /* ... kitöltés ... */
            model = this.ttsModel,
            input = sentence,
            voice = this.currentTtsVoice,
            response_format = this.ttsResponseFormat,
            speed = this.ttsSpeed
        };
        string jsonPayload = JsonUtility.ToJson(payload);
        byte[] bodyRaw = Encoding.UTF8.GetBytes(jsonPayload);
        AudioClip generatedClip = null;

        using (UnityWebRequest request = new UnityWebRequest(ttsApiUrl, "POST"))
        {
            // ... (request setup, SendWebRequest) ...
            request.uploadHandler = new UploadHandlerRaw(bodyRaw);
            AudioType audioType = GetAudioTypeFromFormat(ttsResponseFormat);
            request.downloadHandler = new DownloadHandlerAudioClip(new Uri(ttsApiUrl), audioType);
            request.SetRequestHeader("Authorization", $"Bearer {apiKey}");
            request.SetRequestHeader("Content-Type", "application/json");
            request.timeout = 60;
            yield return request.SendWebRequest();

            if (request.result == UnityWebRequest.Result.Success)
            {
                AudioClip receivedClip = DownloadHandlerAudioClip.GetContent(request);
                if (receivedClip != null && receivedClip.loadState == AudioDataLoadState.Loaded && receivedClip.length > 0)
                {
                    generatedClip = receivedClip;
                    Debug.LogWarning($"[TTS LOG Answer] Generate SUCCESS. Clip Length: {generatedClip.length}s.");
                }
                else { /* Hibakezelés, logolás */ Debug.LogError("[TTS LOG Answer] Generate FAILED (Invalid Clip)."); }
            }
            else { /* Hibakezelés, logolás */ Debug.LogError($"[TTS LOG Answer] Generate FAILED (API Error): {request.error}"); }
        }

        if (generatedClip != null)
        {
            answerAudioQueue.Enqueue(generatedClip);
            Debug.Log($"[TTS LOG Answer] Enqueued answer audio. Answer Queue Size: {answerAudioQueue.Count}");

            // Indítsuk el a lejátszási korutint, ha még nem fut
            if (currentAnswerPlaybackCoroutine == null)
            {
                currentAnswerPlaybackCoroutine = StartCoroutine(PlayAnswerAudioCoroutine());
            }
        }
        Debug.Log($"[TTS LOG Answer] GenerateAndEnqueueAnswerAudio END");
    }

    /// <summary>
    /// Plays audio clips from the answerAudioQueue sequentially on the promptAudioSource.
    /// </summary>
    private IEnumerator PlayAnswerAudioCoroutine()
    {
        Debug.LogWarning("[TTS LOG Answer] PlayAnswerAudioCoroutine STARTED.");

        // Logoljuk a választ a transcriptbe... (komment változatlan)

        while (answerAudioQueue.Count > 0)
        {
            // Várjunk, amíg a promptAudioSource nem játszik
            yield return new WaitUntil(() => promptAudioSource != null && !promptAudioSource.isPlaying);

            if (promptAudioSource == null) // Extra ellenőrzés
            {
                Debug.LogError("[TTS LOG Answer] promptAudioSource became null during playback! Stopping.");
                break;
            }

            AudioClip clipToPlay = answerAudioQueue.Dequeue();
            if (clipToPlay != null)
            {
                Debug.Log($"[TTS LOG Answer] Playing answer clip on promptAudioSource. Length: {clipToPlay.length}s. Remaining in Answer Queue: {answerAudioQueue.Count}");
                promptAudioSource.clip = clipToPlay;
                promptAudioSource.Play();

                // Várjunk a klip végéig (vagy amíg le nem állítják)
                float startTime = Time.time;
                // <<< KIS PONTOSÍTÁS A LOGIKÁBAN: Elég csak az isPlaying-et figyelni, ha a Play() után vagyunk >>>
                // yield return new WaitWhile(() => promptAudioSource.isPlaying && promptAudioSource.clip == clipToPlay && (Time.time - startTime < clipToPlay.length + 5f)); // Timeout
                yield return new WaitWhile(() => promptAudioSource.isPlaying);
                Debug.LogWarning($"[TTS LOG Answer] promptAudioSource.isPlaying is now false for clip. (Actual time: {Time.time - startTime}s vs Clip length: {clipToPlay.length}s)");


                // Klip törlése lejátszás után
                // <<< FONTOS: Csak akkor töröljük, ha biztosan mi hoztuk létre dinamikusan! >>>
                // Ha ez egy előre betöltött klip lenne, ez hibát okozna. TTS esetén valószínűleg helyes.
                if (clipToPlay != null) // Biztonsági ellenőrzés, hátha közben null lett
                {
                    Destroy(clipToPlay);
                    // Debug.Log($"[TTS LOG Answer] Destroyed played answer clip."); // Opcionális log
                }
            }
            else
            {
                Debug.LogWarning("[TTS LOG Answer] Dequeued a null clip from answer queue.");
            }
        }

        Debug.LogWarning("[TTS LOG Answer] PlayAnswerAudioCoroutine FINISHED (Queue Empty). Attempting to call IFM Handler...");

        // --- FONTOS: Jelezzük az IFM-nek, hogy a válasz lejátszása befejeződött ---
        // <<< ÚJ LOGOK KEZDETE >>>
        if (InteractionFlowManager.Instance != null)
        {
            Debug.LogWarning("[TTS LOG Answer] InteractionFlowManager.Instance is VALID. Calling HandleAnswerPlaybackCompleted()...");
            try
            {
                InteractionFlowManager.Instance.HandleAnswerPlaybackCompleted();
                Debug.LogWarning("[TTS LOG Answer] InteractionFlowManager.Instance.HandleAnswerPlaybackCompleted() called successfully.");
            }
            catch (Exception ex)
            {
                Debug.LogError($"[TTS LOG Answer] Exception during HandleAnswerPlaybackCompleted call: {ex.Message}\n{ex.StackTrace}");
            }
        }
        else
        {
            Debug.LogError("[TTS LOG Answer] InteractionFlowManager.Instance is NULL! Cannot call HandleAnswerPlaybackCompleted.");
        }
        // <<< ÚJ LOGOK VÉGE >>>


        currentAnswerPlaybackCoroutine = null; // Jelzi, hogy a korutin végzett
        Debug.LogWarning("[TTS LOG Answer] PlayAnswerAudioCoroutine coroutine reference nulled."); // <<< ÚJ LOG >>>
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
