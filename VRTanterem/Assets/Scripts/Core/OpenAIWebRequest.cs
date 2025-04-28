using UnityEngine;
using UnityEngine.Networking;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using System;
using Newtonsoft.Json.Linq;
using TMPro;
// using UnityEngine.UI; // Eltávolítva, mivel a Button már nem használatos

public class OpenAIWebRequest : MonoBehaviour
{
    // --- API és Asszisztens Beállítások ---
    // [SerializeField] attribútummal tesszük láthatóvá és szerkeszthetővé az Inspectorban,
    // miközben a változók privátak maradnak a kódban.
    [Header("OpenAI Configuration")] // Opcionális: Fejléc az Inspectorban a jobb átláthatóságért
    [Tooltip("Your OpenAI API Key (keep this secret!)")] // Opcionális: Segítő szöveg az Inspectorban
    [SerializeField] private string apiKey = ""; // Üresen hagyjuk, az értéket az Inspectorban kell megadni

    [Tooltip("The ID of the OpenAI Assistant to use")]
    private string assistantID;

    private string apiUrl = "https://api.openai.com/v1"; // Ezt itt hagyhatjuk, ha nem változik gyakran

    // --- UI Elemek ---
    public string userInput = "Hello!"; // Alapértelmezett első üzenet
    public TMP_Text TMPResponseText; // AI válasza
    [SerializeField] private TMP_InputField TMPInputField; // User szöveges üzenetének beviteli mező
    public TMP_Text TMPUserText; // User válasza elküldéskor

    // --- TTS Manager Referencia ---
    [Header("External Components")] // Opcionális fejléc
    [Tooltip("Reference to the TextToSpeechManager component for audio output.")]
    [SerializeField] private TextToSpeechManager textToSpeechManager;

    // --- Belső Változók ---
    private string assistantThreadId; // Az aktuális beszélgetési szál azonosítója
    private string currentRunId; // Az aktuális futtatás azonosítója (streaminghez)
    private StringBuilder messageBuilder = new StringBuilder(); // Üzenetek építéséhez (jelenleg kevésbé használt a streaming miatt)
    private StringBuilder buffer = new StringBuilder(); // Bejövő streaming adatok puffereléséhez
    // private string fullMessage = ""; // Eltávolítva vagy újragondolva, ha a teljes üzenet követése szükséges
    // private string lastProcessedContent = ""; // Eltávolítva vagy újragondolva

    // Whisper beállítások
    private const string WhisperApiUrl = "https://api.openai.com/v1/audio/transcriptions";
    private const string ModelName = "whisper-1";

    private void Start()
    {
        // SendButton listener eltávolítva
        // SendButton.onClick.AddListener(SendButtonClick);
        /*
        // --- KONFIGURÁCIÓ ELLENŐRZÉSE ---
        // Ellenőrizzük, hogy az API kulcs és az Asszisztens ID meg van-e adva az Inspectorban.
        bool configurationValid = true;
        if (string.IsNullOrEmpty(apiKey) || apiKey.Trim().Length < 10) // Egyszerű ellenőrzés
        {
            Debug.LogError("OpenAI API Key is not set or looks invalid in the Inspector! Please provide your API key on the OpenAIWebRequest component.");
            configurationValid = false;
        }
        if (string.IsNullOrEmpty(assistantID) || !assistantID.Trim().StartsWith("asst_")) // Egyszerű ellenőrzés
        {
            Debug.LogError("OpenAI Assistant ID is not set or looks invalid in the Inspector! Please provide the Assistant ID on the OpenAIWebRequest component.");
            configurationValid = false;
        }

        // Ha a konfiguráció hiányos, ne folytassuk a működést (vagy csak korlátozottan)
        if (!configurationValid)
        {
            Debug.LogError("OpenAI configuration is invalid. Disabling OpenAIWebRequest component.");
            enabled = false; // Letiltja a komponenst (nem fog futni az Update, stb.)
            // Opcionálisan visszajelzést adhatunk a UI-on is.
            if (TMPResponseText != null)
            {
                TMPResponseText.text = "ERROR: OpenAI Configuration Missing in Inspector!";
            }
            if (TMPInputField != null)
            {
                TMPInputField.interactable = false; // Letiltjuk a beviteli mezőt
            }
            return; // Kilépünk a Start metódusból, a korutinok nem indulnak el.
        }
        // --- KONFIGURÁCIÓ ELLENŐRZÉSE VÉGE ---
        */

        // InputField 'onSubmit' esemény (Enter lenyomására)
        if (TMPInputField != null)
        {
            TMPInputField.onSubmit.AddListener((value) => {
                if (!string.IsNullOrEmpty(value) && !string.IsNullOrWhiteSpace(assistantThreadId)) // Ellenőrizzük a thread ID-t is
                {
                    messageBuilder.Clear();
                    buffer.Clear();
                    StartCoroutine(SendMessageSequence(value));
                }
                else if (string.IsNullOrEmpty(assistantThreadId))
                {
                    Debug.LogWarning("Cannot send message yet, thread is not ready.");
                    // Esetleg visszajelzés a felhasználónak
                }
            });
        }
        else
        {
            Debug.LogWarning("TMPInputField nincs beállítva az Inspectorban!");
        }

        // Asszisztens adatainak lekérése és Thread létrehozása (csak ha a konfiguráció rendben van)
        // StartCoroutine(GetAssistant());
        // StartCoroutine(CreateThread());

        Debug.Log("[OpenAIWebRequest] Start() finished (Automatic thread/run creation DISABLED for menu integration).");
    }

    /// <summary>
    /// Initializes the component with the necessary configuration and starts the interaction process.
    /// Called by AppStateManager after the menu selection is complete and this module is activated.
    /// </summary>
    /// <param name="selectedAssistantId">The Assistant ID chosen in the menu.</param>
    /// <param name="selectedVoiceId">The Voice ID chosen in the menu (for TTS initialization).</param>
    public void InitializeAndStartInteraction(string selectedAssistantId, string selectedVoiceId)
    {
        Debug.Log($"[OpenAIWebRequest] InitializeAndStartInteraction called. AssistantID: {selectedAssistantId}, VoiceID: {selectedVoiceId}");

        // --- 1. Konfiguráció Mentése ÉS ELLENŐRZÉSE ---
        // Először szerezzük meg az API kulcsot (feltételezve, hogy az AppStateManager tárolja)
        if (AppStateManager.Instance != null)
        {
            // Feltételezve, hogy van egy GetApiKey() metódus az AppStateManagerben,
            // ami visszaadja az Inspectorban beállított kulcsot.
            // Ha nincs, akkor az apiKey-t itt kell valahogy beállítani, pl.
            // this.apiKey = AppStateManager.Instance.GetApiKeyFromInspector();
            // Vagy ha az apiKey mindig csak az Inspectorban van beállítva ezen a komponensen:
            // Akkor az apiKey ellenőrzése maradhatott volna a Start()-ban, de jobb itt.
            // Győződj meg róla, hogy az 'apiKey' változó itt kap értéket!
            // Példa: Ha az AppStateManagerben van egy public property:
            // this.apiKey = AppStateManager.Instance.OpenAiApiKey; // Ha van ilyen property
            // VAGY ha az apiKey itt van [SerializeField]-del:
            // Akkor az ellenőrzés itt történjen meg.
        }
        else
        {
            Debug.LogError("[OpenAIWebRequest] Cannot get API Key: AppStateManager.Instance is null!");
            enabled = false;
            return;
        }

        this.assistantID = selectedAssistantId; // Elmentjük a kapott ID-t

        // --- ÁTHELYEZETT ELLENŐRZÉSEK ---
        bool configurationValid = true;
        // API Kulcs ellenőrzése (győződj meg, hogy a 'this.apiKey' már be van állítva!)
        if (string.IsNullOrEmpty(this.apiKey) || this.apiKey.Length < 10)
        {
            Debug.LogError("[OpenAIWebRequest] Initialization Error: API Key is missing or invalid!", this);
            configurationValid = false;
        }
        // Assistant ID ellenőrzése
        if (string.IsNullOrEmpty(this.assistantID) || !this.assistantID.Trim().StartsWith("asst_"))
        {
            Debug.LogError($"[OpenAIWebRequest] Initialization Error: Assistant ID '{this.assistantID}' is not set or looks invalid!", this);
            configurationValid = false;
        }

        if (!configurationValid)
        {
            Debug.LogError("[OpenAIWebRequest] Initialization failed due to invalid configuration. Disabling component.");
            enabled = false; // Letiltjuk magunkat
                             // Opcionális UI visszajelzés
                             // if (TMPResponseText != null) TMPResponseText.text = "ERROR: Initialization Failed!";
            return; // Megállítjuk az inicializálást
        }
        // --- ELLENŐRZÉSEK VÉGE ---


        // --- 2. Függőségek Resetelése ---
        Debug.Log("[OpenAIWebRequest] Resetting dependent managers...");
        // ... (TTS, Highlighter reset) ...

        // --- 3. TTS Manager Inicializálása ---
        if (textToSpeechManager != null)
        {
            Debug.Log("[OpenAIWebRequest] Initializing TextToSpeechManager...");
            // Fontos: A TTS Managernek is szüksége lehet az API kulcsra!
            textToSpeechManager.Initialize(this.apiKey, selectedVoiceId);
        }
        // ...

        // --- 4. OpenAI Interakció Indítása ---
        Debug.Log("[OpenAIWebRequest] Starting OpenAI interaction coroutines (GetAssistant, CreateThread)...");
        StartCoroutine(GetAssistant());
        StartCoroutine(CreateThread());

        Debug.Log("[OpenAIWebRequest] Initialization and startup complete.");
    }


    // OpenAIWebRequest.cs-ben hozzáadandó metódus
    public void ProcessVoiceInput(string recognizedText)
    {
        Debug.LogWarning($"[OpenAIWebRequest] ProcessVoiceInput CALLED - Frame: {Time.frameCount}, Text: '{recognizedText}'");
        Debug.Log($"[OpenAIWebRequest] Voice input received: '{recognizedText}'");

        // 1. Ellenőrzések (opcionális, de ajánlott)
        if (string.IsNullOrEmpty(recognizedText))
        {
            Debug.LogWarning("[OpenAIWebRequest] Received empty text from voice input. Ignoring.");
            // Esetleg visszajelzés a felhasználónak itt is?
            return;
        }
        if (string.IsNullOrEmpty(assistantThreadId))
        {
            Debug.LogError("[OpenAIWebRequest] Cannot process voice input: Assistant Thread ID is missing.");
            // Visszajelzés a felhasználónak
            if (TMPResponseText != null) TMPResponseText.text = "Error: Assistant connection not ready.";
            return;
        }

        // 2. Megjelenítés (Opcionális, de hasznos visszajelzés)
        // Kiírjuk, mit hallottunk a User szövegmezőbe
        if (TMPUserText != null)
        {
            TMPUserText.text = "User (Voice): " + recognizedText;
        }
        // Opcionálisan beletehetjük az InputField-be is, mintha begépelte volna
        // if (TMPInputField != null)
        // {
        //     TMPInputField.text = recognizedText;
        // }

        // 3. A meglévő üzenetküldési folyamat elindítása
        // Pontosan ugyanazt a korutint indítjuk, mint amit az InputField Enter lenyomása
        Debug.Log("[OpenAIWebRequest] Starting SendMessageSequence for voice input...");
        messageBuilder.Clear(); // Biztosítjuk a tiszta állapotot
        buffer.Clear();         // Biztosítjuk a tiszta állapotot
        StartCoroutine(SendMessageSequence(recognizedText));

        // Az InputField törlését a SendMessageSequence vége már kezeli,
        // így itt nem kell külön foglalkozni vele.
    }


    // Beállítja a közös HTTP fejléceket az OpenAI kérésekhez
    private void SetCommonHeaders(UnityWebRequest request)
    {
        // Most már az Inspectorban beállított apiKey változót használja
        request.SetRequestHeader("Content-Type", "application/json");
        request.SetRequestHeader("Authorization", $"Bearer {apiKey}");
        request.SetRequestHeader("OpenAI-Beta", "assistants=v2");
    }

    /// <summary>
    /// Starts a new Assistant Run specifically for the main lecture content.
    /// Ensures the 'isAnsweringQuestion' flag is false.
    /// </summary>
    public void StartMainLectureRun()
    {
        Debug.LogWarning("[OAIWR_LOG] >>> StartMainLectureRun ENTER.");

        if (string.IsNullOrEmpty(assistantThreadId))
        {
            Debug.LogError("[OAIWR_LOG] Cannot start main lecture run: Assistant Thread ID is missing.");
            return; // Ne indítsunk korutint, ha nincs thread
        }

        Debug.LogWarning("[OAIWR_LOG] Calling CreateAssistantRun for main lecture (NO callback).");

        StartCoroutine(CreateAssistantRun(
            isAnsweringQuestion: false,
            userQuestion: null,
            followUpPrompt: null,
            onRunCompleteCallback: null
        ));
        

        Debug.LogWarning("[OAIWR_LOG] <<< StartMainLectureRun EXIT (Coroutine Started).");
    }

    // Lekéri az asszisztens adatait (opcionális)
    private IEnumerator GetAssistant()
    {
        string url = $"{apiUrl}/assistants/{assistantID}";
        Debug.Log("Getting Assistant at URL: " + url);

        using (UnityWebRequest webRequest = UnityWebRequest.Get(url))
        {
            SetCommonHeaders(webRequest);
            yield return webRequest.SendWebRequest();

            if (webRequest.result != UnityWebRequest.Result.Success)
            {
                Debug.LogError($"Error while getting assistant: {webRequest.error} - {webRequest.downloadHandler.text}");
            }
            else
            {
                Debug.Log("Assistant Retrieved: " + webRequest.downloadHandler.text);
            }
        }
    }

    // Létrehoz egy új beszélgetési szálat (thread) az OpenAI-nál
    private IEnumerator CreateThread()
    {
        string url = $"{apiUrl}/threads";
        Debug.Log("Creating Thread at URL: " + url);

        // Kezdeti üzenet megadása (opcionális)
        JObject requestBody = new JObject();
        // A userInput változót az Inspectorban lehet beállítani, vagy itt marad az alapértelmezett "Hello!"
        if (!string.IsNullOrEmpty(userInput)) // Csak akkor adjuk hozzá, ha van kezdeti üzenet
        {
            requestBody["messages"] = new JArray
             {
                 new JObject
                 {
                     ["role"] = "user",
                     ["content"] = userInput // Az Inspectorban vagy kódban definiált kezdeti üzenet
                 }
             };
            Debug.Log($"Initial message included in thread creation: '{userInput}'");
        }
        else
        {
            Debug.Log("No initial message provided for thread creation.");
        }

        string jsonBody = requestBody.ToString();
        // Debug.Log("Thread creation JSON: " + jsonBody); // Csak szükség esetén

        using (UnityWebRequest webRequest = new UnityWebRequest(url, "POST"))
        {
            byte[] bodyRaw = Encoding.UTF8.GetBytes(jsonBody);
            webRequest.uploadHandler = new UploadHandlerRaw(bodyRaw);
            webRequest.downloadHandler = new DownloadHandlerBuffer();
            SetCommonHeaders(webRequest); // Beállítja az API kulcsot és egyéb fejléceket

            yield return webRequest.SendWebRequest(); // Elküldi a kérést és vár a válaszra

            // Ellenőrizzük a kérés sikerességét
            if (webRequest.result != UnityWebRequest.Result.Success)
            {
                // Hiba esetén logoljuk a részleteket
                Debug.LogError($"Error while creating thread: {webRequest.error} - Status Code: {webRequest.responseCode} - Response: {webRequest.downloadHandler.text}");
            }
            else
            {
                // Sikeres válasz esetén logoljuk
                Debug.Log("Thread Created Successfully. Response: " + webRequest.downloadHandler.text);
                try
                {
                    // Feldolgozzuk a JSON választ
                    JObject responseJson = JObject.Parse(webRequest.downloadHandler.text);
                    // Kinyerjük a thread ID-t
                    assistantThreadId = responseJson["id"]?.ToString();

                    // Ellenőrizzük, hogy kaptunk-e érvényes thread ID-t
                    if (!string.IsNullOrEmpty(assistantThreadId))
                    {
                        Debug.Log($"Thread created successfully with ID: {assistantThreadId}");

                        // --- MÓDOSÍTÁS ITT ---
                        // Ha a thread létrehozásakor küldtünk kezdeti üzenetet (userInput),
                        // akkor indítsunk el egy futtatást (run), hogy választ kapjunk rá.
                        if (!string.IsNullOrEmpty(userInput))
                        {
                            Debug.Log("Initial userInput was included in thread creation. Starting run for initial response.");

                            // Jelenítsük meg a kezdeti user üzenetet is a UI-on, ha van hova
                            if (TMPUserText != null)
                            {
                                TMPUserText.text = "User: " + userInput;
                            }
                            else
                            {
                                Debug.LogWarning("TMPUserText is not assigned in the Inspector. Cannot display initial user input.");
                            }

                            Debug.LogWarning("[OAIWR_LOG] Starting the INITIAL Assistant Run with callback to IFM.HandleInitialPromptCompleted."); // <<< ÚJ LOG
                            StartCoroutine(CreateAssistantRun(
                                isAnsweringQuestion: false, // Ez nem válasz, hanem a kezdeti üdvözlés/kérdés
                                userQuestion: null,
                                followUpPrompt: null,
                                onRunCompleteCallback: InteractionFlowManager.Instance.HandleInitialPromptCompleted // <<< ITT ADJUK ÁT A CALLBACK-ET!
                            ));
                        }
                        // --- MÓDOSÍTÁS VÉGE ---
                    }
                    else
                    {
                        // Hiba, ha nem sikerült kinyerni a thread ID-t a válaszból
                        Debug.LogError("Failed to retrieve assistantThreadId from thread creation response. Response JSON might be invalid or missing the 'id' field.");
                    }
                }
                catch (Exception e)
                {
                    // Hiba a JSON feldolgozása közben
                    Debug.LogError($"Error parsing thread creation response: {e.Message} - Raw Response: {webRequest.downloadHandler.text}");
                }
            }
        }
    }

    // Megpróbálja megszakítani az aktuálisan futó asszisztens futtatást (run)
    private IEnumerator CancelCurrentRun()
    {
        if (!string.IsNullOrEmpty(currentRunId) && !string.IsNullOrEmpty(assistantThreadId))
        {
            string cancelUrl = $"{apiUrl}/threads/{assistantThreadId}/runs/{currentRunId}/cancel";
            Debug.Log($"Attempting to cancel run: {currentRunId} on thread: {assistantThreadId}");

            using (UnityWebRequest webRequest = new UnityWebRequest(cancelUrl, "POST"))
            {
                webRequest.downloadHandler = new DownloadHandlerBuffer(); // Kell a válaszhoz
                SetCommonHeaders(webRequest);

                yield return webRequest.SendWebRequest();

                // A Cancel API általában 200 OK-t ad vissza akkor is, ha a run már nem törölhető (pl. befejeződött)
                if (webRequest.result == UnityWebRequest.Result.Success)
                {
                    Debug.Log($"Cancel request for run {currentRunId} sent. Response: {webRequest.downloadHandler.text}");
                }
                else
                {
                    // Hiba esetén logoljuk, de nem feltétlenül állítjuk le a folyamatot
                    Debug.LogWarning($"Run cancellation request failed for run {currentRunId}: {webRequest.error} - {webRequest.downloadHandler.text}");
                }
            }
            // Nullázzuk a run ID-t, hogy ne próbáljuk újra törölni ugyanazt
            currentRunId = null;
        }
        else
        {
            // Debug.Log("No current run to cancel or thread ID missing."); // Csak szükség esetén
        }
    }

    // --- SendButtonClick metódus törölve ---
    // public void SendButtonClick() { ... }

    // Elindítja az üzenetküldési folyamatot (megszakítás, új üzenet küldése)
    private IEnumerator SendMessageSequence(string input)
    {
        Debug.LogWarning($"[OpenAIWebRequest] SendMessageSequence STARTED - Frame: {Time.frameCount}, Input: '{input}'");
        if (string.IsNullOrEmpty(assistantThreadId))
        {
            Debug.LogError("Cannot send message: Assistant Thread ID is missing. Ensure the thread was created.");
            yield break; // Kilépünk a coroutine-ból, ha nincs thread ID
        }

        // --- RESETTING MANAGERS ---
        Debug.Log("[SendMessageSequence] Resetting TTS Manager and Sentence Highlighter before new message.");
        if (textToSpeechManager != null)
        {
            textToSpeechManager.ResetManager();
        }
        else { Debug.LogWarning("Cannot reset TTS Manager - reference missing."); }
        // --- RESETTING MANAGERS END ---

        // 1. Próbáljuk megszakítani az előző futást (ha volt)
        yield return StartCoroutine(CancelCurrentRun());

        // 2. Rövid várakozás (opcionális, de segíthet elkerülni a race condition-t)
        // yield return new WaitForSeconds(0.1f); // Szükség esetén visszakapcsolható

        // 3. Új üzenet hozzáadása és futtatás indítása
        yield return StartCoroutine(GetAssistantResponse(input)); // Ez indítja a run-t is

        // 4. UI frissítése: Kiírjuk a felhasználó üzenetét és töröljük az input mezőt
        if (TMPUserText != null)
        {
            TMPUserText.text = "User: " + input; // Megjelenítjük, mit küldött a felhasználó
        }
        if (TMPInputField != null)
        {
            TMPInputField.text = ""; // Töröljük az input mezőt
            TMPInputField.ActivateInputField(); // Opcionális: újra fókuszba helyezzük az input mezőt
        }
    }

    // Hozzáadja a felhasználó üzenetét a szálhoz és elindítja az asszisztens futtatását
    private IEnumerator GetAssistantResponse(string userMessageContent)
    {
        Debug.LogWarning($"[OpenAIWebRequest] GetAssistantResponse STARTED - Frame: {Time.frameCount}, Message: '{userMessageContent}'");
        // Ellenőrizzük újra a thread ID-t
        if (string.IsNullOrEmpty(assistantThreadId))
        {
            Debug.LogError("Assistant Thread ID is invalid before adding message.");
            yield break;
        }

        // --- Üzenet hozzáadása a szálhoz ---
        string messageURL = $"{apiUrl}/threads/{assistantThreadId}/messages";
        Debug.Log($"Posting User Message to Thread: {assistantThreadId} at URL: {messageURL}");

        JObject messageBody = new JObject
        {
            ["role"] = "user",
            ["content"] = userMessageContent // A paraméterként kapott üzenetet használjuk
        };

        string messageJson = messageBody.ToString();
        // Debug.Log("Sending User Message JSON: " + messageJson); // Csak szükség esetén

        using (UnityWebRequest webRequest = new UnityWebRequest(messageURL, "POST"))
        {
            byte[] bodyRaw = Encoding.UTF8.GetBytes(messageJson);
            webRequest.uploadHandler = new UploadHandlerRaw(bodyRaw);
            webRequest.downloadHandler = new DownloadHandlerBuffer();
            SetCommonHeaders(webRequest);

            yield return webRequest.SendWebRequest();

            if (webRequest.result != UnityWebRequest.Result.Success)
            {
                Debug.LogError($"Error while posting user message: {webRequest.error} - {webRequest.downloadHandler.text}");
                // Itt nem indítjuk el a run-t, mert már az üzenet hozzáadása sem sikerült
                yield break; // Kilépés a coroutine-ból hiba esetén
            }
            else
            {
                Debug.LogWarning("[OAIWR_LOG] Starting Assistant Run from GetAssistantResponse (NO callback).");
                StartCoroutine(CreateAssistantRun(
                    isAnsweringQuestion: false,
                    userQuestion: null,
                    followUpPrompt: null,
                    onRunCompleteCallback: null
                ));
            }
        }
    }

    // ... (meglévő using direktívák és osztálydefiníció) ...

    // --- ÚJ METÓDUS A KÉRDÉS KÜLDÉSÉRE ELŐADÁS KÖZBEN ---
    /// <summary>
    /// Sends the user's transcribed question to the Assistant during an ongoing lecture.
    /// Adds the message to the thread and starts a run with specific instructions
    /// for the AI to answer briefly and then resume.
    /// Called by InteractionFlowManager.
    /// </summary>
    /// <param name="userQuestionText">The transcribed text of the user's question.</param>
    public void SendUserQuestionDuringLecture(string userQuestionText, string followUpPromptText)
    {
        Debug.Log($"[OpenAIWebRequest] SendUserQuestionDuringLecture called with question: '{userQuestionText}' and prompt: '{followUpPromptText}'");
        Debug.LogWarning($"[OAIWR_LOG] >>> SendUserQuestionDuringLecture ENTER. Question: '{userQuestionText}'");

        if (string.IsNullOrEmpty(assistantThreadId))
        {
            Debug.LogError("[OpenAIWebRequest] Cannot send question: Assistant Thread ID is missing.");
            return;
        }
        if (string.IsNullOrEmpty(userQuestionText))
        {
            Debug.LogWarning("[OpenAIWebRequest] Cannot send empty question.");
            return;
        }
        Debug.LogWarning("[OAIWR_LOG] Starting AddMessageAndStartAnswerRunCoroutine...");
        StartCoroutine(AddMessageAndStartAnswerRunCoroutine(userQuestionText, followUpPromptText));
        Debug.LogWarning("[OAIWR_LOG] <<< SendUserQuestionDuringLecture EXIT (Coroutine Started).");
    }

    // --- ÚJ SEGÉD KORUTIN AZ ÜZENET HOZZÁADÁSÁHOZ ÉS A VÁLASZ FUTTATÁS INDÍTÁSÁHOZ ---
    private IEnumerator AddMessageAndStartAnswerRunCoroutine(string userQuestionText, string followUpPromptText)
    {
        Debug.LogWarning("[OAIWR_LOG] >>> AddMessageAndStartAnswerRunCoroutine ENTER.");
        // --- Üzenet hozzáadása a szálhoz (logika átemelve GetAssistantResponse-ból) ---
        string messageURL = $"{apiUrl}/threads/{assistantThreadId}/messages";
        Debug.Log($"[OpenAIWebRequest] Posting User Question to Thread: {assistantThreadId}");

        JObject messageBody = new JObject
        {
            ["role"] = "user",
            ["content"] = userQuestionText // A paraméterként kapott kérdés
        };
        string messageJson = messageBody.ToString();

        using (UnityWebRequest webRequest = new UnityWebRequest(messageURL, "POST"))
        {
            byte[] bodyRaw = Encoding.UTF8.GetBytes(messageJson);
            webRequest.uploadHandler = new UploadHandlerRaw(bodyRaw);
            webRequest.downloadHandler = new DownloadHandlerBuffer();
            SetCommonHeaders(webRequest);

            Debug.LogWarning("[OAIWR_LOG] Sending user question message...");
            yield return webRequest.SendWebRequest();
            Debug.LogWarning($"[OAIWR_LOG] User question message sent. Result: {webRequest.result}");

            if (webRequest.result != UnityWebRequest.Result.Success)
            {
                Debug.LogError($"[OpenAIWebRequest] Error posting user question: {webRequest.error} - {webRequest.downloadHandler.text}");
                Debug.LogWarning("[OAIWR_LOG] <<< AddMessageAndStartAnswerRunCoroutine EXIT (Error sending message).");
                yield break; // Állítsd le a korutint itt
            }
            else
            {
                Debug.Log("[OpenAIWebRequest] User question added to thread successfully.");

                Debug.LogWarning("[OAIWR_LOG] Calling CreateAssistantRun for answering question (NO callback).");
                StartCoroutine(CreateAssistantRun(
                    isAnsweringQuestion: true,
                    userQuestion: userQuestionText,
                    followUpPrompt: followUpPromptText,
                    onRunCompleteCallback: null
                ));
            }
        }
        Debug.LogWarning("[OAIWR_LOG] <<< AddMessageAndStartAnswerRunCoroutine EXIT (Run Started).");
    }

    // Létrehoz és elindít egy új asszisztens futtatást (run) streaming módban
    private IEnumerator CreateAssistantRun(bool isAnsweringQuestion = false, string userQuestion = null, string followUpPrompt = null, Action onRunCompleteCallback = null)
    {
        Debug.LogWarning($"[OAIWR_LOG] >>> CreateAssistantRun ENTER. IsAnswering: {isAnsweringQuestion}, HasCallback: {onRunCompleteCallback != null}, Frame: {Time.frameCount}");
        if (string.IsNullOrEmpty(assistantThreadId))
        {
            Debug.LogError("[OpenAIWebRequest] Assistant Thread ID is invalid before creating run.");
            yield break; // Kilépés, ha nincs thread ID
        }

        string runUrl = $"{apiUrl}/threads/{assistantThreadId}/runs";
        Debug.Log($"[OpenAIWebRequest] Creating assistant run (Is Answering Question: {isAnsweringQuestion}) for thread: {assistantThreadId} at URL: {runUrl}");

        // --- Run Body összeállítása ---
        var runBody = new JObject
        {
            ["assistant_id"] = assistantID,
            ["stream"] = true // Streaming MINDIG bekapcsolva
        };

        // --- PROMPT ENGINEERING: Speciális instrukciók hozzáadása, ha kérdésre válaszolunk ---
        if (isAnsweringQuestion)
        {
            // Ellenőrizzük, hogy megkaptuk-e a promptot az IFM-től
            if (string.IsNullOrEmpty(followUpPrompt))
            {
                Debug.LogWarning("[OpenAIWebRequest] Follow-up prompt was not provided for additional instructions. Using a default based on language.");
                // Nyelvi kód lekérése
                string langCode = AppStateManager.Instance?.CurrentLanguage?.languageCode ?? "en"; // Alapértelmezett 'en', ha nincs
                // Alapértelmezett prompt a nyelv alapján
                followUpPrompt = langCode == "hu" ?
                    "Van további kérdése ezzel kapcsolatban?" :
                    "Do you have any more questions about this?";
            }

            // Nyelvi kód lekérése az utasításhoz (lehet redundáns, de biztosabb)
            string language = AppStateManager.Instance?.CurrentLanguage?.languageCode ?? "en";

            // Egyszerűsített instrukció - nincs marker, csak a válasz és a prompt kérése
            string additionalInstructions =
                $"INSTRUCTION: The user has interrupted the lecture with a question. " +
                $"Answer ONLY the user's current question briefly and clearly. " +
                $"After your answer, ask: \"{followUpPrompt}\". " +
                $"Use language: {language}";

            runBody["additional_instructions"] = additionalInstructions;
            Debug.LogWarning($"[OpenAIWebRequest] Added simplified question handling instructions using prompt: '{followUpPrompt}' in language: {language}");
        }
        // Ha nem kérdésre válaszolunk (isAnsweringQuestion == false), akkor nem adunk hozzá extra instrukciót,
        // az asszisztens a normál promptja alapján folytatja az előadást.

        string runJson = runBody.ToString();
        // Debug.Log("Run creation JSON: " + runJson); // Csak szükség esetén logoljuk

        // --- Streaming Változók Inicializálása ---
        bool streamEndedSuccessfully = false; // Jelzi, hogy a [DONE] esemény megérkezett-e
        StringBuilder currentResponseChunk = new StringBuilder(); // Az aktuális üzenet darabjait gyűjti (főleg logoláshoz)
        int lastProcessedIndex = 0; // A letöltött adatok feldolgozottságát követi
        buffer.Clear(); // Kiürítjük a bejövő adatok pufferét
        bool answerStreamStartNotified = false; // Jelzi, hogy a VÁLASZ stream kezdetét már jeleztük-e az IFM-nek
        bool lectureStreamStartNotified = false; // Jelzi, hogy az ELŐADÁS stream kezdetét már jeleztük-e az IFM-nek
        int eventSeparatorIndex; // Segédváltozó az SSE események szétválasztásához

        // --- Web Kérés Indítása és Feldolgozása ---
        using (UnityWebRequest webRequest = new UnityWebRequest(runUrl, "POST"))
        {
            webRequest.uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(runJson));
            // Fontos: A DownloadHandlerBuffer kell, hogy a részleges adatokat olvashassuk
            webRequest.downloadHandler = new DownloadHandlerBuffer();
            SetCommonHeaders(webRequest); // API kulcs és egyéb fejlécek beállítása

            // Aszinkron művelet indítása
            UnityWebRequestAsyncOperation asyncOp = webRequest.SendWebRequest();
            // Debug.LogWarning($"[OAIWR_LOG] CreateAssistantRun - WebRequest Sent. Starting WHILE loop. Frame: {Time.frameCount}");

            // Ciklus, amíg a kérés be nem fejeződik VAGY a stream véget nem ér ([DONE])
            while (!asyncOp.isDone && !streamEndedSuccessfully)
            {
                // Debug.LogWarning($"[OAIWR_LOG] CreateAssistantRun - WHILE Loop Top. Frame: {Time.frameCount}");
                // Hibakezelés a cikluson belül
                if (webRequest.result != UnityWebRequest.Result.InProgress)
                {
                    Debug.LogError($"[OpenAIWebRequest] Error during assistant run stream: {webRequest.error} - Status: {webRequest.result} - Code: {webRequest.responseCode} - Partial Response: {webRequest.downloadHandler?.text}");
                    // Itt értesíthetnénk az InteractionFlowManagert a hibáról, ha szükséges
                    // InteractionFlowManager.Instance?.HandleError("Assistant run failed.");
                    yield break; // Kilépés a korutinból hiba esetén
                }

                // Ellenőrizzük, hogy érkezett-e új adat
                if (webRequest.downloadHandler.data != null)
                {
                    int currentLength = webRequest.downloadHandler.data.Length;
                    // Csak akkor dolgozunk, ha van új adat a legutóbbi feldolgozás óta
                    if (currentLength > lastProcessedIndex)
                    {
                        // Kiolvassuk az új adatrészt
                        string newTextChunk = Encoding.UTF8.GetString(webRequest.downloadHandler.data, lastProcessedIndex, currentLength - lastProcessedIndex);
                        lastProcessedIndex = currentLength; // Frissítjük a feldolgozott indexet
                        buffer.Append(newTextChunk); // Hozzáadjuk a pufferhez

                        string bufferContent = buffer.ToString(); // A puffer aktuális tartalma
                        int lastEventEndIndex = 0; // Az utolsó feldolgozott esemény vége a pufferben

                        // Keressük az SSE eseményeket (amik "\n\n"-nel végződnek)
                        while ((eventSeparatorIndex = bufferContent.IndexOf("\n\n", lastEventEndIndex)) != -1)
                        {
                            // Debug.LogWarning($"[OAIWR_LOG] CreateAssistantRun - Processing Event Block. Frame: {Time.frameCount}");
                            // Kivesszük az esemény blokkját
                            string eventBlock = bufferContent.Substring(lastEventEndIndex, eventSeparatorIndex - lastEventEndIndex);
                            // Frissítjük az indexet a következő kereséshez
                            lastEventEndIndex = eventSeparatorIndex + 2; // +2 a "\n\n" miatt

                            // Feldolgozzuk az esemény blokk sorait
                            string[] lines = eventBlock.Split('\n', StringSplitOptions.RemoveEmptyEntries);
                            foreach (string line in lines)
                            {
                                // Az 'event:' sorokat figyelmen kívül hagyjuk, csak a 'data:' érdekel minket
                                if (line.StartsWith("event:"))
                                {
                                    continue;
                                }

                                // Ha 'data:' sorral kezdődik
                                if (line.StartsWith("data:"))
                                {
                                    // Kivesszük a JSON adatot
                                    string jsonString = line.Substring(5).Trim();

                                    // Ellenőrizzük a stream végét jelző [DONE] üzenetet
                                    if (jsonString == "[DONE]")
                                    {
                                        Debug.LogWarning($"[OAIWR DEBUG] Received [DONE] event. Stream ended.");
                                        streamEndedSuccessfully = true; // Beállítjuk a flaget
                                        // Itt már nem kell Flush-t hívni, azt a ciklus után kezeljük
                                        break; // Kilépés a foreach ciklusból (az aktuális eseményblokk feldolgozása)
                                    }

                                    // Megpróbáljuk feldolgozni a JSON adatot
                                    try
                                    {
                                        JObject dataObject = JObject.Parse(jsonString);
                                        string objectType = dataObject["object"]?.ToString();

                                        // Objektumtípus alapján döntünk a feldolgozásról
                                        switch (objectType)
                                        {
                                            case "thread.message.delta":
                                                // --- VÁLASZ/ELŐADÁS STREAM KEZDETÉNEK JELZÉSE ---
                                                // Csak az első érdemi szöveg delta esetén jelezzük az IFM-nek
                                                JToken contentValue = dataObject.SelectToken("delta.content[0].text.value");
                                                if (contentValue != null && !string.IsNullOrEmpty(contentValue.ToString()))
                                                {
                                                    if (isAnsweringQuestion) // Ha VÁLASZ stream
                                                    {
                                                        if (!answerStreamStartNotified) // És még nem jeleztük
                                                        {
                                                            Debug.LogWarning("[OpenAIWebRequest] First text delta received for ANSWER stream. Notifying InteractionFlowManager.");
                                                            InteractionFlowManager.Instance?.HandleAIAnswerStreamStart(); // IFM értesítése
                                                            answerStreamStartNotified = true; // Jelöljük, hogy jeleztünk
                                                        }
                                                    }
                                                    else // Ha ELŐADÁS stream
                                                    {
                                                        if (!lectureStreamStartNotified) // És még nem jeleztük
                                                        {
                                                            Debug.LogWarning("[OpenAIWebRequest] First text delta received for LECTURE stream. Notifying InteractionFlowManager.");
                                                            InteractionFlowManager.Instance?.HandleLectureStreamStart(); // IFM értesítése (új metódus)
                                                            lectureStreamStartNotified = true; // Jelöljük, hogy jeleztünk
                                                        }
                                                    }
                                                }
                                                // --- Stream Kezdet Jelzés Vége ---

                                                // --- Szöveg Delta Feldolgozása és Továbbítása a TTS-nek ---
                                                JArray contentDeltas = dataObject["delta"]?["content"] as JArray;
                                                if (contentDeltas != null)
                                                {
                                                    foreach (var deltaItem in contentDeltas)
                                                    {
                                                        if (deltaItem["type"]?.ToString() == "text")
                                                        {
                                                            string textDelta = deltaItem["text"]?["value"]?.ToString();
                                                            if (!string.IsNullOrEmpty(textDelta))
                                                            {
                                                                // Logolás (opcionális, de hasznos lehet)
                                                                // Debug.LogWarning($"[OAIWR LOOP DEBUG] Appending Delta: '{textDelta}'");
                                                                currentResponseChunk.Append(textDelta); // Összegyűjtjük a választ (logoláshoz)

                                                                // Továbbítjuk a megfelelő TTS metódusnak
                                                                if (textToSpeechManager != null)
                                                                {
                                                                    if (isAnsweringQuestion) // Ha válasz
                                                                    {
                                                                        // Debug.Log($"[OAIWR DEBUG] >>> Calling AppendAnswerText with delta: '{textDelta}'");
                                                                        try { textToSpeechManager.AppendAnswerText(textDelta); }
                                                                        catch (Exception ex) { Debug.LogError($"[OAIWR ERROR] !!! Exception during AppendAnswerText call: {ex.Message}\n{ex.StackTrace}"); }
                                                                    }
                                                                    else // Ha előadás
                                                                    {
                                                                        // Debug.Log($"[OAIWR DEBUG] >>> Calling AppendText (Lecture) with delta: '{textDelta}'");
                                                                        try { textToSpeechManager.AppendText(textDelta); }
                                                                        catch (Exception ex) { Debug.LogError($"[OAIWR ERROR] !!! Exception during AppendText call: {ex.Message}\n{ex.StackTrace}"); }
                                                                    }
                                                                }
                                                                else // Ha nincs TTS Manager
                                                                {
                                                                    Debug.LogError("[OAIWR ERROR] !!! textToSpeechManager reference is NULL when trying to append delta!");
                                                                }
                                                            }
                                                        }
                                                    }
                                                }
                                                break; // thread.message.delta vége

                                            // Egyéb objektumtípusok (pl. run step) figyelmen kívül hagyása
                                            case "thread.run.step":
                                            case "thread.message":
                                                // Ezeket most nem dolgozzuk fel aktívan a stream alatt
                                                break;

                                            // Ismeretlen objektumtípus logolása
                                            default:
                                                Debug.LogWarning($"[OAIWR LOOP DEBUG] Received unhandled object type: '{objectType}'. Full JSON: {jsonString}");
                                                break;
                                        } // switch (objectType) vége
                                    }
                                    catch (Exception e) // Hiba a JSON feldolgozása közben
                                    {
                                        Debug.LogError($"Error parsing stream data JSON: {e.Message} - JSON: {jsonString}");
                                    }

                                    // Ha a [DONE] esemény miatt léptünk ki a foreach-ból, itt is lépjünk ki
                                    if (streamEndedSuccessfully) break;
                                } // if (line.StartsWith("data:")) vége
                            } // foreach line vége

                            // --- Buffer ürítése a feldolgozott rész után ---
                            // Ha feldolgoztunk legalább egy eseményt
                            if (lastEventEndIndex > 0)
                            {
                                // Ha a puffer elég hosszú, eltávolítjuk a feldolgozott részt
                                if (buffer.Length >= lastEventEndIndex)
                                {
                                    buffer.Remove(0, lastEventEndIndex);
                                    bufferContent = buffer.ToString(); // Frissítjük a stringet is
                                    lastEventEndIndex = 0; // Reseteljük az indexet
                                }
                                else // Ez nem fordulhatna elő, de biztonsági ellenőrzés
                                {
                                    Debug.LogError($"[OAIWR Buffer Error] lastEventEndIndex ({lastEventEndIndex}) is invalid for buffer length ({buffer.Length}). Clearing buffer.");
                                    buffer.Clear(); bufferContent = ""; lastEventEndIndex = 0;
                                }
                            }

                            // Ha a [DONE] esemény miatt léptünk ki a while-ból, itt is lépjünk ki
                            if (streamEndedSuccessfully) break;
                        } // while (eseményeket keresünk a pufferben) vége
                    } // if (van új adat) vége
                } // if (van letöltött adat) vége

                // Ha a stream véget ért egy esemény miatt, lépjünk ki a fő while (!asyncOp.isDone) ciklusból is
                if (streamEndedSuccessfully) break;

                // Várakozás a következő frame-re, hogy ne terheljük túl a CPU-t
                yield return null;
            } // while (!asyncOp.isDone && !streamEndedSuccessfully) vége

            // --- Korutin Vége - Utófeldolgozás ---
            Debug.LogWarning($"[OAIWR_LOG] <<< CreateAssistantRun EXIT. Frame: {Time.frameCount}");
            Debug.LogWarning($"[OAIWR DEBUG] Coroutine loop finished OR stream ended. asyncOp.isDone={asyncOp.isDone}, webRequest.result={webRequest.result}, streamEndedSuccessfully={streamEndedSuccessfully}");

            // TTS Bufferek ürítése a stream vége után (fontos, hogy az utolsó mondat is feldolgozódjon)
            // Csak akkor ürítünk, ha a stream sikeresen véget ért ([DONE]) vagy a web kérés sikeres volt
            if (streamEndedSuccessfully || webRequest.result == UnityWebRequest.Result.Success)
            {
                Debug.LogWarning("[OAIWR DEBUG] Flushing TTS buffers post-loop...");
                try
                {
                    if (textToSpeechManager != null)
                    {
                        if (isAnsweringQuestion) // Ha válasz volt
                        {
                            textToSpeechManager.FlushAnswerBuffer(); // Válasz buffer ürítése
                        }
                        else // Ha előadás volt
                        {
                            textToSpeechManager.FlushBuffer(); // Normál buffer ürítése
                        }
                    }
                }
                catch (Exception e)
                {
                    Debug.LogError($"TTS Flush Error (Post-Loop): {e.Message}");
                }
            }

            // Kezeljük azt az esetet, ha a ciklus azért fejeződött be, mert a web kérés véget ért (asyncOp.isDone),
            // de a [DONE] eseményt nem kaptuk meg (pl. hálózati hiba a stream végén)
            if (webRequest.result == UnityWebRequest.Result.Success && !streamEndedSuccessfully)
            {
                Debug.LogWarning("[OAIWR DEBUG] Loop ended via Success & asyncOp.isDone, but [DONE] event wasn't received. Forcing FlushBuffers just in case.");
                // Itt is megpróbáljuk üríteni a megfelelő puffert
                try
                {
                    if (textToSpeechManager != null)
                    {
                        if (isAnsweringQuestion) { textToSpeechManager.FlushAnswerBuffer(); }
                        else { textToSpeechManager.FlushBuffer(); }
                    }
                }
                catch (Exception e) { Debug.LogError($"TTS Flush Error (Post-Loop, No DONE): {e.Message}"); }
                streamEndedSuccessfully = true; // Jelöljük, hogy kezeltük ezt az esetet is
            }

            // Végső logolás a futás állapotáról
            if (!streamEndedSuccessfully && webRequest.result != UnityWebRequest.Result.Success)
            {
                Debug.LogError($"[OpenAIWebRequest] Assistant run finished with error AND stream did not end via [DONE] event: {webRequest.error}");
                // Értesíthetjük az IFM-et a hibáról
            }
            else if (!streamEndedSuccessfully)
            {
                // Ez nem feltétlenül hiba, de jelzi, hogy a [DONE] nem jött meg valamiért
                Debug.LogWarning($"[OpenAIWebRequest] Stream processing finished WITHOUT explicit [DONE] event. Final buffer content: '{buffer}'");
                // Kezeljük ezt az esetet is sikeresként, ha a webRequest maga sikeres volt
                if (webRequest.result == UnityWebRequest.Result.Success)
                {
                    streamEndedSuccessfully = true; // Jelöljük sikeresnek a callback híváshoz
                    Debug.LogWarning("[OAIWR DEBUG] Treating 'No DONE but Success Request' as successful for callback.");
                }
            }
            else
            {
                // Sikeres befejezés ([DONE] megérkezett)
                Debug.Log($"[OpenAIWebRequest] Assistant run stream processing finished successfully. Final buffer state: '{buffer}'");
                // streamEndedSuccessfully már true
            }

            if (webRequest.result == UnityWebRequest.Result.Success && streamEndedSuccessfully)
            {
                Debug.LogWarning("[OAIWR_LOG] Run considered successful for callback invocation."); // <<< ÚJ LOG
                if (onRunCompleteCallback != null)
                {
                    Debug.LogWarning("[OAIWR_LOG] Invoking onRunCompleteCallback...");
                    try
                    {
                        onRunCompleteCallback.Invoke();
                        Debug.LogWarning("[OAIWR_LOG] onRunCompleteCallback invoked successfully.");
                    }
                    catch (Exception ex)
                    {
                        Debug.LogError($"[OAIWR_LOG] Exception during onRunCompleteCallback: {ex.Message}\n{ex.StackTrace}");
                    }
                }
                else
                {
                    Debug.LogWarning("[OAIWR_LOG] No onRunCompleteCallback provided for this successful run.");
                }
            }
            else
            {
                Debug.LogWarning($"[OAIWR_LOG] Run completed unsuccessfully or without stream success flag. Not invoking callback. Result: {webRequest.result}, StreamSuccessFlag: {streamEndedSuccessfully}");
            }

        } // using (UnityWebRequest webRequest = ...) vége

        Debug.LogWarning($"[OAIWR DEBUG] CreateAssistantRun Coroutine ENDED (after using block). streamEndedSuccessfully={streamEndedSuccessfully}");
    }

    public IEnumerator SendAudioToWhisper(byte[] audioData, Action<string> onCompleted)
    {
        if (string.IsNullOrEmpty(apiKey) || apiKey == "SK-xxxxxxxxxxxxxxxxxxxx")
        {
            Debug.LogError("OpenAI API Key is not set in OpenAIWebRequest Inspector!");
            onCompleted?.Invoke(null); // Visszajelzés a hívónak, hogy hiba történt
            yield break; // Leállítja a korutint
        }

        if (audioData == null || audioData.Length == 0)
        {
            Debug.LogError("Audio data is empty or null.");
            onCompleted?.Invoke(null);
            yield break;
        }

        Debug.Log($"Sending {audioData.Length} bytes of audio data to Whisper API...");

        // --- A Multipart Form Data összeállítása ---
        List<IMultipartFormSection> formData = new List<IMultipartFormSection>();

        // 1. A hangfájl adat
        // Fontos a fájlnév megadása (bármi lehet .wav kiterjesztéssel)
        formData.Add(new MultipartFormFileSection("file", audioData, "audio.wav", "audio/wav"));

        // 2. A modell neve
        formData.Add(new MultipartFormDataSection("model", ModelName));

        string targetLanguage = "en"; // Alapértelmezett, ha valami hiba van
        if (AppStateManager.Instance != null && AppStateManager.Instance.CurrentLanguage != null && !string.IsNullOrEmpty(AppStateManager.Instance.CurrentLanguage.languageCode))
        {
            targetLanguage = AppStateManager.Instance.CurrentLanguage.languageCode;
            Debug.Log($"[Whisper] Using language code from AppStateManager: {targetLanguage}");
        }
        else
        {
            Debug.LogWarning("[Whisper] Could not get language code from AppStateManager or it was empty. Defaulting to 'en'. Check LanguageConfig setup.");
        }
        // Használjuk a dinamikusan meghatározott nyelvi kódot
        formData.Add(new MultipartFormDataSection("language", targetLanguage));

        // Opcionális: Válasz formátuma (alapértelmezetten json)
        // formData.Add(new MultipartFormDataSection("response_format", "json"));


        // --- UnityWebRequest létrehozása és konfigurálása ---
        UnityWebRequest request = UnityWebRequest.Post(WhisperApiUrl, formData);

        // API Kulcs hozzáadása a Headerhez
        request.SetRequestHeader("Authorization", $"Bearer {apiKey}");

        // FONTOS: NE állítsd be manuálisan a Content-Type-ot multipart kérésnél!
        // A UnityWebRequest.Post(url, formData) ezt automatikusan kezeli.
        // request.SetRequestHeader("Content-Type", "multipart/form-data"); // <<< EZT NE!

        // Várakozási idő növelése hosszabb hangfájlok esetén (opcionális)
        request.timeout = 60; // 60 másodperc

        // --- Kérés küldése és várakozás a válaszra ---
        yield return request.SendWebRequest();

        // --- Válasz feldolgozása ---
        if (request.result == UnityWebRequest.Result.ConnectionError || request.result == UnityWebRequest.Result.ProtocolError)
        {
            Debug.LogError($"Whisper API Error: {request.error}");
            Debug.LogError($"Response Code: {request.responseCode}");
            Debug.LogError($"Response Body: {request.downloadHandler?.text}"); // Próbáljuk kiírni a hibaüzenetet az API-tól
            onCompleted?.Invoke(null); // Hiba jelzése
        }
        else if (request.result == UnityWebRequest.Result.Success)
        {
            Debug.Log($"Whisper API Success! Response Code: {request.responseCode}");
            string responseJson = request.downloadHandler.text;
            Debug.Log($"Whisper Response JSON: {responseJson}");

            // JSON Parse-olás a transzkripció kinyeréséhez
            string transcription = ParseWhisperResponse(responseJson);

            onCompleted?.Invoke(transcription); // Visszaadjuk a sikeres transzkripciót
        }
        else
        {
            // Egyéb hiba (pl. DataProcessingError)
            Debug.LogError($"Whisper API Request failed with result: {request.result}");
            onCompleted?.Invoke(null);
        }

        // Erőforrások felszabadítása
        request.Dispose();
    }
    private string ParseWhisperResponse(string jsonResponse)
    {
        try
        {
            // Newtonsoft.Json használatával:
            JObject jsonObject = JObject.Parse(jsonResponse);
            string transcription = (string)jsonObject["text"];

            if (string.IsNullOrWhiteSpace(transcription))
            {
                Debug.LogWarning("Whisper returned an empty transcription.");
                return string.Empty; // Vagy null, ahogy preferálod
            }
            return transcription.Trim();

        }
        catch (Exception ex)
        {
            Debug.LogError($"Failed to parse Whisper JSON response: {ex.Message}");
            Debug.LogError($"JSON attempted to parse: {jsonResponse}");
            return null; // Hiba jelzése
        }
    }

}
