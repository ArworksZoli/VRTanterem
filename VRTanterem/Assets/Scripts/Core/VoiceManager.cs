using UnityEngine;
using UnityEngine.Events;
using TMPro;
using Oculus.Voice;
using System.Reflection;
using Meta.WitAi.CallbackHandlers;

public class VoiceManager : MonoBehaviour
{
    [Header("Wit Configuration")]
    [SerializeField] private AppVoiceExperience appVoiceExperience;
    [SerializeField] private WitResponseMatcher responseMatcher;
    [SerializeField] private TextMeshProUGUI transcriptionText;

    [Header("Voice Events")]
    [SerializeField] private UnityEvent wakeWordDetected;
    [SerializeField] private UnityEvent<string> completeTranscription;

    // Állapotváltozók
    private bool _voiceCommandReady = false;  // Készen áll-e a rendszer a hang parancs fogadására
    private bool _isWaitingForWakeWord = true;  // Ébresztőszóra vár-e a rendszer
    private bool _isTranscriptionComplete = false;  // Befejeződött-e egy transzkripció

    private void Awake()
    {
        // Eseménykezelők beállítása
        appVoiceExperience.VoiceEvents.OnRequestCompleted.AddListener(OnRequestCompleted);
        appVoiceExperience.VoiceEvents.OnPartialTranscription.AddListener(OnPartialTranscription);
        appVoiceExperience.VoiceEvents.OnFullTranscription.AddListener(OnFullTranscription);
        appVoiceExperience.VoiceEvents.OnAborted.AddListener(OnAborted);
        appVoiceExperience.VoiceEvents.OnError.AddListener(OnError);
        appVoiceExperience.VoiceEvents.OnStartListening.AddListener(OnStartListening);
        appVoiceExperience.VoiceEvents.OnStoppedListening.AddListener(OnStoppedListening);

        // Wake word matcher beállítása
        var eventField = typeof(WitResponseMatcher).GetField(name: "onMultiValueEvent", bindingAttr: BindingFlags.NonPublic | BindingFlags.Instance);
        if (eventField != null && eventField.GetValue(responseMatcher) is MultiValueEvent onMultiValueEvent)
        {
            onMultiValueEvent.AddListener(WakeWordDetected);
        }

        // Kezdeti állapot beállítása
        transcriptionText.text = "Mondd a varázsszót...";
    }

    private void Start()
    {
        // Kezdjünk el figyelni a wake wordre
        StartListeningForWakeWord();
    }

    private void OnDestroy()
    {
        // Eseménykezelők leiratkozása
        appVoiceExperience.VoiceEvents.OnRequestCompleted.RemoveListener(OnRequestCompleted);
        appVoiceExperience.VoiceEvents.OnPartialTranscription.RemoveListener(OnPartialTranscription);
        appVoiceExperience.VoiceEvents.OnFullTranscription.RemoveListener(OnFullTranscription);
        appVoiceExperience.VoiceEvents.OnAborted.RemoveListener(OnAborted);
        appVoiceExperience.VoiceEvents.OnError.RemoveListener(OnError);
        appVoiceExperience.VoiceEvents.OnStartListening.RemoveListener(OnStartListening);
        appVoiceExperience.VoiceEvents.OnStoppedListening.RemoveListener(OnStoppedListening);

        var eventField = typeof(WitResponseMatcher).GetField(name: "onMultiValueEvent", bindingAttr: BindingFlags.NonPublic | BindingFlags.Instance);
        if (eventField != null && eventField.GetValue(responseMatcher) is MultiValueEvent onMultiValueEvent)
        {
            onMultiValueEvent.RemoveListener(WakeWordDetected);
        }
    }

    // Kezdje el figyelni a wake word-öt
    public void StartListeningForWakeWord()
    {
        Debug.Log("Starting to listen for wake word...");
        _isWaitingForWakeWord = true;
        _voiceCommandReady = false;
        _isTranscriptionComplete = false;

        // Frissítsük a UI-t
        transcriptionText.text = "Mondd a varázsszót...";

        // Indítsuk el a hanghallgatást
        appVoiceExperience.Activate();
    }

    // Az ébresztőszó felismerése után a parancs figyelése
    private void StartListeningForCommand()
    {
        Debug.Log("Starting to listen for command...");
        _isWaitingForWakeWord = false;
        _voiceCommandReady = true;
        _isTranscriptionComplete = false;

        // Leállítjuk az előző figyelést és újat indítunk
        appVoiceExperience.Deactivate();

        // Frissítsük a UI-t
        transcriptionText.text = "Mondhatod a parancsot...";

        // Kis késleltetéssel indítsuk el a parancs figyelését
        Invoke("ActivateCommandListening", 0.5f);
    }

    private void ActivateCommandListening()
    {
        // Indítsunk új hanghallgatást a parancshoz
        appVoiceExperience.Activate();
    }

    // Esemény: Kérés befejezve
    private void OnRequestCompleted()
    {
        Debug.Log("Request completed. IsWaitingForWakeWord: " + _isWaitingForWakeWord);

        // Ha wake word-re vártunk, de nem ismertük fel, indítsuk újra a figyelést
        if (_isWaitingForWakeWord)
        {
            Debug.Log("Restarting wake word detection...");
            Invoke("StartListeningForWakeWord", 1.0f);
        }
        // Ha parancsot vártunk és megkaptuk, akkor térjünk vissza a wake word figyeléshez
        else if (_isTranscriptionComplete)
        {
            Debug.Log("Command processed, returning to wake word detection...");
            Invoke("StartListeningForWakeWord", 2.0f);
        }
        // Ha parancsot vártunk, de nem kaptunk transzkripciót, indítsuk újra a parancs figyelést
        else if (_voiceCommandReady && !_isTranscriptionComplete)
        {
            Debug.Log("No transcription received, restarting command detection...");
            Invoke("ActivateCommandListening", 1.0f);
        }
    }

    // Esemény: Sikertelen hallgatás
    private void OnAborted()
    {
        Debug.Log("Voice recognition aborted");

        // Ha wake word módban voltunk, indítsuk újra
        if (_isWaitingForWakeWord)
        {
            Invoke("StartListeningForWakeWord", 1.0f);
        }
    }

    // Esemény: Hiba történt
    private void OnError(string error, string message)
    {
        Debug.LogError($"Voice recognition error: {error} - {message}");

        // Hiba esetén visszatérünk a wake word figyeléshez
        Invoke("StartListeningForWakeWord", 2.0f);
    }

    // Esemény: Hallgatás elkezdődött
    private void OnStartListening()
    {
        Debug.Log("Started listening. IsWaitingForWakeWord: " + _isWaitingForWakeWord);

        if (_isWaitingForWakeWord)
        {
            transcriptionText.text = "Mondd a varázsszót...";
        }
        else if (_voiceCommandReady)
        {
            transcriptionText.text = "Mondhatod a parancsot...";
        }
    }

    // Esemény: Hallgatás befejeződött
    private void OnStoppedListening()
    {
        Debug.Log("Stopped listening. IsWaitingForWakeWord: " + _isWaitingForWakeWord);
    }

    // Esemény: Wake Word felismerve
    private void WakeWordDetected(string[] values)
    {
        Debug.Log("Wake word detected: " + string.Join(", ", values));

        // Csak akkor foglalkozzunk a wake word-del, ha épp arra várunk
        if (!_isWaitingForWakeWord) return;

        // Jelezzük, hogy felismertük a varázsszót
        transcriptionText.text = "Varázsszó felismerve!";
        wakeWordDetected?.Invoke();

        // Kezdjünk el figyelni a parancsra
        StartListeningForCommand();
    }

    // Esemény: Részleges átírás folyamatban
    private void OnPartialTranscription(string transcription)
    {
        // Csak a parancs módban foglalkozzunk a részleges átírással
        if (_isWaitingForWakeWord || !_voiceCommandReady) return;

        Debug.Log("Partial transcription: " + transcription);
        transcriptionText.text = transcription;
    }

    // Esemény: Teljes átírás elkészült
    private void OnFullTranscription(string transcription)
    {
        // Csak a parancs módban foglalkozzunk a teljes átírással
        if (_isWaitingForWakeWord || !_voiceCommandReady) return;

        Debug.Log("Full transcription: " + transcription);

        // Ha kaptunk érvényes szöveget
        if (!string.IsNullOrEmpty(transcription))
        {
            _isTranscriptionComplete = true;
            transcriptionText.text = "Felismerve: " + transcription;

            // Értesítsük az eseménykezelőket
            completeTranscription?.Invoke(transcription);
        }
    }
}
