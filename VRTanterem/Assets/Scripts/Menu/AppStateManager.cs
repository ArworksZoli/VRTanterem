using UnityEngine;
using UnityEngine.UI;
using TMPro;

public class AppStateManager : MonoBehaviour
{
    // --- Singleton Minta ---
    public static AppStateManager Instance { get; private set; }

    // --- Referenciák és Beállítások ---
    [Header("API Configuration")]
    [Tooltip("Your OpenAI API Key (used by multiple components).")]
    [SerializeField] private string openAiApiKey; // Itt tároljuk a közös API kulcsot

    [Header("Modules")]
    [Tooltip("Assign the parent GameObject containing OpenAIWebRequest, TTSManager, etc.")]
    [SerializeField] private GameObject interactionModuleObject; // Ezt aktiváljuk/deaktiváljuk

    [Header("Version")]
    [Tooltip("App Version Number")]
    [SerializeField] private GameObject versionNumber;

    [Header("Scene References")]
    [Tooltip("A UI Image komponens a táblán, ami a téma képét mutatja.")]
    [SerializeField] private UnityEngine.UI.Image topicDisplayImage;

    [Header("Feature Controllers")]
    [Tooltip("Húzd ide a LectureImageController komponenst tartalmazó GameObjectet, vagy magát a komponenst.")]
    [SerializeField] private LectureImageController lectureImageController;

    [Header("Menu System")]
    [SerializeField] private SelectionManager selectionManagerInstance;

    // --- Tárolt Konfiguráció ---
    public LanguageConfig CurrentLanguage { get; private set; }
    public SubjectConfig CurrentSubject { get; private set; }
    public TopicConfig CurrentTopic { get; private set; }
    public string CurrentVoiceId { get; private set; }
    public string CurrentAssistantId { get; private set; } // Ezt a TopicConfig-ból nyerjük ki

    // --- Életciklus Metódusok ---

    void Awake()
    {
        Debug.Log("[AppStateManager_LOG] Awake started.");

        // Singleton beállítás
        if (Instance != null && Instance != this)
        {
            Debug.LogWarning("[AppStateManager_LOG] Duplicate instance detected. Destroying this one.");
            Destroy(gameObject);
            return;
        }
        Instance = this;
        // DontDestroyOnLoad(gameObject);

        if (selectionManagerInstance == null)
        {
            selectionManagerInstance = FindObjectOfType<SelectionManager>(true); // true: inaktív objektumot is megtalál
            if (selectionManagerInstance == null)
            {
                Debug.LogError("[AppStateManager_LOG] KRITIKUS HIBA: SelectionManager instance NEM TALÁLHATÓ! A menübe visszalépés nem fog működni.", this);
                // Fontold meg az 'enabled = false;' itt, ha a menü kritikus
            }
            else
            {
                Debug.Log("[AppStateManager_LOG] SelectionManager instance sikeresen megtalálva (FindObjectOfType).");
            }
        }

        // --- Kritikus referenciák ellenőrzése ---
        bool setupOk = true;
        if (string.IsNullOrEmpty(openAiApiKey) || openAiApiKey.Length < 10)
        {
            Debug.LogError("[AppStateManager_LOG] Awake Error: OpenAI API Key is not set or looks invalid!", this);
            setupOk = false;
        }
        if (interactionModuleObject == null)
        {
            Debug.LogError("[AppStateManager_LOG] Awake Error: Interaction Module Object is not assigned!", this);
            setupOk = false;
        }
        if (topicDisplayImage == null)
        {
            // Ez most már lehet kritikusabb, ha a LectureImageController-nek szüksége van rá
            Debug.LogError("[AppStateManager_LOG] Awake Error: Topic Display Image is not assigned! Image features will fail.", this);
            setupOk = false;
        }
        // --- LectureImageController Ellenőrzése ---
        if (lectureImageController == null)
        {
            // Lehet, hogy nem hiba, ha ez a funkció opcionális, de most szükségesnek tekintjük.
            Debug.LogError("[AppStateManager_LOG] Awake Error: Lecture Image Controller is not assigned! Keyword image feature will be disabled.", this);
            setupOk = false;
        }
        // --- Ellenőrzés Vége ---


        if (!setupOk)
        {
            Debug.LogError("[AppStateManager_LOG] Setup incomplete. Disabling component.", this);
            enabled = false;
            return;
        }

        // Kezdeti állapotok beállítása
        if (interactionModuleObject.activeSelf)
        {
            interactionModuleObject.SetActive(false);
            Debug.Log("[AppStateManager_LOG] Interaction Module was active, setting to inactive.");
        }
        else
        {
            Debug.Log("[AppStateManager_LOG] Interaction Module is already inactive (initial state).");
        }

        // A topicDisplayImage kezdeti beállítását most már a LectureImageController is kezelheti,
        // de itt is kikapcsolhatjuk biztosítékként.
        if (topicDisplayImage != null)
        {
            topicDisplayImage.enabled = false;
            topicDisplayImage.sprite = null;
            Debug.Log("[AppStateManager_LOG] Initialized Topic Display Image (disabled, sprite cleared).");
        }

        Debug.Log("[AppStateManager_LOG] Awake finished successfully.");
    }

    // --- Fő Vezérlő Metódus ---

    public void StartInteraction(LanguageConfig lang, SubjectConfig subj, TopicConfig topic, string voiceId)
    {
        Debug.LogWarning($"[AppStateManager_LOG] StartInteraction CALLED - Frame: {Time.frameCount}");
        // 1. Bemeneti adatok logolása és ellenőrzése
        if (topic == null)
        {
            Debug.LogError("[AppStateManager_LOG] StartInteraction RECEIVED a NULL Topic object!");
            return;
        }
        Debug.Log($"[AppStateManager_LOG] StartInteraction RECEIVED. Topic Name: '{topic.topicName}', Assistant ID: '{topic.assistantId}', Voice ID: '{voiceId}'");

        // 2. Konfiguráció validálása és mentése
        if (lang == null || subj == null || string.IsNullOrEmpty(voiceId) || string.IsNullOrEmpty(topic.assistantId))
        {
            Debug.LogError("[AppStateManager_LOG] Critical Error: Received incomplete configuration! Cannot proceed.");
            return;
        }

        CurrentLanguage = lang;
        CurrentSubject = subj;
        CurrentTopic = topic;
        CurrentVoiceId = voiceId;
        CurrentAssistantId = topic.assistantId;

        Debug.Log($"[AppStateManager_LOG] Configuration saved. Assistant ID: {CurrentAssistantId}, Voice ID: {CurrentVoiceId}");

        // --- 3. LectureImageController Inicializálása ---

        if (lectureImageController != null)
        {
            Debug.Log($"[AppStateManager_LOG] Initializing LectureImageController for topic '{CurrentTopic.topicName}'...");
            lectureImageController.InitializeForTopic(topicDisplayImage, CurrentTopic);
            Debug.Log("[AppStateManager_LOG] LectureImageController initialized.");
        }
        else
        {
            Debug.LogError("[AppStateManager_LOG] LectureImageController reference is NULL during StartInteraction! Cannot initialize.");
            // Ha a LectureImageController kritikus, itt akár le is állhatnánk: return;
        }

        if (topicDisplayImage != null)
        {
            if (CurrentTopic.topicImage != null)
            {
                Debug.Log($"[AppStateManager_LOG] Setting topic image '{CurrentTopic.topicImage.name}' for topic '{CurrentTopic.topicName}'.");
                topicDisplayImage.sprite = CurrentTopic.topicImage;
                topicDisplayImage.enabled = true; // Most jelenítsük meg
            }
            else
            {
                Debug.LogWarning($"[AppStateManager_LOG] No topic image assigned for topic '{CurrentTopic.topicName}'. Hiding display image.");
                topicDisplayImage.sprite = null;
                topicDisplayImage.enabled = false; // Rejtsük el, ha nincs kép
            }
        }
        else
        {
            Debug.LogWarning("[AppStateManager_LOG] Cannot display topic image: topicDisplayImage reference is null.");
        }

        // --- 4. Menü Elrejtése ---
        // SelectionManager selectionManager = FindObjectOfType<SelectionManager>(); // Régi sor kommentelve vagy törölve
        if (selectionManagerInstance != null)
        {
            selectionManagerInstance.gameObject.SetActive(false);
            Debug.Log("[AppStateManager_LOG] SelectionManager GameObject deactivated via instance reference.");
        }
        else
        {
            Debug.LogWarning("[AppStateManager_LOG] Could not find SelectionManager instance to deactivate menu.");
        }


        // --- 5. Fő Interakciós Modul Aktiválása és Inicializálása ---
        if (interactionModuleObject != null)
        {
            Debug.Log("[AppStateManager_LOG] Activating Interaction Module...");
            interactionModuleObject.SetActive(true);
            versionNumber.SetActive(false);

            // OpenAIWebRequest inicializálása
            OpenAIWebRequest openAIComp = interactionModuleObject.GetComponentInChildren<OpenAIWebRequest>(true);
            if (openAIComp != null)
            {
                Debug.Log($"[AppStateManager_LOG] Found OpenAIWebRequest. Calling InitializeAndStartInteraction with Assistant ID: '{CurrentAssistantId}', Voice ID: '{CurrentVoiceId}'");
                openAIComp.InitializeAndStartInteraction(CurrentAssistantId, CurrentVoiceId);
            }
            else { Debug.LogError("[AppStateManager_LOG] OpenAIWebRequest component not found!"); }

            // InteractionFlowManager inicializálása
            if (InteractionFlowManager.Instance != null)
            {
                Debug.Log("[AppStateManager_LOG] Calling InteractionFlowManager.InitializeInteraction...");
                InteractionFlowManager.Instance.InitializeInteraction();
            }
            else
            {
                // Ha az IFM az interactionModuleObject része, akkor itt már nem lehet null, hacsak nem volt hiba az Awake-jében.
                Debug.LogError("[AppStateManager_LOG] InteractionFlowManager.Instance is NULL after activating the module! Cannot initialize IFM.");
            }

            Debug.Log("[AppStateManager_LOG] Interaction Module activated and core components initialization initiated.");
        }
        else
        {
            // Ezt az esetet is az Awake-nek kellene kezelnie.
            Debug.LogError("[AppStateManager:LOG] Cannot activate Interaction Module - reference is missing!");
        }
        Debug.LogWarning($"[AppStateManager_LOG] StartInteraction FINISHED - Frame: {Time.frameCount}");
    }

    public void ResetToMainMenu()
    {
        Debug.LogWarning($"[AppStateManager_LOG] ResetToMainMenu ELINDÍTVA. Idő: {Time.time}");

        if (interactionModuleObject != null)
        {
            Debug.Log("[AppStateManager_LOG] Interaction Module resetelése és kikapcsolása előkészítve...");

            WhisperMicController whisperCtrl = interactionModuleObject.GetComponentInChildren<WhisperMicController>(true);
            if (whisperCtrl != null)
            {
                Debug.Log("  -> WhisperMicController.StopRecordingAndReset() hívása...");
                whisperCtrl.StopRecordingAndReset();
            }
            else { Debug.LogWarning("  -> WhisperMicController nem található a modulban."); }

            TextToSpeechManager ttsCtrl = interactionModuleObject.GetComponentInChildren<TextToSpeechManager>(true);
            if (ttsCtrl != null)
            {
                Debug.Log("  -> TextToSpeechManager.ResetManager() hívása...");
                ttsCtrl.ResetManager(); // <--- EZ A HÍVÁS MOST MÁR A FRISSÍTETT ResetManager-t HÍVJA
            }
            else { Debug.LogWarning("  -> TextToSpeechManager nem található a modulban."); }

            OpenAIWebRequest oaiCtrl = interactionModuleObject.GetComponentInChildren<OpenAIWebRequest>(true);
            if (oaiCtrl != null)
            {
                Debug.Log("  -> OpenAIWebRequest.CancelAllOngoingRequestsAndResetState() hívása...");
                oaiCtrl.CancelAllOngoingRequestsAndResetState(); // <--- EZ AZ ÚJ HÍVÁS
            }
            else { Debug.LogWarning("  -> OpenAIWebRequest nem található a modulban."); }

            InteractionFlowManager ifmCtrl = InteractionFlowManager.Instance; // Vagy GetComponentInChildren
            if (ifmCtrl != null)
            {
                Debug.Log("  -> InteractionFlowManager.HardResetToIdle() hívása...");
                ifmCtrl.HardResetToIdle(); // <--- EZ AZ ÚJ HÍVÁS
            }
            else { Debug.LogWarning("  -> InteractionFlowManager nem található."); }

            interactionModuleObject.SetActive(false); // A modul kikapcsolása
            Debug.Log("[AppStateManager_LOG] Interaction Module Object kikapcsolva.");
        }
        else
        {
            Debug.LogWarning("[AppStateManager_LOG] Interaction Module Object nincs beállítva vagy már inaktív.");
        }

        // Előadás-specifikus UI és állapotok resetelése
        ResetDisplay(); // Meglévő metódusod a kép törlésére

        if (lectureImageController != null)
        {
            // lectureImageController.ResetController(); // Ezt később implementáljuk
            Debug.Log("[AppStateManager_LOG] LectureImageController.ResetController() hívása itt lesz.");
        }

        // AppStateManager belső állapotának törlése (kiválasztott téma stb.)
        CurrentLanguage = null;
        CurrentSubject = null;
        CurrentTopic = null;
        CurrentVoiceId = null;
        CurrentAssistantId = null;
        Debug.Log("[AppStateManager_LOG] AppStateManager belső kiválasztási állapot törölve.");

        // 2. Menü Aktiválása és Inicializálása
        if (selectionManagerInstance != null)
        {
            Debug.Log("[AppStateManager_LOG] SelectionManager (Menü) aktiválása és inicializálása...");
            selectionManagerInstance.gameObject.SetActive(true); // Menü UI megjelenítése
            selectionManagerInstance.InitializeMenu();        // Menü kezdőállapotba állítása

            if (versionNumber != null) // A verziószám GameObject, amit a StartInteraction elrejtett
            {
                versionNumber.SetActive(true); // Verziószám újra láthatóvá tétele
                Debug.Log("[AppStateManager_LOG] Verziószám UI újra aktiválva.");
            }
        }
        else
        {
            Debug.LogError("[AppStateManager_LOG] SelectionManager instance nem található! Nem lehet visszatérni a menübe.");
        }

        Debug.LogWarning($"[AppStateManager_LOG] ResetToMainMenu BEFEJEZŐDÖTT. Idő: {Time.time}");
    }

    public void ResetDisplay()
    {
        if (topicDisplayImage != null)
        {
            topicDisplayImage.sprite = null;
            topicDisplayImage.enabled = false;
            Debug.Log("[AppStateManager_LOG] Topic display image cleared and hidden.");
        }
    }

}
