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

    private HashSet<string> shownKeywordsInCurrentLecture;

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
            this.shownKeywordsInCurrentLecture = new HashSet<string>();
            return;
        }

        this.displayImage = imageComponent;
        this.defaultTopicSprite = topicConfig.topicImage;
        this.currentKeywordImages = topicConfig.keywordImages ?? new List<KeywordImagePair>();
        this.shownKeywordsInCurrentLecture = new HashSet<string>();

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

        string processedText = text.ToLowerInvariant();
        // bool keywordFoundThisSentence = false; // Erre már nincs szükség, ha nem akarunk alapra visszaállítani

        foreach (var pair in currentKeywordImages)
        {
            if (string.IsNullOrEmpty(pair.keyword)) continue;

            string lowerKeyword = pair.keyword.ToLowerInvariant();

            if (shownKeywordsInCurrentLecture.Contains(lowerKeyword))
            {
                continue;
            }

            if (processedText.Contains(lowerKeyword))
            {
                Debug.Log($"[LectureImageController] Keyword '{pair.keyword}' found in text. Setting image to '{(pair.image != null ? pair.image.name : "None")}'.");
                SetDisplayImage(pair.image);

                shownKeywordsInCurrentLecture.Add(lowerKeyword);
                Debug.Log($"[LectureImageController] Keyword '{pair.keyword}' added to shown list for this lecture.");
                
                break;
            }
        }
    }

    private void SetDisplayImage(Sprite spriteToShow)
    {
        if (displayImage == null) return;

        if (spriteToShow != null)
        {
            if (displayImage.sprite != spriteToShow) // Csak akkor frissíts, ha tényleg más a kép
            {
                displayImage.sprite = spriteToShow;
                // Debug.Log($"[LectureImageController] Display image set to '{spriteToShow.name}'.");
            }
            displayImage.enabled = true;
        }
        else
        {
            if (displayImage.sprite != null) // Csak akkor frissíts, ha tényleg más a kép (vagy volt kép)
            {
                displayImage.sprite = null;
                // Debug.Log("[LectureImageController] Display image cleared (sprite set to null).");
            }
            displayImage.enabled = false;
        }
    }

    // --- Tisztítás ---

    void OnDestroy()
    {
        if (TranscriptLogger.Instance != null)
        {
            TranscriptLogger.Instance.OnNewAIEntryAdded -= HandleNewAIEntry;
            Debug.Log("[LectureImageController] Unsubscribed from TranscriptLogger event on destroy.");
        }
    }
}