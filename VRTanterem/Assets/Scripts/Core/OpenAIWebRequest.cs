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
    [Header("OpenAI Configuration")]
    [Tooltip("Your OpenAI API Key (keep this secret!)")]
    [SerializeField] private string apiKey = "";

    [Tooltip("The ID of the OpenAI Assistant to use")]
    private string assistantID;
    private string aiTeacherFantasyName;

    private string apiUrl = "https://api.openai.com/v1";

    // --- UI Elemek ---
    // public string userInput = "Hello!";
    public TMP_Text TMPResponseText;
    [SerializeField] private TMP_InputField TMPInputField;
    public TMP_Text TMPUserText;

    // --- TTS Manager Referencia ---
    [Header("External Components")]
    [Tooltip("Reference to the TextToSpeechManager component for audio output.")]
    [SerializeField] private TextToSpeechManager textToSpeechManager;

    private StringBuilder fullResponseForLogging = new StringBuilder();
    private StringBuilder buffer = new StringBuilder();

    public event Action OnRunCompleted;

    // --- Belső Változók ---
    private string assistantThreadId;
    private string currentRunId;
    private StringBuilder messageBuilder = new StringBuilder();

    // Whisper beállítások
    private const string WhisperApiUrl = "https://api.openai.com/v1/audio/transcriptions";
    private const string ModelName = "whisper-1";

    private const float FILE_PROCESSING_DELAY_SECONDS = 0.5f;

    public enum AssistantRunType
    {
        MainLecture,
        InterruptionAnswer,
        QuizAnswerAndContinue
    }

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
                    
                }
            });
        }
        else
        {
            Debug.LogWarning("TMPInputField nincs beállítva az Inspectorban!");
        }

        Debug.Log("[OpenAIWebRequest] Start() finished (Automatic thread/run creation DISABLED for menu integration).");
    }


    public void InitializeAndStartInteraction(string selectedAssistantId, string selectedVoiceId, string fantasyName)
    {
        Debug.Log($"[OpenAIWebRequest] InitializeAndStartInteraction called. AssistantID: {selectedAssistantId}, VoiceID: {selectedVoiceId}, FantasyName: {fantasyName}");

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

        this.assistantID = selectedAssistantId;
        this.aiTeacherFantasyName = fantasyName;

        // --- ÁTHELYEZETT ELLENŐRZÉSEK ---
        bool configurationValid = true;
        
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
            enabled = false; 
                             
                             
            return;
        }
        // --- ELLENŐRZÉSEK VÉGE ---


        // --- 2. Függőségek Resetelése ---
        Debug.Log("[OpenAIWebRequest] Resetting dependent managers...");
        

        // --- 3. TTS Manager Inicializálása ---
        if (textToSpeechManager != null)
        {
            Debug.Log("[OpenAIWebRequest] Initializing TextToSpeechManager...");
            
            textToSpeechManager.Initialize(this.apiKey, selectedVoiceId);
        }
        

        // --- 4. OpenAI Interakció Indítása ---
        Debug.Log("[OpenAIWebRequest] Starting OpenAI interaction coroutines (GetAssistant, CreateThread)...");
        StartCoroutine(GetAssistant());
        StartCoroutine(CreateThread());

        Debug.Log("[OpenAIWebRequest] Initialization and startup complete.");
    }

    public void CancelAllOngoingRequestsAndResetState()
    {
        Debug.LogWarning($"[OpenAIWebRequest] CancelAllOngoingRequestsAndResetState ELINDÍTVA. Idő: {Time.time}");

        // 1. Minden, ezen MonoBehaviour által indított korutin leállítása.
        Debug.Log("[OpenAIWebRequest] Minden futó korutin leállítása...");
        StopAllCoroutines();

        // 2. Kritikus állapotváltozók resetelése
        Debug.Log("[OpenAIWebRequest] Belső állapotváltozók resetelése...");

        assistantThreadId = null;
        currentRunId = null;

        // StringBuilderek törlése
        if (messageBuilder != null) messageBuilder.Clear();
        if (buffer != null) buffer.Clear(); // A streaminghez használt buffer
        if (fullResponseForLogging != null) fullResponseForLogging.Clear();

        assistantID = null;

        Debug.LogWarning($"[OpenAIWebRequest] CancelAllOngoingRequestsAndResetState BEFEJEZŐDÖTT. assistantThreadId: {assistantThreadId}, currentRunId: {currentRunId}. Idő: {Time.time}");
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
            
            return;
        }
        if (string.IsNullOrEmpty(assistantThreadId))
        {
            Debug.LogError("[OpenAIWebRequest] Cannot process voice input: Assistant Thread ID is missing.");
           
            if (TMPResponseText != null) TMPResponseText.text = "Error: Assistant connection not ready.";
            return;
        }

        // 2. Megjelenítés (Opcionális, de hasznos visszajelzés)
        
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
        messageBuilder.Clear();
        buffer.Clear();
        StartCoroutine(SendMessageSequence(recognizedText));

        
    }


    // Beállítja a közös HTTP fejléceket az OpenAI kérésekhez
    private void SetCommonHeaders(UnityWebRequest request)
    {
        // Most már az Inspectorban beállított apiKey változót használja
        request.SetRequestHeader("Content-Type", "application/json");
        request.SetRequestHeader("Authorization", $"Bearer {apiKey}");
        request.SetRequestHeader("OpenAI-Beta", "assistants=v2");
    }


    public void StartMainLectureRun()
    {
        Debug.LogWarning($"[OAIWR_LOG] >>> StartMainLectureRun ENTER.");

        if (string.IsNullOrEmpty(assistantThreadId))
        {
            Debug.LogError("[OpenAIWebRequest] Cannot start lecture run: Assistant Thread ID is invalid.");
            return;
        }

        Debug.LogWarning($"[OAIWR_LOG] Calling CreateAssistantRun for main lecture (MainLecture).");
        StartCoroutine(CreateAssistantRun(
            runType: AssistantRunType.MainLecture, // <<< MODIFIED
            onRunCompleteCallback: null // Or a callback if IFM needs it
        ));

        Debug.LogWarning($"[OAIWR_LOG] <<< StartMainLectureRun EXIT (Coroutine Started).");
    }

    public void SendQuizAnswerAndContinueLecture(string originalQuizQuestion, string userAnswerText)
    {
        Debug.LogWarning($"[OAIWR_LOG] >>> SendQuizAnswerAndContinueLecture ENTER. Original Quiz: '{originalQuizQuestion}', User Answer: '{userAnswerText}'");

        if (string.IsNullOrEmpty(assistantThreadId))
        {
            Debug.LogError("[OpenAIWebRequest] Cannot send quiz answer: Assistant Thread ID is missing.");
            return;
        }
        if (string.IsNullOrEmpty(originalQuizQuestion) || string.IsNullOrEmpty(userAnswerText))
        {
            Debug.LogError("[OpenAIWebRequest] Original quiz question or user answer is empty. Cannot proceed.");
            return;
        }

        // This coroutine will add the user's answer as a message, then start a new run
        // with specific instructions for the AI to evaluate the answer and continue the lecture.
        StartCoroutine(AddMessageAndStartQuizEvaluationRunCoroutine(originalQuizQuestion, userAnswerText));
        Debug.LogWarning($"[OAIWR_LOG] <<< SendQuizAnswerAndContinueLecture EXIT (Coroutine Started).");
    }

    // New helper coroutine for the above method
    private IEnumerator AddMessageAndStartQuizEvaluationRunCoroutine(string originalQuizQuestion, string userAnswerText)
    {
        Debug.LogWarning($"[OAIWR_LOG] >>> AddMessageAndStartQuizEvaluationRunCoroutine ENTER. Original Quiz: '{originalQuizQuestion}'");

        // --- Step 1: Add User's Answer as a Message to the Thread ---
        string messageURL = $"{apiUrl}/threads/{assistantThreadId}/messages";
        Debug.Log($"[OpenAIWebRequest] Posting User's Quiz Answer to Thread: {assistantThreadId}");

        JObject messageBody = new JObject
        {
            ["role"] = "user",
            ["content"] = userAnswerText // The user's answer to the quiz
        };
        string messageJson = messageBody.ToString();

        using (UnityWebRequest webRequest = new UnityWebRequest(messageURL, "POST"))
        {
            byte[] bodyRaw = Encoding.UTF8.GetBytes(messageJson);
            webRequest.uploadHandler = new UploadHandlerRaw(bodyRaw);
            webRequest.downloadHandler = new DownloadHandlerBuffer();
            SetCommonHeaders(webRequest);

            Debug.LogWarning("[OAIWR_LOG] Sending user's quiz answer message...");
            yield return webRequest.SendWebRequest();
            Debug.LogWarning($"[OAIWR_LOG] User's quiz answer message sent. Result: {webRequest.result}");

            if (webRequest.result != UnityWebRequest.Result.Success)
            {
                Debug.LogError($"[OpenAIWebRequest] Error posting user's quiz answer: {webRequest.error} - {webRequest.downloadHandler.text}");
                // Notify IFM about the error?
                Debug.LogWarning("[OAIWR_LOG] <<< AddMessageAndStartQuizEvaluationRunCoroutine EXIT (Error sending message).");
                yield break;
            }
            else
            {
                Debug.Log("[OpenAIWebRequest] User's quiz answer added to thread successfully.");

                // --- Step 2: Start the Assistant Run for Quiz Evaluation and Lecture Continuation ---
                Debug.LogWarning("[OAIWR_LOG] Calling CreateAssistantRun for QuizAnswerAndContinue.");
                StartCoroutine(CreateAssistantRun(
                    runType: AssistantRunType.QuizAnswerAndContinue,
                    originalQuizQuestionForContext: originalQuizQuestion,
                    userAnswerForContext: userAnswerText,
                    onRunCompleteCallback: null // Or a specific callback if IFM needs it
                ));
            }
        }
        Debug.LogWarning("[OAIWR_LOG] <<< AddMessageAndStartQuizEvaluationRunCoroutine EXIT (Run Started or Error).");
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
        // --- 1. LÉPÉS: Thread létrehozása ---
        Debug.Log("[OpenAIWebRequest] Step 1: Creating new empty Thread...");
        string createThreadUrl = $"{apiUrl}/threads";
        UnityWebRequest createThreadRequest = new UnityWebRequest(createThreadUrl, "POST")
        {
            uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes("{}")),
            downloadHandler = new DownloadHandlerBuffer()
        };
        SetCommonHeaders(createThreadRequest);

        yield return createThreadRequest.SendWebRequest();

        if (createThreadRequest.result != UnityWebRequest.Result.Success)
        {
            Debug.LogError($"Error during Step 1 (CreateThread): {createThreadRequest.error} | Response: {createThreadRequest.downloadHandler.text}");
            InteractionFlowManager.Instance?.HandleInitializationFailed("Thread creation failed.");
            createThreadRequest.Dispose();
            yield break;
        }

        string createThreadResponseText = createThreadRequest.downloadHandler.text;
        createThreadRequest.Dispose();

        // --- 2. LÉPÉS: Válasz feldolgozása, Thread ID kinyerése ---
        string newAssistantThreadId;
        try
        {
            JObject responseJson = JObject.Parse(createThreadResponseText);
            newAssistantThreadId = responseJson["id"]?.ToString();
            if (string.IsNullOrEmpty(newAssistantThreadId))
            {
                Debug.LogError("Failed to retrieve assistantThreadId from thread creation response.");
                InteractionFlowManager.Instance?.HandleInitializationFailed("Thread ID missing after creation.");
                yield break;
            }
        }
        catch (Exception e)
        {
            Debug.LogError($"Error parsing thread creation response: {e.Message}");
            InteractionFlowManager.Instance?.HandleInitializationFailed("Error parsing thread response.");
            yield break;
        }

        this.assistantThreadId = newAssistantThreadId;
        Debug.Log($"Thread created successfully with ID: {this.assistantThreadId}.");

        // --- 3. LÉPÉS: A "Súgás" üzenet hozzáadása ---
        Debug.Log("[OpenAIWebRequest] Step 3: Adding priming message...");
        string languageCode = AppStateManager.Instance?.CurrentLanguage?.languageCode ?? "hu"; // Alapértelmezett a 'hu', ha valami hiba történne

        string primingContent = $"(System Instruction: Your name for this interaction is '{this.aiTeacherFantasyName}'. IMPORTANT: You must conduct the entire interaction, including the lecture and all responses, strictly in the language with the code: '{languageCode}'. Please start the interaction by introducing yourself with this name, stating the lecture's topic, and then asking for the user's name.)";

        Debug.Log($"Priming message created with language instruction: {languageCode}");
        JObject messageBody = new JObject { ["role"] = "user", ["content"] = primingContent };
        string messageUrl = $"{apiUrl}/threads/{this.assistantThreadId}/messages";

        using (UnityWebRequest addMessageRequest = new UnityWebRequest(messageUrl, "POST"))
        {
            addMessageRequest.uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(messageBody.ToString()));
            addMessageRequest.downloadHandler = new DownloadHandlerBuffer();
            SetCommonHeaders(addMessageRequest);

            yield return addMessageRequest.SendWebRequest();

            if (addMessageRequest.result != UnityWebRequest.Result.Success)
            {
                Debug.LogError($"Error during Step 3 (AddMessage): {addMessageRequest.error} | Response: {addMessageRequest.downloadHandler.text}");
                InteractionFlowManager.Instance?.HandleInitializationFailed("Priming message failed.");
                yield break;
            }
        }

        // --- 4. LÉPÉS: VÁRAKOZÁS a szerveroldali feldolgozásra ---
        Debug.LogWarning("[OAIWR] Priming message sent. Waiting for 1.5 seconds for backend to process it before starting the run...");
        yield return new WaitForSeconds(1.5f); // Using a slightly more conservative delay

        // --- 5. LÉPÉS: A MainLecture run indítása ---
        Debug.Log("[OpenAIWebRequest] Step 5: Starting the first MainLecture run.");
        StartCoroutine(CreateAssistantRun(
            runType: AssistantRunType.MainLecture,
            onRunCompleteCallback: null
        ));
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

                
                if (webRequest.result == UnityWebRequest.Result.Success)
                {
                    Debug.Log($"Cancel request for run {currentRunId} sent. Response: {webRequest.downloadHandler.text}");
                }
                else
                {
                    
                    Debug.LogWarning($"Run cancellation request failed for run {currentRunId}: {webRequest.error} - {webRequest.downloadHandler.text}");
                }
            }
            
            currentRunId = null;
        }
        else
        {
            // Debug.Log("No current run to cancel or thread ID missing."); // Csak szükség esetén
        }
    }

    

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
                Debug.LogWarning("[OAIWR_LOG] Starting Assistant Run from GetAssistantResponse (InterruptionAnswer)."); // Log updated
                StartCoroutine(CreateAssistantRun(
                    runType: AssistantRunType.InterruptionAnswer, // <<< MODIFIED: Assuming this is for a direct answer
                    onRunCompleteCallback: null // Or a callback if IFM needs to know when the short answer is done
                ));
            }
        }
    }

    

    // --- ÚJ METÓDUS A KÉRDÉS KÜLDÉSÉRE ELŐADÁS KÖZBEN ---
    
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

    public IEnumerator AddUserMessageAndStartLectureRun(string userMessage)
    {
        Debug.LogWarning($"[OAIWR_LOG] >>> AddUserMessageAndStartLectureRun ENTER. Message: '{userMessage}'");

        if (string.IsNullOrEmpty(assistantThreadId))
        {
            Debug.LogError("[OpenAIWebRequest] Cannot add message: Assistant Thread ID is invalid.");
            // TODO: Handle error state in IFM?
            yield break;
        }
        if (string.IsNullOrEmpty(userMessage))
        {
            Debug.LogWarning("[OpenAIWebRequest] Cannot add empty user message. Starting lecture run directly.");
            StartMainLectureRun(); // Fallback: start run without adding message
            yield break;
        }

        // --- Step 1: Add User Message to Thread ---
        string messagesUrl = $"{apiUrl}/threads/{assistantThreadId}/messages";
        Debug.Log($"[OpenAIWebRequest] Adding user message to thread: {assistantThreadId} at URL: {messagesUrl}");

        var messageBody = new JObject
        {
            ["role"] = "user",
            ["content"] = userMessage
        };
        string messageJson = messageBody.ToString();
        byte[] bodyRaw = System.Text.Encoding.UTF8.GetBytes(messageJson);

        using (UnityWebRequest messageRequest = new UnityWebRequest(messagesUrl, "POST"))
        {
            messageRequest.uploadHandler = new UploadHandlerRaw(bodyRaw);
            messageRequest.downloadHandler = new DownloadHandlerBuffer();
            messageRequest.SetRequestHeader("Content-Type", "application/json");
            messageRequest.SetRequestHeader("Authorization", $"Bearer {apiKey}");
            messageRequest.SetRequestHeader("OpenAI-Beta", "assistants=v2");

            yield return messageRequest.SendWebRequest();

            if (messageRequest.result == UnityWebRequest.Result.Success)
            {
                Debug.Log($"[OpenAIWebRequest] User message added successfully to thread {assistantThreadId}.");
                Debug.LogWarning("[OAIWR_LOG] Message added. Now calling CreateAssistantRun for MainLecture internally.");
                StartCoroutine(CreateAssistantRun(
                    runType: AssistantRunType.MainLecture, // <<< MODIFIED
                    onRunCompleteCallback: null
                ));
            }
        } // using messageRequest

        Debug.LogWarning($"[OAIWR_LOG] <<< AddUserMessageAndStartLectureRun EXIT.");
    }

    // --- ÚJ SEGÉD KORUTIN AZ ÜZENET HOZZÁADÁSÁHOZ ÉS A VÁLASZ FUTTATÁS INDÍTÁSÁHOZ ---
    private IEnumerator AddMessageAndStartAnswerRunCoroutine(string userQuestionText, string followUpPromptText)
    {
        Debug.LogWarning($"[OAIWR_LOG] >>> AddMessageAndStartAnswerRunCoroutine ENTER. FollowUpPrompt: '{followUpPromptText}'");
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
                Debug.LogWarning("[OAIWR_LOG] Calling CreateAssistantRun for answering question (InterruptionAnswer).");
                StartCoroutine(CreateAssistantRun(
                    runType: AssistantRunType.InterruptionAnswer, // <<< MODIFIED
                                                                  // originalQuizQuestionForContext and userAnswerForContext are not needed for InterruptionAnswer
                    customInstructions: followUpPromptText,
                    onRunCompleteCallback: null
                ));
            }
        }
        Debug.LogWarning("[OAIWR_LOG] <<< AddMessageAndStartAnswerRunCoroutine EXIT (Run Started).");
    }

    public string GetLastFullResponse()
    {
        return fullResponseForLogging.ToString();
    }

    public void ClearLastFullResponse()
    {
        fullResponseForLogging.Clear();
        Debug.LogWarning("[OAIWR] Cleared last full response log.");
    }

    // Létrehoz és elindít egy új asszisztens futtatást (run) streaming módban
    private IEnumerator CreateAssistantRun(
    AssistantRunType runType,
    string originalQuizQuestionForContext = null,
    string userAnswerForContext = null,
    Action onRunCompleteCallback = null,
    string customInstructions = null)
    {
        Debug.LogWarning($"[OAIWR_LOG] >>> CreateAssistantRun ENTER. RunType: {runType}, HasCallback: {onRunCompleteCallback != null}, Frame: {Time.frameCount}");
        if (string.IsNullOrEmpty(assistantThreadId))
        {
            Debug.LogError("[OpenAIWebRequest] Assistant Thread ID is invalid before creating run.");
            // Fontos, hogy jelezzük a hívónak (pl. IFM), hogy a futtatás nem indult el.
            onRunCompleteCallback?.Invoke(); // Meghívjuk a callback-et, jelezve a "befejezést" (hibásat)
            OnRunCompleted?.Invoke(); // Kiváltjuk az általános eseményt is
            yield break; // Kilépünk a korutinból
        }

        string runUrl = $"{apiUrl}/threads/{assistantThreadId}/runs";
        Debug.Log($"[OpenAIWebRequest] Creating assistant run (RunType: {runType}) for thread: {assistantThreadId} at URL: {runUrl}");

        var runBody = new JObject
        {
            ["assistant_id"] = assistantID, // Az osztályszintű assistantID változót használjuk
            ["stream"] = true               // Mindig engedélyezzük a streaminget
        };

        string language = AppStateManager.Instance?.CurrentLanguage?.languageCode ?? "hu";

        // --- PROMPT ENGINEERING: Speciális instrukciók hozzáadása, ha kérdésre válaszolunk ---
        switch (runType)
        {
            case AssistantRunType.InterruptionAnswer:
                if (!string.IsNullOrEmpty(customInstructions))
                {
                    // Ha kaptunk egyéni instrukciót (a followUpPromptText-et), akkor azt használjuk.
                    // Fontos, hogy ez az instrukció már tartalmazza a nyelvi specifikációt és a folytatásra való utasítást.
                    runBody["additional_instructions"] = customInstructions;
                    Debug.LogWarning($"[OpenAIWebRequest] Added CUSTOM INTERRUPT handling instructions (from IFM): {customInstructions}");
                }
                else
                {
                    // Fallback a régi, beégetett instrukcióra, ha valamiért nem kapunk customInstructions-t
                    // (De ideális esetben mindig kellene kapnunk az IFM-ből)
                    string fallbackInterruptInstructions =
                        $"INSTRUCTION: The user has interrupted the lecture or asked a question. " +
                        $"Provide ONLY a SHORT, direct answer to the user's specific question. " +
                        $"Use language: {language}. " +
                        $"CRITICAL: Your response MUST consist ONLY of the short answer. Do NOT ask any follow-up questions. Your role ends after the short answer.";
                    runBody["additional_instructions"] = fallbackInterruptInstructions;
                    Debug.LogWarning($"[OpenAIWebRequest] Added FALLBACK INTERRUPT handling instructions in language: {language} (Custom instructions were empty)");
                }
                break;

            case AssistantRunType.QuizAnswerAndContinue:
                if (string.IsNullOrEmpty(originalQuizQuestionForContext) || string.IsNullOrEmpty(userAnswerForContext))
                {
                    Debug.LogError("[OpenAIWebRequest] QuizAnswerAndContinue run type requires originalQuizQuestion and userAnswerForContext! Aborting run creation.");
                    // Callback-ek és események meghívása a "befejezés" jelzésére
                    onRunCompleteCallback?.Invoke();
                    OnRunCompleted?.Invoke();
                    yield break; // Kilépés a korutinból
                }
                // This instruction guides the AI to evaluate the quiz answer and then seamlessly continue the lecture.
                string quizInstructions =
                    $"INSTRUCTION: You previously asked the user an ellenőrző kérdés (quiz question). The original question was: \"{originalQuizQuestionForContext}\". " +
                    $"The user's answer to your quiz question is: \"{userAnswerForContext}\". " +
                    $"Your task is to: " +
                    $"1. Evaluate if the user's answer is correct based on the lecture material. " +
                    $"2. Provide brief, natural-sounding feedback. If correct, offer praise (e.g., 'Kiváló, ez pontosan így van!' or 'Remekül tudtad!'). If incorrect, briefly state it's not correct and provide the correct answer concisely (e.g., 'Nem egészen. A helyes válasz az volt, hogy... De semmi gond, menjünk tovább!'). " +
                    $"3. CRITICAL: After providing this feedback, SEAMLESSLY and IMMEDIATELY transition into and continue with the next logical segment of the lecture IN THE SAME RESPONSE. Do NOT ask any other questions like 'Van további kérdése?' or 'Érthető volt?'. Your response should be a single, continuous flow of feedback followed by the next part of the lecture. " +
                    $"Use language: {language}.";
                runBody["additional_instructions"] = quizInstructions;
                Debug.LogWarning($"[OpenAIWebRequest] Added QUIZ ANSWER evaluation and LECTURE CONTINUATION instructions in language: {language}");
                break;

            case AssistantRunType.MainLecture:
                // For continuing the main lecture, usually no *additional* instructions are needed here,
                // as the AI should pick up from where it left off based on the thread history and main system prompt.
                // The main system prompt should already contain instructions for delivering the lecture and pausing.
                Debug.Log($"[OpenAIWebRequest] RunType is MainLecture. Relying on main system prompt and thread history.");
                break;
        }

        string runJson = runBody.ToString();
        Debug.LogWarning($"[OAIWR_LOG] Creating Run with Body: {runJson}");

        // --- Streaming Változók Inicializálása ---
        bool streamEndedSuccessfully = false;
        StringBuilder fullResponseForLoggingLocal = new StringBuilder();
        fullResponseForLogging.Clear();
        int lastProcessedIndex = 0;
        buffer.Clear();
        bool streamStartNotifiedToIFM = false;
        int eventSeparatorIndex;
        StringBuilder sentenceBuffer = new StringBuilder();

        // --- Web Kérés Indítása és Feldolgozása ---
        using (UnityWebRequest webRequest = new UnityWebRequest(runUrl, "POST"))
        {
            webRequest.uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(runJson)); // A JSON payload feltöltése
            webRequest.downloadHandler = new DownloadHandlerBuffer(); // Alap buffer a stream fogadásához
            SetCommonHeaders(webRequest); // Közös HTTP fejlécek beállítása (API kulcs, Content-Type, OpenAI-Beta)

            // <<< Kezdő késleltetés >>>
            if (runType == AssistantRunType.MainLecture)
            {
                Debug.LogWarning($"[OAIWR_LOG] RunType is {runType}. Waiting for {FILE_PROCESSING_DELAY_SECONDS} seconds to allow for file/vector store processing before sending run request...");
                yield return new WaitForSeconds(FILE_PROCESSING_DELAY_SECONDS);
                Debug.LogWarning($"[OAIWR_LOG] Delay complete for {runType}. Proceeding to send run request.");
            }

            // Aszinkron művelet indítása
            UnityWebRequestAsyncOperation asyncOp = webRequest.SendWebRequest();
            Debug.LogWarning($"[OAIWR_LOG] CreateAssistantRun - WebRequest Sent. Starting ROBUST WHILE loop for streaming. Frame: {Time.frameCount}");

            // Ciklus, amíg a kérés be nem fejeződik VAGY a stream véget nem ér ([DONE])
            while (!streamEndedSuccessfully)
            {

                if (webRequest.result != UnityWebRequest.Result.InProgress && webRequest.result != UnityWebRequest.Result.Success)
                {
                    Debug.LogError($"[OpenAIWebRequest] Web request failed mid-stream: {webRequest.error} - Status: {webRequest.result}");
                    break; // Hiba esetén kilépünk a ciklusból
                }

                // Ellenőrizzük, hogy érkezett-e új adat
                if (webRequest.downloadHandler.data != null)
                {
                    int currentLength = webRequest.downloadHandler.data.Length;
                    if (currentLength > lastProcessedIndex)
                    {
                        string newTextChunk = Encoding.UTF8.GetString(webRequest.downloadHandler.data, lastProcessedIndex, currentLength - lastProcessedIndex);
                        lastProcessedIndex = currentLength;
                        buffer.Append(newTextChunk);

                        // <<< MÓDOSÍTÁS KEZDETE >>>
                        int processedCharsInThisChunk = 0; // Számláló a bufferből eltávolítandó karakterekhez

                        while (true) // Belső ciklus a bufferben lévő összes teljes esemény feldolgozására
                        {
                            string currentBufferContentForSearch = buffer.ToString(); // Mindig a friss buffer tartalmat használjuk a kereséshez
                            eventSeparatorIndex = currentBufferContentForSearch.IndexOf("\n\n"); // Keressük az első "\n\n"-t

                            if (eventSeparatorIndex != -1) // Találtunk egy teljes eseményt
                            {
                                // Az esemény hossza a "\n\n"-nel együtt
                                int eventLengthWithSeparator = eventSeparatorIndex + 2;
                                string eventBlock = currentBufferContentForSearch.Substring(0, eventSeparatorIndex);

                                // Feldolgozzuk az esemény blokk sorait
                                string[] lines = eventBlock.Split('\n', StringSplitOptions.RemoveEmptyEntries);
                                foreach (string line in lines)
                                {
                                    if (line.StartsWith("event:")) continue;

                                    if (line.StartsWith("data:"))
                                    {
                                        string jsonString = line.Substring(5).Trim();
                                        if (jsonString == "[DONE]")
                                        {
                                            Debug.LogWarning($"[OAIWR DEBUG] Received [DONE] event. Stream ended.");
                                            streamEndedSuccessfully = true;
                                            break; // Kilépés a foreach ciklusból
                                        }
                                        try
                                        {
                                            JObject dataObject = JObject.Parse(jsonString);
                                            string objectType = dataObject["object"]?.ToString();
                                            if (objectType == "thread.message.delta")
                                            {
                                                if (!streamStartNotifiedToIFM)
                                                {
                                                    // JToken contentValueToken = dataObject.SelectToken("delta.content[0].text.value"); // Ellenőrizd, hogy ez a feltétel szükséges-e még, vagy elég, ha az első adatcsomag megérkezik
                                                    // if (contentValueToken != null && !string.IsNullOrEmpty(contentValueToken.ToString())) // A te kódodban ez a rész lehet, hogy máshogy van, a lényeg az IFM hívás egységesítése
                                                    // {
                                                    // Mindig az IFM egységesített stream start handlerjét hívjuk.
                                                    // Cseréld le 'HandleLectureStreamStart'-ot 'HandleAIResponseStreamStart'-ra, ha átnevezted az IFM-ben.
                                                    InteractionFlowManager.Instance?.HandleLectureStreamStart(); // VAGY HandleAIResponseStreamStart();
                                                    streamStartNotifiedToIFM = true;
                                                    Debug.LogWarning($"[OAIWR Stream] Notified IFM about unified AI response stream start (RunType: {runType}).");
                                                    // }
                                                }
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
                                                                // A naplózáshoz és a pufferhez is hozzáadjuk a bejövő szövegdarabot
                                                                fullResponseForLogging.Append(textDelta);
                                                                sentenceBuffer.Append(textDelta);

                                                                // Ellenőrizzük, hogy a pufferben összegyűlt-e már legalább egy teljes mondat
                                                                string currentBufferContent = sentenceBuffer.ToString();

                                                                // Mondatvégi írásjelek, amik alapján darabolunk
                                                                char[] sentenceDelimiters = { '.', '!', '?' };
                                                                int delimiterIndex;

                                                                // Addig ismételjük, amíg van feldolgozható mondat a pufferben
                                                                while ((delimiterIndex = currentBufferContent.IndexOfAny(sentenceDelimiters)) != -1)
                                                                {
                                                                    // Kivesszük a teljes mondatot az írásjellel együtt
                                                                    string completeSentence = currentBufferContent.Substring(0, delimiterIndex + 1);

                                                                    // Átadjuk a Befejezett, TELJES mondatot a TTS Managernek
                                                                    if (textToSpeechManager != null)
                                                                    {
                                                                        textToSpeechManager.AppendText(completeSentence);
                                                                    }

                                                                    // Eltávolítjuk a feldolgozott mondatot a pufferből
                                                                    sentenceBuffer.Remove(0, delimiterIndex + 1);

                                                                    // Frissítjük a puffer tartalmát a következő ciklushoz
                                                                    currentBufferContent = sentenceBuffer.ToString();
                                                                }
                                                            }
                                                        }
                                                    }
                                                }
                                            }
                                        }
                                        catch (Exception e) { Debug.LogError($"Error parsing stream data JSON: {e.Message} - JSON: {jsonString}"); }
                                        if (streamEndedSuccessfully) break;
                                    }
                                } // foreach line vége

                                // Távolítsuk el a feldolgozott eseményt a bufferből
                                buffer.Remove(0, eventLengthWithSeparator);
                                processedCharsInThisChunk += eventLengthWithSeparator; // Növeljük a számlálót (bár ezt már nem használjuk a javított logikában)

                                if (streamEndedSuccessfully) break; // Ha [DONE] volt, kilépünk a belső while(true)-ból is
                            }
                            else // Nincs több teljes esemény a bufferben
                            {
                                break; // Kilépés a belső while(true) ciklusból, várjuk a következő adatcsomagot
                            }
                        } // belső while(true) vége
                        if (streamEndedSuccessfully) break;
                    } // if (currentLength > lastProcessedIndex) vége
                } // if (webRequest.downloadHandler.data != null) vége

                // Ha a stream véget ért egy esemény miatt ([DONE]), lépjünk ki a fő while (!asyncOp.isDone) ciklusból is
                if (streamEndedSuccessfully) break;

                // Várakozás a következő frame-re, hogy ne terheljük túl a CPU-t a folyamatos ellenőrzéssel
                yield return null;
            } // while (!asyncOp.isDone && !streamEndedSuccessfully) vége

            // --- UTÓFELDOLGOZÁS 1. LÉPÉS: Utolsó adatcsomagok begyűjtése ---
            Debug.LogWarning("[OAIWR DEBUG] Main stream loop finished. Performing final data processing pass...");
            if (webRequest.downloadHandler.data != null)
            {
                if (webRequest.downloadHandler.data.Length > lastProcessedIndex)
                {
                    string newTextChunk = Encoding.UTF8.GetString(webRequest.downloadHandler.data, lastProcessedIndex, webRequest.downloadHandler.data.Length - lastProcessedIndex);
                    lastProcessedIndex = webRequest.downloadHandler.data.Length;
                    buffer.Append(newTextChunk);
                    Debug.LogWarning($"[OAIWR DEBUG] Final pass processing new chunk: \"{newTextChunk}\"");

                    // Itt is feldolgozzuk az SSE eseményeket, ahogy a ciklusban
                    while (true)
                    {
                        string currentBufferContentForSearch = buffer.ToString();
                        eventSeparatorIndex = currentBufferContentForSearch.IndexOf("\n\n");

                        if (eventSeparatorIndex != -1)
                        {
                            int eventLengthWithSeparator = eventSeparatorIndex + 2;
                            string eventBlock = currentBufferContentForSearch.Substring(0, eventSeparatorIndex);
                            string[] lines = eventBlock.Split('\n', StringSplitOptions.RemoveEmptyEntries);
                            foreach (string line in lines)
                            {
                                if (line.StartsWith("data:"))
                                {
                                    string jsonString = line.Substring(5).Trim();
                                    if (jsonString == "[DONE]")
                                    {
                                        streamEndedSuccessfully = true;
                                        break;
                                    }
                                    try
                                    {
                                        JObject dataObject = JObject.Parse(jsonString);
                                        if (dataObject["object"]?.ToString() == "thread.message.delta")
                                        {
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
                                                            // Itt is CSAK a pufferekbe írunk, a TTS-t nem hívjuk!
                                                            fullResponseForLogging.Append(textDelta);
                                                            sentenceBuffer.Append(textDelta);
                                                        }
                                                    }
                                                }
                                            }
                                        }
                                    }
                                    catch (Exception e) { Debug.LogError($"Error parsing final stream data JSON: {e.Message} - JSON: {jsonString}"); }
                                    if (streamEndedSuccessfully) break;
                                }
                            }
                            buffer.Remove(0, eventLengthWithSeparator);
                            if (streamEndedSuccessfully) break;
                        }
                        else
                        {
                            break;
                        }
                    }
                }
            }

            // --- UTÓFELDOLGOZÁS 2. LÉPÉS: A puffer végső kiürítése ---
            if (sentenceBuffer.Length > 0)
            {
                Debug.LogWarning($"[OAIWR Puffer] Flushing ALL remaining text from buffer at the very end: '{sentenceBuffer.ToString()}'");
                if (textToSpeechManager != null)
                {
                    // A sentenceBufferben lévő esetlegesen több mondatot is a TTS AppendText-je kezelni fogja.
                    textToSpeechManager.AppendText(sentenceBuffer.ToString());
                }
                sentenceBuffer.Clear();
            }

            // --- Korutin Vége - Utófeldolgozás ---
            Debug.LogWarning($"[OAIWR_LOG] <<< CreateAssistantRun Loop Ended. Frame: {Time.frameCount}, StreamEndedSuccessfully: {streamEndedSuccessfully}, WebRequestResult: {webRequest.result}");

            this.fullResponseForLogging.Clear();
            this.fullResponseForLogging.Append(fullResponseForLoggingLocal.ToString());

            bool runConsideredEffectivelyComplete = streamEndedSuccessfully ||
                                              (webRequest.result == UnityWebRequest.Result.Success && !streamEndedSuccessfully && fullResponseForLogging.Length > 0);


            if (runConsideredEffectivelyComplete)
            {
                Debug.LogWarning("[OAIWR DEBUG] Flushing TTS buffers post-loop as run is considered effectively complete.");
                try
                {
                    if (textToSpeechManager != null)
                    {
                        if (runType == AssistantRunType.InterruptionAnswer || // Most már ez is a fő puffert használja
                            runType == AssistantRunType.MainLecture ||
                            runType == AssistantRunType.QuizAnswerAndContinue)
                        {
                            textToSpeechManager.FlushBuffer(); // A fő előadás csatorna pufferét ürítjük
                        }
                    }
                }
                catch (Exception e)
                {
                    Debug.LogError($"TTS Flush Error (Post-Loop): {e.Message}");
                }

                // Ha a webkérés sikeres volt, de a [DONE] nem jött meg, de volt adat, akkor is sikeresnek tekintjük a stream végét.
                if (!streamEndedSuccessfully && webRequest.result == UnityWebRequest.Result.Success && fullResponseForLogging.Length > 0)
                {
                    Debug.LogWarning("[OAIWR DEBUG] WebRequest was successful, data received, but [DONE] event was not seen. Treating as successful completion for callback logic.");
                    streamEndedSuccessfully = true; // Jelöljük sikeresnek a callback híváshoz
                }
            }
            else // Handle cases where the run failed or no data was processed
            {
                // Ha a webkérés nem volt sikeres, vagy sikeres volt, de nem kaptunk semmilyen adatot (és [DONE] sem jött)
                Debug.LogError($"[OpenAIWebRequest] Assistant run did not complete successfully or produced no data. WebRequest Result: {webRequest.result}, StreamEndedSuccessfully: {streamEndedSuccessfully}, Error: {webRequest.error}, DataReceived: {fullResponseForLogging.Length > 0}");
                // Itt a streamEndedSuccessfully valószínűleg false marad, így a lenti callback logika a hibás ágat fogja követni.
            }

            // Callback-ek és események meghívása CSAK akkor, ha a streamet sikeresen befejezettnek tekintjük
            if (streamEndedSuccessfully) // Ez a flag most már a runConsideredEffectivelyComplete logikáját is tükrözi
            {
                Debug.LogWarning($"[OAIWR_LOG] Stream ended successfully (or considered as such). Invoking callback/event. (webRequest.result was: {webRequest.result})");

                // 1. Specifikus onRunCompleteCallback hívása (ha volt megadva)
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

                // 2. Általános OnRunCompleted esemény kiváltása (MINDIG, ha a stream sikeresen véget ért)
                // Erre az IFM vagy más rendszerszintű figyelők iratkozhatnak fel.
                Debug.LogWarning("[OAIWR_LOG] Invoking OnRunCompleted event for successful run...");
                try
                {
                    OnRunCompleted?.Invoke();
                    Debug.LogWarning("[OAIWR_LOG] OnRunCompleted event invoked for successful run.");
                }
                catch (Exception ex)
                {
                    Debug.LogError($"[OAIWR_LOG] Exception during OnRunCompleted (successful run) invocation: {ex.Message}\n{ex.StackTrace}");
                }
            }
            else // Ha a stream NEM fejeződött be sikeresen
            {
                Debug.LogWarning($"[OAIWR_LOG] Stream did NOT end successfully. WebRequest Result: {webRequest.result}. Invoking general OnRunCompleted to signal sequence end (failure).");
                // Hiba esetén is fontos lehet jelezni az IFM-nek, hogy a futtatási kísérlet lezárult,
                // hogy az pl. újra engedélyezhesse a felhasználói bevitelt vagy hibát jelezhessen.
                // Ezért az OnRunCompleted eseményt itt is kiváltjuk.
                // A specifikus onRunCompleteCallback-ot ilyenkor általában nem hívjuk, mert az a sikerhez kötött.
                try
                {
                    OnRunCompleted?.Invoke();
                    Debug.LogWarning("[OAIWR_LOG] OnRunCompleted event invoked for FAILED run.");
                }
                catch (Exception ex)
                {
                    Debug.LogError($"[OAIWR_LOG] Exception during OnRunCompleted (failed run) invocation: {ex.Message}\n{ex.StackTrace}");
                }
            }

        } // using (UnityWebRequest webRequest = ...) vége

        Debug.LogWarning($"[OAIWR DEBUG] CreateAssistantRun Coroutine ENDED (after using block). streamEndedSuccessfully={streamEndedSuccessfully}");
    }

    public IEnumerator SendAudioToWhisper(byte[] audioData, Action<string> onCompleted)
    {
        if (string.IsNullOrEmpty(apiKey) || apiKey == "SK-xxxxxxxxxxxxxxxxxxxx")
        {
            Debug.LogError("OpenAI API Key is not set in OpenAIWebRequest Inspector!");
            onCompleted?.Invoke(null);
            yield break;
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


        // --- UnityWebRequest létrehozása és konfigurálása ---
        UnityWebRequest request = UnityWebRequest.Post(WhisperApiUrl, formData);

        // API Kulcs hozzáadása a Headerhez
        request.SetRequestHeader("Authorization", $"Bearer {apiKey}");


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
