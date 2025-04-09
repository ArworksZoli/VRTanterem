using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Android;
using UnityEngine.InputSystem;
using TMPro;



public class WhisperMicController : MonoBehaviour
{
    [Header("Input Settings")]
    [SerializeField] private InputActionAsset inputActions; // Húzd ide az Input Actions assetet az Inspectorban
    [SerializeField] private string actionMapName = "XRI RightHand Interaction"; // Vagy "VoiceInput", ha azt hoztad létre
    [SerializeField] private string recordActionName = "RecordVoice"; // A létrehozott Action neve

    [Header("Microphone Settings")]
    [SerializeField] private int recordingDurationSeconds = 60; // Max felvételi idő (biztonsági limit)
    [SerializeField] private int sampleRate = 44100; // Standard hangminőség

    [Header("API Integration (Placeholder)")]
    [SerializeField] private OpenAIWebRequest openAIWebRequest; // Húzd ide az OpenAIWebRequest komponenst tartalmazó GameObjectet/Prefabot

    [Header("External Components")] // Új fejléc vagy a meglévőhöz adjuk
    [Tooltip("Reference to the TextToSpeechManager for playback status")]
    [SerializeField] private TextToSpeechManager textToSpeechManager;

    [SerializeField] private TextMeshProUGUI statusText; // Button visszajelző

    [SerializeField] private AudioClip pressSound;
    [SerializeField] private AudioClip releaseSound;

    private const string IdleText = "Press A for question"; // Vagy amit szeretnél alapértelmezettnek
    private const string RecordingText = "RECORDING";
    private const string ProcessingText = "Processing Audio...";
    private const string TranscribingText = "Transcribing...";
    private const string SendingToAssistantText = "Sending to Assistant...";
    private InputAction recordAction;
    private AudioClip recordedClip;
    private string microphoneDevice;
    private bool isRecording = false;
    private AudioSource audioSource; // Hang lejátszásához/teszteléshez

    void Start()
    {
        
    }

    void Update()
    {
        if (OVRInput.GetDown(OVRInput.Button.One))
        {
            if (pressSound != null) audioSource.PlayOneShot(pressSound);
        }

        if (OVRInput.GetUp(OVRInput.Button.One))
        {
            if (releaseSound != null) audioSource.PlayOneShot(releaseSound);
        }
    }

    void Awake()
    {
        // Kezdeti állapot beállítása a UI-on
        UpdateStatusText(IdleText); // <<< Módosítva

        audioSource = GetComponent<AudioSource>();

        if (statusText != null) statusText.text = "Press A for question";

        audioSource = GetComponent<AudioSource>(); // AudioSource referencia

        // Mikrofon eszköz nevének lekérése (általában null = alapértelmezett)
        microphoneDevice = null; // Vagy Microphone.devices[0] ha specifikus kellene

        // Input Action referencia beállítása
        if (inputActions == null)
        {
            Debug.LogError("Input Actions asset is not assigned in the Inspector!");
            return;
        }
        var actionMap = inputActions.FindActionMap(actionMapName);
        if (actionMap == null)
        {
            Debug.LogError($"Action Map '{actionMapName}' not found!");
            return;
        }
        recordAction = actionMap.FindAction(recordActionName);
        if (recordAction == null)
        {
            Debug.LogError($"Action '{recordActionName}' not found in map '{actionMapName}'!");
            return;
        }

        // Jogosultság kérés (ha még nem tetted meg máshol)
        RequestMicrophonePermission();

        // Ellenőrizzük a TTS Manager referenciát
        if (textToSpeechManager == null)
        {
            Debug.LogWarning("TextToSpeechManager reference not set in WhisperMicController Inspector. Input disabling/enabling based on TTS status will not work.");
        }
    }

    void OnEnable()
    {
        if (recordAction != null)
        {
            recordAction.started += OnRecordStarted; // Átnevezve, ha még nem tetted meg
            recordAction.canceled += OnRecordStopped; // Átnevezve, ha még nem tetted meg
            recordAction.Enable();
            Debug.Log("Record action enabled and listeners attached.");
        }

        // Feliratkozás a TTS eseményekre
        if (textToSpeechManager != null)
        {
            textToSpeechManager.OnTTSPlaybackStart += DisableRecordingInput;
            textToSpeechManager.OnTTSPlaybackEnd += EnableRecordingInput;
            Debug.Log("Subscribed to TTS Manager events.");
        }
    }

    void OnDisable()
    {
        // Leiratkozás az Input Action eseményekről
        if (recordAction != null)
        {
            recordAction.started -= OnRecordStarted;
            recordAction.canceled -= OnRecordStopped;
            // Fontos: Itt csak akkor tiltsd le, ha tényleg le akarod tiltani a script inaktivitása miatt,
            // ne azért, mert a TTS épp megy. A TTS miatti tiltást a handler kezeli.
            // Ha a script disable-kor mindig le kell tiltani:
            if (recordAction.enabled) recordAction.Disable();
            Debug.Log("Record action listeners detached and action disabled.");
        }

        // Leiratkozás a TTS eseményekről
        if (textToSpeechManager != null)
        {
            textToSpeechManager.OnTTSPlaybackStart -= DisableRecordingInput;
            textToSpeechManager.OnTTSPlaybackEnd -= EnableRecordingInput;
            Debug.Log("Unsubscribed from TTS Manager events.");
        }

        // Ha épp felvétel van, állítsuk le (a korábbi javításból)
        if (isRecording)
        {
            Microphone.End(microphoneDevice);
            isRecording = false;
            Debug.LogWarning("Microphone recording stopped due to script disable.");
        }
    }

    // --- Új Handler Metódusok ---
    private void DisableRecordingInput(int sentenceIndex) // <<< PARAMÉTER HOZZÁADVA
    {
        // A metódus törzse változatlan maradhat, az indexet nem használjuk itt.
        if (recordAction != null && recordAction.enabled)
        {
            recordAction.Disable();
            Debug.Log($"Record action DISABLED due to TTS playback start (Sentence Index: {sentenceIndex})."); // Opcionálisan logolhatod az indexet
        }
    }

    private void EnableRecordingInput(int sentenceIndex) // <<< PARAMÉTER HOZZÁADVA
    {
        // A metódus törzse változatlan maradhat, az indexet nem használjuk itt.
        if (recordAction != null && !recordAction.enabled)
        {
            if (!isRecording)
            {
                recordAction.Enable();
                Debug.Log($"Record action ENABLED after TTS playback end (Sentence Index: {sentenceIndex})."); // Opcionálisan logolhatod az indexet
            }
            else
            {
                Debug.LogWarning("TTS ended, but recording is somehow active? Input remains disabled for safety.");
            }
        }
    }

    // Külön metódus a jogosultságkéréshez (átláthatóságért)
    private void RequestMicrophonePermission()
    {
#if UNITY_ANDROID && !UNITY_EDITOR // Csak Android eszközön, Editorban nem kell
        if (!UnityEngine.Android.Permission.HasUserAuthorizedPermission(UnityEngine.Android.Permission.Microphone))
        {
            UnityEngine.Android.Permission.RequestUserPermission(UnityEngine.Android.Permission.Microphone);
        }
#endif
    }

    // --- Hangrögzítés indítása ---
    private void OnRecordStarted(InputAction.CallbackContext context)
    {

        UpdateStatusText(RecordingText);

        if (statusText != null) statusText.text = "RECORDING";

        if (isRecording) return; // Már felvétel van folyamatban

        // Ellenőrizzük újra a jogosultságot, hátha időközben elvették
#if UNITY_ANDROID && !UNITY_EDITOR
        if (!UnityEngine.Android.Permission.HasUserAuthorizedPermission(UnityEngine.Android.Permission.Microphone))
        {
            Debug.LogWarning("Microphone permission was not granted.");
            // Esetleg kérjük újra, vagy adjunk visszajelzést a usernek
            RequestMicrophonePermission();
            return; // Ne indítsuk a felvételt engedély nélkül
        }
#endif

        Debug.Log("Recording Started...");
        isRecording = true;
        recordedClip = Microphone.Start(microphoneDevice, false, recordingDurationSeconds, sampleRate);
        // Ide jöhet vizuális/audio feedback a felhasználónak
    }

    // --- Hangrögzítés leállítása ---
    private void OnRecordStopped(InputAction.CallbackContext context)
    {

        UpdateStatusText(IdleText);

        if (statusText != null) statusText.text = "IDLE";

        if (!isRecording) return; // Nem is volt felvétel

        Debug.Log("Recording Stopped.");
        int lastSample = Microphone.GetPosition(microphoneDevice);
        Microphone.End(microphoneDevice); // Leállítja a felvételt és visszaadja az AudioClip-et

        if (recordedClip != null && lastSample > 0) // Csak akkor dolgozzuk fel, ha volt hang
        {
            // Lemásoljuk a ténylegesen rögzített részt, hogy ne a teljes bufferrel dolgozzunk
            AudioClip trimmedClip = CreateTrimmedClip(recordedClip, lastSample);

            // Itt kell majd meghívni a következő lépést: hangadat előkészítése és küldése
            Debug.Log($"Audio recorded: {trimmedClip.length} seconds. Ready to process.");
            // Példa: visszajátszás teszteléshez (opcionális)
            // audioSource.PlayOneShot(trimmedClip);

            // ----- Következő lépés: Konvertálás WAV-ba és feldolgozás -----
            byte[] wavData = ConvertAudioClipToWav(trimmedClip);
            if (wavData != null)
            {
                ProcessWavData(wavData); // Meghívjuk az új feldolgozó metódust
            }
            else
            {
                Debug.LogError("Failed to convert recorded audio to WAV format.");
            }

            // A levágott klipet már nem kell megtartani a konverzió után,
            // hacsak nem akarod pl. visszajátszani a konvertált hangot.
            // Ha már nincs rá szükség, törölheted:
            if (trimmedClip != null) Destroy(trimmedClip);

        }
        else
        {
            Debug.LogWarning("Stopping recording, but no valid audio data captured.");
        }


        recordedClip = null; // Töröljük a referenciát a nagy bufferre
        isRecording = false;
        // Ide jöhet vizuális/audio feedback vége
    }

    // Segédfüggvény a rögzített AudioClip levágásához a tényleges hosszra
    private AudioClip CreateTrimmedClip(AudioClip originalClip, int lastSamplePosition)
    {
        if (lastSamplePosition <= 0) return null; // Nincs hang

        float[] data = new float[lastSamplePosition * originalClip.channels];
        originalClip.GetData(data, 0);

        // Hozzuk létre az új, levágott AudioClip-et
        AudioClip trimmed = AudioClip.Create("RecordedTrimmed", lastSamplePosition, originalClip.channels, originalClip.frequency, false);
        trimmed.SetData(data, 0);

        // Az eredeti nagy klipet törölhetjük, ha már nincs rá szükség
        Destroy(originalClip);

        return trimmed;
    }

    // --- WAV Konverziós Függvények ---

    /// <summary>
    /// Converts an AudioClip to a WAV byte array.
    /// </summary>
    /// <param name="clip">The AudioClip to convert.</param>
    /// <returns>A byte array representing the WAV file, or null if conversion fails.</returns>
    public static byte[] ConvertAudioClipToWav(AudioClip clip)
    {
        if (clip == null)
        {
            Debug.LogError("Cannot convert null AudioClip to WAV.");
            return null;
        }

        float[] samples = new float[clip.samples * clip.channels];
        if (!clip.GetData(samples, 0))
        {
            Debug.LogError("Failed to get data from AudioClip.");
            return null;
        }

        // A WAV header mérete fixen 44 byte
        byte[] wavFile = new byte[samples.Length * 2 + 44]; // * 2, mert 16 bites PCM-et használunk

        // --- WAV Header írása ---
        // RIFF chunk descriptor
        WriteString(wavFile, 0, "RIFF");
        WriteInt32(wavFile, 4, wavFile.Length - 8); // Teljes méret - 8 byte (RIFF és WAVE nélkül)
        WriteString(wavFile, 8, "WAVE");

        // fmt sub-chunk
        WriteString(wavFile, 12, "fmt ");
        WriteInt32(wavFile, 16, 16); // Sub-chunk mérete (16 for PCM)
        WriteInt16(wavFile, 20, 1);  // Audio format (1 for PCM)
        WriteInt16(wavFile, 22, (short)clip.channels); // Csatornák száma
        WriteInt32(wavFile, 24, clip.frequency); // Mintavételezési frekvencia (Sample Rate)
        WriteInt32(wavFile, 28, clip.frequency * clip.channels * 2); // Byte Rate (SampleRate * NumChannels * BitsPerSample/8)
        WriteInt16(wavFile, 32, (short)(clip.channels * 2)); // Block Align (NumChannels * BitsPerSample/8)
        WriteInt16(wavFile, 34, 16); // Bits Per Sample (16 bites PCM)

        // data sub-chunk
        WriteString(wavFile, 36, "data");
        WriteInt32(wavFile, 40, samples.Length * 2); // Adat mérete (SampleCount * NumChannels * BitsPerSample/8)

        // --- Audio Adatok Konvertálása és Írása (Float -> 16-bit PCM) ---
        int headerOffset = 44;
        for (int i = 0; i < samples.Length; i++)
        {
            // Float [-1.0, 1.0] -> Int16 [-32768, 32767]
            short sampleInt = (short)(samples[i] * 32767.0f);

            // Írás Little Endian formátumban
            byte byte1 = (byte)(sampleInt & 0xff);
            byte byte2 = (byte)((sampleInt >> 8) & 0xff);

            wavFile[headerOffset + i * 2] = byte1;
            wavFile[headerOffset + i * 2 + 1] = byte2;
        }

        Debug.Log($"Converted AudioClip to WAV: {wavFile.Length} bytes, {clip.length} seconds, {clip.frequency} Hz, {clip.channels} channels.");
        return wavFile;
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


    // Ezt a metódust hívjuk meg a StopRecording-ból a WAV adatokkal
    private void ProcessWavData(byte[] wavData)
    {
        if (wavData == null || wavData.Length == 0)
        {
            Debug.LogError("ProcessWavData called with invalid data.");
            return;
        }

        Debug.Log($"Processing WAV data: {wavData.Length} bytes. Ready to send to API.");

        // ----- Következő lépés helye: -----
        // Itt kell meghívni az OpenAIWebRequest szkriptet a wavData-val
        if (openAIWebRequest != null)
        {
            // Pl.: openAIWebRequest.SendAudioToWhisper(wavData); // Ezt a metódust még létre kell hozni az OpenAIWebRequest-ben!
            Debug.Log("Attempting to send data via OpenAIWebRequest (functionality pending).");
            // ----- IDE JÖN AZ API HÍVÁS INDÍTÁSA -----
            StartCoroutine(openAIWebRequest.SendAudioToWhisper(wavData, ProcessWhisperResponse)); // Hozzáadjuk a callback függvényt is!
        }
        else
        {
            Debug.LogError("OpenAIWebRequest reference is not set in the Inspector!");
        }
    }

    // Új metódus a Whisper válaszának feldolgozására
    private void ProcessWhisperResponse(string transcription)
    {
        if (string.IsNullOrEmpty(transcription))
        {
            Debug.LogWarning("Whisper API returned an empty or null transcription.");
            UpdateStatusText("Transcription Failed"); // Visszajelzés a UI-on
            Invoke(nameof(ResetStatusTextToIdle), 2.0f);
            return;
        }
        else
        {
            Debug.Log($"Whisper Transcription: {transcription}");
            UpdateStatusText(SendingToAssistantText); // Visszajelzés, hogy küldjük

            // ----- ÁTADÁS AZ OPENAIWEBREQUEST-NEK -----
            if (openAIWebRequest != null)
            {
                // Meghívjuk az OpenAIWebRequest új metódusát a felismert szöveggel
                openAIWebRequest.ProcessVoiceInput(transcription);
            }
            else
            {
                Debug.LogError("OpenAIWebRequest reference is not set in the Inspector!");
                UpdateStatusText("Error: Assistant unavailable");
                Invoke(nameof(ResetStatusTextToIdle), 2.0f);
                return;
            }

            // Azonnali visszaállítás
            UpdateStatusText(IdleText);
            Debug.Log("Status immediately reset to IdleText after initiating Assistant request.");

            // ----- ÁTADÁS VÉGE -----

            // 2. LÉPÉS: Azonnali visszaállítás az alap ("Press 'A'...") szövegre
            UpdateStatusText(IdleText); // Itt használjuk az IdleText konstans-t!
            Debug.Log("Status immediately reset to IdleText after initiating Assistant request.");
        }

    }

    // Dedikált metódus a statusz szöveg visszaállításához az IdleText-re
    // Ezt hívjuk Invoke-val hiba esetén, hogy a hibaüzenet látható legyen egy ideig.
    private void ResetStatusTextToIdle()
    {
        // Csak akkor állítjuk vissza, ha épp nem veszünk fel hangot
        if (!isRecording)
        {
            UpdateStatusText(IdleText); // Itt is az IdleText-et használjuk
            Debug.Log($"Status reset to IdleText (likely after error delay).");
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
            // Csak akkor logoljunk, ha tényleg próbálunk írni, és nincs hova
            if (!string.IsNullOrEmpty(message))
            {
                Debug.LogWarning("StatusText UI element is not assigned in the Inspector.");
            }
        }
    }


    // Segédfüggvény a statusz szöveg visszaállításához (ha kell)
    private void ResetStatusText()
    {
        UpdateStatusText(IdleText);
    }
}
