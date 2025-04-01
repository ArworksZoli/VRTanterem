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

    [SerializeField] private TextMeshProUGUI statusText; // Button visszajelző

    [SerializeField] private AudioClip pressSound;
    [SerializeField] private AudioClip releaseSound;

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
        
    }

    void Awake()
    {
        if (statusText != null) statusText.text = "Press A";

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
    }

    void OnEnable()
    {
        if (recordAction != null)
        {
            // Feliratkozás az eseményekre
            recordAction.started += StartRecording; // Gomb lenyomva
            recordAction.canceled += StopRecording; // Gomb felengedve
            recordAction.Enable(); // Fontos: engedélyezni kell az Action-t!
            Debug.Log("Record action enabled and listeners attached.");
        }
    }

    void OnDisable()
    {
        if (recordAction != null)
        {
            // Leiratkozás az eseményekről (fontos a memóriaszivárgás elkerülése végett)
            recordAction.started -= StartRecording;
            recordAction.canceled -= StopRecording;
            recordAction.Disable(); // Fontos: letiltani az Action-t, ha a komponens inaktív
            Debug.Log("Record action disabled and listeners detached.");
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
    private void StartRecording(InputAction.CallbackContext context)
    {

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
    private void StopRecording(InputAction.CallbackContext context)
    {

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

            // ----- IDE JÖN MAJD A KÖVETKEZŐ LÉPÉS: ProcessAudioClip(trimmedClip); -----
            // ProcessAudioClip(trimmedClip);
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

    // Ezt a metódust hívjuk majd meg a StopRecording-ból
    private void ProcessAudioClip(AudioClip clip)
    {
        if (clip == null) return;

        Debug.Log($"Processing AudioClip '{clip.name}'...");

        // ----- Következő lépések helye: -----
        // 1. AudioClip konvertálása WAV byte tömbbé (Lépés 4)
        // byte[] wavData = ConvertAudioClipToWav(clip);

        // 2. WAV adatok küldése a Whisper API-nak (Lépés 5)
        // StartCoroutine(SendToWhisperAPI(wavData));

        // A feldolgozás után a clip erőforrást is fel kell szabadítani
        // Destroy(clip); // Vagy később, az API hívás után
    }
}
