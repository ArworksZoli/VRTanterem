using UnityEngine;
using UnityEngine.UI;
using System.Collections.Generic;
using System.Linq;

public class LectureImageController : MonoBehaviour
{
    // --- Referenciák ---
    [Header("Assign Automatically (Usually)")]
    [Tooltip("A UI Image komponens, amin a képek megjelennek. Az AppStateManager adja át.")]
    [SerializeField] private Image displayImage;

    // --- Belső Állapot ---
    private List<KeywordImagePair> currentKeywordImages;
    private Sprite defaultTopicSprite;

    // --- Singleton (Opcionális, de hasznos lehet, ha máshonnan is el kell érni) ---
    // public static LectureImageController Instance { get; private set; }

    // void Awake()
    // {
    //     // Opcionális Singleton beállítás
    //     if (Instance != null && Instance != this)
    //     {
    //         Destroy(gameObject);
    //         return;
    //     }
    //     Instance = this;
    // }

    // --- Inicializálás ---

    public void InitializeForTopic(Image imageComponent, TopicConfig topicConfig)
    {
        if (imageComponent == null)
        {
            Debug.LogError("[LectureImageController] InitializeForTopic: Display Image component is null! Cannot function.", this);
            enabled = false; // Letiltjuk, ha nincs kép
            return;
        }
        if (topicConfig == null)
        {
            Debug.LogError("[LectureImageController] InitializeForTopic: TopicConfig is null! Cannot load keyword images.", this);
            
            this.displayImage = imageComponent;
            this.defaultTopicSprite = null;
            this.currentKeywordImages = new List<KeywordImagePair>();
            this.displayImage.enabled = false;
            return;
        }

        this.displayImage = imageComponent;
        this.defaultTopicSprite = topicConfig.topicImage;
        this.currentKeywordImages = topicConfig.keywordImages ?? new List<KeywordImagePair>();

        Debug.Log($"[LectureImageController] Initialized for topic '{topicConfig.topicName}'. Found {currentKeywordImages.Count} keyword images. Default sprite set to '{(defaultTopicSprite != null ? defaultTopicSprite.name : "None")}'.");

        // Kezdetben az alapértelmezett témaképet mutatjuk (ha van)
        SetDisplayImage(defaultTopicSprite);

        // Feliratkozás a TranscriptLogger eseményére
        if (TranscriptLogger.Instance != null)
        {
            TranscriptLogger.Instance.OnNewAIEntryAdded -= HandleNewAIEntry;
            TranscriptLogger.Instance.OnNewAIEntryAdded += HandleNewAIEntry;
            Debug.Log("[LectureImageController] Subscribed to TranscriptLogger.OnNewAIEntryAdded event.");
        }
        else
        {
            Debug.LogError("[LectureImageController] TranscriptLogger.Instance is null during initialization! Cannot subscribe to AI entries.", this);
        }
    }

    // --- Eseménykezelő ---

    private void HandleNewAIEntry(string aiText)
    {
        // Debug.Log($"[LectureImageController] Received AI text: '{aiText}'"); // Opcionális logolás
        CheckForKeywords(aiText);
    }

    // --- Képfrissítési Logika ---

    private void CheckForKeywords(string text)
    {
        if (displayImage == null || currentKeywordImages == null || currentKeywordImages.Count == 0)
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(text))
        {
            return;
        }

        // A szöveget kisbetűssé alakítjuk az érzéketlen összehasonlításhoz
        string processedText = text.ToLowerInvariant();
        bool keywordFound = false;

        foreach (var pair in currentKeywordImages)
        {
            if (!string.IsNullOrEmpty(pair.keyword) && processedText.Contains(pair.keyword.ToLowerInvariant()))
            {
                Debug.Log($"[LectureImageController] Keyword '{pair.keyword}' found in text. Setting image to '{(pair.image != null ? pair.image.name : "None")}'.");
                SetDisplayImage(pair.image);
                keywordFound = true;
                break;
            }
        }

        // Opcionális: Ha egyáltalán nem találtunk kulcsszót a mondatban,
        // visszaállíthatjuk az alapértelmezett képet, vagy hagyhatjuk az utolsó kulcsszó képét.
        // Ez attól függ, milyen viselkedést szeretnél.
        // Ha vissza akarod állítani:
        /*
        if (!keywordFound)
        {
            // Csak akkor állítjuk vissza, ha a jelenlegi kép NEM az alapértelmezett
            if (displayImage.sprite != defaultTopicSprite)
            {
                Debug.Log("[LectureImageController] No keyword found in the last sentence. Reverting to default topic image.");
                SetDisplayImage(defaultTopicSprite);
            }
        }
        */
        // Ha nem akarod visszaállítani, egyszerűen ne csinálj semmit, ha nem volt találat.
    }

    private void SetDisplayImage(Sprite spriteToShow)
    {
        if (displayImage == null) return;

        if (spriteToShow != null)
        {
            displayImage.sprite = spriteToShow;
            displayImage.enabled = true; // Jelenítsük meg, ha van kép
        }
        else
        {
            // Ha null sprite-ot kapunk (pl. nincs alap kép, vagy a kulcsszóhoz nincs kép rendelve)
            displayImage.sprite = null;
            displayImage.enabled = false; // Rejtsük el
        }
    }

    // --- Tisztítás ---

    void OnDestroy()
    {
        // Nagyon fontos leiratkozni az eseményről, amikor az objektum megszűnik,
        // hogy elkerüljük a memóriaszivárgást és a hibákat.
        if (TranscriptLogger.Instance != null)
        {
            TranscriptLogger.Instance.OnNewAIEntryAdded -= HandleNewAIEntry;
            Debug.Log("[LectureImageController] Unsubscribed from TranscriptLogger event on destroy.");
        }

        // if (Instance == this) Instance = null; // Singleton takarítás
    }

    // Opcionális: Ha az objektum inaktívvá válik, akkor is leiratkozhatunk,
    // és újra fel, amikor aktívvá válik. Ez akkor lehet hasznos, ha az objektum
    // életciklusa bonyolultabb.
    // void OnDisable()
    // {
    //     if (TranscriptLogger.Instance != null)
    //     {
    //         TranscriptLogger.Instance.OnNewAIEntryAdded -= HandleNewAIEntry;
    //     }
    // }
    // void OnEnable()
    // {
    //     // Itt újra fel kellene iratkozni, DE csak ha már inicializálva lettünk!
    //     // Ez bonyolultabbá teszi, maradjunk az Initialize/OnDestroy párosnál egyelőre.
    // }
}