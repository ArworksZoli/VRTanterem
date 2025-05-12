using UnityEngine;

[System.Serializable]
public struct KeywordImagePair
{
    [Tooltip("A kulcsszó, amire a kép megjelenik. Kisbetűs, írásjelek nélkül javasolt a könnyebb összehasonlításért.")]
    public string keyword;

    [Tooltip("A kép, ami megjelenik, ha a kulcsszó elhangzik. A kép Texture Type-jának 'Sprite (2D and UI)'-nak kell lennie.")]
    public Sprite image;
}