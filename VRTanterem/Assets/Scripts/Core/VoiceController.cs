using UnityEngine;
using TMPro;
using UnityEngine.UI;
using UnityEngine.Events;
using UnityEngine.Android;
using System.Collections;

public class QuestVoiceController : MonoBehaviour
{
    [Header("UI References")]
    [SerializeField] private TextMeshProUGUI feedbackText;
    [SerializeField] private bool createDebugUI = false;

    [Header("Input Settings")]
    [SerializeField] private bool useAnyButton = false;
    [SerializeField] private KeyCode testKey = KeyCode.Space; // Csak teszteléshez PC-n

    // Android Speech Recognition beállítások
    [Header("Speech Recognition Settings")]
    [SerializeField] private string speechRecognitionLanguage = "hu-HU"; // Magyar nyelv alapértelmezetten
    [SerializeField] private int maxResults = 3; // Hány lehetséges eredményt adjon vissza a felismerés
    [SerializeField] private string recognitionPrompt = "Mondd el az üzeneted"; // Milyen prompt jelenjen meg az Android felületen

    // Kontroller gombok
    private enum ControllerButton { PrimaryTrigger, PrimaryGrip, SecondaryTrigger, SecondaryGrip, Any }
    [SerializeField] private ControllerButton buttonToUse = ControllerButton.PrimaryGrip;

    // UnityEvent a beszédfelismerés eredményének továbbadásához
    [Header("Voice Recognition Events")]
    public UnityEvent<string> OnSpeechRecognized = new UnityEvent<string>();

    // Állapotváltozók
    private bool isListening = false;
    private bool isProcessing = false;
    private float recognitionStartTime = 0f;
    private GameObject debugPanel;

    // Android osztályok
    private AndroidJavaObject speechRecognizer;
    private AndroidJavaObject recognitionListener;
    private AndroidJavaObject speechRecognizerIntent;
    private bool isAndroidInitialized = false;

    // Debugging
    [Header("Debug")]
    [SerializeField] private bool logVerbose = true;

    // Mikrofon monitorozáshoz
    private AudioClip monitorMic;
    private float[] samples = new float[64];
    private bool isMonitoringMic = false;

    private void Start()
    {
        // 1. Mikrofon engedély kérése
#if PLATFORM_ANDROID
        if (!Permission.HasUserAuthorizedPermission(Permission.Microphone))
        {
            Permission.RequestUserPermission(Permission.Microphone);
            LogError("Mikrofon engedély kérése...");
        }
#endif

        // 2. UI inicializálása
        if (feedbackText == null && !createDebugUI)
        {
            // Próbáljuk az OpenAIWebRequest.cs-ből az TMPUserText-et megtalálni
            var openAIRequest = FindObjectOfType<OpenAIWebRequest>();
            if (openAIRequest != null && openAIRequest.TMPUserText != null)
            {
                feedbackText = (TextMeshProUGUI)openAIRequest.TMPUserText;
                Log("OpenAIWebRequest TMPUserText automatikusan megtalálva és beállítva.");
            }
            else
            {
                LogWarning("Nem sikerült a feedbackText-et automatikusan megtalálni. Debug UI létrehozása...");
                createDebugUI = true;
            }
        }

        // 3. Debug UI létrehozása, ha szükséges
        if (createDebugUI && feedbackText == null)
        {
            CreateDebugUI();
        }

        // 4. Kiinduló állapot beállítása
        UpdateFeedbackText("Nyomd meg a gombot a beszédfelismerés indításához");

        // 5. Android Speech Recognizer inicializálása
#if PLATFORM_ANDROID
        InitializeAndroidSpeechRecognizer();
#else
        LogWarning("Android Speech Recognition csak Android platformon érhető el!");
#endif

        // 6. Mikrofon monitor indítása
        StartCoroutine(MonitorMicrophone());

        // 7. Debug infó
        Log("QuestVoiceController inicializálva. A hangfelismerés készen áll.");
    }

    private void Update()
    {
        // Gomblenyomás ellenőrzése
        bool buttonDown = false;
        bool buttonUp = false;

        // PC-n, teszteléshez
        if (Application.isEditor)
        {
            // Teszteléshez használt billentyű
            buttonDown = Input.GetKeyDown(testKey);
            buttonUp = Input.GetKeyUp(testKey);
        }
        else // Quest kontroller gombjai
        {
            switch (buttonToUse)
            {
                case ControllerButton.PrimaryTrigger:
                    buttonDown = OVRInput.GetDown(OVRInput.Button.PrimaryIndexTrigger);
                    buttonUp = OVRInput.GetUp(OVRInput.Button.PrimaryIndexTrigger);
                    break;
                case ControllerButton.PrimaryGrip:
                    buttonDown = OVRInput.GetDown(OVRInput.Button.PrimaryHandTrigger);
                    buttonUp = OVRInput.GetUp(OVRInput.Button.PrimaryHandTrigger);
                    break;
                case ControllerButton.SecondaryTrigger:
                    buttonDown = OVRInput.GetDown(OVRInput.Button.SecondaryIndexTrigger);
                    buttonUp = OVRInput.GetUp(OVRInput.Button.SecondaryIndexTrigger);
                    break;
                case ControllerButton.SecondaryGrip:
                    buttonDown = OVRInput.GetDown(OVRInput.Button.SecondaryHandTrigger);
                    buttonUp = OVRInput.GetUp(OVRInput.Button.SecondaryHandTrigger);
                    break;
                case ControllerButton.Any:
                    // Bármelyik trigger vagy grip
                    buttonDown = OVRInput.GetDown(OVRInput.Button.PrimaryIndexTrigger) ||
                                 OVRInput.GetDown(OVRInput.Button.PrimaryHandTrigger) ||
                                 OVRInput.GetDown(OVRInput.Button.SecondaryIndexTrigger) ||
                                 OVRInput.GetDown(OVRInput.Button.SecondaryHandTrigger);
                    buttonUp = OVRInput.GetUp(OVRInput.Button.PrimaryIndexTrigger) ||
                              OVRInput.GetUp(OVRInput.Button.PrimaryHandTrigger) ||
                              OVRInput.GetUp(OVRInput.Button.SecondaryIndexTrigger) ||
                              OVRInput.GetUp(OVRInput.Button.SecondaryHandTrigger);
                    break;
            }
        }

        // Ha a gomb le van nyomva, indítjuk a felismerést
        if (buttonDown && !isListening && !isProcessing)
        {
            StartVoiceRecognition();
        }
        // Ha a gomb fel van engedve, és aktív a hallgatás, leállítjuk
        else if (buttonUp && isListening)
        {
            StopVoiceRecognition();
        }
    }

#if PLATFORM_ANDROID
    private void InitializeAndroidSpeechRecognizer()
    {
        try
        {
            using (AndroidJavaClass unityPlayerClass = new AndroidJavaClass("com.unity3d.player.UnityPlayer"))
            {
                using (AndroidJavaObject activity = unityPlayerClass.GetStatic<AndroidJavaObject>("currentActivity"))
                {
                    // Létrehozzuk a SpeechRecognizer példányt
                    using (AndroidJavaClass speechRecognizerClass = new AndroidJavaClass("android.speech.SpeechRecognizer"))
                    {
                        if (speechRecognizerClass.CallStatic<bool>("isRecognitionAvailable", activity))
                        {
                            speechRecognizer = speechRecognizerClass.CallStatic<AndroidJavaObject>("createSpeechRecognizer", activity);

                            // Létrehozzuk a RecognitionListener-t
                            recognitionListener = new AndroidJavaObject("com.DefaultCompany.VoiceTest.UnityRecognitionListener", gameObject.name);

                            // Beállítjuk a SpeechRecognizer listener-ét
                            speechRecognizer.Call("setRecognitionListener", recognitionListener);

                            // Intent létrehozása
                            speechRecognizerIntent = new AndroidJavaObject("android.content.Intent", "android.speech.action.RECOGNIZE_SPEECH");
                            speechRecognizerIntent.Call<AndroidJavaObject>("putExtra", "android.speech.extra.LANGUAGE_MODEL", "free_form");
                            speechRecognizerIntent.Call<AndroidJavaObject>("putExtra", "android.speech.extra.LANGUAGE", speechRecognitionLanguage);
                            speechRecognizerIntent.Call<AndroidJavaObject>("putExtra", "android.speech.extra.MAX_RESULTS", maxResults);
                            speechRecognizerIntent.Call<AndroidJavaObject>("putExtra", "android.speech.extra.PROMPT", recognitionPrompt);

                            isAndroidInitialized = true;
                            Log("Android SpeechRecognizer sikeresen inicializálva!");
                        }
                        else
                        {
                            LogError("Beszédfelismerés nem elérhető ezen az eszközön!");
                        }
                    }
                }
            }
        }
        catch (System.Exception e)
        {
            LogError($"Hiba az Android SpeechRecognizer inicializálásakor: {e.Message}");
        }
    }
#endif

    // Beszédfelismerés indítása
    public void StartVoiceRecognition()
    {
        if (isListening || isProcessing) return;

        Log("Beszédfelismerés indítása...");

#if PLATFORM_ANDROID
        if (isAndroidInitialized)
        {
            try
            {
                // Beszédfelismerés indítása
                speechRecognizer.Call("startListening", speechRecognizerIntent);

                isListening = true;
                recognitionStartTime = Time.time;
                UpdateFeedbackText("Hallgatlak...");
                LogError("QUEST STT: Beszédfelismerés elindítva");
            }
            catch (System.Exception e)
            {
                LogError($"Hiba a beszédfelismerés indításakor: {e.Message}");
            }
        }
        else
        {
            LogError("A beszédfelismerés nincs inicializálva!");
        }
#else
        // Csak teszteléshez, nem Androidon
        isListening = true;
        recognitionStartTime = Time.time;
        UpdateFeedbackText("Hallgatlak... (TESZT MÓD)");
        
        // Szimuláljuk az eredményt 3 másodperc múlva
        StartCoroutine(SimulateVoiceRecognition(3f));
#endif
    }

    // Beszédfelismerés leállítása
    public void StopVoiceRecognition()
    {
        if (!isListening) return;

        Log("Beszédfelismerés leállítása...");

#if PLATFORM_ANDROID
        if (isAndroidInitialized)
        {
            try
            {
                // Beszédfelismerés leállítása
                speechRecognizer.Call("stopListening");
                LogError("QUEST STT: Beszédfelismerés leállítva");
            }
            catch (System.Exception e)
            {
                LogError($"Hiba a beszédfelismerés leállításakor: {e.Message}");
            }
        }
#else
        // Nincs több teendő a szimuláció miatt
#endif

        isListening = false;
        UpdateFeedbackText("Feldolgozás...");
    }

#if !PLATFORM_ANDROID
    // Szimuláljuk a beszédfelismerést tesztelés céljából
    private IEnumerator SimulateVoiceRecognition(float delay)
    {
        yield return new WaitForSeconds(delay);
        
        if (isListening)
        {
            string simulatedText = "Ez egy tesztüzenet a szimulátoron.";
            OnSpeechResults(simulatedText);
        }
    }
#endif

    // Callback a beszédfelismerés eredményére
    public void OnSpeechResults(string results)
    {
        LogError($"BESZÉDFELISMERÉS EREDMÉNYE: {results}");

        isListening = false;
        isProcessing = true;

        if (!string.IsNullOrEmpty(results))
        {
            UpdateFeedbackText("Feldolgozás: " + results);

            // Esemény meghívása az eredménnyel
            OnSpeechRecognized.Invoke(results);

            // Keressük meg az OpenAIWebRequest-et és adjuk át neki közvetlenül is
            var openAIRequest = FindObjectOfType<OpenAIWebRequest>();
            if (openAIRequest != null)
            {
                openAIRequest.ProcessVoiceInput(results);
                Log("Beszédfelismerés eredménye átadva az OpenAIWebRequest-nek.");
            }
        }
        else
        {
            UpdateFeedbackText("Nem sikerült felismerni a beszédet.");
            LogWarning("Üres beszédfelismerési eredmény érkezett!");
        }

        // Visszaállítjuk az alapállapotot
        isProcessing = false;
        StartCoroutine(ResetUIAfterDelay(2f));
    }

    // Callback a beszédfelismerés hibájára
    public void OnSpeechError(string errorMessage)
    {
        LogError($"BESZÉDFELISMERÉSI HIBA: {errorMessage}");

        isListening = false;
        isProcessing = false;

        UpdateFeedbackText($"Hiba: {errorMessage}");
        StartCoroutine(ResetUIAfterDelay(3f));
    }

    // Reset UI késleltetéssel
    private IEnumerator ResetUIAfterDelay(float delay)
    {
        yield return new WaitForSeconds(delay);
        if (!isListening && !isProcessing)
        {
            UpdateFeedbackText("Nyomd meg a gombot a beszédfelismerés indításához");
        }
    }

    // UI frissítése
    private void UpdateFeedbackText(string message)
    {
        Log("UI frissítés: " + message);

        if (feedbackText != null)
        {
            feedbackText.text = message;
        }
    }

    // Debug UI létrehozása
    private void CreateDebugUI()
    {
        Log("Debug UI létrehozása...");

        // Canvas létrehozása
        var canvasGO = new GameObject("VoiceDebugCanvas");
        var canvas = canvasGO.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.WorldSpace;

        // Canvas pozícionálása
        canvas.transform.position = new Vector3(0, 1.5f, 2);
        canvas.transform.localScale = new Vector3(0.00001f, 0.00001f, 0.00001f); // Nagyon kis méret
        canvas.transform.rotation = Quaternion.Euler(0, 180, 0);

        // Panel létrehozása
        debugPanel = new GameObject("DebugPanel");
        debugPanel.transform.parent = canvasGO.transform;
        var panelRT = debugPanel.AddComponent<RectTransform>();
        var panel = debugPanel.AddComponent<Image>();
        panel.color = new Color(0, 0, 0, 0.7f);
        panelRT.sizeDelta = new Vector2(800, 600);
        panelRT.anchoredPosition = Vector2.zero;

        // Szöveg létrehozása
        var textGO = new GameObject("FeedbackText");
        textGO.transform.parent = debugPanel.transform;
        var textRT = textGO.AddComponent<RectTransform>();
        feedbackText = textGO.AddComponent<TextMeshProUGUI>();
        feedbackText.text = "Nyomd meg a gombot a beszédfelismerés indításához";
        feedbackText.color = Color.white;
        feedbackText.fontSize = 48;
        feedbackText.alignment = TextAlignmentOptions.Center;
        textRT.sizeDelta = new Vector2(700, 500);
        textRT.anchoredPosition = Vector2.zero;

        Log("Debug UI létrehozva!");
    }

    // Mikrofon monitoring coroutine
    private IEnumerator MonitorMicrophone()
    {
        Log("Mikrofon monitoring indítása...");

        // Várjunk egy pillanatig, hogy biztos inicializálódjon minden
        yield return new WaitForSeconds(0.5f);

        // Mikrofon inicializálása
        if (Microphone.devices.Length > 0)
        {
            monitorMic = Microphone.Start(null, true, 1, 44100);
            isMonitoringMic = true;
            Log($"Mikrofon monitoring elindult ({Microphone.devices[0]})");
        }
        else
        {
            LogError("HIBA: Nem található mikrofon!");
            yield break;
        }

        // Monitorozás
        while (isMonitoringMic)
        {
            // Ha nem működik a mikrofon, indítsuk újra
            if (!Microphone.IsRecording(null))
            {
                LogError("Mikrofon újraindítása...");
                monitorMic = Microphone.Start(null, true, 1, 44100);
                yield return new WaitForSeconds(0.5f);
            }

            // Ellenőrizzük a hangszintet
            float levelMax = GetMicrophoneLevel();

            // Ha hallgatás módban vagyunk és van számottevő hang
            if (isListening && levelMax > 0.01f)
            {
                float elapsedListeningTime = Time.time - recognitionStartTime;
                LogError($"MIKROFON: Szint={levelMax:F4}, Hallgatási idő={elapsedListeningTime:F1}s");
            }

            // Ha nagyon hangos hang van, azt is jelentjük
            if (levelMax > 0.1f)
            {
                LogError($"MIKROFON: Magas hangszint: {levelMax:F4}");
            }

            yield return new WaitForSeconds(0.2f);
        }
    }

    // Mikrofon hangszint lekérdezése
    private float GetMicrophoneLevel()
    {
        if (monitorMic == null) return 0f;

        float levelMax = 0;
        int pos = Microphone.GetPosition(null);

        if (pos > samples.Length && monitorMic.samples > pos - samples.Length)
        {
            monitorMic.GetData(samples, pos - samples.Length);

            for (int i = 0; i < samples.Length; i++)
            {
                float wavePeak = Mathf.Abs(samples[i]);
                if (levelMax < wavePeak)
                {
                    levelMax = wavePeak;
                }
            }
        }

        return levelMax;
    }

    // Hozzáadott segéd metódusok a logoláshoz
    private void Log(string message)
    {
        if (logVerbose)
            Debug.Log($"[QuestVoiceController] {message}");
    }

    private void LogWarning(string message)
    {
        Debug.LogWarning($"[QuestVoiceController] {message}");
    }

    private void LogError(string message)
    {
        Debug.LogError($"[QuestVoiceController] {message}");
    }

    private void OnDestroy()
    {
        // Hangmonitorozás leállítása
        isMonitoringMic = false;

        if (Microphone.IsRecording(null))
        {
            Microphone.End(null);
        }

        // Android Speech Recognizer felszabadítása
#if PLATFORM_ANDROID
        if (isAndroidInitialized && speechRecognizer != null)
        {
            speechRecognizer.Call("destroy");
            speechRecognizer.Dispose();
            speechRecognizer = null;

            if (recognitionListener != null)
            {
                recognitionListener.Dispose();
                recognitionListener = null;
            }

            if (speechRecognizerIntent != null)
            {
                speechRecognizerIntent.Dispose();
                speechRecognizerIntent = null;
            }
        }
#endif
    }
}
