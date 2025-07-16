using UnityEngine;
using UnityEngine.Networking;
using System;
using System.Text;
using System.Collections;
using System.Collections.Generic;

// Enum to select the TTS provider
public enum TTSProvider
{
    OpenAI,
    ElevenLabs
}

// --- PAYLOAD DEFINITIONS ---

// Payload for OpenAI TTS API
[System.Serializable]
public class TTSRequestPayload
{
    public string model;
    public string input;
    public string voice;
    public string response_format;
    public float speed = 1.0f;
}

// Payload for ElevenLabs TTS API
[System.Serializable]
public class ElevenLabsVoiceSettings
{
    public float stability = 0.75f;
    public float similarity_boost = 0.75f;
}

[System.Serializable]
public class ElevenLabsRequestPayload
{
    public string text;
    public string model_id;
    public ElevenLabsVoiceSettings voice_settings;
}


public class TextToSpeechManager : MonoBehaviour
{
    [Header("TTS Provider")]
    [Tooltip("Choose the Text-to-Speech service to use.")]
    [SerializeField] private TTSProvider ttsProvider = TTSProvider.OpenAI;

    [Header("OpenAI Settings")]
    [Tooltip("API Key for OpenAI TTS service.")]
    [SerializeField] private string openAiApiKey;
    [SerializeField] private string ttsModel = "tts-1";
    [SerializeField] private string ttsApiUrl = "https://api.openai.com/v1/audio/speech";

    [Header("ElevenLabs Settings")]
    [Tooltip("API Key for ElevenLabs TTS service.")]
    [SerializeField] private string elevenLabsApiKey;
    [Tooltip("The ID of the model to use for ElevenLabs TTS (e.g., 'eleven_multilingual_v2').")]
    [SerializeField] private string elevenLabsModelId = "eleven_multilingual_v2";
    [Tooltip("URL for the ElevenLabs TTS API. Use {voice_id} as a placeholder for the voice ID.")]
    [SerializeField] private string elevenLabsApiUrl = "https://api.elevenlabs.io/v1/text-to-speech/{voice_id}";
    [SerializeField] private ElevenLabsVoiceSettings elevenLabsVoiceSettings = new ElevenLabsVoiceSettings();

    [Header("Common TTS Configuration")]
    [Tooltip("The Voice ID for the selected provider.")]
    private string currentTtsVoice;
    [SerializeField] private string ttsResponseFormat = "mp3";
    [Tooltip("Playback speed. 1.0 is normal speed. Range: 0.25 to 4.0. Note: This only affects OpenAI provider.")]
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

    // --- EVENTS ---
    public event Action<int> OnTTSPlaybackStart;
    public event Action<int> OnTTSPlaybackEnd;
    public event Action<string> OnTTSError;
    public event Action OnPlaybackQueueCompleted;

    public bool IsPlaying => audioSource != null && audioSource.isPlaying;

    // --- Internal State & Queues ---
    private StringBuilder sentenceBuffer = new StringBuilder();
    private Queue<SentenceData> pendingSentencesQueue = new Queue<SentenceData>();
    private Queue<SentenceData> playbackQueue = new Queue<SentenceData>();
    private int sentenceCounter = 0;

    private bool isTTSRequestInProgress = false;
    private Coroutine manageTTSCoroutine;
    private Coroutine managePlaybackCoroutine;
    private Coroutine currentPlaybackMonitor = null;
    private Coroutine currentPromptCoroutine = null;

    private bool isPausedForQuestion = false;
    private int resumeFromSentenceIndex = -1;

    private StringBuilder answerSentenceBuffer = new StringBuilder();
    private Coroutine currentAnswerSentenceProcessingCoroutine = null;

    private struct SentenceData
    {
        public int Index;
        public string Text;
        public AudioClip Clip;
    }

    // --- UNITY LIFECYCLE ---

    void Start()
    {
        if (audioSource == null)
        {
            Debug.LogError("[TextToSpeechManager_LOG] Main AudioSource (audioSource) is not assigned! Disabling component.", this);
            enabled = false;
            return;
        }
        if (promptAudioSource == null)
        {
            Debug.LogError("[TextToSpeechManager_LOG] Prompt AudioSource (promptAudioSource) is not assigned! Disabling component.", this);
            enabled = false;
            return;
        }
    }

    void OnDestroy()
    {
        Debug.Log("[TextToSpeechManager_LOG] OnDestroy called. Stopping coroutines and clearing queues.");
        StopAllCoroutines();
        ClearQueuesAndClips();
    }

    // --- INITIALIZATION AND RESET ---

    /// <summary>
    /// Initializes the manager with the voice to use. API keys must be set in the Inspector.
    /// </summary>
    /// <param name="voiceId">The voice ID for the selected TTS provider.</param>
    public void Initialize(string voiceId)
    {
        Debug.Log($"[TextToSpeechManager_LOG] Initialize called. Provider: {ttsProvider}, Voice ID: {voiceId}");

        string activeApiKey = (ttsProvider == TTSProvider.OpenAI) ? openAiApiKey : elevenLabsApiKey;

        if (string.IsNullOrEmpty(activeApiKey))
        {
            Debug.LogError($"[TextToSpeechManager_LOG] API Key for the selected provider ({ttsProvider}) is not set in the Inspector! TTS will be disabled.", this);
            enabled = false;
            return;
        }

        if (string.IsNullOrEmpty(voiceId))
        {
            Debug.LogWarning($"[TextToSpeechManager_LOG] Voice ID is null or empty. Using default if available, but this may cause errors.", this);
        }

        this.currentTtsVoice = voiceId;
        Debug.Log($"[TextToSpeechManager_LOG] Active API Key stored. Current TTS Voice set to: {this.currentTtsVoice}");

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

        if (audioSource != null && audioSource.isPlaying)
        {
            audioSource.Stop();
            Debug.Log("[TextToSpeechManager_LOG] Stopped main audio playback.");
        }
        if (promptAudioSource != null && promptAudioSource.isPlaying)
        {
            promptAudioSource.Stop();
            Debug.LogWarning("[TextToSpeechManager_LOG] Stopped prompt audio playback.");
        }

        if (currentPlaybackMonitor != null) StopCoroutine(currentPlaybackMonitor);
        if (currentPromptCoroutine != null) StopCoroutine(currentPromptCoroutine);
        if (currentAnswerSentenceProcessingCoroutine != null) StopCoroutine(currentAnswerSentenceProcessingCoroutine);
        if (manageTTSCoroutine != null) StopCoroutine(manageTTSCoroutine);
        if (managePlaybackCoroutine != null) StopCoroutine(managePlaybackCoroutine);

        currentPlaybackMonitor = null;
        currentPromptCoroutine = null;
        currentAnswerSentenceProcessingCoroutine = null;
        manageTTSCoroutine = null;
        managePlaybackCoroutine = null;

        isTTSRequestInProgress = false;
        isPausedForQuestion = false;
        resumeFromSentenceIndex = -1;
        sentenceCounter = 0;

        sentenceBuffer.Clear();
        answerSentenceBuffer.Clear();
        ClearQueuesAndClips();

        Debug.LogWarning($"[TextToSpeechManager_LOG] Reset completed.");
    }

    // --- AI ANSWER HANDLING (Append, Flush, Process) ---
    // This section remains largely unchanged, but calls the refactored generation method.

    public void AppendAnswerText(string textDelta)
    {
        if (!enabled) return;
        answerSentenceBuffer.Append(textDelta);
        if (currentAnswerSentenceProcessingCoroutine == null)
        {
            ProcessNextFullSentenceFromAnswerBuffer();
        }
    }

    public void FlushAnswerBuffer()
    {
        if (!enabled) return;
        Debug.LogWarning($"[TTS LOG Answer] FlushAnswerBuffer called.");

        if (currentAnswerSentenceProcessingCoroutine == null)
        {
            string remainingText = answerSentenceBuffer.ToString().Trim();
            answerSentenceBuffer.Clear();
            if (!string.IsNullOrEmpty(remainingText))
            {
                currentAnswerSentenceProcessingCoroutine = StartCoroutine(GenerateAndPlaySingleAnswerPiece(remainingText, true));
            }
            else
            {
                InteractionFlowManager.Instance?.HandleAnswerPlaybackCompleted();
            }
        }
    }

    private void ProcessNextFullSentenceFromAnswerBuffer()
    {
        if (currentAnswerSentenceProcessingCoroutine != null || answerSentenceBuffer.Length == 0)
        {
            return;
        }

        int potentialEndIndex = FindPotentialSentenceEnd(answerSentenceBuffer, 0);

        if (potentialEndIndex != -1)
        {
            string sentence = answerSentenceBuffer.ToString(0, potentialEndIndex + 1).Trim();
            answerSentenceBuffer.Remove(0, potentialEndIndex + 1);

            if (!string.IsNullOrWhiteSpace(sentence))
            {
                currentAnswerSentenceProcessingCoroutine = StartCoroutine(GenerateAndPlaySingleAnswerPiece(sentence, false));
            }
            else if (answerSentenceBuffer.Length > 0)
            {
                ProcessNextFullSentenceFromAnswerBuffer();
            }
        }
    }

    private IEnumerator GenerateAndPlaySingleAnswerPiece(string textPiece, bool explicitlyIsLastPiece)
    {
        if (string.IsNullOrWhiteSpace(textPiece))
        {
            if (explicitlyIsLastPiece)
            {
                InteractionFlowManager.Instance?.HandleAnswerPlaybackCompleted();
            }
            currentAnswerSentenceProcessingCoroutine = null;
            if (!explicitlyIsLastPiece && answerSentenceBuffer.Length > 0)
            {
                ProcessNextFullSentenceFromAnswerBuffer();
            }
            yield break;
        }

        if (promptAudioSource.isPlaying)
        {
            yield return new WaitUntil(() => !promptAudioSource.isPlaying);
        }

        TranscriptLogger.Instance?.AddEntry("AI", textPiece);

        // --- REFACTORED: Call the centralized audio generator ---
        AudioClip generatedClip = null;
        yield return StartCoroutine(GenerateAudioClip(textPiece, clip => generatedClip = clip));
        // The GenerateAudioClip coroutine now handles all API logic, logging, and errors.

        if (generatedClip != null)
        {
            promptAudioSource.clip = generatedClip;
            promptAudioSource.Play();

            float playbackStartTime = Time.time;
            float safetyTimeout = generatedClip.length + 5.0f;

            while (promptAudioSource.isPlaying && promptAudioSource.clip == generatedClip && (Time.time - playbackStartTime < safetyTimeout))
            {
                yield return null;
            }

            if (promptAudioSource.isPlaying && promptAudioSource.clip == generatedClip)
            {
                Debug.LogWarning($"[TTS LOG Answer] Playback TIMEOUT for piece. Stopping audio.");
                promptAudioSource.Stop();
            }

            Destroy(generatedClip);
        }
        else
        {
            Debug.LogError($"[TTS LOG Answer] Skipping playback for piece due to generation failure.");
        }

        // --- Cleanup and next step logic ---
        currentAnswerSentenceProcessingCoroutine = null;

        if (explicitlyIsLastPiece)
        {
            InteractionFlowManager.Instance?.HandleAnswerPlaybackCompleted();
        }
        else if (answerSentenceBuffer.Length > 0)
        {
            ProcessNextFullSentenceFromAnswerBuffer();
        }
        else
        {
            InteractionFlowManager.Instance?.HandleAnswerPlaybackCompleted();
        }
    }

    // --- MAIN LECTURE HANDLING (Append, Flush, Process) ---
    // This section is unchanged.

    public void AppendText(string textDelta)
    {
        if (!enabled) return;
        sentenceBuffer.Append(textDelta);
        ProcessSentenceBuffer();
    }

    public void FlushBuffer()
    {
        if (!enabled) return;
        string remainingText = sentenceBuffer.ToString().Trim();
        if (!string.IsNullOrEmpty(remainingText))
        {
            pendingSentencesQueue.Enqueue(new SentenceData { Index = sentenceCounter++, Text = remainingText });
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
            bool isLikelyEndOfSentence = true;

            if (punctuation == '.' && potentialEndIndex < sentenceBuffer.Length - 1)
            {
                char nextChar = sentenceBuffer[potentialEndIndex + 1];
                if (!char.IsWhiteSpace(nextChar) && nextChar != '"' && nextChar != '\'' && nextChar != ')')
                {
                    isLikelyEndOfSentence = false;
                }
            }

            if (isLikelyEndOfSentence)
            {
                string sentence = sentenceBuffer.ToString(0, potentialEndIndex + 1).Trim();
                if (!string.IsNullOrWhiteSpace(sentence))
                {
                    pendingSentencesQueue.Enqueue(new SentenceData { Index = sentenceCounter++, Text = sentence });
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

    // --- CENTRALIZED TTS GENERATION ---

    /// <summary>
    /// Central coroutine to generate an AudioClip from text using the selected TTS provider.
    /// </summary>
    /// <param name="text">The text to synthesize.</param>
    /// <param name="onComplete">Callback action that receives the generated AudioClip (or null on failure).</param>
    private IEnumerator GenerateAudioClip(string text, Action<AudioClip> onComplete)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            Debug.LogWarning("[TTS] GenerateAudioClip called with empty text. Skipping.");
            onComplete?.Invoke(null);
            yield break;
        }

        UnityWebRequest request;
        string url;
        byte[] bodyRaw;
        string jsonPayload;

        switch (ttsProvider)
        {
            case TTSProvider.OpenAI:
                if (string.IsNullOrEmpty(openAiApiKey)) { Debug.LogError("OpenAI API Key is not set."); onComplete?.Invoke(null); yield break; }
                url = this.ttsApiUrl;
                TTSRequestPayload openAiPayload = new TTSRequestPayload
                {
                    model = this.ttsModel,
                    input = text,
                    voice = this.currentTtsVoice,
                    response_format = this.ttsResponseFormat,
                    speed = this.ttsSpeed
                };
                jsonPayload = JsonUtility.ToJson(openAiPayload);
                bodyRaw = Encoding.UTF8.GetBytes(jsonPayload);

                request = new UnityWebRequest(url, "POST");
                request.SetRequestHeader("Authorization", $"Bearer {openAiApiKey}");
                break;

            case TTSProvider.ElevenLabs:
                if (string.IsNullOrEmpty(elevenLabsApiKey)) { Debug.LogError("ElevenLabs API Key is not set."); onComplete?.Invoke(null); yield break; }
                if (string.IsNullOrEmpty(currentTtsVoice)) { Debug.LogError("[ElevenLabs] Voice ID is not set!"); onComplete?.Invoke(null); yield break; }

                url = this.elevenLabsApiUrl.Replace("{voice_id}", this.currentTtsVoice);
                ElevenLabsRequestPayload elevenPayload = new ElevenLabsRequestPayload
                {
                    text = text,
                    model_id = this.elevenLabsModelId,
                    voice_settings = this.elevenLabsVoiceSettings
                };
                jsonPayload = JsonUtility.ToJson(elevenPayload);
                bodyRaw = Encoding.UTF8.GetBytes(jsonPayload);

                request = new UnityWebRequest(url, "POST");
                request.SetRequestHeader("xi-api-key", elevenLabsApiKey);
                break;

            default:
                Debug.LogError($"Unsupported TTS Provider: {ttsProvider}");
                onComplete?.Invoke(null);
                yield break;
        }

        // Common request setup and execution
        request.uploadHandler = new UploadHandlerRaw(bodyRaw);
        request.uploadHandler.contentType = "application/json";
        AudioType audioType = GetAudioTypeFromFormat(this.ttsResponseFormat);
        // A dummy URI is needed for the constructor, the actual target is the request's URL.
        request.downloadHandler = new DownloadHandlerAudioClip(new Uri(url), audioType);
        request.timeout = 60;

        Debug.Log($"[TTS API Call] Sending request to {ttsProvider} for text: '{text.Substring(0, Math.Min(text.Length, 50))}...'");

        yield return request.SendWebRequest();

        using (request)
        {
            if (request.result == UnityWebRequest.Result.Success)
            {
                AudioClip receivedClip = DownloadHandlerAudioClip.GetContent(request);
                if (receivedClip != null && receivedClip.loadState == AudioDataLoadState.Loaded && receivedClip.length > 0)
                {
                    Debug.Log($"[TTS Success] Received AudioClip from {ttsProvider}. Length: {receivedClip.length:F2}s.");
                    onComplete?.Invoke(receivedClip);
                }
                else
                {
                    string reason = receivedClip == null ? "Clip is null" : $"LoadState is {receivedClip.loadState}";
                    Debug.LogError($"[TTS Error] Invalid AudioClip from {ttsProvider}. Reason: {reason}");
                    OnTTSError?.Invoke("AudioClip generation failed (invalid clip).");
                    if (receivedClip != null) Destroy(receivedClip);
                    onComplete?.Invoke(null);
                }
            }
            else
            {
                string errorLog = $"[TTS API Error - {ttsProvider}] Code: {request.responseCode} - Error: {request.error}";
                if (request.downloadHandler?.data != null)
                {
                    errorLog += $"\nServer Response: {Encoding.UTF8.GetString(request.downloadHandler.data)}";
                }
                Debug.LogError(errorLog);
                OnTTSError?.Invoke($"TTS API Error: {request.error}");
                onComplete?.Invoke(null);
            }
        }
    }

    private IEnumerator ManageTTSRequests()
    {
        while (true)
        {
            yield return new WaitUntil(() => !isTTSRequestInProgress &&
                                           pendingSentencesQueue.Count > 0 &&
                                           playbackQueue.Count < maxPlaybackQueueSize);

            SentenceData sentenceData = pendingSentencesQueue.Dequeue();
            StartCoroutine(GenerateSpeechCoroutine(sentenceData));
        }
    }

    private IEnumerator GenerateSpeechCoroutine(SentenceData data)
    {
        isTTSRequestInProgress = true;
        AudioClip generatedClip = null;

        // Call the centralized generator and wait for its result via the callback
        yield return StartCoroutine(GenerateAudioClip(data.Text, clip => generatedClip = clip));

        if (generatedClip != null)
        {
            data.Clip = generatedClip;
            playbackQueue.Enqueue(data);
        }
        else
        {
            Debug.LogError($"[TTS Generation] Failed to generate clip for sentence {data.Index}. It will be skipped.");
            // OnTTSError is invoked inside GenerateAudioClip
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
                Debug.LogWarning($"Unsupported audio format '{format}'. Falling back to MPEG (MP3).");
                return AudioType.MPEG;
        }
    }

    // --- PLAYBACK CONTROL (Pause, Resume, SpeakSingle) ---
    // This section remains largely unchanged, but calls the refactored generation method.

    public void PausePlayback()
    {
        isPausedForQuestion = true;
        if (audioSource != null && audioSource.isPlaying)
        {
            audioSource.Stop();
        }
        if (currentPlaybackMonitor != null)
        {
            StopCoroutine(currentPlaybackMonitor);
            currentPlaybackMonitor = null;
        }
    }

    public void ResumePlayback(int startFromIndex)
    {
        resumeFromSentenceIndex = startFromIndex;
        isPausedForQuestion = false;
    }

    public void SpeakSingleSentence(string text)
    {
        if (!enabled || promptAudioSource == null) return;

        if (currentPromptCoroutine != null)
        {
            StopCoroutine(currentPromptCoroutine);
        }
        currentPromptCoroutine = StartCoroutine(SpeakSingleSentenceCoroutine(text));
    }

    private IEnumerator SpeakSingleSentenceCoroutine(string text)
    {
        if (promptAudioSource.isPlaying)
        {
            yield return new WaitUntil(() => !promptAudioSource.isPlaying);
        }

        // Generate the audio using the central helper
        AudioClip promptClip = null;
        yield return StartCoroutine(GenerateAudioClip(text, clip => promptClip = clip));

        if (promptClip != null)
        {
            TranscriptLogger.Instance?.AddEntry("AI", text);
            promptAudioSource.clip = promptClip;
            promptAudioSource.Play();

            float startTime = Time.time;
            while (promptAudioSource.isPlaying && promptAudioSource.clip == promptClip && currentPromptCoroutine != null)
            {
                if (Time.time - startTime > promptClip.length + 5.0f)
                {
                    promptAudioSource.Stop();
                    break;
                }
                yield return null;
            }
        }
        else
        {
            Debug.LogError("[SpeakSingleSentenceCoroutine] Skipping prompt playback due to generation error.");
        }

        InteractionFlowManager.Instance?.EnableSpeakButton();

        if (promptClip != null)
        {
            Destroy(promptClip);
        }

        currentPromptCoroutine = null;
    }

    // --- PLAYBACK LOOP ---
    // This section is unchanged.

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
                playbackQueue.Dequeue();
                if (dataToPlay.Clip != null) Destroy(dataToPlay.Clip);
                continue;
            }

            dataToPlay = playbackQueue.Dequeue();
            resumeFromSentenceIndex = -1;

            if (dataToPlay.Clip != null)
            {
                TranscriptLogger.Instance?.AddEntry("AI", dataToPlay.Text);
                OnTTSPlaybackStart?.Invoke(dataToPlay.Index);
                audioSource.clip = dataToPlay.Clip;
                audioSource.Play();

                if (currentPlaybackMonitor != null) StopCoroutine(currentPlaybackMonitor);
                currentPlaybackMonitor = StartCoroutine(MonitorPlaybackEnd(dataToPlay));
            }
            else
            {
                OnTTSPlaybackEnd?.Invoke(dataToPlay.Index);
            }
        }
    }

    private IEnumerator MonitorPlaybackEnd(SentenceData playedData)
    {
        yield return new WaitUntil(() =>
            isPausedForQuestion ||
            audioSource == null ||
            audioSource.clip != playedData.Clip ||
            !audioSource.isPlaying
        );

        if (currentPlaybackMonitor == null) yield break;

        bool stoppedForPause = isPausedForQuestion;
        bool clipIsCorrect = (audioSource != null && audioSource.clip == playedData.Clip);
        bool stoppedPlayingNaturally = (audioSource != null && !audioSource.isPlaying && !stoppedForPause);

        if (clipIsCorrect && stoppedPlayingNaturally)
        {
            OnTTSPlaybackEnd?.Invoke(playedData.Index);

            if (playbackQueue.Count == 0)
            {
                OnPlaybackQueueCompleted?.Invoke();
            }

            if (playedData.Clip != null) Destroy(playedData.Clip);
        }
        else if (!stoppedForPause && playedData.Clip != null)
        {
            Destroy(playedData.Clip);
        }

        currentPlaybackMonitor = null;
    }

    // --- UTILITY ---

    private void ClearQueuesAndClips()
    {
        pendingSentencesQueue.Clear();
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
        if (clearedPlayback > 0)
        {
            Debug.Log($"[TextToSpeechManager_LOG] Cleared {clearedPlayback} clips from playback queue.");
        }
    }
}