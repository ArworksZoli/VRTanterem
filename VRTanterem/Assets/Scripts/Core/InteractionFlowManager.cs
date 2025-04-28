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

        // Input Action beállítása (ha itt kezeled)
        /*
        if (inputActions != null)
        {
            var actionMap = inputActions.FindActionMap(actionMapName);
            if (actionMap != null)
            {
                raiseHandAction = actionMap.FindAction(raiseHandActionName);
                if (raiseHandAction == null) { Debug.LogError($"[IFM] Action '{raiseHandActionName}' not found in map '{actionMapName}'!"); }
            }
            else { Debug.LogError($"[IFM] Action Map '{actionMapName}' not found!"); }
        }
        else { Debug.LogError("[IFM] Input Actions asset not assigned!"); }
        */

        Debug.Log("[IFM] Awake completed.");
    }

    void Start()
    {
        // <<< ÚJ LOG (opcionális, de hasznos lehet) >>>
        Debug.LogWarning($"[InteractionFlowManager] Start BEGIN - Frame: {Time.frameCount}");

        if (questionIndicatorUI != null) questionIndicatorUI.SetActive(false);

        // --- EZT A SORT TÖRÖLD VAGY KOMMENTEZD KI ---
        // whisperMicController?.DisableSpeakButton();
        // -----------------------------------------
        // Indoklás: A WhisperMicController.Awake() már gondoskodik a kezdeti letiltásról.
        // Ennek a sornak a meghívása itt időzítési problémákat okozhat,
        // ha ez a Start() hamarabb fut le, mint a WhisperMicController.Awake() vége.

        Debug.Log("[IFM] Start completed. Waiting for InitializeInteraction call.");

        // <<< ÚJ LOG (opcionális) >>>
        Debug.LogWarning($"[InteractionFlowManager] Start END - Frame: {Time.frameCount}");
    }

    void OnEnable()
    {
        // Input Action figyelése (ha itt kezeled)
        /*
        if (raiseHandAction != null)
        {
            raiseHandAction.started += OnRaiseHandStarted;
            raiseHandAction.Enable(); // Engedélyezzük a jelentkezés gombot
            Debug.Log("[IFM] RaiseHand action listener attached and enabled.");
        }
        */
    }

    void OnDisable()
    {
        // Eseményleiratkozások (fontos!)
        if (textToSpeechManager != null)
        {
            textToSpeechManager.OnTTSPlaybackEnd -= HandleTTSPlaybackEnd;
            textToSpeechManager.OnPlaybackQueueCompleted -= HandlePlaybackQueueCompleted;
        }

        // Input Action leállítása (ha itt kezeled)
        /*
        if (raiseHandAction != null)
        {
            raiseHandAction.started -= OnRaiseHandStarted;
            raiseHandAction.Disable();
            Debug.Log("[IFM] RaiseHand action listener detached and disabled.");
        }
        */
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

        SetState(InteractionState.Lecturing);
        userHasRequestedQuestion = false;
        lastPlayedLectureSentenceIndex = -1;
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
        Debug.LogWarning($"[IFM] HandleUserQuestionReceived. Transcription: '{transcription}'. Current state: {currentState}");
        Debug.LogWarning($"[IFM_LOG] >>> HandleUserQuestionReceived ENTER. State: {currentState}, Flag: {waitingForLectureStartConfirmation}, Text: '{transcription}'");

        // --- Kezdeti ellenőrzések ---
        if (string.IsNullOrEmpty(transcription))
        {
            Debug.LogError("[IFM] Received empty transcription. Ignoring.");
            // Ha üres a szöveg, nem csinálunk semmit, az állapot marad WaitingForUserInput
            // és a mikrofon (elvileg) aktív marad. Lehet, hogy itt is le kellene tiltani?
            // Egyelőre hagyjuk így, de ez egy potenciális finomítási pont.
            return;
        }
        // Ellenőrizzük, hogy a megfelelő állapotban vagyunk-e a felhasználói input fogadására
        if (currentState != InteractionState.WaitingForUserInput && currentState != InteractionState.ProcessingUserInput)
        {
            Debug.LogWarning($"[IFM] Received transcription but state is not WaitingForUserInput or ProcessingUserInput ({currentState}). Ignoring.");
            return;
        }

        // --- UI és Logolás ---
        if (TMPUserText != null)
        {
            TMPUserText.text = "User (Voice): " + transcription;
        }
        TranscriptLogger.Instance?.AddEntry("User", transcription);

        // --- Állapotváltás és Mikrofon Tiltása (Mindig megtörténik, ha érvényes input jött) ---
        // Átmenetileg ProcessingUserInput állapotba váltunk, amíg eldöntjük, mi legyen a következő lépés.
        SetState(InteractionState.ProcessingUserInput);
        whisperMicController?.DisableSpeakButton(); // Letiltjuk a mikrofont

        // --- DÖNTÉS A FLAG ALAPJÁN ---
        if (waitingForLectureStartConfirmation)
        {
            Debug.LogWarning("[IFM_LOG] --- Branch: Lecture Start Confirmation ---");
            waitingForLectureStartConfirmation = false;
            Debug.Log("[IFM_LOG] Flag set to false.");

            // Megkérjük az OpenAIWebRequest-et, hogy indítsa el a normál lecture futtatást (isAnsweringQuestion: false)
            if (openAIWebRequest != null)
            {
                Debug.LogWarning("[IFM_LOG] Calling OAIWR.StartMainLectureRun()...");
                openAIWebRequest.StartMainLectureRun();
                Debug.LogWarning("[IFM_LOG] OAIWR.StartMainLectureRun() called.");
            }
            else // Ha nincs OAIWR referencia, nem tudunk mit tenni
            {
                Debug.LogError("[IFM] Cannot start main lecture: OpenAIWebRequest reference is null!");
                TranscriptLogger.Instance?.AddEntry("System", "Error: Cannot start lecture, connection missing.");
                SetState(InteractionState.Idle); // Vissza alapállapotba hiba esetén
            }
        }
        else
        {
            Debug.LogWarning("[IFM_LOG] --- Branch: Question Handling ---");
            // Akkor ezt az inputot egy RaiseHand utáni kérdésnek tekintjük.
            Debug.Log($"[IFM] Input '{transcription}' received while waitingForLectureStartConfirmation was false. Treating as a question to be sent to OpenAI.");

            // Elküldjük kérdésként az OpenAI-nak a SendUserQuestionDuringLecture metódussal
            if (openAIWebRequest != null)
            {
                try
                {
                    // Prompt lekérése a LanguageConfig-ból a válasz utáni kérdéshez
                    string followUpPrompt = AppStateManager.Instance?.CurrentLanguage?.FollowUpQuestionPrompt;
                    if (string.IsNullOrEmpty(followUpPrompt))
                    {
                        Debug.LogWarning("[IFM] FollowUpQuestionPrompt is missing from current language config! Using fallback.");
                        // Egyszerű fallback, lehetne jobb is
                        followUpPrompt = (AppStateManager.Instance?.CurrentLanguage?.languageCode == "hu") ?
                                         "Van további kérdése ezzel kapcsolatban?" :
                                         "Do you have any more questions about this?";
                    }

                    Debug.LogWarning($"[IFM_LOG] Calling OAIWR.SendUserQuestionDuringLecture('{transcription}', '{followUpPrompt}')...");
                    Debug.LogWarning($"[IFM] Forwarding transcription '{transcription}' (as question) and prompt '{followUpPrompt}' to OpenAIWebRequest...");
                    openAIWebRequest.SendUserQuestionDuringLecture(transcription, followUpPrompt);
                    Debug.LogWarning("[IFM_LOG] OAIWR.SendUserQuestionDuringLecture() called.");
                }
                catch (Exception ex) // Hiba az OpenAIWebRequest hívása közben
                {
                    Debug.LogError($"[IFM] Exception when sending question to OpenAIWebRequest: {ex.Message}\n{ex.StackTrace}");
                    TranscriptLogger.Instance?.AddEntry("System", $"Error sending question: {ex.Message}");
                    SetState(InteractionState.Idle); // Vissza alapállapotba hiba esetén
                }
            }
            else // Ha nincs OAIWR referencia
            {
                Debug.LogError("[IFM] Cannot send question: OpenAIWebRequest reference is null!");
                TranscriptLogger.Instance?.AddEntry("System", "Error: OpenAIWebRequest reference missing.");
                SetState(InteractionState.Idle); // Vissza alapállapotba hiba esetén
            }
        }
        Debug.LogWarning("[IFM_LOG] <<< HandleUserQuestionReceived EXIT.");
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

    /// <summary>
    /// Called by OpenAIWebRequest when the initial Assistant run (greeting + first question)
    /// has successfully completed its stream. Prepares the system to receive user input.
    /// </summary>
    public void HandleInitialPromptCompleted()
    {
        Debug.LogWarning($"[IFM_LOG] >>> HandleInitialPromptCompleted ENTER. Current state: {currentState}");

        // Csak akkor csinálunk bármit, ha releváns állapotban vagyunk
        // (Lecturing, mert a stream start már átváltott, vagy ProcessingUserInput, ha a stream vége gyorsabb volt,
        // vagy akár Idle, ha a HandlePlaybackQueueCompleted hibásan Idle-be váltott - ezt most kezeljük)
        // A lényeg, hogy a kezdeti prompt után vagyunk.
        if (currentState == InteractionState.Lecturing || currentState == InteractionState.ProcessingUserInput || currentState == InteractionState.Idle)
        {
            // Ha Idle állapotban kapjuk ezt a hívást, az azt jelenti, hogy a HandlePlaybackQueueCompleted
            // hibásan Idle-be váltott. Ez a hívás ezt most korrigálja.
            if (currentState == InteractionState.Idle)
            {
                Debug.LogWarning("[IFM_LOG] HandleInitialPromptCompleted called while Idle. Correcting state.");
            }

            Debug.LogWarning("[IFM_LOG] Initial prompt stream completed. Setting flag and state for user input.");

            waitingForLectureStartConfirmation = true; // Most várjuk a user válaszát az első kérdésre
            Debug.Log("[IFM_LOG] Set waitingForLectureStartConfirmation = true");

            SetState(InteractionState.WaitingForUserInput); // Váltunk állapotot

            // Engedélyezzük a mikrofont kis késleltetéssel
            Debug.LogWarning("[IFM_LOG] Starting EnableSpeakButtonAfterDelay coroutine from HandleInitialPromptCompleted..."); // <<< Módosított log >>>
            StartCoroutine(EnableSpeakButtonAfterDelay(0.3f));
        }
        else
        {
            Debug.LogWarning($"[IFM_LOG] HandleInitialPromptCompleted called in unexpected state: {currentState}. Ignoring.");
        }
        Debug.LogWarning($"[IFM_LOG] <<< HandleInitialPromptCompleted EXIT.");
    }

    // --- Privát Metódusok és Eseménykezelők ---

    // Input Action Callback (ha itt kezeled)
    /*
    private void OnRaiseHandStarted(InputAction.CallbackContext context)
    {
        Debug.Log("[IFM] RaiseHand action started.");
        UserRequestsToAskQuestion();
    }
    */

    // --- Privát Metódusok és Eseménykezelők ---

    // Input Action Callback (ha itt kezeled)
    /*
    private void OnRaiseHandStarted(InputAction.CallbackContext context)
    {
        Debug.Log("[IFM] RaiseHand action started.");
        UserRequestsToAskQuestion();
    }
    */

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
        // <<< Log az elején, hogy lássuk a bejövő állapotot és indexet >>>
        Debug.LogWarning($"[IFM_LOG] >>> HandlePlaybackQueueCompleted ENTER. Current state: {currentState}, LastPlayedIndex: {lastPlayedLectureSentenceIndex}");

        // --- EGYSZERŰSÍTETT LOGIKA ---
        // Jelenleg ez a metódus NEM felelős állapotváltásért vagy mikrofon aktiválásért.
        // A kezdeti prompt végét a HandleInitialPromptCompleted kezeli (amit az OAIWR hív).
        // Az előadás végét is valószínűleg az OAIWR-nek kellene jeleznie a stream vége alapján.
        // Ez az esemény (a lejátszási sor kiürülése) túl megbízhatatlan az időzítési verseny miatt.

        Debug.LogWarning("[IFM_LOG] Playback queue completed. No state change initiated by this event handler.");

        // Korábbi logika eltávolítva:
        // if (lastPlayedLectureSentenceIndex < 0 && ...) { ... SetState(WaitingForUserInput)... }
        // else if (currentState == InteractionState.Lecturing && lastPlayedLectureSentenceIndex >= 0) { ... SetState(Idle)... }

        Debug.LogWarning($"[IFM_LOG] <<< HandlePlaybackQueueCompleted EXIT.");
    }

    /// <summary>
    /// Called by TextToSpeechManager when the playback of the AI's answer
    /// (on the promptAudioSource) has completed.
    /// Transitions the state to resume the lecture.
    /// </summary>
    public void HandleAnswerPlaybackCompleted()
    {
        Debug.LogWarning($"[IFM_LOG] >>> HandleAnswerPlaybackCompleted ENTER. Current state: {currentState}"); // <<< ÚJ LOG

        if (currentState == InteractionState.AnsweringQuestion)
        {
            Debug.Log("[IFM] AI answer playback finished. Resuming lecture.");
            // TranscriptLogger.Instance?.AddEntry("AI", ???); // TODO: Hogyan kapjuk meg a teljes választ?

            SetState(InteractionState.ResumingLecture); // Átmeneti állapot

            if (textToSpeechManager != null)
            {
                Debug.Log($"[IFM] Calling ResumePlayback from index {lastPlayedLectureSentenceIndex + 1}");
                textToSpeechManager.ResumePlayback(lastPlayedLectureSentenceIndex + 1); // Folytatjuk a fő előadást

                // Miután elindult a folytatás, visszaválthatunk Lecturing-re
                // és újra engedélyezhetjük a jelentkezést.
                SetState(InteractionState.Lecturing);
                // A SetState(Lecturing) már kezeli a RaiseHand gomb engedélyezését.
            }
            else
            {
                Debug.LogError("[IFM] Cannot resume lecture: textToSpeechManager is null!");
                SetState(InteractionState.Idle); // Hiba esetén Idle
            }
        }
        else
        {
            Debug.LogWarning($"[IFM] HandleAnswerPlaybackCompleted called in unexpected state: {currentState}. Ignoring.");
        }
        Debug.LogWarning($"[IFM_LOG] <<< HandleAnswerPlaybackCompleted EXIT."); // <<< ÚJ LOG
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
