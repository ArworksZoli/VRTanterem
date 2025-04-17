using System.Collections;
using UnityEngine;
using UnityEngine.Android;
using UnityEngine.InputSystem;
using TMPro;
using System;

public class WhisperMicController : MonoBehaviour
{
    [Header("Input Settings")]
    [SerializeField] private InputActionAsset inputActions;
    [Tooltip("The Action Map containing the 'Speak' action.")]
    [SerializeField] private string actionMapName = "VoiceInput"; // Vagy amelyikben a beszéd gomb van
    [Tooltip("The Input Action used to trigger voice recording (hold to speak).")]
    [SerializeField] private string speakActionName = "RecordVoice"; // Átneveztem 'speakActionName'-re a jobb érthetőségért

    [Header("Microphone Settings")]
    [SerializeField] private int recordingDurationSeconds = 15; // Rövidebb alapértelmezett lehet jobb
    [SerializeField] private int sampleRate = 16000; // Whisper jobban szereti a 16kHz-et

    [Header("API Integration")]
    [Tooltip("Reference to the OpenAIWebRequest component (used only for SendAudioToWhisper).")]
    [SerializeField] private OpenAIWebRequest openAIWebRequest; // Ezt még használjuk a Whisper API híváshoz

    [Header("External Components")]
    // [SerializeField] private TextToSpeechManager textToSpeechManager; // <<< ELTÁVOLÍTVA: Már nem kell itt figyelni a TTS-t
    [Tooltip("TextMeshPro component to display recording/processing status.")]
    [SerializeField] private TextMeshProUGUI statusText;
    [Tooltip("Sound played when recording starts.")]
    [SerializeField] private AudioClip pressSound;
    [Tooltip("Sound played when recording stops.")]
    [SerializeField] private AudioClip releaseSound;

    // --- Státusz Szövegek ---
    // Ezeket az InteractionFlowManager is beállíthatja majd, de itt is lehetnek alapértelmezettek
    private const string StatusIdle = "Hold <Speak Button> to talk"; // Általánosabb idle szöveg
    private const string StatusDisabled = ""; // Vagy "..." ha le van tiltva a gomb
    private const string StatusRecording = "RECORDING...";
    private const string StatusProcessing = "Processing...";
    private const string StatusTranscribing = "Transcribing...";
    // private const string SendingToAssistantText = "Sending to Assistant..."; // Ezt inkább az InteractionFlowManager kezelje

    // --- Belső Változók ---
    private InputAction speakAction; // Átnevezve
    private AudioClip recordedClip;
    private string microphoneDevice;
    private bool isRecording = false;
    private AudioSource audioSource;

    void Awake()
    {
        // <<< ÚJ LOG >>>
        Debug.LogWarning($"[WhisperMicController] Awake START - Frame: {Time.frameCount}");

        audioSource = GetComponent<AudioSource>();
        if (audioSource == null) { audioSource = gameObject.AddComponent<AudioSource>(); }
        microphoneDevice = null;

        if (inputActions == null) { Debug.LogError("Input Actions asset not assigned!", this); enabled = false; return; }
        // <<< ÚJ LOG >>>
        Debug.Log($"[WhisperMicController] Input Action Asset OK: {inputActions.name}");

        var actionMap = inputActions.FindActionMap(actionMapName);
        if (actionMap == null) { Debug.LogError($"Action Map '{actionMapName}' not found!", this); enabled = false; return; }
        // <<< ÚJ LOG >>>
        Debug.Log($"[WhisperMicController] Action Map OK: {actionMapName}");

        speakAction = actionMap.FindAction(speakActionName);
        if (speakAction == null) { Debug.LogError($"Action '{speakActionName}' not found in map '{actionMapName}'!", this); enabled = false; return; }
        // <<< ÚJ LOG >>>
        Debug.Log($"[WhisperMicController] Speak Action OK: {speakActionName}");

        RequestMicrophonePermission();
        speakAction?.Disable(); // Itt még lehet null, ha a fenti checkek fail-elnek
        UpdateStatusText(StatusDisabled);

        // <<< MÓDOSÍTOTT LOG >>>
        Debug.LogWarning($"[WhisperMicController] Awake END - Frame: {Time.frameCount}. speakAction is null: {speakAction == null}");
    }

    private void RequestMicrophonePermission()
    {
#if UNITY_ANDROID && !UNITY_EDITOR
        if (!UnityEngine.Android.Permission.HasUserAuthorizedPermission(UnityEngine.Android.Permission.Microphone))
        {
            Debug.Log("Requesting Microphone permission...");
            UnityEngine.Android.Permission.RequestUserPermission(UnityEngine.Android.Permission.Microphone);
        }
        else { /* Debug.Log("Microphone permission already granted."); */ }
#else
        // Debug.Log("Microphone permission check skipped (Not on Android device or in Editor).");
#endif
    }

    void OnEnable()
    {
        Debug.Log("[WhisperMicController] OnEnable called.");
        // Feliratkozás az Input Action eseményekre
        if (speakAction != null)
        {
            speakAction.started += OnRecordStarted;
            speakAction.canceled += OnRecordStopped;
            Debug.Log("[WhisperMicController] Speak action listeners attached.");
            // Az engedélyezést/letiltást az InteractionFlowManager végzi!
            // Itt nem engedélyezzük automatikusan.
        }

        // --- TTS ESEMÉNYEKRŐL LEIRATKOZÁS ---
        // Már nincs rájuk szükség itt.
        // if (textToSpeechManager != null) { ... }
    }

    void OnDisable()
    {
        Debug.Log("[WhisperMicController] OnDisable called.");
        // Leiratkozás az Input Action eseményekről
        if (speakAction != null)
        {
            speakAction.started -= OnRecordStarted;
            speakAction.canceled -= OnRecordStopped;
            // Mindig tiltsuk le, ha a komponens deaktiválódik
            speakAction.Disable();
            Debug.Log("[WhisperMicController] Speak action listeners detached and action disabled.");
        }

        // Ha épp felvétel van, állítsuk le (változatlan)
        if (isRecording)
        {
            StopRecordingInternal(); // Kiszervezve a logikát
            Debug.LogWarning("[WhisperMicController] Microphone recording stopped due to component disable.");
            UpdateStatusText(StatusDisabled); // Vissza tiltott állapotba
        }

        // --- TTS ESEMÉNYEKRŐL LEIRATKOZÁS ---
        // Már nincs rájuk szükség itt.
        // if (textToSpeechManager != null) { ... }
    }

    // --- Külső Vezérlő Metódusok (InteractionFlowManager hívja) ---

    /// <summary>
    /// Enables the speak input action, allowing the user to press/hold the speak button.
    /// Called by InteractionFlowManager when it's appropriate for the user to speak.
    /// </summary>
    public void EnableSpeakButton()
    {
        if (speakAction != null && !speakAction.enabled)
        {
            speakAction.Enable();
            UpdateStatusText(StatusIdle); // Jelezzük, hogy most lehet beszélni
            Debug.Log("[WhisperMicController] Speak Action ENABLED.");
        }
        else if (speakAction == null)
        {
            Debug.LogError("[WhisperMicController] Cannot enable speak action: speakAction is NULL!");
        }
        // Ha már engedélyezve volt, nem csinálunk semmit (vagy csak logolunk)
        // else { Debug.LogWarning("[WhisperMicController] EnableSpeakButton called, but action was already enabled."); }
    }

    /// <summary>
    /// Disables the speak input action, preventing the user from initiating recording.
    /// Called by InteractionFlowManager when the user should not be speaking.
    /// </summary>
    public void DisableSpeakButton()
    {
        if (speakAction != null && speakAction.enabled)
        {
            speakAction.Disable();
            UpdateStatusText(StatusDisabled); // Jelezzük, hogy a gomb inaktív
            Debug.Log("[WhisperMicController] Speak Action DISABLED.");

            // Ha éppen felvétel közben tiltják le, állítsuk le a felvételt
            if (isRecording)
            {
                Debug.LogWarning("[WhisperMicController] Speak action disabled while recording was active. Stopping recording.");
                StopRecordingInternal(); // Leállítjuk a felvételt, de nem dolgozzuk fel
            }
        }
        else if (speakAction == null)
        {
            Debug.LogError("[WhisperMicController] Cannot disable speak action: speakAction is NULL!");
        }
        // Ha már le volt tiltva, nem csinálunk semmit
    }

    // --- TTS Eseménykezelők (Eltávolítva) ---
    // private void HandleTTSPlaybackStart(int sentenceIndex) { ... } // TÖRÖLVE
    // private void HandleTTSPlaybackEnd(int sentenceIndex) { ... } // TÖRÖLVE

    // --- Hangrögzítés Indítása/Leállítása (Input Action Callbackek) ---

    private void OnRecordStarted(InputAction.CallbackContext context)
    {
        if (InteractionFlowManager.Instance == null)
        {
            Debug.LogError("[WhisperMicController] Cannot start recording: InteractionFlowManager.Instance is null!");
            // Itt nem indítjuk a felvételt, de lehet, hogy a gombot le kellene tiltani?
            // Vagy csak egyszerűen nem csinálunk semmit.
            return; // Ne folytassuk
        }

        // Ellenőrizzük az InteractionFlowManager állapotát
        var requiredState = InteractionFlowManager.InteractionState.WaitingForUserInput;
        if (InteractionFlowManager.Instance.CurrentState != requiredState)
        {
            Debug.LogWarning($"[WhisperMicController] Record button pressed, but InteractionFlowManager state is '{InteractionFlowManager.Instance.CurrentState}', not '{requiredState}'. Ignoring recording request.");
            // Ne indítsuk el a felvételt, ha nem a megfelelő állapotban vagyunk.
            // A gomb valószínűleg hibásan lett engedélyezve, vagy a felhasználó túl gyors volt.
            return; // Ne folytassuk
        }

        // Az Input System gondoskodik róla, hogy ez csak akkor hívódjon meg, ha az action engedélyezve van.
        // Dupla ellenőrzés (opcionális):
        // if (!speakAction.enabled) {
        //     Debug.LogWarning("OnRecordStarted called but action is disabled. Ignoring.");
        //     return;
        // }

        if (isRecording)
        {
            Debug.LogWarning("[WhisperMicController] OnRecordStarted called, but already recording.");
            return;
        }

        // Jogosultság ellenőrzés (fontos lehet minden indításkor)
#if UNITY_ANDROID && !UNITY_EDITOR
        if (!UnityEngine.Android.Permission.HasUserAuthorizedPermission(UnityEngine.Android.Permission.Microphone))
        {
            Debug.LogError("[WhisperMicController] Cannot start recording: Microphone permission not granted.");
            RequestMicrophonePermission(); // Megpróbáljuk újra kérni
            return;
        }
#endif

        // Hangjelzés lejátszása
        if (pressSound != null && audioSource != null) audioSource.PlayOneShot(pressSound);

        Debug.Log("[WhisperMicController] Recording Started (State was correct)...");
        UpdateStatusText(StatusRecording);
        isRecording = true;

        // Felvétel indítása
        try
        {
            recordedClip = Microphone.Start(microphoneDevice, false, recordingDurationSeconds, sampleRate);
            if (recordedClip == null)
            {
                Debug.LogError("[WhisperMicController] Microphone.Start failed to return an AudioClip (returned null). Check microphone device and permissions.");
                isRecording = false;
                UpdateStatusText("Mic Error"); // Vagy StatusDisabled
                // Itt lehetne újra letiltani a gombot? Vagy az IFM kezeli? Maradjunk a sima hibajelzésnél.
                // DisableSpeakButton(); // Opcionális
            }
        }
        catch (Exception e)
        {
            Debug.LogError($"[WhisperMicController] Exception during Microphone.Start: {e.Message}");
            isRecording = false;
            UpdateStatusText("Mic Exception");
            // DisableSpeakButton(); // Opcionális
        }
    }

    private void OnRecordStopped(InputAction.CallbackContext context)
    {
        // Az Input System gondoskodik róla, hogy ez csak akkor hívódjon meg, ha az action engedélyezve van.

        if (!isRecording)
        {
            // Ez előfordulhat, ha a gombot felengedik, mielőtt a Start teljesen lefutott volna, vagy hiba történt Start közben.
            Debug.LogWarning("[WhisperMicController] OnRecordStopped called, but was not in 'isRecording' state. Ignoring processing.");
            return;
        }

        Debug.Log("[WhisperMicController] Recording Stopped by user input.");

        // Hangjelzés lejátszása
        if (releaseSound != null && audioSource != null) audioSource.PlayOneShot(releaseSound);

        // Leállítjuk a felvételt és elindítjuk a feldolgozást
        ProcessRecordedAudio();

        // Fontos: Az isRecording flag-et a ProcessRecordedAudio vagy annak híváslánca
        // állítja vissza false-ra a feldolgozás végén vagy hiba esetén.
        // A gomb letiltását az InteractionFlowManager végzi, miután megkapta az átírást.
    }

    /// <summary>
    /// Internal method to stop microphone recording without processing the audio.
    /// Used when disabling the component or the speak button during recording.
    /// </summary>
    private void StopRecordingInternal()
    {
        if (isRecording)
        {
            if (Microphone.IsRecording(microphoneDevice))
            {
                Microphone.End(microphoneDevice);
            }
            isRecording = false;
            // Az eredeti 'recordedClip' megsemmisítése, mivel nem dolgozzuk fel
            if (recordedClip != null)
            {
                Destroy(recordedClip);
                recordedClip = null;
            }
            Debug.Log("[WhisperMicController] Stopped recording internally (no processing).");
        }
    }

    /// <summary>
    /// Stops microphone recording, processes the captured audio, and sends it for transcription.
    /// </summary>
    private void ProcessRecordedAudio()
    {
        if (!isRecording) return; // Biztonsági ellenőrzés

        int lastSample = 0;
        bool wasRecording = Microphone.IsRecording(microphoneDevice);

        if (wasRecording)
        {
            lastSample = Microphone.GetPosition(microphoneDevice);
            Microphone.End(microphoneDevice);
            // Debug.Log($"[WhisperMicController] Microphone.End called. Last sample position: {lastSample}");
        }
        else
        {
            Debug.LogWarning("[WhisperMicController] ProcessRecordedAudio called, but Microphone was not recording according to IsRecording().");
            // Ha nem volt felvétel, de az isRecording flag true volt, akkor is visszaállítjuk
            isRecording = false;
            UpdateStatusText(StatusIdle); // Vagy StatusDisabled, attól függően, mi a logikusabb
            return;
        }

        // Az isRecording flag-et itt még nem állítjuk false-ra, csak a feldolgozás végén/hiba esetén.
        UpdateStatusText(StatusProcessing); // Jelezzük a feldolgozást

        if (recordedClip != null && lastSample > 0)
        {
            AudioClip trimmedClip = CreateTrimmedClip(recordedClip, lastSample);
            // Az eredeti nagy bufferre már nincs szükség, töröljük
            // Fontos: Az eredeti klipet csak a vágás *után* töröljük.
            Destroy(recordedClip);
            recordedClip = null;

            if (trimmedClip != null)
            {
                // Debug.Log($"[WhisperMicController] Audio trimmed: {trimmedClip.length} seconds. Converting to WAV.");
                byte[] wavData = ConvertAudioClipToWav(trimmedClip);
                Destroy(trimmedClip); // A vágott klipre sincs már szükség a konverzió után

                if (wavData != null && wavData.Length > 44) // 44 a WAV header mérete
                {
                    // Sikeres WAV konverzió, küldés Whispernek
                    ProcessWavData(wavData);
                }
                else
                {
                    Debug.LogError("[WhisperMicController] Failed to convert recorded audio to valid WAV format.");
                    UpdateStatusText("Conversion Error");
                    isRecording = false; // Hiba esetén is visszaállítjuk
                    // Gomb visszaengedélyezése? Vagy az IFM kezeli? Maradjunk ennél.
                    // EnableSpeakButton(); // Opcionális
                    Invoke(nameof(ResetStatusToIdleOrDisabled), 2.0f); // Visszaállás késleltetve
                }
            }
            else
            {
                Debug.LogWarning("[WhisperMicController] Processing stopped: trimmed clip was null.");
                UpdateStatusText(StatusIdle); // Vagy StatusDisabled
                isRecording = false; // Hiba esetén is visszaállítjuk
            }
        }
        else
        {
            Debug.LogWarning($"[WhisperMicController] Processing stopped: No valid audio data captured (lastSample={lastSample}, recordedClip null? {!recordedClip}).");
            if (recordedClip != null) Destroy(recordedClip); // Takarítsuk el a nagy buffert is
            recordedClip = null;
            UpdateStatusText(StatusIdle); // Vagy StatusDisabled
            isRecording = false; // Hiba esetén is visszaállítjuk
        }
    }


    // --- Hang Feldolgozás és API Hívás ---

    // CreateTrimmedClip változatlan marad
    private AudioClip CreateTrimmedClip(AudioClip originalClip, int lastSamplePosition)
    {
        if (lastSamplePosition <= 0 || originalClip == null) return null;
        float[] data = new float[lastSamplePosition * originalClip.channels];
        if (!originalClip.GetData(data, 0)) { Debug.LogError("Failed GetData in CreateTrimmedClip."); return null; }
        AudioClip trimmed = AudioClip.Create("RecordedTrimmed", lastSamplePosition, originalClip.channels, originalClip.frequency, false);
        if (!trimmed.SetData(data, 0)) { Debug.LogError("Failed SetData in CreateTrimmedClip."); Destroy(trimmed); return null; }
        return trimmed;
    }

    // ConvertAudioClipToWav és segédfüggvényei változatlanok maradnak
    public static byte[] ConvertAudioClipToWav(AudioClip clip)
    {
        if (clip == null) { Debug.LogError("ConvertAudioClipToWav: Input AudioClip is null."); return null; }
        if (clip.samples <= 0 || clip.channels <= 0 || clip.frequency <= 0) { Debug.LogError($"ConvertAudioClipToWav: Invalid clip parameters (Samples={clip.samples}, Channels={clip.channels}, Freq={clip.frequency})."); return null; }
        float[] samples = new float[clip.samples * clip.channels];
        if (!clip.GetData(samples, 0)) { Debug.LogError("ConvertAudioClipToWav: clip.GetData() failed."); return null; }
        try
        {
            int dataSize = samples.Length * 2; int fileSize = dataSize + 44; byte[] wavFile = new byte[fileSize];
            WriteString(wavFile, 0, "RIFF"); WriteInt32(wavFile, 4, fileSize - 8); WriteString(wavFile, 8, "WAVE"); WriteString(wavFile, 12, "fmt ");
            WriteInt32(wavFile, 16, 16); WriteInt16(wavFile, 20, 1); WriteInt16(wavFile, 22, (short)clip.channels); WriteInt32(wavFile, 24, clip.frequency);
            WriteInt32(wavFile, 28, clip.frequency * clip.channels * 2); WriteInt16(wavFile, 32, (short)(clip.channels * 2)); WriteInt16(wavFile, 34, 16);
            WriteString(wavFile, 36, "data"); WriteInt32(wavFile, 40, dataSize);
            int headerOffset = 44;
            for (int i = 0; i < samples.Length; i++) { short sampleInt = (short)(Mathf.Clamp(samples[i], -1.0f, 1.0f) * 32767.0f); wavFile[headerOffset + i * 2] = (byte)(sampleInt & 0xff); wavFile[headerOffset + i * 2 + 1] = (byte)((sampleInt >> 8) & 0xff); }
            return wavFile;
        }
        catch (Exception e) { Debug.LogError($"ConvertAudioClipToWav: Exception during WAV construction: {e.Message}"); return null; }
    }
    private static void WriteString(byte[] data, int offset, string value) { byte[] bytes = System.Text.Encoding.ASCII.GetBytes(value); System.Buffer.BlockCopy(bytes, 0, data, offset, bytes.Length); }
    private static void WriteInt16(byte[] data, int offset, short value) { data[offset] = (byte)(value & 0xff); data[offset + 1] = (byte)((value >> 8) & 0xff); }
    private static void WriteInt32(byte[] data, int offset, int value) { data[offset] = (byte)(value & 0xff); data[offset + 1] = (byte)((value >> 8) & 0xff); data[offset + 2] = (byte)((value >> 16) & 0xff); data[offset + 3] = (byte)((value >> 24) & 0xff); }


    // Feldolgozás indítása (Whisper API hívás)
    private void ProcessWavData(byte[] wavData)
    {
        if (wavData == null || wavData.Length <= 44) { /*...*/ isRecording = false; return; } // Már kezeltük a ProcessRecordedAudio-ban

        Debug.Log($"[WhisperMicController] Sending {wavData.Length} bytes of WAV data to Whisper API...");
        UpdateStatusText(StatusTranscribing); // Jelezzük az átírást

        if (openAIWebRequest != null)
        {
            // Elindítjuk a Whisper kérést, és átadjuk a callback függvényt
            StartCoroutine(openAIWebRequest.SendAudioToWhisper(wavData, ProcessWhisperResponse));
        }
        else
        {
            Debug.LogError("[WhisperMicController] OpenAIWebRequest reference is not set! Cannot send audio.");
            UpdateStatusText("Error: API unavailable");
            isRecording = false; // Hiba -> isRecording visszaállítása
            Invoke(nameof(ResetStatusToIdleOrDisabled), 2.0f);
        }
    }

    // Whisper válasz feldolgozása (Callback)
    private void ProcessWhisperResponse(string transcription)
    {
        // Fontos: Ez a metódus egy korutinból (SendAudioToWhisper) hívódik vissza.

        if (string.IsNullOrEmpty(transcription))
        {
            Debug.LogWarning("[WhisperMicController] Whisper API returned an empty or null transcription.");
            UpdateStatusText("Transcription Failed");
            isRecording = false; // Sikertelen átírás -> isRecording visszaállítása
            Invoke(nameof(ResetStatusToIdleOrDisabled), 2.0f);
            return;
        }

        Debug.Log($"[WhisperMicController] Whisper Transcription Successful: '{transcription}'");
        // A státusz szöveget ("Sending to Assistant...") már az InteractionFlowManager kezeli,
        // miután megkapta ezt az átírást. Itt nem kell frissíteni.
        // UpdateStatusText(SendingToAssistantText); // <<< ELTÁVOLÍTVA

        // Átadjuk az átírást az InteractionFlowManagernek
        if (InteractionFlowManager.Instance != null)
        {
            InteractionFlowManager.Instance.HandleUserQuestionReceived(transcription);
            // Az InteractionFlowManager felelős a beszéd gomb letiltásáért ezután.
        }
        else
        {
            Debug.LogError("[WhisperMicController] InteractionFlowManager.Instance is null! Cannot forward transcription.");
            UpdateStatusText("Error: Flow unavailable");
            Invoke(nameof(ResetStatusToIdleOrDisabled), 2.0f);
        }

        // Sikeres feldolgozás és továbbítás után visszaállítjuk az isRecording flag-et.
        isRecording = false;
        Debug.Log("[WhisperMicController] Transcription processed and forwarded. isRecording set to false.");
        // A státusz szöveget és a gomb állapotát az InteractionFlowManager kezeli tovább.
    }

    // --- Segédfüggvények ---

    private void ResetStatusToIdleOrDisabled()
    {
        // Ha a gomb engedélyezve van, Idle-re állítjuk, ha nem, Disabled-re.
        if (speakAction != null && speakAction.enabled)
        {
            UpdateStatusText(StatusIdle);
        }
        else
        {
            UpdateStatusText(StatusDisabled);
        }
    }

    // Segédfüggvény a UI szöveg frissítéséhez (null ellenőrzéssel)
    private void UpdateStatusText(string message)
    {
        if (statusText != null)
        {
            statusText.text = message;
        }
        // else { Debug.LogWarning($"StatusText UI element not assigned. Cannot display: {message}"); } // Csak ha szükséges
    }

} // <-- Osztály vége
