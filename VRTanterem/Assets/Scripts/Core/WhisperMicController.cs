using System.Collections;
using UnityEngine;
using UnityEngine.Android;
using UnityEngine.InputSystem;
using TMPro;
using System;
using UnityEngine.UI;

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

    [Header("Microphone Icon UI")]
    [SerializeField] private Image microphoneIconImage;
    [SerializeField] private Color iconColorDefault = new Color32(40, 68, 77, 255);
    [SerializeField] private Color iconColorReady = new Color(0.2f, 0.8f, 0.2f, 1f);
    [SerializeField] private Color iconColorRecording = new Color(0.9f, 0.2f, 0.2f, 1f);

    // --- Státusz Szövegek ---
    private const string StatusIdle = "Hold <Speak Button> to talk";
    private const string StatusDisabled = "";
    private const string StatusRecording = "RECORDING...";
    private const string StatusProcessing = "Processing...";
    private const string StatusTranscribing = "Transcribing...";

    // --- Belső Változók ---
    private InputAction speakAction; // Átnevezve
    private AudioClip recordedClip;
    private string microphoneDevice;
    private bool isRecording = false;
    private AudioSource audioSource;
    private bool isSpeakActionEnabled = false;

    void Awake()
    {
        
        Debug.LogWarning($"[WhisperMicController] Awake START - Frame: {Time.frameCount}");

        audioSource = GetComponent<AudioSource>();
        if (audioSource == null) { audioSource = gameObject.AddComponent<AudioSource>(); }
        microphoneDevice = null;

        if (inputActions == null) { Debug.LogError("Input Actions asset not assigned!", this); enabled = false; return; }
        Debug.Log($"[WhisperMicController] Input Action Asset OK: {inputActions.name}");

        var actionMap = inputActions.FindActionMap(actionMapName);
        if (actionMap == null) { Debug.LogError($"Action Map '{actionMapName}' not found!", this); enabled = false; return; }
        Debug.Log($"[WhisperMicController] Action Map OK: {actionMapName}");

        speakAction = actionMap.FindAction(speakActionName);
        if (speakAction == null) { Debug.LogError($"Action '{speakActionName}' not found in map '{actionMapName}'!", this); enabled = false; return; }
        Debug.Log($"[WhisperMicController] Speak Action OK: {speakActionName}");

        RequestMicrophonePermission();
        
        speakAction?.Disable();
        isSpeakActionEnabled = false;
        UpdateStatusText(StatusDisabled);
        SetMicrophoneIconColor(iconColorDefault);

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
        if (speakAction != null)
        {
            speakAction.started += OnRecordStarted;
            speakAction.canceled += OnRecordStopped;
            Debug.Log("[WhisperMicController] Speak action listeners attached.");
            
            if (isSpeakActionEnabled)
            {
                speakAction.Enable();
                SetMicrophoneIconColor(iconColorReady);
                UpdateStatusText(StatusIdle);
            }
            else
            {
                speakAction.Disable(); // Biztonság kedvéért
                SetMicrophoneIconColor(iconColorDefault);
                UpdateStatusText(StatusDisabled);
            }
        }
    }

    void OnDisable()
    {
        Debug.Log("[WhisperMicController] OnDisable called.");
        if (speakAction != null)
        {
            speakAction.started -= OnRecordStarted;
            speakAction.canceled -= OnRecordStopped;
            speakAction.Disable(); // Mindig tiltsuk le
            isSpeakActionEnabled = false; // Állapot frissítése
            Debug.Log("[WhisperMicController] Speak action listeners detached and action disabled.");
        }

        if (isRecording)
        {
            StopRecordingInternal();
            Debug.LogWarning("[WhisperMicController] Microphone recording stopped due to component disable.");
        }
        UpdateStatusText(StatusDisabled);
        SetMicrophoneIconColor(iconColorDefault); // <<< ÚJ: Ikon színének visszaállítása
    }

    public void EnableSpeakButton()
    {
        if (speakAction == null)
        {
            Debug.LogError("[WhisperMicController] Cannot enable speak action: speakAction is NULL!");
            return;
        }

        if (!isSpeakActionEnabled) // Csak akkor cselekszünk, ha tényleg változás van
        {
            speakAction.Enable();
            isSpeakActionEnabled = true;
            UpdateStatusText(StatusIdle);
            SetMicrophoneIconColor(iconColorReady); // <<< ÚJ: Színváltás "készenléti" állapotra
            Debug.Log("[WhisperMicController] Speak Action ENABLED. Icon set to Ready.");
        }
        // else { Debug.Log("[WhisperMicController] EnableSpeakButton called, but action was already enabled."); }
    }

    public void DisableSpeakButton()
    {
        if (speakAction == null)
        {
            // Debug.LogError("[WhisperMicController] Cannot disable speak action: speakAction is NULL!"); // Lehet, hogy nem hiba, ha nincs beállítva
            return;
        }

        if (isSpeakActionEnabled || speakAction.enabled) // Ellenőrizzük mindkettőt a biztonság kedvéért
        {
            speakAction.Disable();
            isSpeakActionEnabled = false;
            UpdateStatusText(StatusDisabled);
            SetMicrophoneIconColor(iconColorDefault); // <<< ÚJ: Színváltás alapértelmezettre
            Debug.Log("[WhisperMicController] Speak Action DISABLED. Icon set to Default.");

            if (isRecording)
            {
                Debug.LogWarning("[WhisperMicController] Speak action disabled while recording was active. Stopping recording.");
                StopRecordingInternal();
                // Az ikonszín már default-ra lett állítva.
            }
        }
        // else { Debug.Log("[WhisperMicController] DisableSpeakButton called, but action was already disabled."); }
    }

    // --- Hangrögzítés Indítása/Leállítása (Input Action Callbackek) ---

    private void OnRecordStarted(InputAction.CallbackContext context)
    {
        // Az IFM állapot ellenőrzése fontos, hogy ne tudjon a user rosszkor beszélni
        if (InteractionFlowManager.Instance != null &&
            InteractionFlowManager.Instance.CurrentState != InteractionFlowManager.InteractionState.WaitingForUserInput)
        {
            Debug.LogWarning($"[WhisperMicController] Record button pressed, but IFM state is '{InteractionFlowManager.Instance.CurrentState}', not WaitingForUserInput. Ignoring.");
            return;
        }

        // Ha a gomb nincs expliciten engedélyezve az isSpeakActionEnabled által, ne induljon felvétel
        // (Bár az InputAction.Enable/Disable ezt már kezeli, ez egy plusz biztonsági réteg lehet)
        if (!isSpeakActionEnabled)
        {
            Debug.LogWarning("[WhisperMicController] OnRecordStarted called, but isSpeakActionEnabled is false. Ignoring.");
            return;
        }

        if (isRecording)
        {
            Debug.LogWarning("[WhisperMicController] OnRecordStarted called, but already recording.");
            return;
        }

#if UNITY_ANDROID && !UNITY_EDITOR
        if (!UnityEngine.Android.Permission.HasUserAuthorizedPermission(UnityEngine.Android.Permission.Microphone))
        {
            Debug.LogError("[WhisperMicController] Cannot start recording: Microphone permission not granted.");
            RequestMicrophonePermission();
            return;
        }
#endif

        if (pressSound != null && audioSource != null) audioSource.PlayOneShot(pressSound);

        Debug.Log("[WhisperMicController] Recording Started...");
        UpdateStatusText(StatusRecording);
        SetMicrophoneIconColor(iconColorRecording); // <<< ÚJ: Színváltás "felvétel" állapotra
        isRecording = true;

        try
        {
            recordedClip = Microphone.Start(microphoneDevice, false, recordingDurationSeconds, sampleRate);
            if (recordedClip == null)
            {
                Debug.LogError("[WhisperMicController] Microphone.Start failed to return an AudioClip.");
                isRecording = false;
                UpdateStatusText("Mic Error");
                SetMicrophoneIconColor(isSpeakActionEnabled ? iconColorReady : iconColorDefault); // Vissza az előző állapotnak megfelelő színre
            }
        }
        catch (Exception e)
        {
            Debug.LogError($"[WhisperMicController] Exception during Microphone.Start: {e.Message}");
            isRecording = false;
            UpdateStatusText("Mic Exception");
            SetMicrophoneIconColor(isSpeakActionEnabled ? iconColorReady : iconColorDefault);
        }
    }

    private void OnRecordStopped(InputAction.CallbackContext context)
    {
        if (!isRecording)
        {
            Debug.LogWarning("[WhisperMicController] OnRecordStopped called, but was not in 'isRecording' state.");
            // Ha a gomb még mindig "Ready" állapotban van (zöld), hagyjuk úgy. Ha "Default" (szürke), akkor is.
            // Ezt az Enable/DisableSpeakButton kezeli.
            return;
        }

        Debug.Log("[WhisperMicController] Recording Stopped by user input.");
        if (releaseSound != null && audioSource != null) audioSource.PlayOneShot(releaseSound);

        // A szín itt még marad piros, vagy átvált "feldolgozás" színre, ha lenne olyan.
        // Az IFM DisableSpeakButton hívása fogja majd alapértelmezettre (szürke) állítani.
        // Vagy ha a feldolgozás után azonnal újra lehet beszélni, az IFM EnableSpeakButton hívása zöldre.
        // Jelenleg a piros szín marad, amíg az IFM nem avatkozik közbe.
        // Ezért itt nem váltunk színt expliciten, hagyjuk a pirosat, jelezve, hogy valami történik.
        // A ProcessRecordedAudio után az IFM úgyis Disable-t hív.

        ProcessRecordedAudio();
    }

    private void StopRecordingInternal()
    {
        if (isRecording) // Csak akkor, ha tényleg futott
        {
            if (Microphone.IsRecording(microphoneDevice))
            {
                Microphone.End(microphoneDevice);
            }
            isRecording = false;
            if (recordedClip != null)
            {
                Destroy(recordedClip);
                recordedClip = null;
            }
            Debug.Log("[WhisperMicController] Stopped recording internally (no processing).");
            // Az ikonszínt az hívó (pl. DisableSpeakButton, OnDisable) állítja be.
        }
    }

    private void ProcessRecordedAudio()
    {
        if (!isRecording)
        {
            Debug.LogWarning("[WhisperMicController] ProcessRecordedAudio called, but not in 'isRecording' state. Aborting.");
            // Ha valamiért idejutnánk anélkül, hogy isRecording true lenne,
            // biztosítsuk, hogy a gomb és az ikon a megfelelő állapotban van.
            if (isSpeakActionEnabled) // Ha az IFM szerint lehetne beszélni
            {
                SetMicrophoneIconColor(iconColorReady);
                UpdateStatusText(StatusIdle);
            }
            else // Ha az IFM szerint nem lehet beszélni
            {
                SetMicrophoneIconColor(iconColorDefault);
                UpdateStatusText(StatusDisabled);
            }
            return;
        }

        int lastSample = 0;
        bool wasActuallyRecordingOnDevice = Microphone.IsRecording(microphoneDevice);

        if (wasActuallyRecordingOnDevice)
        {
            lastSample = Microphone.GetPosition(microphoneDevice);
            Microphone.End(microphoneDevice);
            Debug.Log($"[WhisperMicController] Microphone.End called. Last sample position: {lastSample}");
        }
        else
        {
            Debug.LogWarning("[WhisperMicController] ProcessRecordedAudio called, but Microphone.IsRecording() returned false. Potential issue or recording ended prematurely.");
            // Ha a Microphone.IsRecording false, de az isRecording flagünk true volt, akkor is visszaállítjuk a flaget
            // és megpróbáljuk helyreállítani a UI-t.
            isRecording = false; // Fontos visszaállítani
            UpdateStatusText("Mic Error"); // Vagy valami informatívabb
            Invoke(nameof(ResetStatusAndIconToIdleOrDisabled), 1.0f); // Késleltetett visszaállítás
            if (recordedClip != null) Destroy(recordedClip); // Takarítás
            recordedClip = null;
            return;
        }

        // Az isRecording flag-et még ne állítsuk false-ra, csak a feldolgozás végén/hiba esetén.
        // Az ikonszín marad piros (recording), amíg a feldolgozás tart.
        UpdateStatusText(StatusProcessing); // Jelezzük a feldolgozást

        if (recordedClip != null && lastSample > 0)
        {
            AudioClip trimmedClip = CreateTrimmedClip(recordedClip, lastSample);
            Destroy(recordedClip); // Az eredeti nagy bufferre már nincs szükség
            recordedClip = null;

            if (trimmedClip != null)
            {
                Debug.Log($"[WhisperMicController] Audio trimmed: {trimmedClip.length} seconds. Converting to WAV.");
                byte[] wavData = ConvertAudioClipToWav(trimmedClip);
                Destroy(trimmedClip); // A vágott klipre sincs már szükség a konverzió után

                if (wavData != null && wavData.Length > 44) // 44 a WAV header mérete
                {
                    // Sikeres WAV konverzió, küldés Whispernek
                    ProcessWavData(wavData);
                    // Az isRecording flag-et a ProcessWavData -> ProcessWhisperResponse lánc végén állítjuk false-ra.
                }
                else
                {
                    Debug.LogError("[WhisperMicController] Failed to convert recorded audio to valid WAV format or WAV data is too short.");
                    UpdateStatusText("Conversion Error");
                    isRecording = false; // Hiba esetén is visszaállítjuk
                    Invoke(nameof(ResetStatusAndIconToIdleOrDisabled), 2.0f); // Visszaállás késleltetve
                }
            }
            else
            {
                Debug.LogWarning("[WhisperMicController] Processing stopped: trimmed clip was null (lastSample might have been too small or GetData failed).");
                UpdateStatusText("Processing Error"); // Vagy StatusIdle / StatusDisabled
                isRecording = false; // Hiba esetén is visszaállítjuk
                Invoke(nameof(ResetStatusAndIconToIdleOrDisabled), 2.0f);
            }
        }
        else
        {
            Debug.LogWarning($"[WhisperMicController] Processing stopped: No valid audio data captured (lastSample={lastSample}, recordedClip was null or became null).");
            if (recordedClip != null) Destroy(recordedClip); // Takarítsuk el, ha még létezne
            recordedClip = null;
            UpdateStatusText("No Audio Data"); // Vagy StatusIdle / StatusDisabled
            isRecording = false; // Hiba esetén is visszaállítjuk
            Invoke(nameof(ResetStatusAndIconToIdleOrDisabled), 2.0f);
        }
    }


    // --- Hang Feldolgozás és API Hívás ---

    // CreateTrimmedClip változatlan marad
    private AudioClip CreateTrimmedClip(AudioClip originalClip, int lastSamplePosition)
    {
        if (originalClip == null)
        {
            Debug.LogError("[WhisperMicController] CreateTrimmedClip: originalClip is null.");
            return null;
        }
        if (lastSamplePosition <= 0)
        {
            Debug.LogError($"[WhisperMicController] CreateTrimmedClip: lastSamplePosition ({lastSamplePosition}) is not positive.");
            return null;
        }

        // Biztonsági ellenőrzés, hogy a lastSamplePosition ne legyen nagyobb, mint a klip hossza
        if (lastSamplePosition > originalClip.samples)
        {
            Debug.LogWarning($"[WhisperMicController] CreateTrimmedClip: lastSamplePosition ({lastSamplePosition}) was greater than originalClip.samples ({originalClip.samples}). Clamping to clip length.");
            lastSamplePosition = originalClip.samples;
        }

        // Ha a clamp után 0 vagy negatív lett (elvileg nem fordulhat elő, ha az eredeti lastSamplePosition pozitív volt)
        if (lastSamplePosition <= 0)
        {
            Debug.LogError($"[WhisperMicController] CreateTrimmedClip: lastSamplePosition became 0 or less after clamping. Cannot create clip.");
            return null;
        }

        float[] data = new float[lastSamplePosition * originalClip.channels];
        if (!originalClip.GetData(data, 0))
        {
            Debug.LogError("[WhisperMicController] CreateTrimmedClip: originalClip.GetData() failed.");
            return null;
        }

        AudioClip trimmed = AudioClip.Create("RecordedTrimmed", lastSamplePosition, originalClip.channels, originalClip.frequency, false);
        if (trimmed == null) // Extra ellenőrzés, bár a Create ritkán ad null-t, ha a paraméterek jók
        {
            Debug.LogError("[WhisperMicController] CreateTrimmedClip: AudioClip.Create returned null.");
            return null;
        }

        if (!trimmed.SetData(data, 0))
        {
            Debug.LogError("[WhisperMicController] CreateTrimmedClip: trimmed.SetData() failed.");
            Destroy(trimmed); // Fontos a létrehozott, de sikertelenül feltöltött klip törlése
            return null;
        }
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
        if (wavData == null || wavData.Length <= 44)
        {
            Debug.LogError("[WhisperMicController] ProcessWavData: Invalid WAV data.");
            isRecording = false; // Fontos visszaállítani
            Invoke(nameof(ResetStatusAndIconToIdleOrDisabled), 1.0f); // Késleltetett visszaállítás
            return;
        }

        Debug.Log($"[WhisperMicController] Sending {wavData.Length} bytes of WAV data to Whisper API...");
        UpdateStatusText(StatusTranscribing);
        // Az ikonszín marad piros (vagy az utolsó beállított), amíg a feldolgozás tart.
        // Az IFM DisableSpeakButton hívása fogja majd alapértelmezettre (szürke) állítani.

        if (openAIWebRequest != null)
        {
            StartCoroutine(openAIWebRequest.SendAudioToWhisper(wavData, ProcessWhisperResponse));
        }
        else
        {
            Debug.LogError("[WhisperMicController] OpenAIWebRequest reference is not set! Cannot send audio.");
            UpdateStatusText("Error: API unavailable");
            isRecording = false;
            Invoke(nameof(ResetStatusAndIconToIdleOrDisabled), 2.0f);
        }
    }

    // Whisper válasz feldolgozása (Callback)
    private void ProcessWhisperResponse(string transcription)
    {
        if (string.IsNullOrEmpty(transcription))
        {
            Debug.LogWarning("[WhisperMicController] Whisper API returned an empty or null transcription.");
            UpdateStatusText("Transcription Failed");
            isRecording = false;
            Invoke(nameof(ResetStatusAndIconToIdleOrDisabled), 2.0f);
            return;
        }

        Debug.Log($"[WhisperMicController] Whisper Transcription Successful: '{transcription}'");

        if (InteractionFlowManager.Instance != null)
        {
            InteractionFlowManager.Instance.HandleUserQuestionReceived(transcription);
        }
        else
        {
            Debug.LogError("[WhisperMicController] InteractionFlowManager.Instance is null! Cannot forward transcription.");
            UpdateStatusText("Error: Flow unavailable");
            Invoke(nameof(ResetStatusAndIconToIdleOrDisabled), 2.0f);
        }

        isRecording = false; // Fontos, hogy itt is false legyen, miután az IFM megkapta
        Debug.Log("[WhisperMicController] Transcription processed and forwarded. isRecording set to false.");
        // Az ikonszínt és a gomb állapotát az IFM kezeli a HandleUserQuestionReceived után.
        // Jellemzően DisableSpeakButton() hívódik, ami az ikont default-ra állítja.
    }

    // --- Segédfüggvények ---

    private void ResetStatusAndIconToIdleOrDisabled()
    {
        if (speakAction != null && isSpeakActionEnabled) // Figyeljünk az isSpeakActionEnabled-re
        {
            UpdateStatusText(StatusIdle);
            SetMicrophoneIconColor(iconColorReady);
        }
        else
        {
            UpdateStatusText(StatusDisabled);
            SetMicrophoneIconColor(iconColorDefault);
        }
    }

    // Segédfüggvény a UI szöveg frissítéséhez (null ellenőrzéssel)
    private void UpdateStatusText(string message)
    {
        if (statusText != null)
        {
            statusText.text = message;
        }
    }

    private void SetMicrophoneIconColor(Color newColor)
    {
        if (microphoneIconImage != null)
        {
            microphoneIconImage.color = newColor;
        }
        // else { Debug.LogWarning("[WhisperMicController] Microphone Icon Image not assigned."); } // Csak ha hibakereséshez kell
    }

} // <-- Osztály vége
