using UnityEngine;
using System;
using System.Collections;
using TMPro;
using UnityEngine.UI;
using System.Net;
using UnityEngine.Networking;
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
        // <<< Logolás a metódus elején (flag értékkel) >>>
        Debug.LogWarning($"[IFM_LOG] >>> HandleUserQuestionReceived ENTER. State: {currentState}, Flag at START: {waitingForLectureStartConfirmation}, Text: '{transcription}'");

        // --- Kezdeti ellenőrzések ---
        if (string.IsNullOrEmpty(transcription))
        {
            Debug.LogError("[IFM] Received empty transcription. Ignoring.");
            return;
        }
        if (currentState != InteractionState.WaitingForUserInput)
        {
            Debug.LogWarning($"[IFM] Received transcription but state is not WaitingForUserInput ({currentState}). Ignoring.");
            return;
        }

        // --- UI és Logolás ---
        if (TMPUserText != null)
        {
            TMPUserText.text = "User (Voice): " + transcription;
        }
        TranscriptLogger.Instance?.AddEntry("User", transcription);

        // --- Állapotváltás és Mikrofon Tiltása ---
        SetState(InteractionState.ProcessingUserInput);
        whisperMicController?.DisableSpeakButton();

        // --- DÖNTÉS A FLAG ALAPJÁN ---
        Debug.LogWarning($"[IFM_LOG] Checking flag BEFORE 'if'. waitingForLectureStartConfirmation = {waitingForLectureStartConfirmation}");
        if (waitingForLectureStartConfirmation)
        {
            // --- Branch 1: Lecture Start Confirmation ---
            // (Ez az ág változatlan, az AddUserMessageAndStartLectureRun hívással)
            Debug.LogWarning("[IFM_LOG] --- Branch: Lecture Start Confirmation ---");
            waitingForLectureStartConfirmation = false;
            Debug.Log($"[IFM_LOG] Flag set to false inside branch. New value: {waitingForLectureStartConfirmation}");

            if (openAIWebRequest != null)
            {
                Debug.LogWarning($"[IFM_LOG] Calling OAIWR.AddUserMessageAndStartLectureRun('{transcription}')...");
                isOaiRunComplete = false;
                StartCoroutine(openAIWebRequest.AddUserMessageAndStartLectureRun(transcription));
                Debug.LogWarning("[IFM_LOG] OAIWR.AddUserMessageAndStartLectureRun() coroutine started.");
            }
            else
            {
                Debug.LogError("[IFM] Cannot start main lecture: OpenAIWebRequest reference is null!");
                TranscriptLogger.Instance?.AddEntry("System", "Error: Cannot start lecture, connection missing.");
                SetState(InteractionState.Idle);
            }
        }
        else
        {
            // --- Branch 2: Handling Response After System Prompt (Interruption or Follow-up) ---
            Debug.LogWarning("[IFM_LOG] --- Branch: Handling Response After System Prompt (Not Initial Confirmation) ---");
            Debug.LogWarning($"[IFM_LOG] Entered this branch because waitingForLectureStartConfirmation was {waitingForLectureStartConfirmation}");

            string lowerTranscription = transcription.ToLowerInvariant();
            bool wantsToContinue = lowerTranscription.Contains("nincs") ||
                                   lowerTranscription.Contains("nem") ||
                                   lowerTranscription.Contains("folytas") ||
                                   lowerTranscription.Contains("mehet") ||
                                   lowerTranscription.Contains("tovább") ||
                                   lowerTranscription.Contains("no") ||
                                   lowerTranscription.Contains("continue");

            if (wantsToContinue)
            {
                // Felhasználó nem kérdez többet, folytatjuk az előadást
                Debug.LogWarning("[IFM_LOG] User indicated no further questions. Resuming lecture.");
                if (openAIWebRequest != null)
                {
                    // <<< MÓDOSÍTÁS KEZDETE >>>
                    // Most már itt is az AddUserMessageAndStartLectureRun-t hívjuk,
                    // hogy a "Nincs" válasz bekerüljön a thread-be a folytatás előtt.
                    Debug.LogWarning($"[IFM_LOG] Calling OAIWR.AddUserMessageAndStartLectureRun('{transcription}') for lecture continuation...");
                    isOaiRunComplete = false;
                    StartCoroutine(openAIWebRequest.AddUserMessageAndStartLectureRun(transcription)); // <<< EZT HÍVJUK
                    Debug.LogWarning("[IFM_LOG] OAIWR.AddUserMessageAndStartLectureRun() coroutine started for lecture continuation.");
                    // <<< MÓDOSÍTÁS VÉGE >>>
                }
                else
                {
                    Debug.LogError("[IFM] Cannot resume lecture: OpenAIWebRequest reference is null!");
                    TranscriptLogger.Instance?.AddEntry("System", "Error: Cannot resume lecture, connection missing.");
                    SetState(InteractionState.Idle);
                }
            }
            else
            {
                // Felhasználó újabb kérdést tett fel (ez a rész változatlan)
                Debug.LogWarning("[IFM_LOG] User asked another question. Sending to OpenAI.");
                Debug.Log($"[IFM] Input '{transcription}' treated as a follow-up question.");

                if (openAIWebRequest != null)
                {
                    try
                    {
                        string followUpPrompt = AppStateManager.Instance?.CurrentLanguage?.FollowUpQuestionPrompt;
                        if (string.IsNullOrEmpty(followUpPrompt))
                        {
                            Debug.LogWarning("[IFM] FollowUpQuestionPrompt is missing for SendUserQuestionDuringLecture fallback! Using default.");
                            followUpPrompt = (AppStateManager.Instance?.CurrentLanguage?.languageCode == "hu") ?
                                             "Van további kérdése ezzel kapcsolatban?" :
                                             "Do you have any more questions about this?";
                        }

                        Debug.LogWarning($"[IFM_LOG] Calling OAIWR.SendUserQuestionDuringLecture('{transcription}', '{followUpPrompt}')...");
                        isOaiRunComplete = false;
                        openAIWebRequest.SendUserQuestionDuringLecture(transcription, followUpPrompt);
                        Debug.LogWarning("[IFM_LOG] OAIWR.SendUserQuestionDuringLecture() called for follow-up question.");
                    }
                    catch (Exception ex)
                    {
                        Debug.LogError($"[IFM] Exception when sending follow-up question to OpenAIWebRequest: {ex.Message}\n{ex.StackTrace}");
                        TranscriptLogger.Instance?.AddEntry("System", $"Error sending question: {ex.Message}");
                        SetState(InteractionState.Idle);
                    }
                }
                else
                {
                    Debug.LogError("[IFM] Cannot send follow-up question: OpenAIWebRequest reference is null!");
                    TranscriptLogger.Instance?.AddEntry("System", "Error: OpenAIWebRequest reference missing.");
                    SetState(InteractionState.Idle);
                }
            }
        }
        Debug.LogWarning($"[IFM_LOG] <<< HandleUserQuestionReceived EXIT. Flag value at END: {waitingForLectureStartConfirmation}");
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
        Debug.LogWarning($"[IFM_LOG] >>> HandlePlaybackQueueCompleted ENTER. Current state: {currentState}, LastPlayedIndex: {lastPlayedLectureSentenceIndex}");

        // --- MÓDOSÍTOTT LOGIKA ---
        if (currentState == InteractionState.Lecturing)
        {
            Debug.LogWarning("[IFM_LOG] Playback queue completed during LECTURING state.");

            // Ellenőrizzük, hogy az AI run is befejeződött-e MÁR
            if (isOaiRunComplete)
            {
                Debug.LogWarning("[IFM_LOG] AI Run is ALSO complete. Assuming natural pause. Switching to wait for user input.");
                SetState(InteractionState.WaitingForUserInput);
                Debug.LogWarning("[IFM_LOG] Activating microphone after natural lecture pause.");
                // Itt is használhatjuk a késleltetett engedélyezést
                StartCoroutine(EnableSpeakButtonAfterDelay(0.3f)); // Vagy más érték
            }
            else
            {
                // A lejátszási sor üres, de az AI run még nem jelzett véget.
                // Várakozunk tovább, nem váltunk állapotot.
                Debug.LogWarning("[IFM_LOG] Playback queue empty, but AI run is NOT YET marked as complete. Waiting for OnRunCompleted event.");
            }
        }
        else
        {
            // Más állapotokban a lejátszási sor kiürülése nem vált ki állapotváltást itt.
            // (Pl. a kezdeti prompt végét a HandleInitialPromptCompleted kezeli,
            // a válasz végét a HandleAnswerPlaybackCompleted)
            Debug.LogWarning($"[IFM_LOG] Playback queue completed in state {currentState}. No state change initiated by this event handler in this state.");
        }

        Debug.LogWarning($"[IFM_LOG] <<< HandlePlaybackQueueCompleted EXIT.");
    }

    /// <summary>
    /// Called by TextToSpeechManager when the playback of the AI's answer
    /// (on the promptAudioSource) has completed.
    /// Transitions the state to resume the lecture.
    /// </summary>
    public void HandleAnswerPlaybackCompleted()
    {
        Debug.LogWarning($"[IFM_LOG] >>> HandleAnswerPlaybackCompleted ENTER. Current state: {currentState}");

        // 1. Ellenőrzi, hogy tényleg a válaszadás után vagyunk-e
        if (currentState == InteractionState.AnsweringQuestion)
        {
            // 2. Logolja, hogy most automatikusan folytatjuk az előadást
            Debug.LogWarning("[IFM_LOG] AI answer playback finished. Automatically resuming lecture."); // <<< MÓDOSÍTOTT LOG

            // 3. Elindítjuk az előadás folytatását az OAIWR-en keresztül
            if (openAIWebRequest != null)
            {
                // Itt a sima StartMainLectureRun kell, mert a Q&A már lezajlott és a thread tartalmazza.
                Debug.LogWarning("[IFM_LOG] Calling OAIWR.StartMainLectureRun() to resume lecture after answer.");
                isOaiRunComplete = false; // Fontos: Új run indul, még nem fejeződött be
                openAIWebRequest.StartMainLectureRun();

                // 4. Állapot beállítása: Várunk az AI lecture streamjére
                SetState(InteractionState.ProcessingUserInput); // <<< MÓDOSÍTOTT ÁLLAPOT
                Debug.LogWarning("[IFM_LOG] OAIWR.StartMainLectureRun() called, state set to ProcessingUserInput.");
            }
            else
            {
                // Hibakezelés, ha nincs OAIWR kapcsolat
                Debug.LogError("[IFM] Cannot resume lecture after answer: OpenAIWebRequest reference is null!");
                TranscriptLogger.Instance?.AddEntry("System", "Error: Cannot resume lecture, connection missing.");
                SetState(InteractionState.Idle); // Hiba esetén Idle állapot
            }
        }
        else
        {
            // Ha nem AnsweringQuestion állapotban hívódik meg, az hiba, logoljuk.
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
