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
        Debug.Log($"[AppStateManager] StartInteraction called. Lang: {lang?.displayName}, Subject: {subj?.subjectName}, Topic: {topic?.topicName}, Voice: {voiceId}");

        // --- 1. Konfiguráció Validálása és Mentése ---
        if (lang == null || subj == null || topic == null || string.IsNullOrEmpty(voiceId) || string.IsNullOrEmpty(topic.assistantId))
        {
            Debug.LogError("[AppStateManager] Critical Error: Received incomplete configuration from SelectionManager! Cannot proceed.");
            // Ideális esetben itt vissza kellene navigálni a menübe, vagy legalább megállni.
            // Pl. FindObjectOfType<SelectionManager>()?.InitializeMenu(); // Vissza a menü elejére
            return;
        }

        CurrentLanguage = lang;
        CurrentSubject = subj;
        CurrentTopic = topic;
        CurrentVoiceId = voiceId;
        CurrentAssistantId = topic.assistantId; // Kinyerjük és elmentjük az Assistant ID-t

        Debug.Log($"[AppStateManager] Configuration saved. Assistant ID: {CurrentAssistantId}, Voice ID: {CurrentVoiceId}");

        // --- 2. Menü Elrejtése ---
        SelectionManager selectionManager = FindObjectOfType<SelectionManager>();
        if (selectionManager != null)
        {
            selectionManager.gameObject.SetActive(false); // Deaktiváljuk az egész menü GameObject-et
            Debug.Log("[AppStateManager] SelectionManager GameObject deactivated.");
        }
        else
        {
            Debug.LogWarning("[AppStateManager] Could not find SelectionManager GameObject to deactivate.");
        }

        // --- 3. Fő Interakciós Modul Aktiválása és Inicializálása ---
        if (interactionModuleObject != null)
        {
            Debug.Log("[AppStateManager] Activating Interaction Module...");
            interactionModuleObject.SetActive(true); // Aktiváljuk a GameObject-et

            // --- Inicializáló Hívások (FÁZIS 2) ---
            // Most, hogy a modul aktív, megkeressük a komponenseit és inicializáljuk őket.
            // Ezek a metódusok még nem léteznek, de ide kerülnek majd a hívások.

            // OpenAIWebRequest inicializálása
            OpenAIWebRequest openAIComp = interactionModuleObject.GetComponentInChildren<OpenAIWebRequest>(true); // true: inaktívakat is keres
            if (openAIComp != null)
            {
                Debug.Log("[AppStateManager] Found OpenAIWebRequest. Calling InitializeAndStartInteraction (Phase 2)...");
                // openAIComp.InitializeAndStartInteraction(CurrentAssistantId, openAiApiKey); // Ezt majd a 2. fázisban implementáljuk
            }
            else { Debug.LogError("[AppStateManager] OpenAIWebRequest component not found on the Interaction Module Object or its children!"); }

            // TextToSpeechManager inicializálása
            TextToSpeechManager ttsComp = interactionModuleObject.GetComponentInChildren<TextToSpeechManager>(true); // true: inaktívakat is keres
            if (ttsComp != null)
            {
                Debug.Log("[AppStateManager] Found TextToSpeechManager. Calling Initialize (Phase 2)...");
                // ttsComp.Initialize(openAiApiKey, CurrentVoiceId); // Ezt majd a 2. fázisban implementáljuk
            }
            else { Debug.LogError("[AppStateManager] TextToSpeechManager component not found on the Interaction Module Object or its children!"); }

            // WhisperMicController és SentenceHighlighter automatikusan inicializálódik az OnEnable-ben,
            // amikor az interactionModuleObject aktívvá válik.
            // A SentenceHighlighter Reset-jét az OpenAIWebRequest Initialize hívása intézi majd.

            Debug.Log("[AppStateManager] Interaction Module activated. Initialization calls (Phase 2) are placeholders.");
        }
        else
        {
            // Ez elvileg nem fordulhat elő az Awake ellenőrzés miatt, de biztonság kedvéért itt marad.
            Debug.LogError("[AppStateManager] Cannot activate Interaction Module - reference is missing!");
        }
    }
}
