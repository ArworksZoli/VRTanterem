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
    [SerializeField] private string ttsVoice = "onyx";
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

    /// <param name="key">OpenAI API Key</param>
    public void Initialize(string key)
    {
        if (string.IsNullOrEmpty(key))
        {
            Debug.LogError("[TextToSpeechManager] Initialization failed: API Key is null or empty.", this);
            enabled = false;
            return;
        }
        apiKey = key;
        Debug.Log("[TextToSpeechManager] Initialized successfully with API Key.");

        // Most már elindíthatjuk a korutinokat, mert van API kulcsunk
        if (manageTTSCoroutine == null)
        {
            manageTTSCoroutine = StartCoroutine(ManageTTSRequests());
        }
        if (managePlaybackCoroutine == null)
        {
            managePlaybackCoroutine = StartCoroutine(ManagePlayback());
        }
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
            voice = this.ttsVoice,
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
    // -------------------------------------------
    {
        yield return new WaitForSeconds(0.1f); // Várakozás a Play() után
        // yield return new WaitUntil(() => !audioSource.isPlaying); // <<< EREDETI VÁRAKOZÁS
        // --- MÓDOSÍTÁS: Robusztusabb várakozás (leállás vagy klipváltás esetére is) ---
        yield return new WaitUntil(() => audioSource == null || !audioSource.isPlaying || audioSource.clip != playedData.Clip);
        // --------------------------------------------------------------------------

        // --- ÚJ: Ellenőrzés, hogy a korutin le lett-e állítva külsőleg (pl. ResetManager által) ---
        if (currentPlaybackMonitor == null)
        {
            // Ha a monitor null, az azt jelenti, hogy ezt a korutint már leállították.
            // Nem kell semmit csinálni, a leállító felelős a cleanup-ért.
            // Debug.Log($"[TTS Playback Monitor] Monitor for sentence {playedData.Index} was stopped externally. Exiting.");
            yield break; // Kilépünk a korutinból
        }
        // ---------------------------------------------------------------------------------------

        // --- MÓDOSÍTÁS: Csak akkor hívjuk az eseményt és törlünk, ha természetesen fejeződött be ---
        // Ellenőrizzük, hogy az audioSource még létezik, nem játszik, ÉS még mindig a mi klipünk van benne.
        if (audioSource != null && !audioSource.isPlaying && audioSource.clip == playedData.Clip)
        // ---------------------------------------------------------------------------------------
        {
            // Debug.Log("[TTS Playback Monitor] Playback finished."); // Eredeti log
            // Debug.Log($"[TTS Playback Monitor] Playback finished naturally for sentence {playedData.Index}."); // Új log indexszel

            // OnTTSPlaybackEnd?.Invoke(); // <<< EREDETI HÍVÁS
            // --- MÓDOSÍTÁS: Esemény kiváltása indexszel, try-catch blokkban ---
            try
            {
                OnTTSPlaybackEnd?.Invoke(playedData.Index);
            }
            catch (Exception ex)
            {
                // Hibakezelés, ha az eseményre feliratkozott kód hibát dob
                Debug.LogError($"[TTS Playback] Error invoking OnTTSPlaybackEnd event handler for index {playedData.Index}: {ex.Message}\n{ex.StackTrace}");
            }
            // -----------------------------------------------------------------

            // if (playedClip != null) // <<< EREDETI FELTÉTEL
            // --- MÓDOSÍTÁS: Klip törlése az adatból ---
            if (playedData.Clip != null)
            {
                // Destroy(playedClip); // <<< EREDETI
                Destroy(playedData.Clip); // <<< MÓDOSÍTÁS
                // Debug.Log($"[TTS Playback Monitor] Destroyed played clip for sentence {playedData.Index}."); // Új log
            }
            // -----------------------------------------
        }
        else
        {
            // Ha nem természetesen fejeződött be (pl. Stop() hívás, klip csere), akkor nem hívjuk az OnTTSPlaybackEnd-et
            // és a klip törlését a leállítást végző kódra bízzuk (pl. ResetManager vagy a következő Play hívás).
            // Debug.Log($"[TTS Playback Monitor] Playback for sentence {playedData.Index} did not finish naturally (likely stopped or clip changed). No event fired, clip cleanup deferred.");

            // Opcionális: Biztonsági törlés itt is, ha a klip még létezik, de ez redundáns lehet.
            // if (playedData.Clip != null) { Destroy(playedData.Clip); }
        }

        // Nullázzuk a monitort, jelezve, hogy a következő lejátszás indulhat
        // (a ManagePlayback WaitUntil-ja feloldódhat, ha van új elem a sorban)
        currentPlaybackMonitor = null;
    }
    // --- ÚJ Korutin VÉGE ---

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
