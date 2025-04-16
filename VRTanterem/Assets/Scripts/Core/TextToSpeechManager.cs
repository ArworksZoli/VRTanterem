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

    // --- ESEMÉNYEK ---
    /// <summary>Fired when a regular lecture sentence starts playing.</summary>
    public event Action<int> OnTTSPlaybackStart;
    /// <summary>Fired when a regular lecture sentence finishes playing.</summary>
    public event Action<int> OnTTSPlaybackEnd;
    /// <summary>Fired when a TTS error occurs.</summary>
    public event Action<string> OnTTSError;
    // Nincs külön prompt vége esemény, a SpeakSingleSentence korutinja kezeli a végét.

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

    // --- ÚJ ÁLLAPOTJELZŐK ---
    private bool isPausedForQuestion = false;
    private int resumeFromSentenceIndex = -1; // Honnan kell folytatni a lejátszást

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
        if (audioSource == null)
        {
            Debug.LogError("[TextToSpeechManager] AudioSource is not assigned in the Inspector! TTS will not work.", this);
            enabled = false;
            return;
        }
    }

    void OnDestroy()
    {
        Debug.Log("[TextToSpeechManager] OnDestroy called. Stopping coroutines and clearing queues.");
        StopAllCoroutines(); // Leállítunk minden futó korutint ebben a scriptben

        // Töröljük a memóriából a még lejátszásra váró klipeket is
        ClearQueuesAndClips();
    }

    // --- INICIALIZÁLÁS ÉS RESET ---

    public void Initialize(string key, string voiceId)
    {
        Debug.Log($"[TextToSpeechManager] Initialize called. Voice ID: {voiceId}");
        // ... (A meglévő ellenőrzések és API kulcs/hang beállítás változatlan) ...
        if (string.IsNullOrEmpty(key)) { /*...*/ enabled = false; return; }
        if (string.IsNullOrEmpty(voiceId)) { /*...*/ }
        if (audioSource == null) { /*...*/ enabled = false; return; }

        this.apiKey = key;
        this.currentTtsVoice = voiceId;
        Debug.Log($"[TextToSpeechManager] API Key stored. Current TTS Voice set to: {this.currentTtsVoice}");

        // Korutinok indítása (ha még nem futnak)
        if (manageTTSCoroutine == null)
        {
            manageTTSCoroutine = StartCoroutine(ManageTTSRequests());
            Debug.Log("[TextToSpeechManager] ManageTTSRequests coroutine started.");
        }
        if (managePlaybackCoroutine == null)
        {
            managePlaybackCoroutine = StartCoroutine(ManagePlayback());
            Debug.Log("[TextToSpeechManager] ManagePlayback coroutine started.");
        }

        Debug.Log("[TextToSpeechManager] Initialization complete.");
    }

    public void ResetManager()
    {
        Debug.Log("[TextToSpeechManager] Resetting state...");

        // Lejátszás leállítása
        if (audioSource != null && audioSource.isPlaying)
        {
            audioSource.Stop();
            Debug.Log("[TextToSpeechManager] Stopped active audio playback.");
        }

        // Futó korutinok leállítása (különösen a monitor és prompt)
        if (currentPlaybackMonitor != null)
        {
            StopCoroutine(currentPlaybackMonitor);
            currentPlaybackMonitor = null;
            Debug.Log("[TextToSpeechManager] Stopped current playback monitor coroutine.");
        }
        if (currentPromptCoroutine != null)
        {
            StopCoroutine(currentPromptCoroutine);
            currentPromptCoroutine = null;
            Debug.Log("[TextToSpeechManager] Stopped current prompt coroutine.");
        }
        // A fő ciklusokat (ManageTTSRequests, ManagePlayback) nem állítjuk le itt,
        // mert az Initialize újraindítja őket, ha szükséges. De ha biztosra akarunk menni:
        // if (manageTTSCoroutine != null) { StopCoroutine(manageTTSCoroutine); manageTTSCoroutine = null; }
        // if (managePlaybackCoroutine != null) { StopCoroutine(managePlaybackCoroutine); managePlaybackCoroutine = null; }


        // Állapotjelzők és várólisták törlése
        isTTSRequestInProgress = false;
        isPausedForQuestion = false; // <<< ÚJ: Pause flag reset
        resumeFromSentenceIndex = -1; // <<< ÚJ: Folytatási index reset
        sentenceBuffer.Clear();
        sentenceCounter = 0;

        ClearQueuesAndClips(); // Kiszervezve egy külön metódusba

        Debug.Log($"[TextToSpeechManager] Reset completed.");
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
            Debug.Log($"[TextToSpeechManager] Cleared {clearedPlayback} clips from playback queue.");
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
        Debug.Log("[TextToSpeechManager] PausePlayback called.");
        isPausedForQuestion = true;

        // Ha éppen játszik le valamit az AudioSource, állítsuk le
        if (audioSource != null && audioSource.isPlaying)
        {
            audioSource.Stop();
            Debug.Log("[TextToSpeechManager] Stopped currently playing audio due to pause request.");
        }

        // Ha van aktív lejátszás monitor korutin, állítsuk le,
        // mert a lejátszás megszakadt, nem természetesen ért véget.
        if (currentPlaybackMonitor != null)
        {
            StopCoroutine(currentPlaybackMonitor);
            currentPlaybackMonitor = null; // Fontos nullázni!
            Debug.Log("[TextToSpeechManager] Stopped playback monitor due to pause.");
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
        Debug.Log($"[TextToSpeechManager] ResumePlayback called. Resuming from index: {startFromIndex}");
        resumeFromSentenceIndex = startFromIndex; // Beállítjuk, honnan kell folytatni
        isPausedForQuestion = false; // Engedélyezzük a lejátszást

        // A ManagePlayback korutin automatikusan észreveszi, hogy isPausedForQuestion már false,
        // és a resumeFromSentenceIndex alapján fogja folytatni. Nincs szükség a korutin újraindítására.
        Debug.Log("[TextToSpeechManager] Playback queue processing will resume.");
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
            Debug.LogError("[TextToSpeechManager] Cannot SpeakSingleSentence: Component disabled, API key missing, or AudioSource missing.");
            // Ha hiba van, lehet, hogy mégis engedélyezni kell a gombot, hogy a user tudjon reagálni?
            InteractionFlowManager.Instance?.EnableSpeakButton(); // Próbáljuk meg engedélyezni
            return;
        }

        // Ha már fut egy prompt korutin, állítsuk le az előzőt
        if (currentPromptCoroutine != null)
        {
            Debug.LogWarning("[TextToSpeechManager] SpeakSingleSentence called while another prompt was potentially processing. Stopping previous prompt coroutine.");
            StopCoroutine(currentPromptCoroutine);
        }

        // Indítjuk az új prompt korutint
        currentPromptCoroutine = StartCoroutine(SpeakSingleSentenceCoroutine(text));
    }

    private IEnumerator SpeakSingleSentenceCoroutine(string text)
    {
        Debug.Log($"[TextToSpeechManager] SpeakSingleSentenceCoroutine started for: '{text}'");

        // 1. Várjunk, amíg az AudioSource nem játszik le mást
        if (audioSource.isPlaying)
        {
            Debug.Log("[SpeakSingleSentenceCoroutine] AudioSource is busy. Waiting...");
            yield return new WaitUntil(() => !audioSource.isPlaying);
            Debug.Log("[SpeakSingleSentenceCoroutine] AudioSource became free.");
        }

        // 2. Generáljuk le a hangot ehhez az egy mondathoz
        //    Használhatnánk a GenerateSpeechCoroutine-t, de az a playbackQueue-ba tenne.
        //    Egyszerűbb itt egy dedikált kérést indítani.
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

        Debug.Log("[SpeakSingleSentenceCoroutine] Sending TTS request for prompt...");
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
                if (promptClip != null && promptClip.loadState == AudioDataLoadState.Loaded && promptClip.length > 0)
                {
                    Debug.Log($"[SpeakSingleSentenceCoroutine] Prompt AudioClip generated successfully (Length: {promptClip.length}s).");
                }
                else
                {
                    Debug.LogError("[SpeakSingleSentenceCoroutine] Failed to get valid AudioClip for prompt. Clip is null, not loaded, or empty.");
                    generationError = true;
                    if (promptClip != null) Destroy(promptClip); // Takarítás
                    promptClip = null;
                }
            }
            else
            {
                Debug.LogError($"[SpeakSingleSentenceCoroutine] TTS API Error for prompt: {request.error} - {request.downloadHandler?.text}");
                generationError = true;
            }
        }

        // 3. Játsszuk le a generált klipet (ha sikerült)
        if (!generationError && promptClip != null)
        {
            Debug.Log("[SpeakSingleSentenceCoroutine] Playing prompt clip...");
            audioSource.clip = promptClip;
            audioSource.Play();

            // 4. Várjuk meg, amíg a lejátszás befejeződik
            //    Figyelünk arra is, ha közben leállítanák (pl. ResetManager)
            float startTime = Time.time;
            while (audioSource.isPlaying && audioSource.clip == promptClip && currentPromptCoroutine != null)
            {
                // Biztonsági timeout, ha valamiért beragadna
                if (Time.time - startTime > promptClip.length + 5.0f) // 5 mp ráhagyás
                {
                    Debug.LogError("[SpeakSingleSentenceCoroutine] Playback timeout reached for prompt! Stopping playback.");
                    audioSource.Stop();
                    break;
                }
                yield return null;
            }
            Debug.Log("[SpeakSingleSentenceCoroutine] Prompt playback finished or was interrupted.");
        }
        else
        {
            Debug.LogError("[SpeakSingleSentenceCoroutine] Skipping prompt playback due to generation error.");
        }

        // 5. Hívjuk meg az InteractionFlowManager-t a beszéd gomb engedélyezéséhez
        //    Akkor is meghívjuk, ha hiba történt a generálás/lejátszás során,
        //    hogy a folyamat ne akadjon el teljesen.
        if (InteractionFlowManager.Instance != null)
        {
            Debug.Log("[SpeakSingleSentenceCoroutine] Enabling Speak Button via InteractionFlowManager.");
            InteractionFlowManager.Instance.EnableSpeakButton();
        }
        else
        {
            Debug.LogError("[SpeakSingleSentenceCoroutine] Cannot enable speak button: InteractionFlowManager.Instance is null!");
        }

        // 6. Takarítás: Töröljük a prompt klipet
        if (promptClip != null)
        {
            Destroy(promptClip);
            // Debug.Log("[SpeakSingleSentenceCoroutine] Destroyed prompt clip.");
        }

        // 7. Nullázzuk a korutin referenciát, jelezve, hogy befejeződött
        currentPromptCoroutine = null;
        Debug.Log("[SpeakSingleSentenceCoroutine] Finished.");
    }


    // --- LEJÁTSZÁS CIKLUS (ManagePlayback, MonitorPlaybackEnd) ---

    private IEnumerator ManagePlayback()
    {
        while (true)
        {
            // Vár, amíg:
            // 1. Nincs pause kérdés miatt VAGY a pause fel lett oldva.
            // 2. Az AudioSource létezik és éppen nem játszik le semmit.
            // 3. Van lejátszandó klip a várólistában.
            yield return new WaitUntil(() => !isPausedForQuestion &&
                                             audioSource != null &&
                                             !audioSource.isPlaying &&
                                             playbackQueue.Count > 0);

            // Ellenőrizzük, hogy a sor elején lévő elem indexe megfelelő-e a folytatáshoz
            SentenceData dataToPlay = playbackQueue.Peek(); // Csak megnézzük, nem vesszük ki még!

            // Ha a resumeFromSentenceIndex be van állítva (nem -1), és a soron következő
            // mondat indexe KISEBB, mint ahonnan folytatni kell, akkor ugorjuk át.
            if (resumeFromSentenceIndex != -1 && dataToPlay.Index < resumeFromSentenceIndex)
            {
                playbackQueue.Dequeue(); // Kivesszük a sorból
                Debug.Log($"[TTS Playback] Skipping sentence {dataToPlay.Index} (Resuming from {resumeFromSentenceIndex}).");
                if (dataToPlay.Clip != null) Destroy(dataToPlay.Clip); // Töröljük a felesleges klipet
                continue; // Vissza a ciklus elejére, hogy a következőt ellenőrizze
            }

            // Ha idáig eljutottunk, akkor vagy nincs folytatási index, vagy elértük a megfelelő indexet.
            // Most már kivehetjük az elemet a sorból.
            dataToPlay = playbackQueue.Dequeue();
            resumeFromSentenceIndex = -1; // Nullázzuk a folytatási indexet, mert megtaláltuk/elindítjuk

            if (dataToPlay.Clip != null)
            {
                // Debug.Log($"[TTS Playback] Playing sentence {dataToPlay.Index}. Length: {dataToPlay.Clip.length}s. Remaining in queue: {playbackQueue.Count}");

                // Esemény kiváltása a lejátszás kezdetéről
                try { OnTTSPlaybackStart?.Invoke(dataToPlay.Index); }
                catch (Exception ex) { Debug.LogError($"Error in OnTTSPlaybackStart handler: {ex.Message}"); }

                audioSource.clip = dataToPlay.Clip;
                audioSource.Play();

                // Indítjuk a monitor korutint, ami figyeli a lejátszás végét
                // és kiváltja az OnTTSPlaybackEnd eseményt.
                if (currentPlaybackMonitor != null) StopCoroutine(currentPlaybackMonitor); // Biztonsági leállítás, ha valamiért maradt volna
                currentPlaybackMonitor = StartCoroutine(MonitorPlaybackEnd(dataToPlay));
            }
            else
            {
                Debug.LogWarning($"[TTS Playback] Dequeued data for sentence {dataToPlay.Index} with a null AudioClip. Skipping playback.");
                // Kiváltjuk a vége eseményt is, hogy a Highlighter/Flow Manager tudjon továbblépni
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
        if (audioSource == null || playedData.Clip == null)
        {
            Debug.LogError($"[TTS Playback Monitor] Error: AudioSource or playedData.Clip is null for sentence {playedData.Index}. Aborting monitor.");
            currentPlaybackMonitor = null;
            yield break;
        }

        // Várakozás, amíg a lejátszás véget ér VAGY a klip megváltozik VAGY pause van
        yield return new WaitUntil(() =>
            isPausedForQuestion ||                  // <<< ÚJ: Pause esetén is kilépünk
            audioSource == null ||
            audioSource.clip != playedData.Clip ||
            !audioSource.isPlaying
        );

        // Ellenőrizzük, hogy ezt a korutint nem állították-e le külsőleg (pl. ResetManager, PausePlayback)
        if (currentPlaybackMonitor == null)
        {
            // Debug.Log($"[TTS Playback Monitor] Monitor for sentence {playedData.Index} was stopped externally. Clip cleanup handled elsewhere.");
            yield break; // A klipet a leállító kód kezeli
        }

        // Ellenőrizzük a leállás okát
        bool stoppedForPause = isPausedForQuestion;
        bool clipIsCorrect = (audioSource != null && audioSource.clip == playedData.Clip);
        bool stoppedPlayingNaturally = (audioSource != null && !audioSource.isPlaying);

        if (!stoppedForPause && clipIsCorrect && stoppedPlayingNaturally)
        {
            // Természetes befejezés
            // Debug.Log($"[TTS Playback Monitor] Playback finished naturally for sentence {playedData.Index}. Firing End event.");
            try { OnTTSPlaybackEnd?.Invoke(playedData.Index); }
            catch (Exception ex) { Debug.LogError($"[TTS Playback] Error invoking OnTTSPlaybackEnd event handler for index {playedData.Index}: {ex.Message}\n{ex.StackTrace}"); }

            if (playedData.Clip != null) Destroy(playedData.Clip);
        }
        else
        {
            // Megszakadt lejátszás (pause, klipváltás, hiba)
            string reason = stoppedForPause ? "Paused for question" :
                            audioSource == null ? "AudioSource is null" :
                            audioSource.clip != playedData.Clip ? "Clip changed" :
                            "Playback interrupted";
            // Debug.LogWarning($"[TTS Playback Monitor] Playback for sentence {playedData.Index} did NOT finish naturally ({reason}). No End event fired by this monitor instance.");

            // Ha pause miatt álltunk le, NE töröljük a klipet, mert még kellhet a folytatáshoz!
            if (!stoppedForPause && playedData.Clip != null)
            {
                // Debug.LogWarning($"[TTS Playback Monitor] Destroying clip for interrupted/superseded sentence {playedData.Index}.");
                Destroy(playedData.Clip);
            }
            else if (stoppedForPause)
            {
                // Debug.Log($"[TTS Playback Monitor] Playback paused for sentence {playedData.Index}. Clip kept for potential resume.");
            }
        }

        currentPlaybackMonitor = null; // Jelzi, hogy ez a monitor végzett
    }

} // <-- Osztály vége
