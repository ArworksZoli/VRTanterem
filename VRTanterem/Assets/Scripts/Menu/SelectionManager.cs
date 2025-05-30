using UnityEngine;
using UnityEngine.UI;
using System.Collections.Generic;
using TMPro; // Szükséges a szöveges gombokhoz
using System.Linq;

public class SelectionManager : MonoBehaviour
{
    // ... (Változók ugyanazok maradnak: availableLanguages, panelek, gomblisták, selected változók) ...
    [Header("Configuration Data")]
    [Tooltip("Húzd ide az összes elérhető LanguageConfig ScriptableObject assetet.")]
    [SerializeField] private List<LanguageConfig> availableLanguages;

    [Header("UI Panels")]
    [SerializeField] private GameObject languagePanel;
    [SerializeField] private GameObject subjectPanel;
    [SerializeField] private GameObject topicPanel;
    [SerializeField] private GameObject voicePanel;
    private GameObject currentActivePanel;
    private Stack<GameObject> panelHistory = new Stack<GameObject>();

    [Header("UI Buttons (Assign in Inspector)")]
    [Tooltip("Húzd ide a LanguagePanel gombjait a megfelelő sorrendben.")]
    [SerializeField] private List<Button> languageButtons;
    [Tooltip("Húzd ide a SubjectPanel gombjait a megfelelő sorrendben.")]
    [SerializeField] private List<Button> subjectButtons;
    [Tooltip("Húzd ide a TopicPanel gombjait a megfelelő sorrendben.")]
    [SerializeField] private List<Button> topicButtons;
    [Tooltip("Húzd ide a VoicePanel gombjait a megfelelő sorrendben.")]
    [SerializeField] private List<Button> voiceButtons;

    // Referencia az AppStateManager-re (később)
    // [SerializeField] private AppStateManager appStateManager;

    private LanguageConfig selectedLanguage;
    private SubjectConfig selectedSubject;
    private TopicConfig selectedTopic;
    private string selectedVoiceId;


    void Start()
    {
        InitializeMenu();
    }

    public void InitializeMenu()
    {
        // ... (Ugyanaz, mint előbb: panelek elrejtése, ellenőrzések) ...
        languagePanel.SetActive(false);
        subjectPanel.SetActive(false);
        topicPanel.SetActive(false);
        voicePanel.SetActive(false);

        panelHistory.Clear();

        if (availableLanguages == null || availableLanguages.Count == 0) { /*...*/ return; }
        if (languagePanel == null || languageButtons == null || languageButtons.Count == 0) { /*...*/ return; }

        PopulateLanguagePanel();
        languagePanel.SetActive(true);
        currentActivePanel = languagePanel;
    }

    // --- VISSZAHOZVA: Helper function to set button text ---
    private void SetButtonText(Button button, string text)
    {
        if (button == null) return;
        TMP_Text tmpText = button.GetComponentInChildren<TMP_Text>();
        if (tmpText != null)
        {
            tmpText.text = text;
        }
        else
        {
            // Fallback a régebbi Text komponensre, ha szükséges
            Text legacyText = button.GetComponentInChildren<Text>();
            if (legacyText != null)
            {
                legacyText.text = text;
            }
            else
            {
                // Ha egyik sincs, és szöveget akartunk írni, az baj lehet
                Debug.LogWarning($"Nem található Text vagy TMP_Text komponens a(z) '{button.name}' gombon a szöveg beállításához: '{text}'");
            }
        }
    }
    // --- VÉGE: Helper function ---


    void PopulateLanguagePanel() // IKONOS - Nincs SetButtonText
    {
        Debug.Log("Populating Language Panel with fixed buttons (icons only)...");
        int configCount = availableLanguages?.Count ?? 0;
        int buttonCount = languageButtons?.Count ?? 0;

        for (int i = 0; i < buttonCount; i++)
        {
            if (i < configCount)
            {
                LanguageConfig lang = availableLanguages[i];
                Button btn = languageButtons[i];
                if (btn != null)
                {
                    btn.onClick.RemoveAllListeners();
                    btn.onClick.AddListener(() => SelectLanguage(lang));
                    btn.gameObject.SetActive(true);
                }
            }
            else { if (languageButtons[i] != null) languageButtons[i].gameObject.SetActive(false); }
        }
        if (configCount > buttonCount) { /* Warning log */ }
    }

    public void SelectLanguage(LanguageConfig language)
    {
        // ... (Logika ugyanaz) ...
        Debug.Log($"Language selected: {language.displayName}");
        selectedLanguage = language;

        if (currentActivePanel != null)
        {
            panelHistory.Push(currentActivePanel);
        }

        languagePanel.SetActive(false);

        if (selectedLanguage.availableSubjects == null || selectedLanguage.availableSubjects.Count == 0)
        {
            Debug.LogError($"Nincsenek tantárgyak a '{language.displayName}' nyelvhez!");
            // Hiba esetén ideális esetben visszaléphetnénk vagy hibaüzenetet jeleníthetnénk meg.
            // A GoBack() funkció ilyenkor is működni fog, ha van előzmény.
            // Ha itt return-ölünk, akkor nem lesz aktív panel, ami nem ideális.
            // Fontold meg a GoBack() automatikus hívását, vagy a languagePanel újraaktiválását.
            // Pl: GoBack(); vagy languagePanel.SetActive(true); panelHistory.Pop(); return;
            return;
        }
        if (subjectButtons == null || subjectButtons.Count == 0) { Debug.LogError("Tantárgy gombok nincsenek beállítva!"); return; }

        PopulateSubjectPanel();
        subjectPanel.SetActive(true);
        currentActivePanel = subjectPanel;
    }

    void PopulateSubjectPanel() // IKONOS - Nincs SetButtonText
    {
        Debug.Log("Populating Subject Panel with fixed buttons (icons only)...");
        var subjects = selectedLanguage?.availableSubjects;
        int configCount = subjects?.Count ?? 0;
        int buttonCount = subjectButtons?.Count ?? 0;

        for (int i = 0; i < buttonCount; i++)
        {
            if (i < configCount)
            {
                SubjectConfig subj = subjects[i];
                Button btn = subjectButtons[i];
                if (btn != null)
                {
                    btn.onClick.RemoveAllListeners();
                    btn.onClick.AddListener(() => SelectSubject(subj));
                    btn.gameObject.SetActive(true);
                }
            }
            else { if (subjectButtons[i] != null) subjectButtons[i].gameObject.SetActive(false); }
        }
        if (configCount > buttonCount) { /* Warning log */ }
    }

    public void SelectSubject(SubjectConfig subject)
    {
        Debug.Log($"Subject selected: {subject.subjectName}");
        selectedSubject = subject;

        if (currentActivePanel != null)
        {
            panelHistory.Push(currentActivePanel);
        }

        subjectPanel.SetActive(false);

        if (selectedSubject.availableTopics == null || selectedSubject.availableTopics.Count == 0) { /*...*/ Debug.LogError("Nincsenek témák ehhez a tantárgyhoz!"); return; }
        if (topicButtons == null || topicButtons.Count == 0) { Debug.LogError("Téma gombok nincsenek beállítva!"); return; }

        PopulateTopicPanel();
        topicPanel.SetActive(true);
        currentActivePanel = topicPanel;
    }

    void PopulateTopicPanel()
    {
        if (selectedSubject == null || selectedSubject.availableTopics == null)
        {
            Debug.LogError("PopulateTopicPanel: Nincs kiválasztott tantárgy vagy nincsenek elérhető témák!");
            // Esetleg inaktiváld a panelt itt
            topicPanel.SetActive(false);
            return;
        }

        List<TopicConfig> topics = selectedSubject.availableTopics;
        int configCount = topics.Count;
        int buttonCount = topicButtons.Count; // A te 4 gombod

        Debug.Log($"Populating Topic Panel. Topics available: {configCount}, Buttons available: {buttonCount}"); // Logolás

        for (int i = 0; i < buttonCount; i++)
        {
            Button btn = topicButtons[i]; // Vedd ki a gombot a listából

            if (btn == null)
            {
                Debug.LogWarning($"Topic Button index {i} is null in the list.");
                continue; // Hagyd ki ezt a ciklus iterációt, ha a gomb hiányzik a listából
            }

            if (i < configCount)
            {
                // Van ehhez a gombhoz téma
                TopicConfig topic = topics[i]; // Vedd ki a megfelelő témát

                if (topic == null)
                {
                    Debug.LogWarning($"TopicConfig at index {i} for subject '{selectedSubject.subjectName}' is null.");
                    btn.gameObject.SetActive(false); // Rejtsd el a gombot, ha a téma érvénytelen
                    continue;
                }

                // --- Szöveg Beállítása és Raycast Target Kikapcsolása ---
                TextMeshProUGUI tmpText = btn.GetComponentInChildren<TextMeshProUGUI>();
                if (tmpText != null)
                {
                    tmpText.text = topic.topicName;
                    tmpText.raycastTarget = false; // <<< --- KRITIKUSAN FONTOS!
                    Debug.Log($"Button '{btn.name}': Set text to '{topic.topicName}', Raycast Target set to false.");
                }
                else
                {
                    Debug.LogWarning($"Button '{btn.name}' does not have a TextMeshProUGUI child component!");
                }

                // --- Listener Hozzáadása ---
                btn.onClick.RemoveAllListeners(); // Először törölj minden régit
                btn.onClick.AddListener(() => SelectTopic(topic));
                Debug.Log($"Button '{btn.name}': Added listener for topic '{topic.topicName}' (ID: {topic.assistantId})"); // Logolás a listener hozzáadásáról

                // --- Gomb Aktiválása ---
                btn.gameObject.SetActive(true);
                btn.interactable = true; // Biztosítsd, hogy interaktív legyen
            }
            else
            {
                // Nincs több téma ehhez a gombhoz, rejtsd el
                btn.gameObject.SetActive(false);
                Debug.Log($"Button '{btn.name}': Deactivated (no more topics).");
            }
        }
    }

    public void SelectTopic(TopicConfig topic)
    {
        Debug.Log($"===== SelectTopic CALLED for: {topic.topicName} =====");
        selectedTopic = topic;

        if (currentActivePanel != null)
        {
            panelHistory.Push(currentActivePanel);
        }

        topicPanel.SetActive(false);

        Debug.Log("Checking availableVoiceIds...");
        if (selectedTopic.availableVoiceIds == null || selectedTopic.availableVoiceIds.Count == 0)
        {
            Debug.LogWarning($"No voices configured for {topic.topicName}. Proceeding to Finalize/Skipping voice panel.");
            // Ha itt nincs hangválasztás, és egyből finalizálunk, akkor a topicPanel volt az utolsó "menü" panel
            // A currentActivePanel itt a topicPanel marad (ami most false). A GoBack() visszahozná.
            FinalizeSelectionAndStart();
            return;
        }

        Debug.Log("Checking voiceButtons list...");
        if (voiceButtons == null || voiceButtons.Count == 0)
        {
            Debug.LogError("SelectionManager: VoicePanel buttons are not assigned! Aborting.");
            return;
        }

        Debug.Log("Checks passed, calling PopulateVoicePanel...");
        PopulateVoicePanel();
        Debug.Log("Activating voicePanel...");
        voicePanel.SetActive(true);
        currentActivePanel = voicePanel;
    }


    void PopulateVoicePanel() // SZÖVEGES (feltételezve) - Van SetButtonText
    {
        Debug.Log("Populating Voice Panel with fixed buttons (text)...");
        var voices = selectedTopic?.availableVoiceIds;
        int configCount = voices?.Count ?? 0;
        int buttonCount = voiceButtons?.Count ?? 0;

        // Egyszerű név hozzárendelés ID alapján (lokalizálható lenne bonyolultabban)
        // Ezt a szótárat ki lehetne szervezni, vagy akár a LanguageConfig-ba tenni
        Dictionary<string, string> voiceDisplayNames = new Dictionary<string, string>();
        if (selectedLanguage != null && selectedLanguage.languageCode == "hu")
        {
            voiceDisplayNames.Add("onyx", "István"); // Példa nevek
            voiceDisplayNames.Add("ash", "Jenő");
            voiceDisplayNames.Add("nova", "Éva");
            voiceDisplayNames.Add("shimmer", "Krisztina");
            // ... stb. a 4 férfi/női hanghoz
        }
        else
        { // Default to English or other languages
            voiceDisplayNames.Add("onyx", "Stephen");
            voiceDisplayNames.Add("ash", "Eugene");
            voiceDisplayNames.Add("nova", "Eve");
            voiceDisplayNames.Add("shimmer", "Christine");
            // ... etc.
        }
        // Add other standard voices if needed
        if (!voiceDisplayNames.ContainsKey("fable")) voiceDisplayNames.Add("fable", "Fable");
        if (!voiceDisplayNames.ContainsKey("echo")) voiceDisplayNames.Add("echo", "Echo");


        for (int i = 0; i < buttonCount; i++)
        {
            if (i < configCount)
            {
                string voiceId = voices[i];
                Button btn = voiceButtons[i];
                if (btn != null)
                {
                    string displayName = voiceDisplayNames.ContainsKey(voiceId) ? voiceDisplayNames[voiceId] : voiceId; // Használjuk a szebb nevet, ha van
                    SetButtonText(btn, displayName); // <<< SZÖVEG BEÁLLÍTÁSA
                    btn.onClick.RemoveAllListeners();
                    btn.onClick.AddListener(() => SelectVoice(voiceId));
                    btn.gameObject.SetActive(true);
                }
            }
            else { if (voiceButtons[i] != null) voiceButtons[i].gameObject.SetActive(false); }
        }
        if (configCount > buttonCount) { /* Warning log */ }
    }

    public void SelectVoice(string voiceId)
    {
        Debug.Log($"Voice selected: {voiceId}");
        selectedVoiceId = voiceId;

        voicePanel.SetActive(false);
        
        FinalizeSelectionAndStart();
    }

    void FinalizeSelectionAndStart()
    {
        Debug.Log("[SelectionManager] FinalizeSelectionAndStart called.");

        // Közvetlenül az elején ellenőrizzük a selectedTopic-ot
        if (selectedTopic == null)
        {
            Debug.LogError("[SelectionManager] FinalizeSelectionAndStart called, but selectedTopic is NULL!");
            return; // Vagy más hibakezelés
        }
        else
        {
            // Írjuk ki a topic nevét és az ID-ját, amit át fogunk adni
            Debug.Log($"[SelectionManager] Finalizing. Topic Name: '{selectedTopic.topicName}', Assistant ID from Topic: '{selectedTopic.assistantId}'");
        }

        // --- 1. Ellenőrizzük, hogy minden szükséges adat ki van-e választva ---
        if (selectedLanguage == null || selectedSubject == null || selectedTopic == null || string.IsNullOrEmpty(selectedVoiceId))
        {
            Debug.LogError("[SelectionManager] Selection incomplete! Cannot proceed to finalize.");
            // Opcionálisan itt is visszaugorhatnánk a menü elejére
            InitializeMenu();
            return;
        }

        // --- 2. Ellenőrizzük a kritikus adatokat (Assistant ID) ---
        // Különösen fontos, mert enélkül az AppStateManager sem tud mit kezdeni vele.
        if (string.IsNullOrEmpty(selectedTopic.assistantId))
        {
            Debug.LogError($"[SelectionManager] Critical Error: The selected topic '{selectedTopic.topicName}' does not have an Assistant ID assigned in its configuration! Cannot start interaction.");
            // Itt is érdemes lehet visszalépni vagy hibaüzenetet adni a felhasználónak.
            InitializeMenu();
            return;
        }

        // --- 3. Logoljuk a végleges választást ---
        Debug.Log($"[SelectionManager] All selections complete. Handing over to AppStateManager with:" +
                  $"\n - Language: {selectedLanguage.displayName}" +
                  $"\n - Subject: {selectedSubject.subjectName}" +
                  $"\n - Topic: {selectedTopic.topicName}" +
                  $"\n - Voice: {selectedVoiceId}" +
                  $"\n - Assistant ID: {selectedTopic.assistantId}");

        // --- 4. Átadjuk az irányítást az AppStateManager-nek (CSAK EGYSZER!) ---
        if (AppStateManager.Instance != null)
        {
            Debug.Log($"[SelectionManager] Calling AppStateManager.StartInteraction via Instance...");
            AppStateManager.Instance.StartInteraction(selectedLanguage, selectedSubject, selectedTopic, selectedVoiceId);
            Debug.Log("[SelectionManager] Handover to AppStateManager successful.");
        }
        else
        {
            // Ha nincs AppStateManager.Instance, az nagy hiba.
            Debug.LogError("[SelectionManager] CRITICAL ERROR: AppStateManager.Instance is null! Cannot start the main application logic.");
            // Itt lehetne valamilyen fallback vagy hibaállapot.
        }
    }

    public void GoBack()
    {
        if (panelHistory.Count > 0)
        {
            GameObject previousPanel = panelHistory.Pop();

            // Jelenlegi aktív panel deaktiválása (ha van)
            if (currentActivePanel != null)
            {
                currentActivePanel.SetActive(false);
            }

            // Előző panel aktiválása
            previousPanel.SetActive(true);
            currentActivePanel = previousPanel; // Frissítjük az aktuális aktív panelt

            // "Megszakítjuk" a kiválasztást, ami az előző panelről a következőre vezetett.
            // Ez biztosítja, hogy ha a felhasználó visszalép, majd másikat választ, ne maradjanak "beragadt" értékek.
            if (currentActivePanel == languagePanel)
            {
                selectedSubject = null; // Visszatértünk a Nyelv panelre, töröljük a kiválasztott tantárgyat
                selectedTopic = null;   // és témát, hangot is
                selectedVoiceId = null;
                // A selectedLanguage megmarad, hiszen ezen a panelen vagyunk.
            }
            else if (currentActivePanel == subjectPanel)
            {
                selectedTopic = null;   // Visszatértünk a Tantárgy panelre, töröljük a kiválasztott témát
                selectedVoiceId = null; // és hangot is
                // A selectedLanguage és selectedSubject megmarad.
            }
            else if (currentActivePanel == topicPanel)
            {
                selectedVoiceId = null; // Visszatértünk a Téma panelre, töröljük a kiválasztott hangot
                // selectedLanguage, selectedSubject, selectedTopic megmarad.
            }
            // Ha a voicePanel-ről léptünk vissza a topicPanel-re, a selectedVoiceId már törölve lett.
            // A voicePanel maga nem kerül a history-ba, ha onnan egyből a FinalizeSelectionAndStart() következik.
            // De ha a SelectTopic után nincs hangválasztás, akkor a topicPanel-ről a FinalizeSelectionAndStart() jön,
            // ilyenkor a GoBack() a subjectPanel-re visz.

            Debug.Log($"Navigated back to: {currentActivePanel.name}");
        }
        else
        {
            Debug.LogWarning("No panel in history to go back to. Already at the first panel or history is empty.");
            // Itt dönthetsz úgy, hogy pl. bezárod az egész menüt, vagy a visszalépő gomb inaktívvá válik,
            // ha az első panelen van, és nincs hova visszalépni.
        }
    }
}
