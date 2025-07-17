using UnityEngine;
using UnityEngine.UI;
using System.Collections.Generic;
using TMPro;
using System.Linq;

public class SelectionManager : MonoBehaviour
{
    [Header("Configuration Data")]
    [SerializeField] private List<LanguageConfig> availableLanguages;

    [Header("UI Panels")]
    [SerializeField] private GameObject languagePanel;
    [SerializeField] private GameObject subjectPanel;
    [SerializeField] private GameObject topicPanel;
    [SerializeField] private GameObject modePanel; // <<< ÚJ
    [SerializeField] private GameObject voicePanel;
    private GameObject currentActivePanel;
    private Stack<GameObject> panelHistory = new Stack<GameObject>();

    [Header("UI Buttons (Assign in Inspector)")]
    [SerializeField] private List<Button> languageButtons;
    [SerializeField] private List<Button> subjectButtons;
    [SerializeField] private List<Button> topicButtons;
    [SerializeField] private List<Button> modeButtons; // <<< ÚJ
    [SerializeField] private List<Button> voiceButtons;

    [Header("Dynamic Texts")]
    [SerializeField] private TextMeshProUGUI pleaseWaitText;

    // --- Kiválasztott állapotok ---
    private LanguageConfig selectedLanguage;
    private SubjectConfig selectedSubject;
    private TopicConfig selectedTopic;
    private InteractionMode selectedMode; // <<< ÚJ
    private string finalAssistantId; // <<< ÚJ: A végleges, kiválasztott Assistant ID
    private string selectedVoiceId;
    private string selectedFantasyVoiceName;


    void Start()
    {
        InitializeMenu();
    }

    public void InitializeMenu()
    {
        languagePanel.SetActive(false);
        subjectPanel.SetActive(false);
        topicPanel.SetActive(false);
        modePanel.SetActive(false); // <<< ÚJ
        voicePanel.SetActive(false);

        panelHistory.Clear();

        if (availableLanguages == null || availableLanguages.Count == 0) { return; }
        if (languagePanel == null || languageButtons == null || languageButtons.Count == 0) { return; }

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

        if (pleaseWaitText != null)
        {
            pleaseWaitText.text = language.PleaseWaitPrompt;
            Debug.Log($"PleaseWaitText frissítve a következőre: '{language.PleaseWaitPrompt}'");
        }

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
                Debug.Log($"Button '{btn.name}': Added listener for topic '{topic.topicName}'");

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

        // --- DINAMIKUS DÖNTÉS ---
        if (topic.assistantMappings == null || topic.assistantMappings.Count == 0)
        {
            Debug.LogError($"KRITIKUS HIBA: A '{topic.topicName}' témához nincsenek Assistant ID-k hozzárendelve a 'assistantMappings' listában!");
            GoBack(); // Visszalépünk, mert innen nem lehet továbblépni
            return;
        }

        if (topic.assistantMappings.Count == 1)
        {
            // Ha csak egy mód van, automatikusan kiválasztjuk és átugorjuk a panelt
            Debug.Log($"A '{topic.topicName}' témához csak egy mód van. Automatikus kiválasztás és továbblépés...");
            var onlyOption = topic.assistantMappings[0];
            SelectMode(onlyOption.Mode, onlyOption.AssistantId); // <<< KÖZVETLEN HÍVÁS
        }
        else
        {
            // Ha több mód van, megjelenítjük a választó panelt
            Debug.Log($"A '{topic.topicName}' témához több mód is van. A Mód választó panel megjelenítése...");
            PopulateModePanel();
            modePanel.SetActive(true);
            currentActivePanel = modePanel;
        }
    }

    void PopulateModePanel()
    {
        var modes = selectedTopic.assistantMappings;
        int configCount = modes.Count;
        int buttonCount = modeButtons.Count;

        for (int i = 0; i < buttonCount; i++)
        {
            Button btn = modeButtons[i];
            if (btn == null) continue;

            if (i < configCount)
            {
                var modeMapping = modes[i];
                // A gomb szövegét az enum nevéből vesszük (pl. "Lecture")
                SetButtonText(btn, modeMapping.Mode.ToString());
                btn.onClick.RemoveAllListeners();
                btn.onClick.AddListener(() => SelectMode(modeMapping.Mode, modeMapping.AssistantId));
                btn.gameObject.SetActive(true);
            }
            else
            {
                btn.gameObject.SetActive(false); // Felesleges gombok elrejtése
            }
        }
    }

    public void SelectMode(InteractionMode mode, string assistantId)
    {
        Debug.Log($"Mode selected: {mode}, Assistant ID: {assistantId}");
        selectedMode = mode;
        finalAssistantId = assistantId;

        if (currentActivePanel != null)
        {
            panelHistory.Push(currentActivePanel);
        }

        if (modePanel.activeSelf) // Csak akkor rejtsük el, ha látható volt
        {
            modePanel.SetActive(false);
        }

        // --- Folytatás a Hangválasztóval ---
        if (selectedTopic.availableVoiceIds == null || selectedTopic.availableVoiceIds.Count == 0)
        {
            Debug.LogWarning($"Nincsenek hangok beállítva a '{selectedTopic.topicName}' témához. Finalizálás...");
            FinalizeSelectionAndStart();
        }
        else
        {
            PopulateVoicePanel();
            voicePanel.SetActive(true);
            currentActivePanel = voicePanel;
        }
    }

    private string GetFantasyNameForVoiceId(string voiceId)
    {
        if (selectedLanguage == null || string.IsNullOrEmpty(voiceId))
        {
            return voiceId; // Visszaadjuk a technikai ID-t, ha nincs elég info
        }

        // Ez a logika megegyezik a PopulateVoicePanel-ben lévővel.
        // Fontos, hogy a kulcsok (pl. "onyx") egyezzenek azokkal az ID-kkal,
        // amiket a TopicConfig-ban az availableVoiceIds listában megadsz.
        Dictionary<string, string> voiceDisplayNames = new Dictionary<string, string>();
        if (selectedLanguage.languageCode == "hu")
        {
            voiceDisplayNames.Add("onyx", "István");
            voiceDisplayNames.Add("ash", "Jenő"); // Feltételezve, hogy az "ash" helyett "alloy"-t használsz az OpenAI standard hangok miatt. Ha "ash" egyedi, akkor az maradjon.
            voiceDisplayNames.Add("nova", "Éva");
            voiceDisplayNames.Add("shimmer", "Krisztina");
            voiceDisplayNames.Add("xjlfQQ3ynqiEyRpArrT8", "Éva");
            voiceDisplayNames.Add("Xb7hH8MSUJpSbSDYk0k2", "Krisztina");
            voiceDisplayNames.Add("xQ7QVYmweeFQQ6autam7", "István");
            voiceDisplayNames.Add("M336tBVZHWWiWb4R54ui", "Jenő");
        }
        else // Alapértelmezett angol vagy más nyelv
        {
            voiceDisplayNames.Add("onyx", "Stephen");
            voiceDisplayNames.Add("ash", "Eugene");
            voiceDisplayNames.Add("nova", "Eve");
            voiceDisplayNames.Add("shimmer", "Christine");
            voiceDisplayNames.Add("xjlfQQ3ynqiEyRpArrT8", "Eve");
            voiceDisplayNames.Add("Xb7hH8MSUJpSbSDYk0k2", "Christine");
            voiceDisplayNames.Add("xQ7QVYmweeFQQ6autam7", "Stephen");
            voiceDisplayNames.Add("M336tBVZHWWiWb4R54ui", "Eugene");
        }
        // További általános hangok, ha kellenek és nincsenek a nyelvspecifikus listában
        if (!voiceDisplayNames.ContainsKey("fable")) voiceDisplayNames.Add("fable", "Fable");
        if (!voiceDisplayNames.ContainsKey("echo")) voiceDisplayNames.Add("echo", "Echo");


        if (voiceDisplayNames.TryGetValue(voiceId, out string fantasyName))
        {
            return fantasyName;
        }
        return voiceId; // Ha nincs a listában, visszaadjuk a technikai ID-t
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
            voiceDisplayNames.Add("xjlfQQ3ynqiEyRpArrT8", "Éva");
            voiceDisplayNames.Add("Xb7hH8MSUJpSbSDYk0k2", "Krisztina");
            voiceDisplayNames.Add("xQ7QVYmweeFQQ6autam7", "István");
            voiceDisplayNames.Add("M336tBVZHWWiWb4R54ui", "Jenő");
            // ... stb. a 4 férfi/női hanghoz
        }
        else
        { // Default to English or other languages
            voiceDisplayNames.Add("onyx", "Stephen");
            voiceDisplayNames.Add("ash", "Eugene");
            voiceDisplayNames.Add("nova", "Eve");
            voiceDisplayNames.Add("shimmer", "Christine");
            voiceDisplayNames.Add("xjlfQQ3ynqiEyRpArrT8", "Eve");
            voiceDisplayNames.Add("Xb7hH8MSUJpSbSDYk0k2", "Christine");
            voiceDisplayNames.Add("xQ7QVYmweeFQQ6autam7", "Stephen");
            voiceDisplayNames.Add("M336tBVZHWWiWb4R54ui", "Eugene");
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
        selectedFantasyVoiceName = GetFantasyNameForVoiceId(voiceId);

        Debug.Log($"Selected Fantasy Voice Name: {selectedFantasyVoiceName}");

        voicePanel.SetActive(false);
        
        FinalizeSelectionAndStart();
    }

    void FinalizeSelectionAndStart()
    {
        // <<< MÓDOSÍTOTT LOGIKA >>>
        Debug.Log("[SelectionManager] FinalizeSelectionAndStart called.");

        if (selectedLanguage == null || selectedSubject == null || selectedTopic == null || string.IsNullOrEmpty(finalAssistantId) || string.IsNullOrEmpty(selectedVoiceId))
        {
            Debug.LogError("[SelectionManager] Selection incomplete! Cannot proceed to finalize. Hiányzó adatok: " +
                           (selectedLanguage == null ? "Nyelv, " : "") +
                           (selectedSubject == null ? "Tantárgy, " : "") +
                           (selectedTopic == null ? "Téma, " : "") +
                           (string.IsNullOrEmpty(finalAssistantId) ? "Assistant ID, " : "") +
                           (string.IsNullOrEmpty(selectedVoiceId) ? "Hang ID" : ""));
            InitializeMenu();
            return;
        }

        Debug.Log($"[SelectionManager] All selections complete. Handing over to AppStateManager with:" +
                  $"\n - Language: {selectedLanguage.displayName}" +
                  $"\n - Subject: {selectedSubject.subjectName}" +
                  $"\n - Topic: {selectedTopic.topicName}" +
                  $"\n - Mode: {selectedMode}" + // <<< ÚJ
                  $"\n - Assistant ID (Final): {finalAssistantId}" + // <<< MÓDOSÍTVA
                  $"\n - Voice ID (Technical): {selectedVoiceId}" +
                  $"\n - Voice Name (Fantasy): {selectedFantasyVoiceName}");

        if (AppStateManager.Instance != null)
        {
            Debug.Log($"[SelectionManager] Calling AppStateManager.StartInteraction via Instance...");
            // Átadjuk a végleges Assistant ID-t is!
            AppStateManager.Instance.StartInteraction(selectedLanguage, selectedSubject, selectedTopic, selectedVoiceId, selectedFantasyVoiceName, finalAssistantId);
            Debug.Log("[SelectionManager] Handover to AppStateManager successful.");
        }
        else
        {
            Debug.LogError("[SelectionManager] CRITICAL ERROR: AppStateManager.Instance is null!");
        }
    }

    public void GoBack()
    {
        // <<< MÓDOSÍTOTT LOGIKA >>>
        if (panelHistory.Count > 0)
        {
            GameObject previousPanel = panelHistory.Pop();

            if (currentActivePanel != null)
            {
                currentActivePanel.SetActive(false);
            }

            previousPanel.SetActive(true);
            currentActivePanel = previousPanel;

            // Állapotok resetelése a visszalépésnek megfelelően
            if (currentActivePanel == topicPanel)
            {
                selectedMode = default; // Enum alaphelyzetbe állítása
                finalAssistantId = null;
                selectedVoiceId = null;
                selectedFantasyVoiceName = null;
            }
            else if (currentActivePanel == subjectPanel)
            {
                selectedTopic = null;
                selectedMode = default;
                finalAssistantId = null;
                selectedVoiceId = null;
                selectedFantasyVoiceName = null;
            }
            else if (currentActivePanel == languagePanel)
            {
                selectedSubject = null;
                selectedTopic = null;
                selectedMode = default;
                finalAssistantId = null;
                selectedVoiceId = null;
                selectedFantasyVoiceName = null;
            }

            Debug.Log($"Navigated back to: {currentActivePanel.name}");
        }
        else
        {
            Debug.LogWarning("No panel in history to go back to.");
        }
    }
}
