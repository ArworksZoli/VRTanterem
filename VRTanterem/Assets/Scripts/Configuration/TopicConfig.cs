using UnityEngine;
using System.Collections.Generic;

// Ez az attribútum lehetővé teszi, hogy a Unity Editorban
// jobb klikk -> Create -> Configuration -> Topic menüponttal hozzunk létre ilyen objektumot.
[CreateAssetMenu(fileName = "NewTopicConfig", menuName = "Configuration/Topic Config", order = 3)]
public class TopicConfig : ScriptableObject
{
    [Tooltip("A téma neve, ami megjelenik a felhasználói felületen.")]
    public string topicName = "Új Téma";

    [Tooltip("Az ehhez a témához tartozó OpenAI Assistant ID.")]
    public string assistantId = "asst_..."; // Fontos: Ide kell majd beírni a valódi ID-t

    [Tooltip("Az ehhez a témához választható OpenAI Voice ID-k (pl. alloy, echo, fable, onyx, nova, shimmer).")]
    public List<string> availableVoiceIds = new List<string> { "alloy", "shimmer" }; // Alapértelmezettként kettő, de szerkeszthető

    [Tooltip("A témához kapcsolódó kép, ami a táblán jelenik meg. A kép Texture Type-jának 'Sprite (2D and UI)'-nak kell lennie.")]
    public Sprite topicImage;
}
