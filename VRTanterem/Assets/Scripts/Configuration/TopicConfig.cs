using UnityEngine;
using System.Collections.Generic;

[System.Serializable]
public class ModeAssistantPair
{
    [Tooltip("Az interakció típusa (pl. Előadás, Vizsga).")]
    public InteractionMode Mode;

    [Tooltip("Az ehhez a módhoz tartozó egyedi OpenAI Assistant ID.")]
    public string AssistantId;
}


[CreateAssetMenu(fileName = "NewTopicConfig", menuName = "Configuration/Topic Config", order = 3)]
public class TopicConfig : ScriptableObject
{
    [Tooltip("A téma neve, ami megjelenik a felhasználói felületen.")]
    public string topicName = "Új Téma";

    [Header("Assistants for Interaction Modes")]
    [Tooltip("Adj hozzá annyi elemet, ahány módot ez a téma támogat. Minden módhoz rendelj egy egyedi Assistant ID-t.")]
    public List<ModeAssistantPair> assistantMappings = new List<ModeAssistantPair>();

    [Tooltip("Az ehhez a témához választható OpenAI Voice ID-k (pl. alloy, echo, fable, onyx, nova, shimmer).")]
    public List<string> availableVoiceIds = new List<string> { "alloy", "shimmer" };

    [Tooltip("A témához kapcsolódó kép, ami a táblán jelenik meg. A kép Texture Type-jának 'Sprite (2D and UI)'-nak kell lennie.")]
    public Sprite topicImage;

    [Header("Keyword Images")]
    [Tooltip("Kulcsszó-kép párosítások az előadás közbeni képváltáshoz. A kulcsszavakat az AI szövegében keressük.")]
    public List<KeywordImagePair> keywordImages = new List<KeywordImagePair>();
}