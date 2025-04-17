using UnityEngine;
using System;
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

    [Header("Core Component References")]
    [SerializeField] private OpenAIWebRequest openAIWebRequest;
    [SerializeField] private TextToSpeechManager textToSpeechManager;
    [SerializeField] private WhisperMicController whisperMicController;

    [Header("UI Elements")]
    [SerializeField] private GameObject questionIndicatorUI;

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
        }
        Debug.Log("[IFM] OnDestroy: Unsubscribed from events.");
    }


    // --- Publikus Metódusok (Más scriptek hívják) ---

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
            textToSpeechManager.OnTTSPlaybackEnd -= HandleTTSPlaybackEnd; // Dupla feliratkozás elkerülése
            textToSpeechManager.OnTTSPlaybackEnd += HandleTTSPlaybackEnd;
            Debug.Log("[IFM] Subscribed to TextToSpeechManager.OnTTSPlaybackEnd.");
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
        Debug.Log($"[IFM] HandleUserQuestionReceived. Transcription: '{transcription}'. Current state: {currentState}");
        // Előfordulhat, hogy a Whisper gyorsabb, mint az állapotváltás, ezért engedjük a WaitingForUserInput-ot is
        if (currentState != InteractionState.WaitingForUserInput && currentState != InteractionState.ProcessingUserInput)
        {
            Debug.LogWarning($"[IFM] Received transcription but state is not WaitingForUserInput or ProcessingUserInput ({currentState}). Ignoring.");
            return;
        }

        // Állapot váltása és a beszéd gomb letiltása
        SetState(InteractionState.ProcessingUserInput);
        whisperMicController?.DisableSpeakButton(); // Fontos: Itt tiltjuk le a beszéd gombot!

        if (openAIWebRequest != null)
        {
            Debug.Log("[IFM] Forwarding transcription to OpenAIWebRequest...");
            openAIWebRequest.SendUserQuestionDuringLecture(transcription);
        }
        else { /* Hibakezelés */ SetState(InteractionState.Idle); }
    }

    // Ezt hívja az OpenAIWebRequest, amikor a VÁLASZ streamje elkezdődik
    public void HandleAIAnswerStreamStart()
    {
        Debug.Log($"[IFM] HandleAIAnswerStreamStart called. Current state: {currentState}");
        // Csak akkor váltunk, ha épp a feldolgozásra vártunk
        if (currentState == InteractionState.ProcessingUserInput)
        {
            SetState(InteractionState.AnsweringQuestion);
            // A beszéd gomb már le van tiltva (HandleUserQuestionReceived-ben történt)
            // A jelentkezés gomb is maradjon letiltva a válasz alatt
        }
        else
        {
            Debug.LogWarning($"[IFM] HandleAIAnswerStreamStart called in unexpected state: {currentState}. Ignoring state change.");
        }
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
                    // A SpeakSingleSentence korutinja a végén engedélyezi a gombot
                    textToSpeechManager.SpeakSingleSentence("Mi a kérdésed?");
                }
                else { /* Hibakezelés */ SetState(InteractionState.Idle); }
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

    // SetState változatlan
    private void SetState(InteractionState newState)
    {
        if (currentState == newState) return;
        Debug.Log($"[IFM] State Changing: {currentState} -> {newState}");
        currentState = newState;
        // Ide lehetne további logikát tenni állapotváltáskor
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
