using UnityEngine;
using UnityEngine.UI;
using System.Collections.Generic;

public class SelectionManager : MonoBehaviour
{
    [Header("Configuration Data")]
    [Tooltip("Húzd ide az összes elérhető LanguageConfig ScriptableObject assetet.")]
    [SerializeField] private List<LanguageConfig> availableLanguages;

    [Header("UI Panels")]
    [Tooltip("Húzd ide a Nyelvválasztó Panel GameObjectet.")]
    [SerializeField] private GameObject languagePanel;
    [Tooltip("Húzd ide a Tantárgyválasztó Panel GameObjectet.")]
    [SerializeField] private GameObject subjectPanel;
    [Tooltip("Húzd ide a Témaválasztó Panel GameObjectet.")]
    [SerializeField] private GameObject topicPanel;
    [Tooltip("Húzd ide a Hangválasztó Panel GameObjectet.")]
    [SerializeField] private GameObject voicePanel;

    // Ide jön majd referencia az AppStateManager-re
    // [SerializeField] private AppStateManager appStateManager;

    // Tárolók a kiválasztott elemeknek
    private LanguageConfig selectedLanguage;
    private SubjectConfig selectedSubject;
    private TopicConfig selectedTopic;
    private string selectedVoiceId;

    // Később ide kerülnek a kiválasztást kezelő metódusok
    // pl. SelectLanguage(LanguageConfig language), ShowSubjectPanel(), stb.

    void Start()
    {
        // Kezdeti állapot beállítása: Csak a nyelvválasztó látszik
        InitializeMenu();
    }

    void InitializeMenu()
    {
        // Biztonság kedvéért minden panelt elrejtünk, majd csak a nyelvválasztót mutatjuk
        languagePanel.SetActive(false);
        subjectPanel.SetActive(false);
        topicPanel.SetActive(false);
        voicePanel.SetActive(false);

        if (availableLanguages == null || availableLanguages.Count == 0)
        {
            Debug.LogError("SelectionManager: Nincsenek nyelvi konfigurációk hozzárendelve az Inspectorban!");
            // Itt lehetne valamilyen hiba UI-t megjeleníteni
            return;
        }

        if (languagePanel != null)
        {
            // TODO: Töltsd fel a languagePanel-t gombokkal az availableLanguages alapján
            // (Ezt a következő lépésben részletezzük)
            PopulateLanguagePanel(); // Ezt a metódust még meg kell írni

            languagePanel.SetActive(true); // Mutasd a nyelvválasztó panelt
        }
        else
        {
            Debug.LogError("SelectionManager: LanguagePanel nincs hozzárendelve az Inspectorban!");
        }
    }

    // --- Helyőrzők a későbbi metódusoknak ---

    void PopulateLanguagePanel()
    {
        Debug.Log("Populating Language Panel...");
        // Itt kell majd dinamikusan létrehozni a gombokat a nyelvekhez
    }

    public void SelectLanguage(LanguageConfig language) // Ezt hívják majd a nyelvválasztó gombok
    {
        Debug.Log($"Language selected: {language.displayName}");
        selectedLanguage = language;

        languagePanel.SetActive(false);
        // TODO: Töltsd fel a subjectPanel-t a selectedLanguage.availableSubjects alapján
        PopulateSubjectPanel(); // Ezt a metódust még meg kell írni
        subjectPanel.SetActive(true);
    }

    void PopulateSubjectPanel()
    {
        Debug.Log("Populating Subject Panel...");
        // Itt kell majd dinamikusan létrehozni a gombokat a tantárgyakhoz
    }

    public void SelectSubject(SubjectConfig subject) // Ezt hívják majd a tantárgyválasztó gombok
    {
        Debug.Log($"Subject selected: {subject.subjectName}");
        selectedSubject = subject;

        subjectPanel.SetActive(false);
        // TODO: Töltsd fel a topicPanel-t a selectedSubject.availableTopics alapján
        PopulateTopicPanel(); // Ezt a metódust még meg kell írni
        topicPanel.SetActive(true);
    }

    void PopulateTopicPanel()
    {
        Debug.Log("Populating Topic Panel...");
        // Itt kell majd dinamikusan létrehozni a gombokat a témákhoz
    }

    public void SelectTopic(TopicConfig topic) // Ezt hívják majd a témaválasztó gombok
    {
        Debug.Log($"Topic selected: {topic.topicName}");
        selectedTopic = topic;

        topicPanel.SetActive(false);
        // TODO: Töltsd fel a voicePanel-t a selectedTopic.availableVoiceIds alapján
        PopulateVoicePanel(); // Ezt a metódust még meg kell írni
        voicePanel.SetActive(true);
    }

    void PopulateVoicePanel()
    {
        Debug.Log("Populating Voice Panel...");
        // Itt kell majd dinamikusan létrehozni a gombokat a hangokhoz
    }

    public void SelectVoice(string voiceId) // Ezt hívják majd a hangválasztó gombok
    {
        Debug.Log($"Voice selected: {voiceId}");
        selectedVoiceId = voiceId;

        voicePanel.SetActive(false);
        FinalizeSelectionAndStart(); // Indíthatjuk az alkalmazást
    }

    void FinalizeSelectionAndStart()
    {
        Debug.Log("All selections made! Starting main application...");
        Debug.Log($"Final Config: Lang={selectedLanguage.languageCode}, Subject={selectedSubject.subjectName}, Topic={selectedTopic.topicName}, Assistant={selectedTopic.assistantId}, Voice={selectedVoiceId}");

        // TODO: Itt kell majd szólni az AppStateManager-nek, hogy váltson állapotot
        // és adja át a kiválasztott konfigurációt (assistantId, voiceId, languageCode)
        // pl. appStateManager.StartMainInteraction(selectedTopic.assistantId, selectedVoiceId, selectedLanguage.languageCode);

        // Egyelőre csak logolunk, a tényleges indítást később implementáljuk az AppStateManagerrel.
    }
}
