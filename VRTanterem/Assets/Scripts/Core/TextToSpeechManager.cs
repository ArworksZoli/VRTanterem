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
    [SerializeField] private AudioSource audioSource;

    public event Action<int> OnTTSPlaybackStart;
    public event Action<int> OnTTSPlaybackEnd;
    public event Action<string> OnTTSError;
    public bool IsPlaying => audioSource != null && audioSource.isPlaying;

    // Belső állapotok és várólisták
    private string apiKey;
    private StringBuilder sentenceBuffer = new StringBuilder();
    private Queue<SentenceData> pendingSentencesQueue = new Queue<SentenceData>();
    private Queue<SentenceData> playbackQueue = new Queue<SentenceData>();
    private int sentenceCounter = 0;

    private bool isTTSRequestInProgress = false;
    private Coroutine manageTTSCoroutine;
    private Coroutine managePlaybackCoroutine;
    private Coroutine currentPlaybackMonitor = null;

    // Struktúra az index, szöveg és klip tárolásához
    private struct SentenceData
    {
        public int Index;
        public string Text;
        public AudioClip Clip;
    }

    void Start()
    {
        // Ellenőrizzük, hogy az AudioSource be van-e állítva
        if (audioSource == null)
        {
            Debug.LogError("[TextToSpeechManager] AudioSource is not assigned in the Inspector! TTS will not work.", this);
            enabled = false; // Letiltjuk a komponenst
            return;
        }

    }

    /// <summary>
    /// Initializes the TTS Manager with the API key and the selected voice.
    /// Starts the internal coroutines for processing and playback.
    /// Called by OpenAIWebRequest after its own initialization.
    /// </summary>
    /// <param name="key">OpenAI API Key.</param>
    /// <param name="voiceId">The voice ID selected in the menu (e.g., "alloy", "nova").</param>
    public void Initialize(string key, string voiceId)
    {
        Debug.Log($"[TextToSpeechManager] Initialize called. Voice ID: {voiceId}");

        // --- 1. Ellenőrzések ---
        if (string.IsNullOrEmpty(key))
        {
            Debug.LogError("[TextToSpeechManager] Initialization failed: API Key is null or empty.", this);
            enabled = false; // Letiltjuk magunkat, ha nincs kulcs
            return;
        }
        if (string.IsNullOrEmpty(voiceId))
        {
            Debug.LogWarning("[TextToSpeechManager] Initialization Warning: Voice ID is null or empty. Using default or last known voice might occur if not handled.", this);
            // Dönthetsz úgy, hogy itt is letiltod, vagy megpróbálsz egy alapértelmezettet használni.
            // Maradjunk annál, hogy logoljuk, de nem tiltjuk le azonnal.
        }
        if (audioSource == null) // Ezt az ellenőrzést ide is betehetjük
        {
            Debug.LogError("[TextToSpeechManager] Initialization failed: AudioSource is not assigned!", this);
            enabled = false;
            return;
        }

        // --- 2. Konfiguráció Mentése ---
        this.apiKey = key;
        this.currentTtsVoice = voiceId; // Elmentjük a kapott hang ID-t a belső változóba

        Debug.Log($"[TextToSpeechManager] API Key stored. Current TTS Voice set to: {this.currentTtsVoice}");

        // --- 3. Korutinok Indítása ---
        // Csak az inicializálás után indítjuk el a feldolgozó ciklusokat.
        // Ellenőrizzük, hogy ne induljanak újra, ha valamiért többször hívódna meg az Initialize.
        if (manageTTSCoroutine == null)
        {
            manageTTSCoroutine = StartCoroutine(ManageTTSRequests());
            Debug.Log("[TextToSpeechManager] ManageTTSRequests coroutine started.");
        }
        else { Debug.LogWarning("[TextToSpeechManager] ManageTTSRequests coroutine already running."); }

        if (managePlaybackCoroutine == null)
        {
            managePlaybackCoroutine = StartCoroutine(ManagePlayback());
            Debug.Log("[TextToSpeechManager] ManagePlayback coroutine started.");
        }
        else { Debug.LogWarning("[TextToSpeechManager] ManagePlayback coroutine already running."); }

        Debug.Log("[TextToSpeechManager] Initialization complete.");
    }

    public void ResetManager()
    {
        Debug.Log("[TextToSpeechManager] Resetting state...");

        if (audioSource != null && audioSource.isPlaying)
        {
            audioSource.Stop();
        }

        if (currentPlaybackMonitor != null)
        {
            StopCoroutine(currentPlaybackMonitor);
            currentPlaybackMonitor = null;
        }

        isTTSRequestInProgress = false;
        sentenceBuffer.Clear();
        pendingSentencesQueue.Clear(); // Ürítjük a SentenceData sorokat

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
        playbackQueue.Clear(); // Biztos, ami biztos

        // Mondatszámláló nullázása
        sentenceCounter = 0; // <<< Fontos a reset!

        Debug.Log($"[TextToSpeechManager] Reset completed. Cleared {clearedPlayback} clips from playback queue.");
    }

    /// <param name="textDelta">The piece of text received from the stream.</param>
    public void AppendText(string textDelta)
    {
        if (!enabled || string.IsNullOrEmpty(apiKey)) return; // Ne csináljon semmit, ha nincs inicializálva

        sentenceBuffer.Append(textDelta);
        ProcessSentenceBuffer();
    }

    public void FlushBuffer()
    {
        if (!enabled || string.IsNullOrEmpty(apiKey)) return;

        string remainingText = sentenceBuffer.ToString().Trim();
        if (!string.IsNullOrEmpty(remainingText))
        {
            // Debug.Log($"[TTS Flush] Adding remaining buffer content to queue: '{remainingText.Substring(0, Math.Min(remainingText.Length, 50))}...'"); // Eredeti log
            // --- MÓDOSÍTÁS: SentenceData létrehozása indexszel ---
            pendingSentencesQueue.Enqueue(new SentenceData { Index = sentenceCounter, Text = remainingText });
            // Debug.Log($"[TTS Flush] Enqueued remaining buffer (Index: {sentenceCounter})"); // Új log indexszel
            sentenceCounter++; // Növeljük a számlálót
            // ----------------------------------------------------
        }
        sentenceBuffer.Clear();
    }


    // --- MONDAT DETEKTÁLÁS ---
    private void ProcessSentenceBuffer()
    {
        int searchStartIndex = 0;
        while (true)
        {
            // ... (Mondatvég keresés és `isLikelyEndOfSentence` logika változatlan) ...
            int potentialEndIndex = FindPotentialSentenceEnd(sentenceBuffer, searchStartIndex);
            if (potentialEndIndex == -1) break;
            char punctuation = sentenceBuffer[potentialEndIndex];
            bool isLikelyEndOfSentence = true;
            // ... (számjegy, következő karakter ellenőrzés változatlan) ...


            if (isLikelyEndOfSentence)
            {
                string sentence = sentenceBuffer.ToString(0, potentialEndIndex + 1).Trim();
                if (!string.IsNullOrWhiteSpace(sentence))
                {
                    // pendingSentencesQueue.Enqueue(sentence); // <<< EREDETI SOR
                    // --- MÓDOSÍTÁS: SentenceData létrehozása indexszel ---
                    pendingSentencesQueue.Enqueue(new SentenceData { Index = sentenceCounter, Text = sentence });
                    // Debug.Log($"[TTS Sentence Detected] Enqueued (Index: {sentenceCounter}): '{sentence}'"); // Új log indexszel
                    sentenceCounter++; // Növeljük a számlálót
                    // ----------------------------------------------------
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
                return i;
            }
        }
        return -1;
    }


    // --- TTS KÉRÉS KEZELÉSE ---
    private IEnumerator ManageTTSRequests()
    {
        while (true)
        {
            yield return new WaitUntil(() => !isTTSRequestInProgress &&
                                             pendingSentencesQueue.Count > 0 &&
                                             playbackQueue.Count < maxPlaybackQueueSize);

            // string sentenceToSend = pendingSentencesQueue.Dequeue(); // <<< EREDETI SOR
            // --- MÓDOSÍTÁS: SentenceData Dequeue ---
            SentenceData sentenceData = pendingSentencesQueue.Dequeue();
            // ---------------------------------------
            // Debug.Log($"[TTS Manager] Dequeued sentence for TTS: '{sentenceToSend}'"); // Eredeti log
            // Debug.Log($"[TTS Manager] Dequeued sentence (Index: {sentenceData.Index}) for TTS."); // Új log

            // StartCoroutine(GenerateSpeechCoroutine(sentenceToSend)); // <<< EREDETI HÍVÁS
            // --- MÓDOSÍTÁS: SentenceData átadása ---
            StartCoroutine(GenerateSpeechCoroutine(sentenceData));
            // ---------------------------------------

            // yield return new WaitForSeconds(0.1f);
        }
    }

    private IEnumerator GenerateSpeechCoroutine(SentenceData data)
    // -------------------------------------------
    {
        isTTSRequestInProgress = true;
        // Debug.Log($"[TTS API Call] Sending text: '{text.Substring(0, Math.Min(text.Length, 50))}...'"); // Eredeti log
        // Debug.Log($"[TTS API Call] Sending text (Index: {data.Index}): '{data.Text.Substring(0, Math.Min(data.Text.Length, 50))}...'"); // Új log

        TTSRequestPayload payload = new TTSRequestPayload
        {
            model = this.ttsModel,
            // input = text, // <<< EREDETI
            input = data.Text, // <<< MÓDOSÍTÁS: Adatból vesszük a szöveget
            voice = this.currentTtsVoice,
            response_format = this.ttsResponseFormat,
            speed = this.ttsSpeed
        };
        // ... (JSON, WebRequest beállítások változatlanok) ...
        string jsonPayload = JsonUtility.ToJson(payload);
        byte[] bodyRaw = Encoding.UTF8.GetBytes(jsonPayload);

        using (UnityWebRequest request = new UnityWebRequest(ttsApiUrl, "POST"))
        {
            // ... (request beállítások változatlanok) ...
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
                if (receivedClip != null && receivedClip.loadState == AudioDataLoadState.Loaded)
                {
                    // playbackQueue.Enqueue(receivedClip); // <<< EREDETI SOR
                    // --- MÓDOSÍTÁS: SentenceData Enqueue klippel kiegészítve ---
                    data.Clip = receivedClip; // Klip hozzáadása az adathoz
                    playbackQueue.Enqueue(data); // Teljes adat a sorba
                    // ----------------------------------------------------------
                    // Debug.Log($"[TTS Success] Received AudioClip (Length: {receivedClip.length}s). Playback Queue size: {playbackQueue.Count}"); // Eredeti log
                    // Debug.Log($"[TTS Success] Received AudioClip for sentence {data.Index} (Length: {receivedClip.length}s). Playback Queue size: {playbackQueue.Count}"); // Új log
                }
                else
                {
                    Debug.LogError($"[TTS Error] Failed to get valid AudioClip for sentence {data.Index}. Clip is null or not loaded. Error: {request.error}"); // Index hozzáadva a loghoz
                    OnTTSError?.Invoke($"AudioClip generation failed for sentence {data.Index}."); // <<< ÚJ: Hiba esemény kiváltása
                }
            }
            else
            {
                string errorDetails = request.downloadHandler?.text ?? "No response body";
                Debug.LogError($"[TTS API Error] Sentence {data.Index} - Code: {request.responseCode} - Error: {request.error}\nResponse: {errorDetails}"); // Index hozzáadva a loghoz
                OnTTSError?.Invoke($"TTS API Error for sentence {data.Index}: {request.error}"); // <<< ÚJ: Hiba esemény kiváltása
            }
        }

        isTTSRequestInProgress = false;
    }

    private AudioType GetAudioTypeFromFormat(string format)
    {
        switch (format.ToLower())
        {
            case "mp3": return AudioType.MPEG;
            case "wav": return AudioType.WAV;
            case "ogg": return AudioType.OGGVORBIS;
            default:
                Debug.LogWarning($"[TTS] Unsupported audio format '{format}' for direct AudioClip conversion. Falling back to MPEG (MP3).");
                return AudioType.MPEG;
        }
    }


    // --- LEJÁTSZÁS KEZELÉSE ---
    private IEnumerator ManagePlayback()
    {
        while (true)
        {
            yield return new WaitUntil(() => audioSource != null && !audioSource.isPlaying && playbackQueue.Count > 0); // AudioSource null check hozzáadva

            // AudioClip clipToPlay = playbackQueue.Dequeue(); // <<< EREDETI SOR
            // --- MÓDOSÍTÁS: SentenceData Dequeue ---
            SentenceData dataToPlay = playbackQueue.Dequeue();
            // ---------------------------------------

            // if (clipToPlay != null) // <<< EREDETI FELTÉTEL
            // --- MÓDOSÍTÁS: Feltétel a data klipjére ---
            if (dataToPlay.Clip != null)
            // -----------------------------------------
            {
                // Debug.Log($"[TTS Playback] Playing clip. Length: {clipToPlay.length}s. Remaining in queue: {playbackQueue.Count}"); // Eredeti log
                // Debug.Log($"[TTS Playback] Playing sentence {dataToPlay.Index}. Length: {dataToPlay.Clip.length}s. Remaining in queue: {playbackQueue.Count}"); // Új log

                // OnTTSPlaybackStart?.Invoke(); // <<< EREDETI HÍVÁS
                // --- MÓDOSÍTÁS: Esemény kiváltása indexszel ---
                try { OnTTSPlaybackStart?.Invoke(dataToPlay.Index); }
                catch (Exception ex) { Debug.LogError($"Error in OnTTSPlaybackStart handler: {ex.Message}"); }
                // ---------------------------------------------

                // audioSource.clip = clipToPlay; // <<< EREDETI
                audioSource.clip = dataToPlay.Clip; // <<< MÓDOSÍTÁS
                audioSource.Play();

                // currentPlaybackMonitor = StartCoroutine(MonitorPlaybackEnd(clipToPlay)); // <<< EREDETI HÍVÁS
                // --- MÓDOSÍTÁS: SentenceData átadása a monitornak ---
                currentPlaybackMonitor = StartCoroutine(MonitorPlaybackEnd(dataToPlay));
                // ---------------------------------------------------
            }
            else
            {
                // Debug.LogWarning("[TTS Playback] Dequeued a null AudioClip. Skipping playback."); // Eredeti log
                Debug.LogWarning($"[TTS Playback] Dequeued data for sentence {dataToPlay.Index} with a null AudioClip. Skipping playback."); // Új log indexszel
                // ... (többi hibakezelés változatlan) ...
            }
        }
    }
    private IEnumerator MonitorPlaybackEnd(SentenceData playedData)
    {
        // Kezdeti biztonsági ellenőrzések
        if (audioSource == null || playedData.Clip == null)
        {
            Debug.LogError($"[TTS Playback Monitor] Error: AudioSource or playedData.Clip is null for sentence {playedData.Index}. Aborting monitor.");
            currentPlaybackMonitor = null; // Jelzi, hogy a monitor leállt
            yield break;
        }

        // Debug log: Monitor indítása
        // Debug.Log($"[TTS Playback Monitor] Starting monitor for sentence {playedData.Index} (Clip: {playedData.Clip.name}, Length: {playedData.Clip.length}s)");

        // Várakozás, amíg a lejátszás véget ér VAGY a klip megváltozik
        // Fontos: A WaitUntil lefutása után még ugyanabban a frame-ben vagyunk!
        yield return new WaitUntil(() =>
            audioSource == null ||                  // Ha az AudioSource eltűnik
            audioSource.clip != playedData.Clip ||  // Ha a klip megváltozott alatta
            !audioSource.isPlaying                  // Ha a lejátszás megállt (természetesen vagy Stop() miatt)
        );

        // Debug log: WaitUntil vége
        // Debug.Log($"[TTS Playback Monitor] WaitUntil finished for sentence {playedData.Index}. Checking conditions...");

        // Ellenőrizzük, hogy ezt a korutint nem állították-e le külsőleg (pl. ResetManager)
        // mialatt a WaitUntil futott. Ha igen, ne csináljunk semmit.
        if (currentPlaybackMonitor == null)
        {
            Debug.Log($"[TTS Playback Monitor] Monitor for sentence {playedData.Index} was stopped externally (currentPlaybackMonitor is null). Exiting.");
            // A klipet a leállítást végző kódnak kell kezelnie.
            yield break;
        }

        // Most ellenőrizzük a leállás okát:
        bool clipIsCorrect = (audioSource != null && audioSource.clip == playedData.Clip);
        bool stoppedPlaying = (audioSource != null && !audioSource.isPlaying);

        if (clipIsCorrect && stoppedPlaying)
        {
            // A MI klipünk játszódott le és fejeződött be természetesen.
            Debug.Log($"[TTS Playback Monitor] Playback finished naturally for sentence {playedData.Index}. Firing End event.");

            // --- Esemény kiváltása ---
            try
            {
                OnTTSPlaybackEnd?.Invoke(playedData.Index);
            }
            catch (Exception ex)
            {
                Debug.LogError($"[TTS Playback] Error invoking OnTTSPlaybackEnd event handler for index {playedData.Index}: {ex.Message}\n{ex.StackTrace}");
            }

            // --- Klip megsemmisítése ---
            // Itt már biztonságosan megsemmisíthetjük, mert a lejátszás befejeződött.
            if (playedData.Clip != null) // Dupla ellenőrzés
            {
                // Debug.Log($"[TTS Playback Monitor] Destroying naturally finished clip for sentence {playedData.Index}.");
                Destroy(playedData.Clip);
                // Opcionális: audioSource.clip = null; // Hogy ne maradjon referencia
            }
        }
        else
        {
            // A lejátszás nem természetesen fejeződött be erre a klipre nézve.
            // Okok:
            // 1. A klip megváltozott (audioSource.clip != playedData.Clip) -> a ManagePlayback gyorsabb volt.
            // 2. Az AudioSource eltűnt (audioSource == null).
            // 3. Valamiért még játszik, de a clip már nem a miénk (nem valószínű a WaitUntil miatt).
            string reason = audioSource == null ? "AudioSource is null" :
                            audioSource.clip != playedData.Clip ? "Clip changed" :
                            "Unknown condition";
            Debug.LogWarning($"[TTS Playback Monitor] Playback for sentence {playedData.Index} did NOT finish naturally ({reason}). No End event fired for this index.");

            // Mivel ez a klip már nem fog lejátszódni (vagy mert megváltozott alatta, vagy mert hiba történt),
            // takarítsuk el a memóriából, ha még létezik.
            if (playedData.Clip != null)
            {
                Debug.LogWarning($"[TTS Playback Monitor] Destroying clip for interrupted/superseded sentence {playedData.Index}.");
                Destroy(playedData.Clip);
            }
        }

        // Jelzi, hogy ez a monitor végzett, a ManagePlayback indíthatja a következőt
        // (vagy ha már elindította, akkor ez a korutin most befejeződik).
        currentPlaybackMonitor = null;
    }


    void OnDestroy()
    {
        Debug.Log("[TextToSpeechManager] OnDestroy called. Stopping coroutines and clearing queues.");
        if (manageTTSCoroutine != null) StopCoroutine(manageTTSCoroutine);
        if (managePlaybackCoroutine != null) StopCoroutine(managePlaybackCoroutine);
        if (currentPlaybackMonitor != null) StopCoroutine(currentPlaybackMonitor);

        // Töröljük a memóriából a még lejátszásra váró klipeket is
        // A pendingSentencesQueue-ban nincsenek klipek, csak ürítjük
        pendingSentencesQueue.Clear();

        // A playbackQueue ürítésekor a SentenceData-ból kell a klipet venni
        int clearedPlayback = 0;
        while (playbackQueue.Count > 0)
        {
            // AudioClip clip = playbackQueue.Dequeue(); // <<< EREDETI SOR
            // --- MÓDOSÍTÁS: SentenceData Dequeue és klip törlése ---
            SentenceData data = playbackQueue.Dequeue();
            if (data.Clip != null)
            {
                Destroy(data.Clip);
                clearedPlayback++;
            }
            // ----------------------------------------------------
        }
        playbackQueue.Clear(); // Biztos, ami biztos
        Debug.Log($"[TextToSpeechManager] Destroyed. Cleared {clearedPlayback} clips from playback queue.");
    }
}
