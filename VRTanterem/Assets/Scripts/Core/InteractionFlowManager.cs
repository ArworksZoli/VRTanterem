using UnityEngine;
using System;
using System.Collections;
using TMPro;
using UnityEngine.UI;
using System.Net;
using UnityEngine.Networking;
using System.Collections.Generic;
// using UnityEngine.InputSystem; // Ha az inputot is itt kezeled

public class InteractionFlowManager : MonoBehaviour
{
    // --- Singleton Minta ---
    public static InteractionFlowManager Instance { get; private set; }

    // --- Állapot Definíció ---
    public enum InteractionState
    {
        Idle,
        Lecturing,
        QuestionPending,
        WaitingForUserInput, // Prompt elhangzása után, Beszéd gomb aktív
        ProcessingUserInput, // Beszéd gomb lenyomva/felengedve, Whisper dolgozik
        AnsweringQuestion,   // AI válasza érkezik/TTS olvassa
        ResumingLecture
    }

    [Header("State (Read Only)")]
    [SerializeField]
    private InteractionState currentState = InteractionState.Idle;
    public InteractionState CurrentState => currentState;

    private bool userHasRequestedQuestion = false;
    private int lastPlayedLectureSentenceIndex = -1;
    private bool waitingForLectureStartConfirmation = false;
    private bool isOaiRunComplete = true;

    // Kvíz változók
    private string lastCompleteUtteranceFromAI = string.Empty;
    private bool expectingQuizAnswer = false;
    private string currentQuizQuestionText = string.Empty;

    [Header("Core Component References")]
    [SerializeField] private OpenAIWebRequest openAIWebRequest;
    [SerializeField] private TextToSpeechManager textToSpeechManager;
    [SerializeField] private WhisperMicController whisperMicController;

    [Header("UI Elements")]
    [SerializeField] private GameObject questionIndicatorUI;
    [SerializeField] private TMP_Text TMPUserText;
    [SerializeField] private Button raiseHandButton;

    // --- Input Action (Opcionális, ha itt kezeled) ---
    // [Header("Input Settings")]
    // [SerializeField] private InputActionAsset inputActions;
    // [SerializeField] private string actionMapName = "Default"; // Vagy a releváns map
    // [SerializeField] private string raiseHandActionName = "RaiseHand";
    // private InputAction raiseHandAction;

    // --- Unity Életciklus Metódusok ---

    void Awake()
    {
        // Singleton
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;

        // Referencia ellenőrzések (változatlan)
        if (openAIWebRequest == null || textToSpeechManager == null || whisperMicController == null)
        { Debug.LogError($"[IFM] Core component references missing on {gameObject.name}! Disabling.", this); enabled = false; return; }
        if (questionIndicatorUI == null)
        { Debug.LogWarning($"[IFM] Question Indicator UI not assigned on {gameObject.name}.", this); }

        Debug.Log("[IFM] Awake completed.");
    }

    void Start()
    {
        // <<< ÚJ LOG (opcionális, de hasznos lehet) >>>
        Debug.LogWarning($"[InteractionFlowManager] Start BEGIN - Frame: {Time.frameCount}");

        if (questionIndicatorUI != null) questionIndicatorUI.SetActive(false);

        Debug.Log("[IFM] Start completed. Waiting for InitializeInteraction call.");

        // <<< ÚJ LOG (opcionális) >>>
        Debug.LogWarning($"[InteractionFlowManager] Start END - Frame: {Time.frameCount}");
    }

    void OnEnable()
    {
        Debug.LogWarning($"[IFM OnEnable] Checking openAIWebRequest reference before subscribing. Is null? {openAIWebRequest == null}");
        if (openAIWebRequest != null)
        {
            openAIWebRequest.OnRunCompleted -= HandleRunCompleted; // Először leiratkozás (biztonsági)
            openAIWebRequest.OnRunCompleted += HandleRunCompleted;
            Debug.Log("[IFM] Subscribed to OpenAIWebRequest.OnRunCompleted event.");
        }
        else
        {
            Debug.LogError("[IFM] Cannot subscribe to OnRunCompleted: openAIWebRequest reference is null!");
        }
    }

    void OnDisable()
    {
        // Eseményleiratkozások (fontos!)
        if (textToSpeechManager != null)
        {
            textToSpeechManager.OnTTSPlaybackEnd -= HandleTTSPlaybackEnd;
            textToSpeechManager.OnPlaybackQueueCompleted -= HandlePlaybackQueueCompleted;
        }

        if (openAIWebRequest != null)
        {
            openAIWebRequest.OnRunCompleted -= HandleRunCompleted;
            Debug.Log("[IFM] Unsubscribed from OpenAIWebRequest.OnRunCompleted event.");
        }
        Debug.Log("[IFM] OnDisable finished.");
    }

    void OnDestroy()
    {
        // Dupla biztonság a leiratkozásra
        if (textToSpeechManager != null)
        {
            textToSpeechManager.OnTTSPlaybackEnd -= HandleTTSPlaybackEnd;
            textToSpeechManager.OnPlaybackQueueCompleted -= HandlePlaybackQueueCompleted;
        }
        Debug.Log("[IFM] OnDestroy: Unsubscribed from events.");
    }

    public void InitializeInteraction()
    {
        Debug.Log("[IFM] InitializeInteraction called.");
        if (!enabled) { Debug.LogError("[IFM] Cannot initialize, component is disabled!"); return; }

        isOaiRunComplete = true;
        lastCompleteUtteranceFromAI = string.Empty;
        expectingQuizAnswer = false;
        currentQuizQuestionText = string.Empty;

        SetState(InteractionState.Lecturing);
        userHasRequestedQuestion = false;
        lastPlayedLectureSentenceIndex = -1;
        waitingForLectureStartConfirmation = false;
        if (questionIndicatorUI != null) questionIndicatorUI.SetActive(false);

        // Eseményfeliratkozások
        if (textToSpeechManager != null)
        {
            textToSpeechManager.OnTTSPlaybackEnd -= HandleTTSPlaybackEnd;
            textToSpeechManager.OnTTSPlaybackEnd += HandleTTSPlaybackEnd;

            textToSpeechManager.OnPlaybackQueueCompleted -= HandlePlaybackQueueCompleted;
            textToSpeechManager.OnPlaybackQueueCompleted += HandlePlaybackQueueCompleted;

            Debug.Log("[IFM] Subscribed to TextToSpeechManager events (End, QueueCompleted).");
        }

        // Biztosítjuk, hogy a beszéd gomb le van tiltva az elején
        whisperMicController?.DisableSpeakButton();
        // A jelentkezés gombot engedélyezzük (ha itt kezelnénk)
        // raiseHandAction?.Enable();

        Debug.Log("[IFM] Initialization complete. Current state: Lecturing.");
    }

    public void HandleInitializationFailed(string errorMessage)
    {
        Debug.LogError($"[IFM] OpenAI Initialization Failed: {errorMessage}. Setting state to Idle.");
        TranscriptLogger.Instance?.AddEntry("System", $"Error: Initialization failed. {errorMessage}");
        // UI-on is jelezhetünk hibát
        // if (TMP_ErrorText != null) TMP_ErrorText.text = "Initialization Error. Please check logs.";

        SetState(InteractionState.Idle); // Visszaállunk egy biztonságos állapotba
        // Esetleg letilthatjuk a további interakciót lehetővé tévő gombokat
        whisperMicController?.DisableSpeakButton();
        DisableRaiseHandButtonUI();
    }

    // Ezt hívja az Input Listener (pl. UI gomb, kézfelemelés trigger, vagy az OnRaiseHandStarted)
    public void UserRequestsToAskQuestion()
    {
        Debug.Log($"[IFM] UserRequestsToAskQuestion called. Current state: {currentState}");
        if (currentState == InteractionState.Lecturing && !userHasRequestedQuestion)
        {
            userHasRequestedQuestion = true;
            if (questionIndicatorUI != null) questionIndicatorUI.SetActive(true);
            SetState(InteractionState.QuestionPending);
            // A jelentkezés gombot itt le lehet tiltani, hogy ne lehessen spamelni
            // raiseHandAction?.Disable();
        }
        else { /* Logolás, hogy miért ignoráljuk */ }
    }

    // Ezt hívja a WhisperMicController
    public void HandleUserQuestionReceived(string transcription)
    {
        Debug.LogWarning($"[IFM_LOG] >>> HandleUserQuestionReceived ENTER. State: {currentState}, ExpectingQuiz: {expectingQuizAnswer}, WaitingLectureStart: {waitingForLectureStartConfirmation}, Text: '{transcription}'");

        // --- Kezdeti ellenőrzések ---
        if (string.IsNullOrEmpty(transcription))
        {
            Debug.LogError("[IFM] Received empty transcription. Ignoring.");
            // Fontos lehet itt visszaváltani WaitingForUserInput-ba és újra engedélyezni a mikrofont,
            // ha a felhasználó véletlenül nem mondott semmit.
            if (currentState == InteractionState.ProcessingUserInput) // Ha már váltottunk, de nem jött szöveg
            {
                SetState(InteractionState.WaitingForUserInput);
                StartCoroutine(EnableSpeakButtonAfterDelay(0.1f)); // Gyors újraengedélyezés
            }
            return;
        }
        if (currentState != InteractionState.WaitingForUserInput)
        {
            Debug.LogWarning($"[IFM] Received transcription but state is not WaitingForUserInput ({currentState}). Ignoring.");
            return;
        }

        // --- UI és Logolás ---
        if (TMPUserText != null) TMPUserText.text = "User (Voice): " + transcription;
        TranscriptLogger.Instance?.AddEntry("User", transcription);

        // --- Állapotváltás és Mikrofon Tiltása ---
        SetState(InteractionState.ProcessingUserInput); // Váltunk, mert elkezdjük feldolgozni
        whisperMicController?.DisableSpeakButton();     // Mikrofon tiltása a feldolgozás alatt

        // --- DÖNTÉSI LOGIKA ---

        // 1. Elsődlegesen ellenőrizzük, hogy kvízválaszra vártunk-e
        if (expectingQuizAnswer)
        {
            Debug.LogWarning($"[IFM_LOG] --- Branch: Quiz Answer Handling --- User answered: '{transcription}' to quiz: '{currentQuizQuestionText}'");
            if (openAIWebRequest != null)
            {
                if (string.IsNullOrEmpty(currentQuizQuestionText))
                {
                    Debug.LogError("[IFM_LOG] CRITICAL: Expecting quiz answer, but currentQuizQuestionText is empty! Cannot send to OAI. Aborting quiz flow.");
                    // Hiba történt, valószínűleg a kvízkérdés nem lett megfelelően elmentve.
                    // Visszaállunk egy biztonságos állapotba.
                    expectingQuizAnswer = false; // Reset flag
                    SetState(InteractionState.Idle); // Vagy Lecturing, ha az biztonságosabb
                                                     // Érdemes lehet itt egy hibaüzenetet is mondatni a felhasználónak.
                }
                else
                {
                    isOaiRunComplete = false; // Új OAI run indul, még nem fejeződött be
                    Debug.LogWarning($"[IFM_LOG] Calling OAIWR.SendQuizAnswerAndContinueLecture with Question: '{currentQuizQuestionText}', Answer: '{transcription}'");
                    openAIWebRequest.SendQuizAnswerAndContinueLecture(currentQuizQuestionText, transcription);
                    Debug.LogWarning("[IFM_LOG] OAIWR.SendQuizAnswerAndContinueLecture() called.");
                }
            }
            else
            {
                Debug.LogError("[IFM] Cannot send quiz answer: OpenAIWebRequest reference is null!");
                SetState(InteractionState.Idle);
            }
            // A kvízválasz elküldése után reseteljük a kvíz állapotjelzőket
            expectingQuizAnswer = false;
            currentQuizQuestionText = string.Empty; // Fontos, hogy csak a sikeres küldés után, vagy ha hiba van és megszakítjuk
        }
        // 2. Ha nem kvízválaszra vártunk, ellenőrizzük a kezdeti előadás megerősítését
        else if (waitingForLectureStartConfirmation)
        {
            Debug.LogWarning("[IFM_LOG] --- Branch: Lecture Start Confirmation ---");
            waitingForLectureStartConfirmation = false; // Csak egyszer használjuk ezt a flaget
                                                        // Debug.Log($"[IFM_LOG] Flag 'waitingForLectureStartConfirmation' set to false."); // Már a régi kódban is volt

            if (openAIWebRequest != null)
            {
                Debug.LogWarning($"[IFM_LOG] Calling OAIWR.AddUserMessageAndStartLectureRun('{transcription}') for initial lecture start.");
                isOaiRunComplete = false;
                StartCoroutine(openAIWebRequest.AddUserMessageAndStartLectureRun(transcription));
                Debug.LogWarning("[IFM_LOG] OAIWR.AddUserMessageAndStartLectureRun() coroutine started.");
            }
            else
            {
                Debug.LogError("[IFM] Cannot start main lecture: OpenAIWebRequest reference is null!");
                SetState(InteractionState.Idle);
            }
        }
        // 3. Ha sem kvízválasz, sem kezdeti megerősítés nem volt, akkor ez egy általános kérdés vagy "nincs kérdésem"
        else
        {
            Debug.LogWarning("[IFM_LOG] --- Branch: General Question Handling (Not Quiz, Not Initial Confirmation) ---");
            string lowerTranscription = transcription.ToLowerInvariant();
            bool wantsToContinue = false;
            LanguageConfig currentLang = AppStateManager.Instance?.CurrentLanguage;
            string langCode = currentLang?.languageCode?.ToLowerInvariant() ?? "en"; // Alapértelmezett angol, ha nincs nyelv

            // Általános, nyelvfüggetlen(ebb) kulcsszavak
            string[] generalContinueKeywords = {
                "oké", "értem", "rendben", "mehet", "tovább", "folytas", // "folytasd", "folytathatod"
                "okay", "ok", "i understand", "got it", "go on", "proceed", "continue"
            };

            // Nyelvspecifikus "nincs kérdésem" / "nem" típusú válaszok
            string[] noQuestionKeywords;
            if (langCode == "hu")
            {
                noQuestionKeywords = new string[] { "nincs", "nem kérdezek", "nem", "semmi" };
            }
            else // Alapértelmezés (angol és egyéb)
            {
                noQuestionKeywords = new string[] { "no", "nope", "nothing", "i don't have any", "no questions" };
            }

            foreach (string keyword in generalContinueKeywords)
            {
                if (lowerTranscription.Contains(keyword))
                {
                    wantsToContinue = true;
                    break;
                }
            }

            if (!wantsToContinue) // Csak akkor ellenőrizzük a "no question" kulcsszavakat, ha az általánosak nem illeszkedtek
            {
                foreach (string keyword in noQuestionKeywords)
                {
                    // Itt lehetünk szigorúbbak, pl. a "no" önmagában csak akkor legyen folytatás,
                    // ha a kontextus (AI kérdése) egyértelműen erre utal.
                    // Egy egyszerű "no." válasz esetén a Contains(keyword) megfelelő.
                    if (lowerTranscription.Contains(keyword))
                    {
                        // Speciális eset: ha a felhasználó csak annyit mond "no", és az AI előtte kérdezte, hogy van-e kérdés,
                        // akkor ez egyértelműen a folytatásra utal.
                        // Ha az AI nem kérdezett, egy sima "no" lehet másra válasz.
                        // Jelenlegi kontextusban (AI kérdése után) ez rendben van.
                        wantsToContinue = true;
                        break;
                    }
                }
            }

            if (wantsToContinue)
            {
                Debug.LogWarning($"[IFM_LOG] User indicated no further questions or wants to continue ('{transcription}'). Resuming lecture.");
                if (openAIWebRequest != null)
                {
                    Debug.LogWarning($"[IFM_LOG] Calling OAIWR.AddUserMessageAndStartLectureRun('{transcription}') to acknowledge user and continue lecture.");
                    isOaiRunComplete = false;
                    StartCoroutine(openAIWebRequest.AddUserMessageAndStartLectureRun(transcription));
                    Debug.LogWarning("[IFM_LOG] OAIWR.AddUserMessageAndStartLectureRun() coroutine started for lecture continuation.");
                }
                else
                {
                    Debug.LogError("[IFM] Cannot resume lecture: OpenAIWebRequest reference is null!");
                    SetState(InteractionState.Idle);
                }
            }
            else // A felhasználó valószínűleg kérdést tett fel
            {
                Debug.LogWarning($"[IFM_LOG] User asked a question OR gave an ambiguous short answer ('{transcription}'). Sending to OpenAI for a short answer.");
                if (openAIWebRequest != null)
                {
                    string followUpPromptForInterruption = AppStateManager.Instance?.CurrentLanguage?.FollowUpQuestionPrompt ?? "Answer the user's question directly and briefly, then stop.";

                    Debug.LogWarning($"[IFM_LOG] Calling OAIWR.SendUserQuestionDuringLecture('{transcription}', '{followUpPromptForInterruption}')...");
                    isOaiRunComplete = false;
                    openAIWebRequest.SendUserQuestionDuringLecture(transcription, followUpPromptForInterruption);
                    Debug.LogWarning("[IFM_LOG] OAIWR.SendUserQuestionDuringLecture() called for interruption.");
                }
                else
                {
                    Debug.LogError("[IFM] Cannot send user question: OpenAIWebRequest reference is null!");
                    SetState(InteractionState.Idle);
                }
            }
        }
        Debug.LogWarning($"[IFM_LOG] <<< HandleUserQuestionReceived EXIT. ExpectingQuiz: {expectingQuizAnswer}, WaitingLectureStart: {waitingForLectureStartConfirmation}");
    }

    // Ezt hívja az OpenAIWebRequest, amikor a VÁLASZ streamje elkezdődik
    public void HandleAIAnswerStreamStart()
    {
        Debug.LogWarning($"[IFM_LOG] >>> HandleAIAnswerStreamStart ENTER. State: {currentState}");
        Debug.LogWarning($"[IFM] HandleAIAnswerStreamStart called. Current state: {currentState}");

        // Csak akkor váltunk, ha épp a feldolgozásra vártunk
        if (currentState == InteractionState.ProcessingUserInput)
        {
            SetState(InteractionState.AnsweringQuestion);

            // Nem kell külön kezelni a SentenceHighlighter-t, 
            // mert az OpenAIWebRequest már továbbítja a delta-kat
        }
        else
        {
            Debug.LogWarning($"[IFM] HandleAIAnswerStreamStart called in unexpected state: {currentState}. Ignoring state change.");
        }
        Debug.LogWarning($"[IFM_LOG] <<< HandleAIAnswerStreamStart EXIT.");
    }

    public void HandleLectureStreamStart()
    {
        Debug.LogWarning($"[IFM_LOG] >>> HandleLectureStreamStart ENTER. State: {currentState}");
        Debug.LogWarning($"[IFM] HandleLectureStreamStart called. Current state: {currentState}");

        // Akkor váltunk Lecturing-re, ha épp a feldolgozásra vártunk
        // (miután a user megerősítette, hogy kezdődhet az előadás a HandleUserQuestionReceived-ben)
        if (currentState == InteractionState.ProcessingUserInput)
        {
            Debug.Log("[IFM] Lecture stream started. Setting state to Lecturing.");
            // Átváltunk Lecturing állapotba.
            // A SetState metódus gondoskodik a megfelelő gombok (RaiseHand: on, Mic: off) beállításáról.
            SetState(InteractionState.Lecturing);
        }
        else
        {
            // Ha valamiért más állapotban hívódik meg, azt logoljuk, de nem váltunk állapotot.
            Debug.LogWarning($"[IFM] HandleLectureStreamStart called in unexpected state: {currentState}. Ignoring state change.");
        }
        Debug.LogWarning($"[IFM_LOG] <<< HandleLectureStreamStart EXIT.");
    }

    public void HandleInitialPromptCompleted()
    {
        Debug.LogWarning($"[IFM_LOG] >>> HandleInitialPromptCompleted ENTER. Current state: {currentState}");

        if (currentState == InteractionState.Lecturing || currentState == InteractionState.ProcessingUserInput || currentState == InteractionState.Idle)
        {
            if (currentState == InteractionState.Idle)
            {
                Debug.LogWarning("[IFM_LOG] HandleInitialPromptCompleted called while Idle. Correcting state.");
            }

            Debug.LogWarning("[IFM_LOG] Initial prompt stream completed. Setting flag and state for user input.");

            waitingForLectureStartConfirmation = true; // Most várjuk a user válaszát az első kérdésre
            Debug.LogWarning($"[IFM_LOG] Set waitingForLectureStartConfirmation = {waitingForLectureStartConfirmation}");

            SetState(InteractionState.WaitingForUserInput); // Váltunk állapotot

            // Engedélyezzük a mikrofont kis késleltetéssel
            Debug.LogWarning("[IFM_LOG] Starting EnableSpeakButtonAfterDelay coroutine from HandleInitialPromptCompleted..."); // <<< Módosított log >>>
            StartCoroutine(EnableSpeakButtonAfterDelay(0.3f));
        }
        else
        {
            Debug.LogWarning($"[IFM_LOG] HandleInitialPromptCompleted called in unexpected state: {currentState}. Ignoring.");
        }
        Debug.LogWarning($"[IFM_LOG] <<< HandleInitialPromptCompleted EXIT. Flag value: {waitingForLectureStartConfirmation}");
    }

    private void HandleRunCompleted()
    {
        Debug.LogWarning($"[IFM_LOG] >>> HandleRunCompleted (OAIWR Event Received). Current state: {currentState}");
        isOaiRunComplete = true;
        
        Debug.LogWarning($"[IFM_LOG] Set isOaiRunComplete = {isOaiRunComplete}");
        Debug.LogWarning($"[IFM_LOG] <<< HandleRunCompleted EXIT.");
    }

    // TTS Eseménykezelő
    private void HandleTTSPlaybackEnd(int finishedSentenceIndex)
    {
        Debug.Log($"[IFM] HandleTTSPlaybackEnd received for index {finishedSentenceIndex}. Current state: {currentState}");

        switch (currentState)
        {
            case InteractionState.Lecturing:
                lastPlayedLectureSentenceIndex = finishedSentenceIndex;
                break;

            case InteractionState.QuestionPending:
                lastPlayedLectureSentenceIndex = finishedSentenceIndex;
                userHasRequestedQuestion = false;
                if (questionIndicatorUI != null) questionIndicatorUI.SetActive(false);

                SetState(InteractionState.WaitingForUserInput);

                if (textToSpeechManager != null)
                {
                    textToSpeechManager.PausePlayback();
                    Debug.Log("[IFM] Requesting TTS to speak prompt.");

                    string promptText = "What is your question?"; // Alapértelmezett angol, ha valami hiba van

                    // Próbáljuk meg lekérni a lokalizált szöveget
                    if (AppStateManager.Instance != null && AppStateManager.Instance.CurrentLanguage != null)
                    {
                        if (!string.IsNullOrEmpty(AppStateManager.Instance.CurrentLanguage.AskQuestionPrompt))
                        {
                            promptText = AppStateManager.Instance.CurrentLanguage.AskQuestionPrompt;
                            Debug.Log($"[IFM] Using localized prompt for language '{AppStateManager.Instance.CurrentLanguage.name}': '{promptText}'"); // Tegyük fel, hogy van 'name' property a LanguageConfig-ban
                        }
                        else
                        {
                            Debug.LogWarning($"[IFM] AskQuestionPrompt is empty for the current language '{AppStateManager.Instance.CurrentLanguage.name}'. Using default.");
                        }
                    }
                    else
                    {
                        Debug.LogError("[IFM] Cannot get localized prompt: AppStateManager or CurrentLanguage is null. Using default.");
                    }

                    // A SpeakSingleSentence korutinja a végén engedélyezi a gombot
                    textToSpeechManager.SpeakSingleSentence(promptText); // A dinamikusan betöltött szöveget használjuk
                }
                else
                {
                    Debug.LogError("[IFM] Cannot speak prompt: textToSpeechManager is null!");
                    SetState(InteractionState.Idle); // Meglévő hibakezelés
                }
                break;

            case InteractionState.WaitingForUserInput:
                // Ez akkor fut le, ha a "Mi a kérdésed?" prompt lejátszása befejeződött.
                // A SpeakSingleSentence korutinja már meghívta az EnableSpeakButton-t,
                // így itt nincs teendőnk, csak logolhatunk.
                Debug.Log("[IFM] Prompt playback finished (handled by SpeakSingleSentenceCoroutine). Speak button should be enabled.");
                break;

            case InteractionState.AnsweringQuestion:
                // Az AI válaszának lejátszása fejeződött be.
                Debug.Log("[IFM] AI answer playback finished. Resuming lecture.");
                SetState(InteractionState.ResumingLecture);

                if (textToSpeechManager != null)
                {
                    textToSpeechManager.ResumePlayback(lastPlayedLectureSentenceIndex + 1);
                    // Miután elindult a folytatás, visszaválthatunk Lecturing-re
                    // és újra engedélyezhetjük a jelentkezést.
                    SetState(InteractionState.Lecturing);
                    // raiseHandAction?.Enable(); // Jelentkezés gomb újra aktív
                }
                else { /* Hibakezelés */ SetState(InteractionState.Idle); }
                break;

            default:
                Debug.LogWarning($"[IFM] HandleTTSPlaybackEnd called in unexpected state: {currentState}. Ignoring.");
                break;
        }
    }

    private void HandlePlaybackQueueCompleted()
    {
        Debug.LogWarning($"[IFM_LOG] >>> HandlePlaybackQueueCompleted ENTER. Current state: {currentState}, LastPlayedIndex: {lastPlayedLectureSentenceIndex}, isOaiRunComplete: {isOaiRunComplete}");

        if (!isOaiRunComplete)
        {
            Debug.LogWarning("[IFM_LOG] Playback queue empty, but AI run is NOT YET marked as complete. Waiting for OnRunCompleted event before analyzing AI utterance.");
            Debug.LogWarning($"[IFM_LOG] <<< HandlePlaybackQueueCompleted EXIT (Waiting for OAI Run to complete).");
            return;
        }

        Debug.LogWarning("[IFM_LOG] OAI Run is complete. Proceeding to analyze AI's last utterance.");

        if (openAIWebRequest != null)
        {
            lastCompleteUtteranceFromAI = openAIWebRequest.GetLastFullResponse();
            if (string.IsNullOrEmpty(lastCompleteUtteranceFromAI))
            {
                lastCompleteUtteranceFromAI = TranscriptLogger.Instance?.GetLastAIEntryText() ?? string.Empty;
                // Logolás a fallbackről, de a lastCompleteUtteranceFromAI a teljes szöveget tartalmazza
                Debug.LogWarning($"[IFM_LOG] OAIWR GetLastFullResponse was empty. Fallback to TranscriptLogger. Full text for analysis: '{lastCompleteUtteranceFromAI}'");
            }
            else
            {
                // Logolás, de a lastCompleteUtteranceFromAI a teljes szöveget tartalmazza
                Debug.LogWarning($"[IFM_LOG] Last utterance from AI (via OAIWR). Full text for analysis: '{lastCompleteUtteranceFromAI}'");
            }
        }
        else
        {
            lastCompleteUtteranceFromAI = string.Empty;
            Debug.LogError("[IFM_LOG] OpenAIWebRequest is null, cannot get last AI utterance.");
        }

        if ((currentState == InteractionState.Lecturing || currentState == InteractionState.AnsweringQuestion) && !string.IsNullOrEmpty(lastCompleteUtteranceFromAI))
        {
            // <<< KVÍZKÉRDÉS DETEKTÁLÁSA >>>
            bool isQuiz = false;
            string lowerUtterance = lastCompleteUtteranceFromAI.ToLowerInvariant();
            Debug.LogWarning($"[IFM_LOG] Analyzing for quiz (full lowercased text): '{lowerUtterance}'");

            // --- KULCSSZAVAK BETÖLTÉSE A LANGUAGECONFIG-BÓL ---
            LanguageConfig currentLang = AppStateManager.Instance?.CurrentLanguage;
            List<string> explicitQuizIntroducersList = new List<string>();
            List<string> generalQuestionPromptsList = new List<string>();

            if (currentLang != null)
            {
                if (currentLang.ExplicitQuizIntroducers != null && currentLang.ExplicitQuizIntroducers.Count > 0)
                {
                    explicitQuizIntroducersList.AddRange(currentLang.ExplicitQuizIntroducers);
                    Debug.Log($"[IFM_LOG] Loaded {explicitQuizIntroducersList.Count} explicit quiz introducers from LanguageConfig '{currentLang.displayName}'.");
                }
                else
                {
                    Debug.LogWarning($"[IFM_LOG] No ExplicitQuizIntroducers found in LanguageConfig '{currentLang.displayName}'. Using empty list.");
                }

                if (currentLang.GeneralQuestionPrompts != null && currentLang.GeneralQuestionPrompts.Count > 0)
                {
                    generalQuestionPromptsList.AddRange(currentLang.GeneralQuestionPrompts);
                    Debug.Log($"[IFM_LOG] Loaded {generalQuestionPromptsList.Count} general question prompts from LanguageConfig '{currentLang.displayName}'.");
                }
                else
                {
                    Debug.LogWarning($"[IFM_LOG] No GeneralQuestionPrompts found in LanguageConfig '{currentLang.displayName}'. Using empty list.");
                }
            }
            else
            {
                Debug.LogError("[IFM_LOG] CurrentLanguage is null in AppStateManager! Cannot load quiz/general prompts. Quiz detection might be unreliable.");
            }

            foreach (string introducer in explicitQuizIntroducersList)
            {
                // Fontos: Ha a LanguageConfig-ban nem kisbetűvel vannak a kulcsszavak, itt kell ToLowerInvariant()-t használni rajtuk is.
                // De a LanguageConfig tooltipje azt javasolta, hogy ott már kisbetűsek legyenek.
                if (lowerUtterance.Contains(introducer.ToLowerInvariant())) // Biztonság kedvéért itt is ToLowerInvariant
                {
                    isQuiz = true;
                    Debug.LogWarning($"[IFM_LOG] Detected EXPLICIT QUIZ introducer: '{introducer}'");
                    break;
                }
            }

            if (!isQuiz && lowerUtterance.Trim().EndsWith("?"))
            {
                bool isGeneralPrompt = false;
                foreach (string generalPrompt in generalQuestionPromptsList)
                {
                    if (lowerUtterance.Contains(generalPrompt.ToLowerInvariant())) // Biztonság kedvéért itt is ToLowerInvariant
                    {
                        isGeneralPrompt = true;
                        Debug.LogWarning($"[IFM_LOG] Ends with '?', but contains general prompt exclusion: '{generalPrompt}'");
                        break;
                    }
                }
                if (!isGeneralPrompt)
                {
                    isQuiz = true;
                    Debug.LogWarning($"[IFM_LOG] Detected as POTENTIAL QUIZ (ends with '?' and not a general prompt). This detection might need refinement.");
                }
            }
            // <<< KVÍZKÉRDÉS DETEKTÁLÁSA VÉGE >>>

            if (isQuiz)
            {
                Debug.LogWarning($"[IFM_LOG] AI asked a QUIZ QUESTION: '{lastCompleteUtteranceFromAI}'. Setting up for quiz answer.");
                expectingQuizAnswer = true;
                currentQuizQuestionText = lastCompleteUtteranceFromAI;
                SetState(InteractionState.WaitingForUserInput);
                StartCoroutine(EnableSpeakButtonAfterDelay(0.3f));
            }
            else
            {
                Debug.LogWarning($"[IFM_LOG] AI finished speaking (NOT a quiz question based on analysis). Assuming natural pause for general questions. Utterance: '{lastCompleteUtteranceFromAI.Substring(0, Math.Min(lastCompleteUtteranceFromAI.Length, 100))}'");
                expectingQuizAnswer = false;
                currentQuizQuestionText = string.Empty;
                SetState(InteractionState.WaitingForUserInput);
                StartCoroutine(EnableSpeakButtonAfterDelay(0.3f));
            }
        }
        else if (string.IsNullOrEmpty(lastCompleteUtteranceFromAI) && currentState == InteractionState.Lecturing)
        {
            Debug.LogWarning("[IFM_LOG] Playback queue empty, OAI run complete, but AI utterance was empty. Moving to WaitingForUserInput as a fallback.");
            expectingQuizAnswer = false;
            currentQuizQuestionText = string.Empty;
            SetState(InteractionState.WaitingForUserInput);
            StartCoroutine(EnableSpeakButtonAfterDelay(0.3f));
        }
        else
        {
            Debug.LogWarning($"[IFM_LOG] Playback queue completed in state {currentState} or AI utterance was empty. No specific quiz/question analysis triggered.");
        }

        openAIWebRequest?.ClearLastFullResponse();

        Debug.LogWarning($"[IFM_LOG] <<< HandlePlaybackQueueCompleted EXIT. ExpectingQuizAnswer: {expectingQuizAnswer}");
    }

    /// <summary>
    /// Called by TextToSpeechManager when the playback of the AI's answer
    /// (on the promptAudioSource) has completed.
    /// Transitions the state to resume the lecture.
    /// </summary>
    public void HandleAnswerPlaybackCompleted()
    {
        Debug.LogWarning($"[IFM_LOG] >>> HandleAnswerPlaybackCompleted ENTER. Current state: {currentState}");

        if (currentState == InteractionState.AnsweringQuestion)
        {
            Debug.LogWarning("[IFM_LOG] AI's short answer playback (on prompt channel) finished.");
            // Mivel ez egy rövid válasz volt egy megszakításra, az AI-nak most folytatnia kellene az előadást
            // onnan, ahol abbahagyta, vagy a következő logikai ponttól.
            // Az OpenAIWebRequest.StartMainLectureRun() ezt a célt szolgálja.
            if (openAIWebRequest != null)
            {
                Debug.LogWarning("[IFM_LOG] Requesting OAIWR.StartMainLectureRun() to resume lecture after short answer.");
                isOaiRunComplete = false;
                openAIWebRequest.StartMainLectureRun(); // Ez AssistantRunType.MainLecture-t fog használni

                // Az állapotot ProcessingUserInput-ra állítjuk, amíg az AI el nem kezdi a lecture stream-et.
                // Amikor a lecture stream elindul, a HandleLectureStreamStart() majd Lecturing-re vált.
                SetState(InteractionState.ProcessingUserInput);
                Debug.LogWarning("[IFM_LOG] OAIWR.StartMainLectureRun() called, state set to ProcessingUserInput, waiting for lecture stream.");
            }
            else
            {
                Debug.LogError("[IFM] Cannot resume lecture after answer: OpenAIWebRequest reference is null!");
                SetState(InteractionState.Idle);
            }
        }
        else
        {
            Debug.LogWarning($"[IFM] HandleAnswerPlaybackCompleted called in unexpected state: {currentState}. Ignoring.");
        }
        Debug.LogWarning($"[IFM_LOG] <<< HandleAnswerPlaybackCompleted EXIT.");
    }


    private IEnumerator EnableSpeakButtonAfterDelay(float delay)
    {
        // <<< Log a korutin elején >>>
        Debug.LogWarning($"[IFM_LOG] >>> EnableSpeakButtonAfterDelay ENTER. Waiting {delay} seconds...");
        yield return new WaitForSeconds(delay);

        // <<< Log közvetlenül a hívás előtt >>>
        Debug.LogWarning("[IFM_LOG] Delay finished. Attempting to call whisperMicController.EnableSpeakButton()...");
        try
        {
            whisperMicController?.EnableSpeakButton();
            // <<< Log a sikeres hívás után (ha nem volt null) >>>
            if (whisperMicController != null)
            {
                Debug.LogWarning("[IFM_LOG] whisperMicController.EnableSpeakButton() called successfully.");
            }
            else
            {
                Debug.LogError("[IFM_LOG] whisperMicController reference was NULL! Cannot enable speak button.");
            }
        }
        catch (Exception ex)
        {
            // <<< Log hiba esetén >>>
            Debug.LogError($"[IFM_LOG] Exception during EnableSpeakButton(): {ex.Message}\n{ex.StackTrace}");
        }
        Debug.LogWarning($"[IFM_LOG] <<< EnableSpeakButtonAfterDelay EXIT."); // <<< Log a korutin végén >>>
    }

    private void EnableRaiseHandButtonUI() // <<< HOZZÁADVA
    {
        if (raiseHandButton != null)
        {
            raiseHandButton.interactable = true;
            // Debug.Log("[UI] Enabling Raise Hand Button");
        }
        else { Debug.LogWarning("[IFM] Raise Hand Button reference is missing in Inspector!"); }
    }

    private void DisableRaiseHandButtonUI() // <<< HOZZÁADVA
    {
        if (raiseHandButton != null)
        {
            raiseHandButton.interactable = false;
            // Debug.Log("[UI] Disabling Raise Hand Button");
        }
        else { Debug.LogWarning("[IFM] Raise Hand Button reference is missing in Inspector!"); }
    }

    // SetState változatlan
    private void SetState(InteractionState newState)
    {
        if (currentState == newState) return;
        Debug.Log($"[IFM] State Changing: {currentState} -> {newState}");
        currentState = newState;

        // --- Gombok Kezelése Állapotváltáskor ---
        switch (newState)
        {
            case InteractionState.Idle:
                DisableSpeakButton();
                DisableRaiseHandButtonUI(); // Jelentkezés tiltva
                break;

            case InteractionState.Lecturing:
                DisableSpeakButton(); // Mikrofon tiltva, amíg az AI beszél
                EnableRaiseHandButtonUI();  // <<< JELENTKEZÉS ENGEDÉLYEZVE >>>
                break;

            case InteractionState.QuestionPending:
                DisableSpeakButton();
                DisableRaiseHandButtonUI(); // Jelentkezés tiltva, amíg a promptra várunk
                break;

            case InteractionState.WaitingForUserInput:
                // A SpeakSingleSentence (prompt) végén engedélyezi a mikrofont.
                // Itt explicit nem kell hívni az EnableSpeakButton-t.
                DisableRaiseHandButtonUI(); // Jelentkezés tiltva, mikrofont várunk
                break;

            case InteractionState.ProcessingUserInput:
                DisableSpeakButton(); // Mikrofon tiltva a feldolgozás alatt
                DisableRaiseHandButtonUI(); // Jelentkezés tiltva
                break;

            case InteractionState.AnsweringQuestion:
                DisableSpeakButton(); // Mikrofon tiltva a válasz alatt
                DisableRaiseHandButtonUI(); // <<< JELENTKEZÉS TILTVA A VÁLASZ ALATT >>>
                break;

            case InteractionState.ResumingLecture:
                DisableSpeakButton(); // Mikrofon tiltva a folytatás alatt
                DisableRaiseHandButtonUI(); // Jelentkezés tiltva az átmenet alatt
                break;

            default: // Ismeretlen állapot esetén mindent letiltunk
                DisableSpeakButton();
                DisableRaiseHandButtonUI();
                break;
        }
        // --- Gombok Kezelése Vége ---
    }

    // Enable/Disable Speak Button hívások változatlanok
    // Ezek most már a WhisperMicController létező metódusait hívják
    public void EnableSpeakButton()
    {
        whisperMicController?.EnableSpeakButton();
    }

    public void DisableSpeakButton()
    {
        whisperMicController?.DisableSpeakButton();
    }
}
