using UnityEngine;

public class CharacterAnimatorSync : MonoBehaviour
{
    [Header("Component References")]
    [Tooltip("A karakter Animator komponense.")]
    [SerializeField] private Animator characterAnimator;

    [Tooltip("A TextToSpeechManager komponens a jelenetben.")]
    [SerializeField] private TextToSpeechManager textToSpeechManager;

    // Az Animator paraméter nevének konstansként tárolása (elírások elkerülése)
    private const string IS_TALKING_PARAM = "IsTalking";

    // Az előző frame állapotának tárolása az optimalizáláshoz
    private bool wasSpeakingLastFrame = false;

    void Start()
    {
        // Ellenőrizzük a referenciákat
        if (characterAnimator == null)
        {
            Debug.LogError("[CharacterAnimatorSync] Hiba: Character Animator nincs hozzárendelve az Inspectorban!", this);
            enabled = false; // Letiltjuk a scriptet, ha nincs Animator
            return;
        }
        if (textToSpeechManager == null)
        {
            Debug.LogError("[CharacterAnimatorSync] Hiba: TextToSpeechManager nincs hozzárendelve az Inspectorban!", this);
            enabled = false; // Letiltjuk a scriptet, ha nincs TTS Manager
            return;
        }

        // Kezdeti állapot beállítása (feltételezzük, hogy induláskor nem beszél)
        InitializeAnimatorState();
    }

    void Update()
    {
        // Ellenőrizzük, hogy a TTS Manager és az AudioSource-ok érvényesek-e még
        if (textToSpeechManager == null || characterAnimator == null)
        {
            // Ha valamelyik null lett futás közben, ne csináljunk semmit
            // (A Start()-ban már lekezeltük az indulási null esetet)
            return;
        }

        // Ellenőrizzük, hogy bármelyik releváns AudioSource játszik-e le hangot
        bool isCurrentlySpeaking = IsAnyAudioSourcePlaying();

        // Csak akkor frissítjük az Animator paramétert, ha az állapot megváltozott
        if (isCurrentlySpeaking != wasSpeakingLastFrame)
        {
            // Debug.Log($"[CharacterAnimatorSync] Speaking state changed: {wasSpeakingLastFrame} -> {isCurrentlySpeaking}"); // Opcionális logolás
            characterAnimator.SetBool(IS_TALKING_PARAM, isCurrentlySpeaking);
            wasSpeakingLastFrame = isCurrentlySpeaking; // Frissítjük az eltárolt állapotot
        }
    }

    /// <summary>
    /// Ellenőrzi, hogy a TextToSpeechManager által kezelt fő vagy prompt AudioSource éppen játszik-e le hangot.
    /// </summary>
    /// <returns>True, ha bármelyik releváns AudioSource játszik, egyébként false.</returns>
    private bool IsAnyAudioSourcePlaying()
    {
        // Biztonsági ellenőrzés, hogy a TTS Manager még létezik-e
        if (textToSpeechManager == null) return false;

        // Ellenőrizzük a fő audio source-t
        bool mainIsPlaying = textToSpeechManager.MainAudioSource != null && textToSpeechManager.MainAudioSource.isPlaying;

        // Ellenőrizzük a prompt audio source-t
        bool promptIsPlaying = textToSpeechManager.PromptAudioSource != null && textToSpeechManager.PromptAudioSource.isPlaying;

        // Akkor beszél, ha BÁRMELYIK játszik
        return mainIsPlaying || promptIsPlaying;
    }

    /// <summary>
    /// Beállítja az Animator kezdeti állapotát a biztonság kedvéért.
    /// </summary>
    private void InitializeAnimatorState()
    {
        // Lekérdezzük az aktuális állapotot, hogy elkerüljük a felesleges SetBool hívást, ha már jó
        // Bár a wasSpeakingLastFrame miatt ez nem feltétlenül szükséges, de ártani nem árt.
        try
        {
            bool initialState = IsAnyAudioSourcePlaying();
            characterAnimator.SetBool(IS_TALKING_PARAM, initialState);
            wasSpeakingLastFrame = initialState;
            // Debug.Log($"[CharacterAnimatorSync] Initial Animator state set. IsTalking: {initialState}"); // Opcionális log
        }
        catch (System.Exception e)
        {
            Debug.LogError($"[CharacterAnimatorSync] Hiba az Animator kezdeti állapotának beállításakor: {e.Message}", this);
            // Ha itt hiba van, valószínűleg az Animatorral vagy a paraméterrel van gond
            enabled = false;
        }
    }

    // Opcionális: Ha a TextToSpeechManager objektum megszűnhet,
    // érdemes lehet itt lekezelni és leállítani a figyelést.
    // void OnDisable()
    // {
    //     // Itt nem kell leiratkozni eseményekről, mert az Update-et használjuk.
    //     // Esetleg visszaállíthatjuk az animátort Idle állapotba.
    //     if (characterAnimator != null && characterAnimator.gameObject.activeInHierarchy)
    //     {
    //         characterAnimator.SetBool(IS_TALKING_PARAM, false);
    //     }
    // }
}
