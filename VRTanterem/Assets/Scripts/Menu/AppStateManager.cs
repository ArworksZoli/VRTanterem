using UnityEngine;
using UnityEngine.UI;

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

    [Header("Scene References")]
    [Tooltip("A UI Image komponens a táblán, ami a téma képét mutatja.")]
    [SerializeField] private UnityEngine.UI.Image topicDisplayImage;

    [Header("Feature Controllers")]
    [Tooltip("Húzd ide a LectureImageController komponenst tartalmazó GameObjectet, vagy magát a komponenst.")]
    [SerializeField] private LectureImageController lectureImageController;

    // --- Tárolt Konfiguráció ---
    public LanguageConfig CurrentLanguage { get; private set; }
    public SubjectConfig CurrentSubject { get; private set; }
    public TopicConfig CurrentTopic { get; private set; }
    public string CurrentVoiceId { get; private set; }
    public string CurrentAssistantId { get; private set; } // Ezt a TopicConfig-ból nyerjük ki

    // --- Életciklus Metódusok ---

    void Awake()
    {
        Debug.Log("[AppStateManager] Awake started.");

        // Singleton beállítás
        if (Instance != null && Instance != this)
        {
            Debug.LogWarning("[AppStateManager] Duplicate instance detected. Destroying this one.");
            Destroy(gameObject);
            return;
        }
        Instance = this;
        // DontDestroyOnLoad(gameObject);

        // --- Kritikus referenciák ellenőrzése ---
        bool setupOk = true;
        if (string.IsNullOrEmpty(openAiApiKey) || openAiApiKey.Length < 10)
        {
            Debug.LogError("[AppStateManager] Awake Error: OpenAI API Key is not set or looks invalid!", this);
            setupOk = false;
        }
        if (interactionModuleObject == null)
        {
            Debug.LogError("[AppStateManager] Awake Error: Interaction Module Object is not assigned!", this);
            setupOk = false;
        }
        if (topicDisplayImage == null)
        {
            // Ez most már lehet kritikusabb, ha a LectureImageController-nek szüksége van rá
            Debug.LogError("[AppStateManager] Awake Error: Topic Display Image is not assigned! Image features will fail.", this);
            setupOk = false;
        }
        // --- LectureImageController Ellenőrzése ---
        if (lectureImageController == null)
        {
            // Lehet, hogy nem hiba, ha ez a funkció opcionális, de most szükségesnek tekintjük.
            Debug.LogError("[AppStateManager] Awake Error: Lecture Image Controller is not assigned! Keyword image feature will be disabled.", this);
            setupOk = false;
        }
        // --- Ellenőrzés Vége ---


        if (!setupOk)
        {
            Debug.LogError("[AppStateManager] Setup incomplete. Disabling component.", this);
            enabled = false;
            return;
        }

        // Kezdeti állapotok beállítása
        if (interactionModuleObject.activeSelf)
        {
            interactionModuleObject.SetActive(false);
            Debug.Log("[AppStateManager] Interaction Module was active, setting to inactive.");
        }
        else
        {
            Debug.Log("[AppStateManager] Interaction Module is already inactive (initial state).");
        }

        // A topicDisplayImage kezdeti beállítását most már a LectureImageController is kezelheti,
        // de itt is kikapcsolhatjuk biztosítékként.
        if (topicDisplayImage != null)
        {
            topicDisplayImage.enabled = false;
            topicDisplayImage.sprite = null;
            Debug.Log("[AppStateManager] Initialized Topic Display Image (disabled, sprite cleared).");
        }

        Debug.Log("[AppStateManager] Awake finished successfully.");
    }

    // --- Fő Vezérlő Metódus ---

    public void StartInteraction(LanguageConfig lang, SubjectConfig subj, TopicConfig topic, string voiceId)
    {
        Debug.LogWarning($"[AppStateManager] StartInteraction CALLED - Frame: {Time.frameCount}");
        // 1. Bemeneti adatok logolása és ellenőrzése
        if (topic == null)
        {
            Debug.LogError("[AppStateManager] StartInteraction RECEIVED a NULL Topic object!");
            return;
        }
        Debug.Log($"[AppStateManager] StartInteraction RECEIVED. Topic Name: '{topic.topicName}', Assistant ID: '{topic.assistantId}', Voice ID: '{voiceId}'");

        // 2. Konfiguráció validálása és mentése
        if (lang == null || subj == null || string.IsNullOrEmpty(voiceId) || string.IsNullOrEmpty(topic.assistantId))
        {
            Debug.LogError("[AppStateManager] Critical Error: Received incomplete configuration! Cannot proceed.");
            return;
        }

        CurrentLanguage = lang;
        CurrentSubject = subj;
        CurrentTopic = topic;
        CurrentVoiceId = voiceId;
        CurrentAssistantId = topic.assistantId;

        Debug.Log($"[AppStateManager] Configuration saved. Assistant ID: {CurrentAssistantId}, Voice ID: {CurrentVoiceId}");

        // --- 3. LectureImageController Inicializálása ---

        if (lectureImageController != null)
        {
            Debug.Log($"[AppStateManager] Initializing LectureImageController for topic '{CurrentTopic.topicName}'...");
            lectureImageController.InitializeForTopic(topicDisplayImage, CurrentTopic);
            Debug.Log("[AppStateManager] LectureImageController initialized.");
        }
        else
        {
            Debug.LogError("[AppStateManager] LectureImageController reference is NULL during StartInteraction! Cannot initialize.");
            // Ha a LectureImageController kritikus, itt akár le is állhatnánk: return;
        }

        if (topicDisplayImage != null)
        {
            if (CurrentTopic.topicImage != null)
            {
                Debug.Log($"[AppStateManager] Setting topic image '{CurrentTopic.topicImage.name}' for topic '{CurrentTopic.topicName}'.");
                topicDisplayImage.sprite = CurrentTopic.topicImage;
                topicDisplayImage.enabled = true; // Most jelenítsük meg
            }
            else
            {
                Debug.LogWarning($"[AppStateManager] No topic image assigned for topic '{CurrentTopic.topicName}'. Hiding display image.");
                topicDisplayImage.sprite = null;
                topicDisplayImage.enabled = false; // Rejtsük el, ha nincs kép
            }
        }
        else
        {
            Debug.LogWarning("[AppStateManager] Cannot display topic image: topicDisplayImage reference is null.");
        }

        // --- 4. Menü Elrejtése ---
        SelectionManager selectionManager = FindObjectOfType<SelectionManager>();
        if (selectionManager != null)
        {
            selectionManager.gameObject.SetActive(false);
            Debug.Log("[AppStateManager] SelectionManager GameObject deactivated.");
        }
        else
        {
            Debug.LogWarning("[AppStateManager] Could not find SelectionManager GameObject to deactivate.");
        }


        // --- 5. Fő Interakciós Modul Aktiválása és Inicializálása ---
        if (interactionModuleObject != null)
        {
            Debug.Log("[AppStateManager] Activating Interaction Module...");
            interactionModuleObject.SetActive(true);

            // OpenAIWebRequest inicializálása
            OpenAIWebRequest openAIComp = interactionModuleObject.GetComponentInChildren<OpenAIWebRequest>(true);
            if (openAIComp != null)
            {
                Debug.Log($"[AppStateManager] Found OpenAIWebRequest. Calling InitializeAndStartInteraction with Assistant ID: '{CurrentAssistantId}', Voice ID: '{CurrentVoiceId}'");
                openAIComp.InitializeAndStartInteraction(CurrentAssistantId, CurrentVoiceId);
            }
            else { Debug.LogError("[AppStateManager] OpenAIWebRequest component not found!"); }

            // InteractionFlowManager inicializálása
            if (InteractionFlowManager.Instance != null)
            {
                Debug.Log("[AppStateManager] Calling InteractionFlowManager.InitializeInteraction...");
                InteractionFlowManager.Instance.InitializeInteraction();
            }
            else
            {
                // Ha az IFM az interactionModuleObject része, akkor itt már nem lehet null, hacsak nem volt hiba az Awake-jében.
                Debug.LogError("[AppStateManager] InteractionFlowManager.Instance is NULL after activating the module! Cannot initialize IFM.");
            }

            Debug.Log("[AppStateManager] Interaction Module activated and core components initialization initiated.");
        }
        else
        {
            // Ezt az esetet is az Awake-nek kellene kezelnie.
            Debug.LogError("[AppStateManager] Cannot activate Interaction Module - reference is missing!");
        }
        Debug.LogWarning($"[AppStateManager] StartInteraction FINISHED - Frame: {Time.frameCount}");
    }

    public void ResetDisplay()
    {
        if (topicDisplayImage != null)
        {
            topicDisplayImage.sprite = null;
            topicDisplayImage.enabled = false;
            Debug.Log("[AppStateManager] Topic display image cleared and hidden.");
        }
    }

}
