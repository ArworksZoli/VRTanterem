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
    [SerializeField] private string ttsVoice = "onyx"; // Ahogy kérted
    [SerializeField] private string ttsResponseFormat = "mp3";
    [SerializeField] private string ttsApiUrl = "https://api.openai.com/v1/audio/speech";
    [Tooltip("Playback speed. 1.0 is normal speed. Range: 0.25 to 4.0")]
    [Range(0.25f, 4.0f)] // Ez egy csúszkát ad az Inspectorban
    [SerializeField] private float ttsSpeed = 1.0f;
    [Tooltip("Maximum number of audio clips to keep ready for playback.")]
    [SerializeField] private int maxPlaybackQueueSize = 3; // Korlátozott előretekintés

    [Header("Components")]
    [SerializeField] private AudioSource audioSource;

    // Belső állapotok és várólisták
    private string apiKey; // Ezt az OpenAIWebRequest fogja beállítani
    private StringBuilder sentenceBuffer = new StringBuilder(); // Beérkező szöveg gyűjtése mondatokká
    private Queue<string> pendingSentencesQueue = new Queue<string>(); // Mondatok, amik TTS generálásra várnak
    private Queue<AudioClip> playbackQueue = new Queue<AudioClip>(); // Kész AudioClip-ek lejátszásra várva
    private bool isTTSRequestInProgress = false; // Fut-e éppen TTS API kérés?
    private Coroutine manageTTSCoroutine;
    private Coroutine managePlaybackCoroutine;


    void Start()
    {
        // Ellenőrizzük, hogy az AudioSource be van-e állítva
        if (audioSource == null)
        {
            Debug.LogError("[TextToSpeechManager] AudioSource is not assigned in the Inspector! TTS will not work.", this);
            enabled = false; // Letiltjuk a komponenst
            return;
        }

        // Indítjuk a kezelő korutinokat, de csak ha inicializálva lettünk (API kulcsot kaptunk)
        // A korutinok indítását áttesszük az Initialize metódusba.
    }

    /// <summary>
    /// Initializes the TTS Manager with the necessary API key.
    /// Called by OpenAIWebRequest.
    /// </summary>
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

    /// <summary>
    /// Appends incoming text delta to the sentence buffer and processes it.
    /// Called by OpenAIWebRequest.
    /// </summary>
    /// <param name="textDelta">The piece of text received from the stream.</param>
    public void AppendText(string textDelta)
    {
        if (!enabled || string.IsNullOrEmpty(apiKey)) return; // Ne csináljon semmit, ha nincs inicializálva

        sentenceBuffer.Append(textDelta);
        ProcessSentenceBuffer();
    }

    /// <summary>
    /// Processes any remaining text in the buffer, typically called at the end of a stream.
    /// </summary>
    public void FlushBuffer()
    {
        if (!enabled || string.IsNullOrEmpty(apiKey)) return;

        // Hozzáadja a maradékot, még ha nincs is mondatvégi jel (trimmelve)
        string remainingText = sentenceBuffer.ToString().Trim();
        if (!string.IsNullOrEmpty(remainingText))
        {
            Debug.Log($"[TTS Flush] Adding remaining buffer content to queue: '{remainingText.Substring(0, Math.Min(remainingText.Length, 50))}...'");
            pendingSentencesQueue.Enqueue(remainingText);
        }
        sentenceBuffer.Clear();
    }


    // --- MONDAT DETEKTÁLÁS ---
    private void ProcessSentenceBuffer()
    {
        int searchStartIndex = 0;
        while (true) // Ciklus, amíg találunk feldolgozható mondatot
        {
            int potentialEndIndex = FindPotentialSentenceEnd(sentenceBuffer, searchStartIndex);

            if (potentialEndIndex == -1) break; // Nincs több potenciális vég

            char punctuation = sentenceBuffer[potentialEndIndex];
            bool isLikelyEndOfSentence = true;

            // 1. Számjegy ellenőrzés pont esetén
            if (punctuation == '.' && potentialEndIndex > 0 && char.IsDigit(sentenceBuffer[potentialEndIndex - 1]))
            {
                isLikelyEndOfSentence = false;
            }

            // 2. Következő karakter ellenőrzés (ha még mindig esélyes)
            if (isLikelyEndOfSentence)
            {
                bool isLastChar = potentialEndIndex == sentenceBuffer.Length - 1;
                bool isFollowedByWhitespace = !isLastChar && char.IsWhiteSpace(sentenceBuffer[potentialEndIndex + 1]);
                // Finomítás: Elfogadjuk, ha idézőjel követi, amit whitespace követ
                bool isFollowedByQuoteThenSpace = !isLastChar && (sentenceBuffer[potentialEndIndex + 1] == '"' || sentenceBuffer[potentialEndIndex + 1] == '\'') &&
                                                 (potentialEndIndex + 2 == sentenceBuffer.Length || char.IsWhiteSpace(sentenceBuffer[potentialEndIndex + 2]));

                if (!isLastChar && !isFollowedByWhitespace && !isFollowedByQuoteThenSpace)
                {
                    // Ha pont és nem követi szóköz/idézőjel+szóköz/buffer vége, akkor valószínűleg nem mondatvég
                    if (punctuation == '.')
                    {
                        isLikelyEndOfSentence = false;
                    }
                    // ? és ! esetén engedékenyebbek vagyunk, mert ritkábban fordulnak elő mondat közben rosszul
                }
            }

            if (isLikelyEndOfSentence)
            {
                // Megvan a mondat!
                string sentence = sentenceBuffer.ToString(0, potentialEndIndex + 1).Trim();
                if (!string.IsNullOrWhiteSpace(sentence))
                {
                    pendingSentencesQueue.Enqueue(sentence);
                    // Debug.Log($"[TTS Sentence Detected] Queueing: '{sentence}'");
                }
                sentenceBuffer.Remove(0, potentialEndIndex + 1);
                searchStartIndex = 0; // Újra kell kezdeni a keresést
            }
            else
            {
                // Ez nem volt igazi mondatvég, keressünk tovább
                searchStartIndex = potentialEndIndex + 1;
                if (searchStartIndex >= sentenceBuffer.Length) break; // Elértük a végét
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
        while (true) // Folyamatosan fut a háttérben
        {
            // Várakozás, amíg indíthatunk új kérést
            yield return new WaitUntil(() => !isTTSRequestInProgress &&
                                             pendingSentencesQueue.Count > 0 &&
                                             playbackQueue.Count < maxPlaybackQueueSize);

            // Indíthatunk egyet
            string sentenceToSend = pendingSentencesQueue.Dequeue();
            // Debug.Log($"[TTS Manager] Dequeued sentence for TTS: '{sentenceToSend}'");
            StartCoroutine(GenerateSpeechCoroutine(sentenceToSend));

            // Opcionális: Rövid várakozás, hogy ne terheljük túl azonnal az API-t
            // yield return new WaitForSeconds(0.1f);
        }
    }

    private IEnumerator GenerateSpeechCoroutine(string text)
    {
        isTTSRequestInProgress = true;
        // Debug.Log($"[TTS API Call] Sending text: '{text.Substring(0, Math.Min(text.Length, 50))}...'");

        TTSRequestPayload payload = new TTSRequestPayload
        {
            model = this.ttsModel,
            input = text,
            voice = this.ttsVoice,
            response_format = this.ttsResponseFormat,
            speed = this.ttsSpeed
        };
        string jsonPayload = JsonUtility.ToJson(payload); // Egyszerűbb esetekre jó
        byte[] bodyRaw = Encoding.UTF8.GetBytes(jsonPayload);

        using (UnityWebRequest request = new UnityWebRequest(ttsApiUrl, "POST"))
        {
            request.uploadHandler = new UploadHandlerRaw(bodyRaw);
            AudioType audioType = GetAudioTypeFromFormat(ttsResponseFormat);
            request.downloadHandler = new DownloadHandlerAudioClip(new Uri(ttsApiUrl), audioType); // Uri szükséges

            request.SetRequestHeader("Authorization", $"Bearer {apiKey}");
            request.SetRequestHeader("Content-Type", "application/json");
            request.timeout = 60; // Adjunk neki időt

            yield return request.SendWebRequest();

            if (request.result == UnityWebRequest.Result.Success)
            {
                AudioClip receivedClip = DownloadHandlerAudioClip.GetContent(request);
                if (receivedClip != null && receivedClip.loadState == AudioDataLoadState.Loaded) // Ellenőrizzük a betöltést is!
                {
                    playbackQueue.Enqueue(receivedClip);
                    // Debug.Log($"[TTS Success] Received AudioClip (Length: {receivedClip.length}s). Playback Queue size: {playbackQueue.Count}");
                }
                else
                {
                    Debug.LogError($"[TTS Error] Failed to get valid AudioClip from TTS response. Clip is null or not loaded. Error: {request.error}");
                    // Itt nem tudjuk lejátszani, de a kérés lefutott
                }
            }
            else
            {
                Debug.LogError($"[TTS API Error] Code: {request.responseCode} - Error: {request.error}\nResponse: {request.downloadHandler?.text}");
            }
        } // using automatikusan Dispose-olja a request-et

        isTTSRequestInProgress = false; // Lezárult a kérés (akár sikeres, akár nem)
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
        while (true) // Folyamatosan fut
        {
            // Várjuk meg, amíg az AudioSource szabad ÉS van mit lejátszani
            yield return new WaitUntil(() => !audioSource.isPlaying && playbackQueue.Count > 0);

            // Van mit lejátszani és az AudioSource szabad
            AudioClip clipToPlay = playbackQueue.Dequeue();

            if (clipToPlay != null)
            {
                // Debug.Log($"[TTS Playback] Playing clip. Length: {clipToPlay.length}s. Remaining in queue: {playbackQueue.Count}");
                audioSource.clip = clipToPlay;
                audioSource.Play();

                // Ütemezzük a klip törlését a lejátszás után
                // Fontos: A Destroy nem azonnal töröl, csak megjelöli.
                // A 'clipToPlay.length' másodperc múlva hívódik meg a Destroy.
                // Adjunk hozzá egy kis puffert (pl. 0.5 mp).
                Destroy(clipToPlay, clipToPlay.length + 0.5f);
            }
            else
            {
                Debug.LogWarning("[TTS Playback] Dequeued a null AudioClip. Skipping playback.");
            }

            // Pici várakozás, hogy ne pörögjön a ciklus feleslegesen, ha épp nincs mit lejátszani
            // de ez a WaitUntil miatt nem feltétlen szükséges már
            // yield return null;
        }
    }

    void OnDestroy()
    {
        // Állítsuk le a korutinokat, ha az objektum megsemmisül
        if (manageTTSCoroutine != null) StopCoroutine(manageTTSCoroutine);
        if (managePlaybackCoroutine != null) StopCoroutine(managePlaybackCoroutine);

        // Töröljük a memóriából a még lejátszásra váró klipeket is
        while (playbackQueue.Count > 0)
        {
            AudioClip clip = playbackQueue.Dequeue();
            if (clip != null) Destroy(clip);
        }
        Debug.Log("[TextToSpeechManager] Destroyed. Coroutines stopped and playback queue cleared.");
    }
}
