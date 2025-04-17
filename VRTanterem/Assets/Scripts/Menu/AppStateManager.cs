using UnityEngine;

// Előre deklaráljuk a Config típusokat, hogy a script tudjon róluk
// Ha ezek más névtérben vannak, használd a 'using' direktívát
// using YourProject.Configuration; // Példa

// Feltételezzük, hogy ezek a típusok globálisan elérhetők vagy a megfelelő using direktíva megvan
// public class LanguageConfig : ScriptableObject { /*...*/ }
// public class SubjectConfig : ScriptableObject { /*...*/ }
// public class TopicConfig : ScriptableObject { /*...*/ }


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

    // --- Tárolt Konfiguráció ---
    // Ezeket a SelectionManager tölti fel a StartInteraction hívásakor
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
        // DontDestroyOnLoad(gameObject); // Csak akkor kell, ha több jelenetet használsz

        // Ellenőrizzük a kritikus referenciákat és beállításokat
        bool setupOk = true;
        if (string.IsNullOrEmpty(openAiApiKey) || openAiApiKey.Length < 10) // Egyszerű ellenőrzés
        {
            Debug.LogError("[AppStateManager] Awake Error: OpenAI API Key is not set or looks invalid in the Inspector!", this);
            setupOk = false;
        }
        if (interactionModuleObject == null)
        {
            Debug.LogError("[AppStateManager] Awake Error: Interaction Module Object is not assigned in the Inspector!", this);
            setupOk = false;
        }

        if (!setupOk)
        {
            Debug.LogError("[AppStateManager] Setup incomplete. Disabling component.", this);
            enabled = false; // Letiltjuk a komponenst, ha a beállítások hiányosak
            return;
        }

        // Kezdetben a fő modul legyen inaktív
        if (interactionModuleObject.activeSelf) // Csak akkor logolunk/deaktiválunk, ha aktív volt
        {
            interactionModuleObject.SetActive(false);
            Debug.Log("[AppStateManager] Interaction Module was active, setting to inactive.");
        }
        else
        {
            Debug.Log("[AppStateManager] Interaction Module is already inactive (initial state).");
        }

        Debug.Log("[AppStateManager] Awake finished successfully.");
    }

    // --- Fő Vezérlő Metódus ---

    /// <summary>
    /// Ezt a metódust hívja meg a SelectionManager, miután minden ki lett választva.
    /// Elmenti a konfigurációt, elrejti a menüt, és aktiválja/inicializálja az interakciós modult.
    /// </summary>
    
    public void StartInteraction(LanguageConfig lang, SubjectConfig subj, TopicConfig topic, string voiceId)
    {
        Debug.LogWarning($"[AppStateManager] StartInteraction CALLED - Frame: {Time.frameCount}");
        // 1. Logolás (Ez már megvan)
        if (topic != null)
        {
            Debug.Log($"[AppStateManager] StartInteraction RECEIVED. Topic Name: '{topic.topicName}', Assistant ID from received Topic: '{topic.assistantId}'");
        }
        else
        {
            Debug.LogError("[AppStateManager] StartInteraction RECEIVED a NULL Topic object!");
            return; // Fontos kilépni, ha nincs topic
        }
        Debug.Log($"[AppStateManager] Received Voice ID: '{voiceId}'");

        // --- 2. Konfiguráció Validálása és Mentése (EZ HIÁNYZOTT!) ---
        if (lang == null || subj == null /* topic már ellenőrizve */ || string.IsNullOrEmpty(voiceId) || string.IsNullOrEmpty(topic.assistantId))
        {
            Debug.LogError("[AppStateManager] Critical Error: Received incomplete configuration! Cannot proceed.");
            // Ideális esetben itt vissza kellene navigálni a menübe, vagy legalább megállni.
            return;
        }

        CurrentLanguage = lang;
        CurrentSubject = subj;
        CurrentTopic = topic;
        CurrentVoiceId = voiceId;
        CurrentAssistantId = topic.assistantId; // <<< A LÉNYEGES SOR!

        Debug.Log($"[AppStateManager] Configuration saved. Assistant ID: {CurrentAssistantId}, Voice ID: {CurrentVoiceId}"); // Ellenőrző log

        // --- 3. Menü Elrejtése (Ez valószínűleg itt volt korábban, tedd vissza, ha kell) ---
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


        // --- 4. Fő Interakciós Modul Aktiválása és Inicializálása ---
        if (interactionModuleObject != null)
        {
            Debug.Log("[AppStateManager] Activating Interaction Module...");
            interactionModuleObject.SetActive(true);

            // OpenAIWebRequest inicializálása (CSAK EGYSZER!)
            OpenAIWebRequest openAIComp = interactionModuleObject.GetComponentInChildren<OpenAIWebRequest>(true);
            if (openAIComp != null)
            {
                // Most már a helyesen beállított CurrentAssistantId-t adjuk át
                Debug.Log($"[AppStateManager] Found OpenAIWebRequest. About to call InitializeAndStartInteraction. CurrentAssistantId value: '{CurrentAssistantId}'");
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
                Debug.LogError("[AppStateManager] InteractionFlowManager.Instance is NULL after activating the module! Cannot initialize IFM.");
            }

            Debug.Log("[AppStateManager] Interaction Module activated and core components initialization initiated.");
        }
        else
        {
            Debug.LogError("[AppStateManager] Cannot activate Interaction Module - reference is missing!");
        }
    }

}
