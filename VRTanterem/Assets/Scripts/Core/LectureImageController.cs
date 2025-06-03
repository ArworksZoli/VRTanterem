using UnityEngine;
using UnityEngine.UI;
using System.Collections;
using System.Collections.Generic;
using System.Linq;

public class LectureImageController : MonoBehaviour
{
    // --- Referenciák ---
    [Header("Assign Automatically (Usually)")]
    [Tooltip("A UI Image komponens, amin a képek megjelennek. Az AppStateManager adja át.")]
    [SerializeField] private Image displayImage;

    [Header("Image Display Settings")]
    [Tooltip("Késleltetés másodpercben, mielőtt a kulcsszóhoz tartozó kép megjelenik a detektálás után.")]
    [SerializeField] private float imageDisplayDelay = 0f;

    // --- Belső Állapot ---
    private List<KeywordImagePair> currentKeywordImages;
    private Sprite defaultTopicSprite;
    private HashSet<string> shownKeywordsInCurrentLecture;
    private Coroutine activeDisplayCoroutine = null;

    // --- Inicializálás ---

    public void InitializeForTopic(Image imageComponent, TopicConfig topicConfig)
    {
        if (activeDisplayCoroutine != null)
        {
            StopCoroutine(activeDisplayCoroutine);
            activeDisplayCoroutine = null;
            Debug.Log("[LectureImageController_LOG] Stopped previous image display coroutine during initialization.");
        }

        if (imageComponent == null)
        {
            Debug.LogError("[LectureImageController_LOG] InitializeForTopic: Display Image component is null! Cannot function.", this);
            enabled = false; // Letiltjuk, ha nincs kép
            return;
        }
        if (topicConfig == null)
        {
            Debug.LogError("[LectureImageController_LOG] InitializeForTopic: TopicConfig is null! Cannot load keyword images.", this);
            
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

        Debug.Log($"[LectureImageController_LOG] Initialized for topic '{topicConfig.topicName}'. Found {currentKeywordImages.Count} keyword images. Default sprite set to '{(defaultTopicSprite != null ? defaultTopicSprite.name : "None")}'. Delay: {imageDisplayDelay}s.");

        // Kezdetben az alapértelmezett témaképet mutatjuk (ha van)
        SetDisplayImage(defaultTopicSprite);

        // Feliratkozás a TranscriptLogger eseményére
        if (TranscriptLogger.Instance != null)
        {
            TranscriptLogger.Instance.OnNewAIEntryAdded -= HandleNewAIEntry;
            TranscriptLogger.Instance.OnNewAIEntryAdded += HandleNewAIEntry;
            Debug.Log("[LectureImageController_LOG] Subscribed to TranscriptLogger.OnNewAIEntryAdded event.");
        }
        else
        {
            Debug.LogError("[LectureImageController_LOG] TranscriptLogger.Instance is null during initialization! Cannot subscribe to AI entries.", this);
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
        if (displayImage == null || currentKeywordImages == null || currentKeywordImages.Count == 0) return;
        if (string.IsNullOrWhiteSpace(text)) return;

        string processedText = text.ToLowerInvariant();

        foreach (var pair in currentKeywordImages)
        {
            if (string.IsNullOrEmpty(pair.keyword)) continue;

            string lowerKeyword = pair.keyword.ToLowerInvariant();

            if (shownKeywordsInCurrentLecture.Contains(lowerKeyword)) continue;

            if (processedText.Contains(lowerKeyword))
            {
                Debug.Log($"[LectureImageController_LOG] Keyword '{pair.keyword}' found. Scheduling image '{(pair.image != null ? pair.image.name : "None")}' with delay: {imageDisplayDelay}s.");

                // Leállítjuk az előzőleg futó késleltetett kép megjelenítést, ha volt
                if (activeDisplayCoroutine != null)
                {
                    StopCoroutine(activeDisplayCoroutine);
                    // Debug.Log("[LectureImageController_LOG] Stopped previous pending display coroutine.");
                }

                // Elindítjuk az új korutint a kép késleltetett megjelenítésére
                activeDisplayCoroutine = StartCoroutine(DisplayImageAfterDelayCoroutine(pair.image));

                shownKeywordsInCurrentLecture.Add(lowerKeyword);
                Debug.Log($"[LectureImageController_LOG] Keyword '{pair.keyword}' added to shown list for this lecture.");

                break; // Megtaláltuk az első új kulcsszót, kilépünk a ciklusból
            }
        }
    }

    private IEnumerator DisplayImageAfterDelayCoroutine(Sprite spriteToShow)
    {
        // Debug.Log($"[LectureImageController_LOG] Coroutine: Waiting {imageDisplayDelay}s to display '{(spriteToShow != null ? spriteToShow.name : "None")}'.");

        // Csak akkor várunk, ha a késleltetés nagyobb mint 0
        if (imageDisplayDelay > 0.001f) // Kis epsilon érték a float összehasonlításhoz
        {
            yield return new WaitForSeconds(imageDisplayDelay);
        }

        // Debug.Log($"[LectureImageController_LOG] Coroutine: Delay finished. Setting image to '{(spriteToShow != null ? spriteToShow.name : "None")}'.");
        SetDisplayImage(spriteToShow);
        activeDisplayCoroutine = null; // Korutin befejeződött, töröljük a referenciát
    }

    private void SetDisplayImage(Sprite spriteToShow)
    {
        if (displayImage == null) return;

        if (spriteToShow != null)
        {
            if (displayImage.sprite != spriteToShow)
            {
                displayImage.sprite = spriteToShow;
            }
            displayImage.enabled = true;
        }
        else
        {
            if (displayImage.sprite != null)
            {
                displayImage.sprite = null;
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
            Debug.Log("[LectureImageController_LOG] Unsubscribed from TranscriptLogger event on destroy.");
        }

        if (activeDisplayCoroutine != null)
        {
            StopCoroutine(activeDisplayCoroutine);
            activeDisplayCoroutine = null;
            // Debug.Log("[LectureImageController_LOG] Stopped active image display coroutine on destroy.");
        }
    }
}