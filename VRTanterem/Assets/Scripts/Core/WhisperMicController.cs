using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Android;
using UnityEngine.InputSystem;
using TMPro;
using System;

public class WhisperMicController : MonoBehaviour
{
    // ... (A Headerek és SerializeField változók változatlanok maradnak) ...
    [Header("Input Settings")]
    [SerializeField] private InputActionAsset inputActions;
    [SerializeField] private string actionMapName = "XRI RightHand Interaction";
    [SerializeField] private string recordActionName = "RecordVoice";

    [Header("Microphone Settings")]
    [SerializeField] private int recordingDurationSeconds = 60;
    [SerializeField] private int sampleRate = 44100;

    [Header("API Integration (Placeholder)")]
    [SerializeField] private OpenAIWebRequest openAIWebRequest;

    [Header("External Components")]
    [Tooltip("Reference to the TextToSpeechManager for playback status")]
    [SerializeField] private TextToSpeechManager textToSpeechManager;
    [SerializeField] private TextMeshProUGUI statusText;
    [SerializeField] private AudioClip pressSound;
    [SerializeField] private AudioClip releaseSound;

    private const string IdleText = "Press A for question";
    private const string RecordingText = "RECORDING";
    private const string ProcessingText = "Processing Audio...";
    private const string TranscribingText = "Transcribing...";
    private const string SendingToAssistantText = "Sending to Assistant...";

    private int activeTTSPlaybackCount = 0;
    private object ttsCounterLock = new object();

    private InputAction recordAction;
    private AudioClip recordedClip;
    private string microphoneDevice;
    private bool isRecording = false;
    private AudioSource audioSource;

    // ... (Start, Update, Awake változatlanok maradnak) ...

    void Awake()
    {
        // Kezdeti állapot beállítása a UI-on
        UpdateStatusText(IdleText);

        audioSource = GetComponent<AudioSource>();
        if (audioSource == null)
        {
            Debug.LogWarning("AudioSource component missing on WhisperMicController GameObject. Adding one.");
            audioSource = gameObject.AddComponent<AudioSource>();
        }


        // Mikrofon eszköz nevének lekérése
        microphoneDevice = null;

        // Input Action referencia beállítása
        if (inputActions == null)
        {
            Debug.LogError("Input Actions asset is not assigned in the Inspector!");
            enabled = false; return;
        }
        var actionMap = inputActions.FindActionMap(actionMapName);
        if (actionMap == null)
        {
            Debug.LogError($"Action Map '{actionMapName}' not found!");
            enabled = false; return;
        }
        recordAction = actionMap.FindAction(recordActionName);
        if (recordAction == null)
        {
            Debug.LogError($"Action '{recordActionName}' not found in map '{actionMapName}'!");
            enabled = false; return;
        }

        // Jogosultság kérés
        RequestMicrophonePermission();

        // Ellenőrizzük a TTS Manager referenciát
        if (textToSpeechManager == null)
        {
            Debug.LogWarning("TextToSpeechManager reference not set in WhisperMicController Inspector. Input disabling/enabling based on TTS status will not work.");
        }
        Debug.Log("WhisperMicController Awake finished.");
    }

    private void RequestMicrophonePermission()
    {
#if UNITY_ANDROID && !UNITY_EDITOR // Csak Android eszközön, Editorban nem kell
        if (!UnityEngine.Android.Permission.HasUserAuthorizedPermission(UnityEngine.Android.Permission.Microphone))
        {
            Debug.Log("Requesting Microphone permission..."); // Logoljuk a kérést
            UnityEngine.Android.Permission.RequestUserPermission(UnityEngine.Android.Permission.Microphone);
            // Megjegyzés: A kérés aszinkron, a válasz nem azonnal érkezik meg.
            // A kódban lévő ellenőrzések (pl. OnRecordStarted-ben) kezelik, ha nincs meg az engedély.
        }
        else
        {
             Debug.Log("Microphone permission already granted."); // Logoljuk, ha már megvan
        }
#else
        // Editorban vagy más platformon nincs szükség explicit kérésre itt
        Debug.Log("Microphone permission check skipped (Not on Android device or in Editor).");
#endif
    }

    void OnEnable()
    {
        Debug.Log("WhisperMicController OnEnable called.");
        if (recordAction != null)
        {
            recordAction.started += OnRecordStarted;
            recordAction.canceled += OnRecordStopped;

            // Kezdeti engedélyezés: Csak akkor, ha NINCS aktív lejátszás
            // (A számláló állapotát is figyelembe vesszük, ha esetleg OnDisable/OnEnable között változna)
            lock (ttsCounterLock) // Biztonságos hozzáférés a számlálóhoz
            {
                if (activeTTSPlaybackCount == 0)
                {
                    if (!recordAction.enabled) recordAction.Enable(); // Csak ha nincs már engedélyezve
                    Debug.Log("Record action ENABLED initially in OnEnable (TTS count is 0).");
                }
                else
                {
                    if (recordAction.enabled) recordAction.Disable(); // Ha valamiért engedélyezve maradt, tiltsuk
                    Debug.Log($"Record action DISABLED initially in OnEnable (TTS count is {activeTTSPlaybackCount}).");
                }
            }
            Debug.Log("Record action listeners attached.");
        }

        // Feliratkozás a TTS eseményekre
        if (textToSpeechManager != null)
        {
            textToSpeechManager.OnTTSPlaybackStart += HandleTTSPlaybackStart; // Átnevezzük a handlert
            textToSpeechManager.OnTTSPlaybackEnd += HandleTTSPlaybackEnd;     // Átnevezzük a handlert
            Debug.Log("Subscribed to TTS Manager events.");

            // Ha OnEnablekor már játszik a TTS (elméletileg a számláló > 0),
            // a fenti logika már letiltotta az actiont.
        }
    }

    void OnDisable()
    {
        Debug.Log("WhisperMicController OnDisable called.");
        // Leiratkozás az Input Action eseményekről
        if (recordAction != null)
        {
            recordAction.started -= OnRecordStarted;
            recordAction.canceled -= OnRecordStopped;
            if (recordAction.enabled) recordAction.Disable();
            Debug.Log("Record action listeners detached and action disabled due to component disable.");
        }

        // Leiratkozás a TTS eseményekről
        if (textToSpeechManager != null)
        {
            textToSpeechManager.OnTTSPlaybackStart -= HandleTTSPlaybackStart;
            textToSpeechManager.OnTTSPlaybackEnd -= HandleTTSPlaybackEnd;
            Debug.Log("Unsubscribed from TTS Manager events.");
        }

        // Ha épp felvétel van, állítsuk le
        if (isRecording)
        {
            Microphone.End(microphoneDevice);
            isRecording = false;
            Debug.LogWarning("Microphone recording stopped due to script disable.");
            UpdateStatusText(IdleText);
        }

        // Opcionális: Nullázzuk a számlálót OnDisable-ben?
        // Lehet, hogy jobb, ha megmarad az értéke, ha csak ideiglenes a disable.
        // De ha biztosra akarunk menni, nullázhatjuk:
        // lock(ttsCounterLock) { activeTTSPlaybackCount = 0; }
    }

    private void HandleTTSPlaybackStart(int sentenceIndex)
    {
        lock (ttsCounterLock) // Biztonságos módosítás
        {
            activeTTSPlaybackCount++;
            Debug.Log($"[TTS Counter] Playback START detected (Index: {sentenceIndex}). Count incremented to: {activeTTSPlaybackCount}");

            // Ha ez az ELSŐ lejátszás (számláló 1 lett), akkor tiltsuk le az inputot
            if (activeTTSPlaybackCount == 1)
            {
                if (recordAction != null && recordAction.enabled)
                {
                    recordAction.Disable();
                    Debug.LogError($"--- Record action DISABLED (TTS Count became 1) ---");
                }
                else if (recordAction != null && !recordAction.enabled)
                {
                    // Már le volt tiltva, ami rendben van, ha pl. OnEnable-kor már futott a TTS
                    Debug.LogWarning("HandleTTSPlaybackStart: Action already disabled when TTS count became 1.");
                }
                else if (recordAction == null)
                {
                    Debug.LogError("HandleTTSPlaybackStart: Cannot disable action, recordAction is NULL!");
                }
            }
        }
    }

    private void HandleTTSPlaybackEnd(int sentenceIndex)
    {
        lock (ttsCounterLock) // Biztonságos módosítás
        {
            if (activeTTSPlaybackCount > 0) // Csak akkor csökkentünk, ha volt mit
            {
                activeTTSPlaybackCount--;
                Debug.Log($"[TTS Counter] Playback END detected (Index: {sentenceIndex}). Count decremented to: {activeTTSPlaybackCount}");

                // Ha ez volt az UTOLSÓ lejátszás (számláló 0 lett), akkor engedélyezzük az inputot ÉS visszaállítjuk a státuszt
                if (activeTTSPlaybackCount == 0)
                {
                    if (recordAction != null && !recordAction.enabled)
                    {
                        // Ellenőrizzük az isRecording flag-et (amit az 1. lépésben már false-ra kellett állítani)
                        if (!isRecording)
                        {
                            recordAction.Enable();
                            Debug.LogError($"--- Record action ENABLED (TTS Count became 0) ---");

                            // --- MÓDOSÍTÁS: Státusz visszaállítása IdleText-re ---
                            UpdateStatusText(IdleText);
                            Debug.Log("[HandleTTSPlaybackEnd] TTS finished, setting status to IdleText.");
                            // ----------------------------------------------------
                        }
                        else
                        {
                            // Ennek már nem szabadna előfordulnia az 1. lépés után, de a biztonság kedvéért itt marad a log
                            Debug.LogWarning("HandleTTSPlaybackEnd: TTS Count is 0, but recording flag is still active? Input remains disabled and status not reset.");
                        }
                    }
                    // ... (a többi else if ág változatlan maradhat) ...
                    else if (recordAction != null && recordAction.enabled)
                    {
                        Debug.LogWarning("HandleTTSPlaybackEnd: Action already enabled when TTS count became 0.");
                        // Itt is visszaállíthatjuk a státuszt, ha valamiért már engedélyezve volt a gomb
                        UpdateStatusText(IdleText);
                    }
                    else if (recordAction == null)
                    {
                        Debug.LogError("HandleTTSPlaybackEnd: Cannot enable action, recordAction is NULL!");
                    }
                }
            }
            else
            {
                Debug.LogWarning($"HandleTTSPlaybackEnd called (Index: {sentenceIndex}), but TTS count was already 0!");
            }
        }
    }

    // --- Hangrögzítés indítása ---
    private void OnRecordStarted(InputAction.CallbackContext context)
    {
        // --- MÓDOSÍTÁS: Ellenőrzés a számláló alapján ---
        bool canRecord = false;
        lock (ttsCounterLock)
        {
            canRecord = (activeTTSPlaybackCount == 0);
        }

        if (!canRecord)
        {
            Debug.LogWarning("OnRecordStarted called, but TTS is currently active (count > 0). Ignoring input.");
            // Opcionális hangjelzés/UI visszajelzés
            return;
        }
        // --- Ellenőrzés vége ---

        if (isRecording)
        {
            Debug.LogWarning("OnRecordStarted called, but already recording.");
            return;
        }

        // ... (Jogosultság ellenőrzés, hang lejátszás, Microphone.Start változatlan) ...
        // Hangjelzés lejátszása
        if (pressSound != null && audioSource != null)
        {
            audioSource.PlayOneShot(pressSound);
        }

        Debug.Log("Recording Started...");
        UpdateStatusText(RecordingText);
        isRecording = true;
        recordedClip = Microphone.Start(microphoneDevice, false, recordingDurationSeconds, sampleRate);

        if (recordedClip == null)
        {
            Debug.LogError("Microphone.Start failed to return an AudioClip.");
            isRecording = false;
            UpdateStatusText(IdleText);
            return;
        }
    }

    // --- Hangrögzítés leállítása ---
    private void OnRecordStopped(InputAction.CallbackContext context)
    {
        if (!isRecording)
        {
            Debug.LogWarning("OnRecordStopped called, but was not recording.");
            return; // Nem is volt felvétel
        }

        Debug.Log("Recording Stopped.");

        // Hangjelzés lejátszása
        if (releaseSound != null && audioSource != null)
        {
            audioSource.PlayOneShot(releaseSound);
        }
        else if (releaseSound == null)
        {
            Debug.Log("Release sound not assigned.");
        }
        else if (audioSource == null)
        {
            Debug.LogWarning("AudioSource missing for release sound.");
        }

        int lastSample = Microphone.GetPosition(microphoneDevice);
        Microphone.End(microphoneDevice); // Leállítja a felvételt

        // Fontos: Az isRecording flag-et csak a feldolgozás *után* állítjuk false-ra,
        // hogy a Disable/EnableRecordingInput helyesen működjön, ha pont ekkor ér véget a TTS.
        // Viszont a UI-t már most visszaállíthatjuk.
        UpdateStatusText(ProcessingText); // Jelezzük a feldolgozást

        if (recordedClip != null && lastSample > 0)
        {
            AudioClip trimmedClip = CreateTrimmedClip(recordedClip, lastSample);
            recordedClip = null; // Az eredeti nagy bufferre már nincs szükség

            if (trimmedClip != null)
            {
                Debug.Log($"Audio recorded: {trimmedClip.length} seconds. Ready to process.");
                byte[] wavData = ConvertAudioClipToWav(trimmedClip);
                Destroy(trimmedClip); // A vágott klipre sincs már szükség a konverzió után

                if (wavData != null)
                {
                    ProcessWavData(wavData);
                }
                else
                {
                    Debug.LogError("Failed to convert recorded audio to WAV format.");
                    UpdateStatusText("Conversion Error");
                    Invoke(nameof(ResetStatusTextToIdle), 2.0f); // Visszaállás Idle-re kis késleltetéssel
                    isRecording = false; // Itt már biztosan befejeztük
                }
            }
            else
            {
                Debug.LogWarning("Stopping recording, but trimmed clip was null.");
                UpdateStatusText(IdleText); // Visszaállás Idle-re
                isRecording = false; // Itt már biztosan befejeztük
            }
        }
        else
        {
            Debug.LogWarning("Stopping recording, but no valid audio data captured (lastSample=0 or recordedClip=null).");
            if (recordedClip != null) Destroy(recordedClip); // Takarítsuk el a nagy buffert is
            recordedClip = null;
            UpdateStatusText(IdleText); // Visszaállás Idle-re
            isRecording = false; // Itt már biztosan befejeztük
        }

        // Az isRecording flag beállítása a feldolgozás végén (vagy hiba esetén)
        // A ProcessWavData hívja a ProcessWhisperResponse-t, ami visszaállítja IdleText-re
        // a sikeres feldolgozás végén. A hibaágakat itt kezeltük.
        // A flag beállítása itt történik meg, miután a feldolgozás elindult vagy hibára futott.
        // isRecording = false; // <<< ÁTHELYEZVE A FELDOLGOZÁSI ÁGAK VÉGÉRE/HIBAÁGAKBA
    }


    // ... (CreateTrimmedClip, ConvertAudioClipToWav és segédfüggvényei változatlanok) ...
    private AudioClip CreateTrimmedClip(AudioClip originalClip, int lastSamplePosition)
    {
        if (lastSamplePosition <= 0 || originalClip == null) return null;

        float[] data = new float[lastSamplePosition * originalClip.channels];
        if (!originalClip.GetData(data, 0))
        {
            Debug.LogError("Failed to GetData from original clip in CreateTrimmedClip.");
            return null; // Hiba történt az adatok lekérésekor
        }

        AudioClip trimmed = AudioClip.Create("RecordedTrimmed", lastSamplePosition, originalClip.channels, originalClip.frequency, false);
        if (!trimmed.SetData(data, 0))
        {
            Debug.LogError("Failed to SetData for trimmed clip in CreateTrimmedClip.");
            Destroy(trimmed); // Ne hagyjunk szemetet
            return null; // Hiba történt az adatok beállításakor
        }

        // Az eredeti nagy klipet itt már nem töröljük, mert a hívó helyen kezeljük (OnRecordStopped)
        // Destroy(originalClip);

        return trimmed;
    }

    // --- WAV Konverziós Függvények ---
    public static byte[] ConvertAudioClipToWav(AudioClip clip)
    {
        if (clip == null)
        {
            Debug.LogError("ConvertAudioClipToWav Error: Input AudioClip is null.");
            return null;
        }

        // Logoljuk a klip adatait a konverzió megkezdésekor
        Debug.Log($"ConvertAudioClipToWav: Starting conversion for clip '{clip.name}'. Samples={clip.samples}, Channels={clip.channels}, Freq={clip.frequency}, Length={clip.length}s, LoadState={clip.loadState}");

        // Ellenőrizzük a minták számát
        if (clip.samples <= 0)
        {
            Debug.LogError($"ConvertAudioClipToWav Error: AudioClip has zero or negative samples ({clip.samples}). Cannot convert.");
            return null;
        }
        // Ellenőrizzük a csatornák számát
        if (clip.channels <= 0)
        {
            Debug.LogError($"ConvertAudioClipToWav Error: AudioClip has zero or negative channels ({clip.channels}). Cannot convert.");
            return null;
        }
        // Ellenőrizzük a frekvenciát
        if (clip.frequency <= 0)
        {
            Debug.LogError($"ConvertAudioClipToWav Error: AudioClip has zero or negative frequency ({clip.frequency}). Cannot convert.");
            return null;
        }

        float[] samples = new float[clip.samples * clip.channels];
        bool getDataSuccess = false;
        try
        {
            Debug.Log("ConvertAudioClipToWav: Attempting to call clip.GetData()...");
            getDataSuccess = clip.GetData(samples, 0);
            Debug.Log($"ConvertAudioClipToWav: clip.GetData() returned: {getDataSuccess}. Samples array length: {samples.Length}");
        }
        catch (Exception e)
        {
            // Elkapunk bármilyen kivételt a GetData során
            Debug.LogError($"ConvertAudioClipToWav Error: Exception during clip.GetData: {e.Message}\nStack Trace: {e.StackTrace}");
            return null; // Megszakítjuk a konverziót
        }

        if (!getDataSuccess)
        {
            Debug.LogError("ConvertAudioClipToWav Error: clip.GetData() returned false. Failed to retrieve audio data.");
            return null;
        }

        // Ellenőrizzük, hogy a samples tömb valóban tartalmaz-e adatot (nem csak nullákat pl.) - Opcionális, de hasznos lehet
        // float sum = 0; for(int i=0; i<Mathf.Min(samples.Length, 100); ++i) sum += Mathf.Abs(samples[i]);
        // Debug.Log($"ConvertAudioClipToWav: Sanity check - Sum of first 100 sample magnitudes: {sum}");
        // if (sum == 0 && samples.Length > 0) Debug.LogWarning("ConvertAudioClipToWav Warning: GetData succeeded but sample data seems to be all zeros.");


        // --- WAV Fájl Összeállítása ---
        try // A teljes WAV összeállítást try-catch blokkba tesszük
        {
            int dataSize = samples.Length * 2; // 16 bites PCM
            int fileSize = dataSize + 44;      // Adat + Header mérete
            byte[] wavFile = new byte[fileSize];

            // Header írása (változatlan)
            WriteString(wavFile, 0, "RIFF");
            WriteInt32(wavFile, 4, fileSize - 8);
            WriteString(wavFile, 8, "WAVE");
            WriteString(wavFile, 12, "fmt ");
            WriteInt32(wavFile, 16, 16);
            WriteInt16(wavFile, 20, 1);
            WriteInt16(wavFile, 22, (short)clip.channels);
            WriteInt32(wavFile, 24, clip.frequency);
            WriteInt32(wavFile, 28, clip.frequency * clip.channels * 2);
            WriteInt16(wavFile, 32, (short)(clip.channels * 2));
            WriteInt16(wavFile, 34, 16);
            WriteString(wavFile, 36, "data");
            WriteInt32(wavFile, 40, dataSize);

            // Adatok konvertálása és írása (változatlan, de a try-catch blokkon belül van)
            int headerOffset = 44;
            for (int i = 0; i < samples.Length; i++)
            {
                float sampleFloat = Mathf.Clamp(samples[i], -1.0f, 1.0f); // Biztonsági clamp
                short sampleInt = (short)(sampleFloat * 32767.0f);
                wavFile[headerOffset + i * 2] = (byte)(sampleInt & 0xff);
                wavFile[headerOffset + i * 2 + 1] = (byte)((sampleInt >> 8) & 0xff);
            }

            Debug.Log($"ConvertAudioClipToWav: Successfully constructed WAV data: {wavFile.Length} bytes.");
            return wavFile;
        }
        catch (Exception e)
        {
            // Elkapunk bármilyen kivételt a WAV összeállítása során (pl. indexelési hiba)
            Debug.LogError($"ConvertAudioClipToWav Error: Exception during WAV file construction: {e.Message}\nStack Trace: {e.StackTrace}");
            return null;
        }
    }

    // Segédfüggvények a header írásához (Little Endian)
    private static void WriteString(byte[] data, int offset, string value)
    {
        byte[] bytes = System.Text.Encoding.ASCII.GetBytes(value);
        System.Buffer.BlockCopy(bytes, 0, data, offset, bytes.Length);
    }

    private static void WriteInt16(byte[] data, int offset, short value)
    {
        data[offset] = (byte)(value & 0xff);
        data[offset + 1] = (byte)((value >> 8) & 0xff);
    }

    private static void WriteInt32(byte[] data, int offset, int value)
    {
        data[offset] = (byte)(value & 0xff);
        data[offset + 1] = (byte)((value >> 8) & 0xff);
        data[offset + 2] = (byte)((value >> 16) & 0xff);
        data[offset + 3] = (byte)((value >> 24) & 0xff);
    }


    // Feldolgozás indítása
    private void ProcessWavData(byte[] wavData)
    {
        if (wavData == null || wavData.Length == 0)
        {
            Debug.LogError("ProcessWavData called with invalid data.");
            UpdateStatusText("Processing Error");
            Invoke(nameof(ResetStatusTextToIdle), 2.0f);
            isRecording = false; // Itt is be kell állítani a flag-et hiba esetén
            return;
        }

        Debug.Log($"Processing WAV data: {wavData.Length} bytes. Sending to Whisper API...");
        UpdateStatusText(TranscribingText); // Jelezzük az átírást

        if (openAIWebRequest != null)
        {
            StartCoroutine(openAIWebRequest.SendAudioToWhisper(wavData, ProcessWhisperResponse));
        }
        else
        {
            Debug.LogError("OpenAIWebRequest reference is not set in the Inspector!");
            UpdateStatusText("Error: API unavailable");
            Invoke(nameof(ResetStatusTextToIdle), 2.0f);
            isRecording = false; // Itt is be kell állítani a flag-et hiba esetén
        }
    }

    // Whisper válasz feldolgozása
    private void ProcessWhisperResponse(string transcription)
    {
        if (string.IsNullOrEmpty(transcription))
        {
            Debug.LogWarning("Whisper API returned an empty or null transcription.");
            UpdateStatusText("Transcription Failed");
            Invoke(nameof(ResetStatusTextToIdle), 2.0f);
            isRecording = false; // <<< HIBA ESETÉN IS FALSE
            return;
        }

        Debug.Log($"Whisper Transcription: {transcription}");
        UpdateStatusText(SendingToAssistantText); // Visszajelzés, hogy küldjük az asszisztensnek

        if (openAIWebRequest != null)
        {
            openAIWebRequest.ProcessVoiceInput(transcription);
            // Az asszisztens válaszának megérkezése után az OpenAIWebRequest
            // valószínűleg visszaállítja a status textet Idle-re vagy elindítja a TTS-t.
            // Itt már nem kell azonnal visszaállítani Idle-re.

            // --- MÓDOSÍTÁS: isRecording false-ra állítása SIKER esetén ---
            isRecording = false;
            Debug.Log("[ProcessWhisperResponse] Handed off to OpenAIWebRequest, setting isRecording = false.");
            // -------------------------------------------------------------
        }
        else
        {
            Debug.LogError("OpenAIWebRequest reference is not set in the Inspector!");
            UpdateStatusText("Error: Assistant unavailable");
            Invoke(nameof(ResetStatusTextToIdle), 2.0f);
            isRecording = false; // <<< HIBA ESETÉN IS FALSE
        }
    }

    private void ResetStatusTextToIdle()
    {
        // Csak akkor állítjuk vissza, ha épp nem veszünk fel hangot ÉS a TTS sem beszél
        // (hogy ne írjuk felül pl. a "RECORDING" vagy egy folyamatban lévő hibaüzenetet,
        // vagy ha a TTS épp elindulni készül)
        if (!isRecording && (textToSpeechManager == null || !textToSpeechManager.IsPlaying))
        {
            UpdateStatusText(IdleText);
            Debug.Log($"Status reset to IdleText (likely after error delay or completion).");
        }
        else
        {
            // Logoljuk, miért nem állítottuk vissza (segít a debuggolásban)
            string reason = isRecording ? "isRecording=true" : "";
            if (textToSpeechManager != null && textToSpeechManager.IsPlaying)
            {
                if (!string.IsNullOrEmpty(reason)) reason += ", ";
                reason += "TTS is playing";
            }
            Debug.Log($"ResetStatusTextToIdle called, but not resetting because: {reason}.");
        }
    }

    // Segédfüggvény a UI szöveg frissítéséhez (null ellenőrzéssel)
    private void UpdateStatusText(string message)
    {
        if (statusText != null)
        {
            statusText.text = message;
        }
        else
        {
            // Csak akkor logoljunk, ha tényleg próbálunk írni, és nincs hova,
            // és nem az alapértelmezett szövegről van szó (hogy ne spameljen)
            if (!string.IsNullOrEmpty(message) && message != IdleText)
            {
                Debug.LogWarning($"StatusText UI element is not assigned in the Inspector. Cannot display: {message}");
            }
        }
    }

    // Ez a segédfüggvény valószínűleg már nem szükséges, mert a ResetStatusTextToIdle
    // és a normál munkafolyamat (pl. ProcessWhisperResponse után) kezeli a visszaállítást.
    // Ha mégis kellene egy direkt visszaállítási pont, akkor használható.
    // private void ResetStatusText()
    // {
    //     UpdateStatusText(IdleText);
    // }

} // <- Itt a WhisperMicController osztály vége