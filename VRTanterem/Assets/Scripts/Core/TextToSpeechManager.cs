using UnityEngine;
using System.Runtime.InteropServices;
using UnityEngine.UI;
using TMPro;
using System.Collections;
using System.Text;
using System;
using System.Collections.Generic;

public class TextToSpeechManager : MonoBehaviour
{
    // JavaScript függvények importálása
#if UNITY_WEBGL && !UNITY_EDITOR
    [DllImport("__Internal")]
    private static extern bool InitializeSpeechSynthesis();
    
    [DllImport("__Internal")]
    private static extern void SpeakText(string text, float rate, float pitch, float volume, string language);
    
    [DllImport("__Internal")]
    private static extern void CancelSpeech();
    
    [DllImport("__Internal")]
    private static extern void PauseSpeech();
    
    [DllImport("__Internal")]
    private static extern void ResumeSpeech();
    
    [DllImport("__Internal")]
    private static extern bool IsSpeaking();
#endif

    // OpenAIWebRequest referencia
    [SerializeField] private OpenAIWebRequest openAIRequest;

    // Beszéd paraméterei
    [SerializeField] private float speechRate = 1.0f;
    [SerializeField] private float speechPitch = 1.0f;
    [SerializeField] private float speechVolume = 1.0f;
    [SerializeField] private string languageCode = "hu-HU"; // Magyar nyelv kódja

    // Felolvasási beállítások
    [SerializeField] private int optimalChunkSize = 300;        // Optimális felolvasási blokkméret karakterekben (100-ról növelve)
    [SerializeField] private float minSpeechDelay = 0.05f;      // Minimális várakozás a beszéd motornak (másodperc)
    [SerializeField] private bool allowPartialSentences = true; // Engedélyezzük-e a mondatok feldarabolását

    // UI elemek (opcionális)
    [SerializeField] private Button pauseButton;
    [SerializeField] private Button stopButton;
    [SerializeField] private Toggle autoPlayToggle;
    [SerializeField] private Image pauseButtonIcon;
    [SerializeField] private Image stopButtonIcon;
    [SerializeField] private Sprite pauseSprite;
    [SerializeField] private Sprite resumeSprite;
    [SerializeField] private Sprite playSprite;

    // Párhuzamos szövegfelolvasás változói
    private StringBuilder accumulatedText = new StringBuilder(); // Teljes összegyűjtött szöveg
    private int lastReadPosition = 0;                           // Utolsó felolvasott pozíció
    private bool isSpeaking = false;                            // Aktív felolvasás jelzője
    private bool isInitialized = false;                         // Inicializálási állapot
    private string currentConversationId = "";                  // Aktuális beszélgetés azonosítója
    private bool processorActive = false;                       // Beszédprocesszor aktív-e
    private bool stopRequested = false;                         // Teljes leállítás kérésének jelzése

    // Állapot kezeléshez szükséges változók
    private bool isPaused = false;
    private bool autoPlayEnabled = true;  // Alapértelmezetten bekapcsolva

    // Elválasztó karakterek a mondatok végének meghatározásához (erős határok)
    private readonly char[] sentenceEndChars = new char[] { '.', '!', '?', '\n' };

    // Elválasztó karakterek gyengébb határok meghatározásához (kötőjel eltávolítva)
    private readonly char[] weakBreakChars = new char[] { ':', ';', ',', '(', ')', '{', '}', '[', ']' };

    // Beszéd feldolgozó coroutine referencia
    private Coroutine speechProcessorCoroutine = null;

    void Start()
    {
        Debug.Log("[TextToSpeechManager] Folyamatos szövegtárolós felolvasás inicializálása kezdődik");

        // Beszédszintézis inicializálása
#if UNITY_WEBGL && !UNITY_EDITOR
        try {
            isInitialized = InitializeSpeechSynthesis();
            Debug.Log("[TextToSpeechManager] Beszédszintézis támogatás: " + isInitialized);
        } catch (Exception e) {
            Debug.LogError("[TextToSpeechManager] Hiba a beszédszintézis inicializálásakor: " + e.Message);
            isInitialized = false;
        }
#else
        Debug.Log("[TextToSpeechManager] Beszédszintézis csak WebGL buildekben működik");
        isInitialized = false;
#endif

        // UI elemek inicializálása
        InitializeUI();

        // Beszédprocesszor coroutine indítása a megfelelő ponton
        StartSpeechProcessor();

        Debug.Log("[TextToSpeechManager] Inicializálás befejezve");
    }

    // Play gomb funkció - mindig újrakezdi a szöveg felolvasását az elejétől
    public void RestartSpeechFromBeginning()
    {
        Debug.Log("[TextToSpeechManager] Felolvasás újraindítása az elejétől");

        // Leállítjuk az aktuális beszédet, ha éppen aktív
        if (isSpeaking || isPaused)
        {
#if UNITY_WEBGL && !UNITY_EDITOR
        try {
            CancelSpeech();
        } catch (Exception e) {
            Debug.LogError("[TextToSpeechManager] Hiba a felolvasás leállításakor: " + e.Message);
        }
#endif
            // Ha szüneteltetve van, visszaállítjuk az alapértelmezett állapotot
            if (isPaused)
            {
                isPaused = false;
                UpdatePauseButtonVisual(false);
            }
        }

        // Alaphelyzetbe állítjuk az olvasási pozíciót
        lastReadPosition = 0;

        // Alaphelyzetbe állítjuk a leállítási kérést
        stopRequested = false;
        isSpeaking = false;

        // Teljesen leállítjuk a beszédprocesszort, ha fut
        if (speechProcessorCoroutine != null)
        {
            StopCoroutine(speechProcessorCoroutine);
            speechProcessorCoroutine = null;
        }

        // Rövid várakozás után újraindítjuk a processort
        StartCoroutine(RestartAfterDelay());
    }

    private IEnumerator RestartAfterDelay()
    {
        yield return new WaitForSeconds(0.3f);

        // Újraindítjuk a beszédprocesszort
        processorActive = false;

        // Csak akkor indítjuk el a beszédet, ha van mit felolvasni
        if (accumulatedText.Length > 0)
        {
            StartSpeechProcessor();
            Debug.Log("[TextToSpeechManager] Beszédprocesszor újraindítva");
        }
        else
        {
            Debug.Log("[TextToSpeechManager] Nincs felolvasandó szöveg");
        }
    }

    private void InitializeUI()
    {
        // Szünet gomb inicializálása (maradhat változatlan)
        if (pauseButton != null)
        {
            pauseButton.onClick.RemoveAllListeners();
            pauseButton.onClick.AddListener(TogglePause);
            UpdatePauseButtonVisual(false);
        }

        // Play gomb inicializálása (egyszerűsítve)
        if (stopButton != null)
        {
            // Átnevezzük a kommentekben a jobb érthetőség érdekében
            // Play gomb inicializálása
            stopButton.onClick.RemoveAllListeners();
            stopButton.onClick.AddListener(RestartSpeechFromBeginning);

            // Beállítjuk a Play ikont a gombra (fix ikon)
            if (stopButtonIcon != null && playSprite != null)
            {
                stopButtonIcon.sprite = playSprite;
            }
        }

        // Automatikus lejátszás kapcsoló inicializálása (maradhat változatlan)
        if (autoPlayToggle != null)
        {
            autoPlayToggle.onValueChanged.RemoveAllListeners();
            autoPlayToggle.isOn = autoPlayEnabled;
            autoPlayToggle.onValueChanged.AddListener(OnAutoPlayToggleChanged);
        }
    }

    // Szünet/folytatás állapot váltása
    public void TogglePause()
    {
        if (isPaused)
        {
            // Folytatás
            ResumeSpeaking();
            isPaused = false;
        }
        else
        {
            // Szüneteltetés
            PauseSpeaking();
            isPaused = true;
        }

        // Frissítjük a gomb megjelenését
        UpdatePauseButtonVisual(isPaused);

        Debug.Log($"[TextToSpeechManager] Beszéd {(isPaused ? "szüneteltetve" : "folytatva")}");
    }

    // Gomb megjelenésének frissítése
    private void UpdatePauseButtonVisual(bool paused)
    {
        if (pauseButtonIcon != null)
        {
            // A gomb ikonját frissítjük a szünet/folytatás állapot alapján
            // Feltételezve, hogy két különböző sprite áll rendelkezésre
            pauseButtonIcon.sprite = paused ? resumeSprite : pauseSprite;
        }
    }

    // Automatikus lejátszás állapot kezelése
    private void OnAutoPlayToggleChanged(bool isOn)
    {
        autoPlayEnabled = isOn;
        Debug.Log($"[TextToSpeechManager] Automatikus felolvasás: {(autoPlayEnabled ? "bekapcsolva" : "kikapcsolva")}");

        // Ha kikapcsolták az automatikus lejátszást és éppen folyamatban van
        if (!autoPlayEnabled && isSpeaking)
        {
            // Leállítjuk a beszédet
            StopSpeaking();
        }
        // Ha bekapcsolták és van feldolgozatlan szöveg
        else if (autoPlayEnabled && accumulatedText.Length > lastReadPosition)
        {
            // Először állítsuk le a beszédet, ha már fut
            if (isSpeaking || isPaused)
            {
                StopSpeaking();

                // Rövid várakozás után indítjuk újra
                StartCoroutine(DelayedRestart());
            }
            else
            {
                // Újraindítjuk a beszédet
                RestartSpeechFromBeginning();
            }
        }
    }

    private IEnumerator DelayedRestart()
    {
        yield return new WaitForSeconds(0.5f);
        RestartSpeechFromBeginning();
    }

    // Beszédprocesszor indítása
    private void StartSpeechProcessor()
    {
        if (!processorActive)
        {
            if (speechProcessorCoroutine != null)
            {
                StopCoroutine(speechProcessorCoroutine);
            }

            speechProcessorCoroutine = StartCoroutine(SpeechProcessor());
            processorActive = true;
        }
    }

    // Várakozás a beszéd befejezésére - javított verzió a yield-hiba nélkül
    private IEnumerator WaitForSpeechToComplete(string spokenText)
    {
        float startTime = Time.time;
        float estimatedDuration = EstimateSpeechDuration(spokenText);
        float pauseStartTime = 0;
        float totalPauseTime = 0;

        // Növelt minimum várakozás a teljesebb felolvasás érdekében
        float minWaitTime = estimatedDuration * 1.05f;
        float maxWaitTime = estimatedDuration * 1.2f;
        bool speechFinished = false;

        Debug.Log($"[TextToSpeechManager] Becsült beszédidő: {estimatedDuration} másodperc, max várakozás: {maxWaitTime} másodperc");

        // Kezdeti kötelező várakozás - megnövelt minimum várakozási idő
        float timeElapsed = 0;
        while (timeElapsed < minWaitTime)
        {
            // Ha szüneteltetve van, mérjük a szüneteltetés idejét
            if (isPaused)
            {
                if (pauseStartTime == 0) pauseStartTime = Time.time;
                yield return new WaitForSeconds(0.1f);
                continue;
            }
            else if (pauseStartTime > 0)
            {
                // Szüneteltetés vége, számoljuk a szüneteltetett időt
                totalPauseTime += (Time.time - pauseStartTime);
                pauseStartTime = 0;
            }

            // Ha leállítást kértek közben
            if (stopRequested)
            {
                Debug.Log("[TextToSpeechManager] Beszéd várakozás megszakítva (leállítás miatt)");
                yield break;
            }

            yield return new WaitForSeconds(0.1f);
            timeElapsed += 0.1f;
        }

        // A minimum idő letelt, most már figyeljük a beszéd tényleges befejezését
        int consecutiveFalseCount = 0;

        while (!speechFinished && (Time.time - startTime - totalPauseTime < maxWaitTime))
        {
            // Ha szüneteltetve van
            if (isPaused)
            {
                if (pauseStartTime == 0) pauseStartTime = Time.time;
                yield return new WaitForSeconds(0.1f);
                continue;
            }
            else if (pauseStartTime > 0)
            {
                // Szüneteltetés vége, számoljuk a szüneteltetett időt
                totalPauseTime += (Time.time - pauseStartTime);
                pauseStartTime = 0;
            }

            // Ha leállítást kértek közben
            if (stopRequested)
            {
                Debug.Log("[TextToSpeechManager] Beszéd várakozás megszakítva (leállítás miatt)");
                yield break;
            }

#if UNITY_WEBGL && !UNITY_EDITOR
        bool currentSpeakingState = CheckIfStillSpeaking();
        
        if (!currentSpeakingState) {
            consecutiveFalseCount++;
            
            if (consecutiveFalseCount >= 15) {
                speechFinished = true;
            }
        } else {
            consecutiveFalseCount = 0;
        }
#else
            if (Time.time - startTime - totalPauseTime > estimatedDuration)
            {
                speechFinished = true;
            }
#endif
            yield return new WaitForSeconds(0.1f);
        }

        if (Time.time - startTime - totalPauseTime >= maxWaitTime)
        {
            Debug.Log("[TextToSpeechManager] Maximális várakozási idő letelt, folytatás");
        }
        else if (speechFinished)
        {
            Debug.Log("[TextToSpeechManager] Beszéd befejeződött, eltelt idő: " + (Time.time - startTime - totalPauseTime) + " másodperc");
        }

        yield return new WaitForSeconds(0.5f);
    }

    // Segédfüggvény a beszéd állapotának ellenőrzésére - kivételkezelt és platformfüggő változat
    private bool CheckIfStillSpeaking()
    {
        bool isSpeaking = false;

#if UNITY_WEBGL && !UNITY_EDITOR
    try
    {
        isSpeaking = IsSpeaking();
    }
    catch (Exception e)
    {
        Debug.LogWarning("[TextToSpeechManager] Hiba a beszéd állapotának ellenőrzésekor: " + e.Message);
        // Kivétel esetén feltételezzük, hogy már nem beszél
        isSpeaking = false;
    }
#else
        // Nem-WebGL környezetben mindig false-t ad vissza
        isSpeaking = false;
#endif

        return isSpeaking;
    }

    // Beszédidő becslése a szöveg alapján - szóköz-tudatos verzió
    private float EstimateSpeechDuration(string text)
    {
        if (string.IsNullOrEmpty(text))
            return 0;

        // Karakter elemzés - szóközök és egyéb nem kiejtendő karakterek számolása
        int totalLength = text.Length;
        int spaceCount = 0;
        int punctuationCount = 0;

        foreach (char c in text)
        {
            if (char.IsWhiteSpace(c))
                spaceCount++;
            else if (Array.IndexOf(sentenceEndChars, c) >= 0 || Array.IndexOf(weakBreakChars, c) >= 0)
                punctuationCount++;
        }

        // Tényleges kiejtendő karakterek száma (szóközök 95%-át figyelmen kívül hagyjuk)
        int effectiveCharCount = totalLength - (int)(spaceCount * 0.95f);

        // Alapidő számítása: gyorsabb karaktersebesség (18 karakter/másodperc)
        float baseTime = effectiveCharCount / 18.0f;

        // Szünetek számolása a központozás alapján
        float pauseTime = 0;

        foreach (char c in text)
        {
            if (Array.IndexOf(sentenceEndChars, c) >= 0)
                pauseTime += 0.3f;  // Mondatvégi szünet
            else if (Array.IndexOf(weakBreakChars, c) >= 0)
                pauseTime += 0.15f; // Gyengébb szünet
        }

        // Hosszabb szövegek esetén extra idő a beszédszintézis motornak
        float lengthFactor = Mathf.Min(0.3f, effectiveCharCount / 450f);

        // Minimum idő + a szöveg alapján becsült idő + szünetek + hossz faktor
        float estimatedTime = Mathf.Max(0.8f, baseTime * 1.1f + pauseTime + lengthFactor);
        estimatedTime += 0.7f;

        // Debug információ a becsléshez
        Debug.Log($"[Speech Estimation] Total: {totalLength}, Spaces: {spaceCount}, " +
                  $"Punctuation: {punctuationCount}, Effective: {effectiveCharCount}, " +
                  $"Base: {baseTime:F2}s, Pauses: {pauseTime:F2}s, Final: {estimatedTime:F2}s");

        return estimatedTime;
    }

    // Beszédfeldolgozó coroutine - optimalizált verzió a gyorsabb indításhoz
    private IEnumerator SpeechProcessor()
    {
        Debug.Log("[TextToSpeechManager] Beszédprocesszor coroutine elindult");
        yield return new WaitForSeconds(0.05f);

        bool isFirstEverSpeech = true;
        bool isFirstChunk = true;

        while (processorActive)
        {
            // Leállítás ellenőrzése
            if (stopRequested)
            {
                yield return new WaitForSeconds(0.2f);
                continue;
            }

            // Szüneteltetés ellenőrzése
            if (isPaused)
            {
                yield return new WaitForSeconds(0.2f);
                continue;
            }

            // Beszéd folytatása, ha nem beszélünk és van még felolvasatlan szöveg
            if (accumulatedText.Length > lastReadPosition && !isSpeaking)
            {
                int availableTextLength = accumulatedText.Length - lastReadPosition;

                if (availableTextLength > 0)
                {
                    // Első felolvasás extra késleltetése
                    if (isFirstEverSpeech)
                    {
                        yield return new WaitForSeconds(0.5f);
                        isFirstEverSpeech = false;
                    }

                    // Blokkméret beállítása
                    if (isFirstChunk)
                    {
                        optimalChunkSize = Mathf.Min(optimalChunkSize, 150);
                        yield return new WaitForSeconds(0.1f);
                        isFirstChunk = false;
                    }
                    else
                    {
                        optimalChunkSize = 300;
                    }

                    // Következő szövegrész felolvasása
                    string textToRead = GetNextTextChunk();

                    if (!string.IsNullOrEmpty(textToRead))
                    {
                        isSpeaking = true;
                        Debug.Log($"[TextToSpeechManager] Beszéd indítása ({textToRead.Length} karakter): {textToRead.Substring(0, Math.Min(30, textToRead.Length))}...");
                        Speak(textToRead);

                        yield return new WaitForSeconds(0.02f);
                        yield return StartCoroutine(WaitForSpeechToComplete(textToRead));
                        isSpeaking = false;

                        // Leállítás ellenőrzése a beszéd befejezése után
                        if (stopRequested)
                        {
                            processorActive = false;
                            break;
                        }
                    }
                }
            }

            yield return new WaitForSeconds(0.2f);
        }

        Debug.Log("[TextToSpeechManager] Beszédprocesszor leállítva");
    }

    // Következő felolvasandó szövegrész meghatározása
    private string GetNextTextChunk()
    {
        if (lastReadPosition >= accumulatedText.Length)
        {
            return "";
        }

        // Meghatározzuk a maximális karakterszámot, amit felolvasunk
        int maxChunkSize = Math.Min(optimalChunkSize, accumulatedText.Length - lastReadPosition);

        // Ha nincs elég karakter, akkor azt olvassuk fel, ami van
        if (maxChunkSize <= 0)
        {
            return "";
        }

        // Keresünk egy megfelelő határpontot a szövegben a természetes olvasáshoz
        int chunkEndPos = FindBestBreakPosition(lastReadPosition, maxChunkSize);

        // Kiolvassuk a következő felolvasandó részt
        string nextChunk = accumulatedText.ToString(lastReadPosition, chunkEndPos - lastReadPosition);

        // Frissítjük az utolsó olvasott pozíciót
        lastReadPosition = chunkEndPos;

        return nextChunk;
    }

    // Megfelelő határpont keresése a szövegben
    private int FindBestBreakPosition(int startPos, int maxLength)
    {
        // Ha a teljes szöveg végéig akarunk olvasni, vagy csak kevés szöveg van hátra
        if (startPos + maxLength >= accumulatedText.Length)
        {
            return accumulatedText.Length;
        }

        int endPos = startPos + maxLength;
        string textToAnalyze = accumulatedText.ToString(startPos, maxLength);

        // 1. Először keresünk egy valódi mondatvég karaktert (erős határok)
        for (int i = textToAnalyze.Length - 1; i >= 0; i--)
        {
            char c = textToAnalyze[i];
            if (Array.IndexOf(sentenceEndChars, c) >= 0)
            {
                // Speciális ellenőrzés a pontok esetében
                if (c == '.')
                {
                    // Ellenőrizzük, hogy nem számozott listaelem-e
                    bool isNumberedList = IsNumberedListItem(textToAnalyze, i);

                    // Ellenőrizzük, hogy nem rövidítés-e
                    bool isAbbreviation = IsAbbreviation(textToAnalyze, i);

                    // Ha listaelem vagy rövidítés, akkor nem mondatvég
                    if (isNumberedList || isAbbreviation)
                    {
                        continue; // Folytassuk a keresést
                    }
                }

                // Ha valódi mondatvég, használjuk ezt a vágási pontot
                // Mondatvég után esetleg van még whitespace, azt is vegyük bele
                int j = i + 1;
                while (j < textToAnalyze.Length && char.IsWhiteSpace(textToAnalyze[j]))
                {
                    j++;
                }
                return startPos + j;
            }
        }

        // 2. Ha nincs mondatvég, keresünk vessző, pontosvessző, kettőspont, stb. karaktert (gyenge határok)
        for (int i = textToAnalyze.Length - 1; i >= 0; i--)
        {
            char c = textToAnalyze[i];
            if (Array.IndexOf(weakBreakChars, c) >= 0)
            {
                // Bizonyosodjunk meg, hogy ez egy valódi határ (pl. nem egy számban levő vessző)
                bool isValidBreak = true;

                // Ellenőrizzük, hogy nem számok között van-e a vessző
                if (c == ',' && i > 0 && i < textToAnalyze.Length - 1)
                {
                    if (char.IsDigit(textToAnalyze[i - 1]) && char.IsDigit(textToAnalyze[i + 1]))
                    {
                        isValidBreak = false;  // Ez valószínűleg egy szám ezres elválasztója
                    }
                }

                if (isValidBreak)
                {
                    // Szünet után esetleg van még whitespace, azt is vegyük bele
                    int j = i + 1;
                    while (j < textToAnalyze.Length && char.IsWhiteSpace(textToAnalyze[j]))
                    {
                        j++;
                    }
                    return startPos + j;
                }
            }
        }

        // 3. Keresünk egy szóközt (szóhatárt) - visszafelé haladva
        // Legalább a blokk 60%-ánál legyen, hogy megfelelő méretű legyen
        int minWordBreakPos = (int)(maxLength * 0.6f);
        for (int i = textToAnalyze.Length - 1; i >= minWordBreakPos; i--)
        {
            // Szóköz vagy más whitespace karakter
            if (char.IsWhiteSpace(textToAnalyze[i]))
            {
                return startPos + i + 1; // A szóköz utáni karaktert vesszük
            }
        }

        // 4. Ha a feldarabolás engedélyezett, és nem találtunk jó határt,
        // keressünk legalább egy olyan helyet, ahol biztos nem vágunk ketté szót
        if (allowPartialSentences)
        {
            // Megnézzük a blokk végén lévő karaktert
            if (endPos < accumulatedText.Length)
            {
                // Ha a blokk végén és a következő karakternél mindkettő betű/szám, 
                // akkor szó közepén vágna - keressünk jobb helyet
                if (char.IsLetterOrDigit(textToAnalyze[textToAnalyze.Length - 1]) &&
                    char.IsLetterOrDigit(accumulatedText[endPos]))
                {
                    // Keressünk visszafelé egy nem betű/szám karaktert
                    for (int i = textToAnalyze.Length - 1; i >= 0; i--)
                    {
                        if (!char.IsLetterOrDigit(textToAnalyze[i]) && !IsHyphenInWord(textToAnalyze, i))
                        {
                            return startPos + i + 1;
                        }
                    }
                }
                else
                {
                    // A blokk végén amúgy is szóhatár van, használhatjuk
                    return endPos;
                }
            }
        }

        // 5. Ha semmilyen jó határpont nem található és a feldarabolás engedélyezett,
        // használjuk a maximális méretet
        if (allowPartialSentences)
        {
            return endPos;
        }

        // 6. Ha a feldarabolás nem engedélyezett, használjunk rövidebb blokkot
        return startPos + (maxLength / 2);
    }

    // Ellenőrzi, hogy a megadott pozícióban lévő pont egy számozott listaelem része-e
    private bool IsNumberedListItem(string text, int dotPosition)
    {
        // Ellenőrizzük, hogy ez egy pont és van-e előtte karakter
        if (dotPosition <= 0 || text[dotPosition] != '.')
            return false;

        // Először ellenőrizzük, hogy az előtte lévő karakter szám-e
        if (!char.IsDigit(text[dotPosition - 1]))
            return false;

        // Hátramegyünk, amíg számokat látunk
        int numberStart = dotPosition - 1;
        while (numberStart > 0 && char.IsDigit(text[numberStart - 1]))
        {
            numberStart--;
        }

        // Ellenőrizzük, hogy a szám előtt szóköz vagy bekezdés kezdet van-e
        if (numberStart > 0 && !char.IsWhiteSpace(text[numberStart - 1]))
            return false;

        // Ellenőrizzük, hogy a pont után szóköz és szöveg következik-e
        if (dotPosition + 1 < text.Length && char.IsWhiteSpace(text[dotPosition + 1]))
        {
            // Ez valószínűleg egy számozott listaelem
            return true;
        }

        return false;
    }

    // Ellenőrzi, hogy a megadott pozícióban lévő pont egy rövidítés része-e
    private bool IsAbbreviation(string text, int dotPosition)
    {
        // Ellenőrizzük, hogy ez egy pont és van-e előtte karakter
        if (dotPosition <= 0 || text[dotPosition] != '.')
            return false;

        // Gyakori magyar rövidítések ellenőrzése
        if (dotPosition >= 2)
        {
            // Kétbetűs rövidítések
            if (dotPosition >= 2)
            {
                string prevTwoChars = text.Substring(dotPosition - 2, 2).ToLower();
                if (prevTwoChars == "dr" || prevTwoChars == "id" || prevTwoChars == "kb" ||
                    prevTwoChars == "pl" || prevTwoChars == "vö" || prevTwoChars == "st" ||
                    prevTwoChars == "ua" || prevTwoChars == "ún" || prevTwoChars == "kft" ||
                    prevTwoChars == "bt" || prevTwoChars == "rt" || prevTwoChars == "zrt" ||
                    prevTwoChars == "ifj" || prevTwoChars == "jr" || prevTwoChars == "sr")
                {
                    // Ellenőrizzük, hogy ez egy szó vége, vagy rövidítés közepe
                    if (dotPosition + 1 >= text.Length || char.IsWhiteSpace(text[dotPosition + 1]))
                    {
                        return true;
                    }
                }
            }

            // Egybetűs rövidítések (pl. a., b., E., stb.)
            if (dotPosition >= 1 && char.IsLetter(text[dotPosition - 1]))
            {
                // Ha egyetlen betű van a pont előtt és előtte szóköz vagy szöveg kezdete
                if (dotPosition == 1 || char.IsWhiteSpace(text[dotPosition - 2]))
                {
                    return true;
                }
            }
        }

        return false;
    }

    // Ellenőrzi, hogy a kötőjel szón belül van-e (pl. "web-alapú" vs. "ez - az")
    // Ellenőrzi, hogy a kötőjel szón belül van-e (pl. "web-alapú" vs. "ez - az")
    private bool IsHyphenInWord(string text, int hyphenPosition)
    {
        if (hyphenPosition < 0 || hyphenPosition >= text.Length || text[hyphenPosition] != '-')
            return false;

        // Ellenőrizzük, hogy betűk vannak-e a kötőjel mindkét oldalán
        bool hasLetterBefore = false;
        bool hasLetterAfter = false;

        // Ellenőrizzük a kötőjel előtti karaktereket
        for (int i = hyphenPosition - 1; i >= 0 && !char.IsWhiteSpace(text[i]); i--)
        {
            if (char.IsLetter(text[i]))
            {
                hasLetterBefore = true;
                break;
            }
        }

        // Ellenőrizzük a kötőjel utáni karaktereket
        for (int i = hyphenPosition + 1; i < text.Length && !char.IsWhiteSpace(text[i]); i++)
        {
            if (char.IsLetter(text[i]))
            {
                hasLetterAfter = true;
                break;
            }
        }

        // Ha mindkét oldalon betűk vannak szóköz nélkül, akkor ez egy kötőjeles szó
        return hasLetterBefore && hasLetterAfter;
    }

    // Szövegfelolvasás
    public void Speak(string text)
    {
        if (string.IsNullOrEmpty(text))
        {
            Debug.Log("[TextToSpeechManager] Nincs felolvasandó szöveg");
            return;
        }

#if UNITY_WEBGL && !UNITY_EDITOR
        if (isInitialized)
        {
            try {
                Debug.Log("[TextToSpeechManager] Felolvasás: " + text.Substring(0, Math.Min(30, text.Length)) + "...");
                SpeakText(text, speechRate, speechPitch, speechVolume, languageCode);
            } catch (Exception e) {
                Debug.LogError("[TextToSpeechManager] Hiba a felolvasás során: " + e.Message);
                isSpeaking = false;
            }
        }
        else
        {
            Debug.LogWarning("[TextToSpeechManager] A beszédszintézis nem inicializált vagy nem támogatott");
            isSpeaking = false;
        }
#else
        Debug.Log("[TextToSpeechManager] Beszédszintézis csak WebGL buildekben működik. Szöveg: " + text);

        // Nem-WebGL környezetben szimuláljuk a beszédet
        StartCoroutine(SimulateSpeech(text));
#endif
    }

#if !UNITY_WEBGL || UNITY_EDITOR
    // Beszéd szimulációja nem-WebGL környezetben
    private IEnumerator SimulateSpeech(string text)
    {
        float simulatedDuration = EstimateSpeechDuration(text);
        yield return new WaitForSeconds(simulatedDuration);
        isSpeaking = false;
    }
#endif

    // Felolvasás leállítása
    public void StopSpeaking()
    {
        Debug.Log("[TextToSpeechManager] Felolvasás leállítása...");
        stopRequested = true;

#if UNITY_WEBGL && !UNITY_EDITOR
    try {
        CancelSpeech();
    } catch (Exception e) {
        Debug.LogError("[TextToSpeechManager] Hiba a felolvasás leállításakor: " + e.Message);
    }
#endif
        isSpeaking = false;

        // Teljesen leállítjuk a beszédprocesszort
        if (speechProcessorCoroutine != null)
        {
            StopCoroutine(speechProcessorCoroutine);
            speechProcessorCoroutine = null;
        }

        processorActive = false;

        // Ha szüneteltetve volt, visszaállítjuk alaphelyzetbe
        if (isPaused)
        {
            isPaused = false;
            UpdatePauseButtonVisual(false);
        }

        // Eltávolítjuk ezt a sort: UpdateStopButtonVisual(false);

        StartCoroutine(CleanupAfterStop());
    }

    private IEnumerator CleanupAfterStop()
    {
        // Rövid várakozás a rendszer stabilizálódásához
        yield return new WaitForSeconds(0.5f);

        // Alaphelyzetbe állítás
        stopRequested = false;
    }

    private IEnumerator RestartSpeechProcessor()
    {
        // Várunk egy kicsit a rendszer stabilizálódására
        yield return new WaitForSeconds(1.5f);

        // Újraindítjuk a beszédprocesszort
        processorActive = false;
        StartSpeechProcessor();
    }

    // Felolvasás szüneteltetése
    public void PauseSpeaking()
    {
#if UNITY_WEBGL && !UNITY_EDITOR
        PauseSpeech();
#endif
    }

    // Felolvasás folytatása
    public void ResumeSpeaking()
    {
#if UNITY_WEBGL && !UNITY_EDITOR
        ResumeSpeech();
#endif
    }

    // Új AI válasz feldolgozása
    public void ReadAIResponse(string response)
    {
        // Ha üres a válasz, kilépünk
        if (string.IsNullOrEmpty(response))
        {
            return;
        }

        // Ellenőrizzük, hogy új beszélgetésről van-e szó
        if (IsNewConversation(response))
        {
            Debug.Log("[TextToSpeechManager] Új beszélgetés kezdődik");
            ResetConversation();
            currentConversationId = CreateConversationId(response);
        }

        // Kiszámítjuk az új szöveget (ha van)
        string newText = ExtractNewContent(response);

        // Ha van új tartalom, hozzáadjuk a tárolt szöveghez
        if (!string.IsNullOrEmpty(newText))
        {
            AppendNewContent(newText);
        }

        // Csak akkor indítjuk el a felolvasást, ha az automatikus lejátszás engedélyezett
        if (autoPlayEnabled)
        {
            // Alaphelyzetbe állítjuk a leállítási kérést
            stopRequested = false;

            // Ha a processzor le van állítva, újraindítjuk
            if (!processorActive)
            {
                StartSpeechProcessor();
                // Eltávolítjuk ezt a sort: UpdateStopButtonVisual(true);
            }
        }
    }

    // Új tartalom kinyerése a teljes válaszból
    private string ExtractNewContent(string fullResponse)
    {
        if (accumulatedText.Length == 0)
        {
            // Ha még nincs tartalmi előzmény, a teljes szöveg új
            return fullResponse;
        }

        // Ellenőrizzük, hogy a válasz tartalmazza-e az eddigi szöveget
        string currentText = accumulatedText.ToString();

        if (fullResponse.Length > currentText.Length && fullResponse.StartsWith(currentText))
        {
            // Ha folytatódik a szöveg, csak az újat vesszük
            return fullResponse.Substring(currentText.Length);
        }
        else if (fullResponse != currentText)
        {
            // Váratlan tartalom, valószínűleg egy átszervezés vagy frissítés
            Debug.Log("[TextToSpeechManager] Tartalom váratlanul megváltozott, teljes újraszinkronizálás");
            return fullResponse;
        }

        // Nincs új tartalom
        return "";
    }

    // Új tartalom hozzáadása a tárolt szöveghez
    private void AppendNewContent(string newContent)
    {
        if (!string.IsNullOrEmpty(newContent))
        {
            Debug.Log($"[TextToSpeechManager] Új tartalom hozzáadása ({newContent.Length} karakter): {newContent.Substring(0, Math.Min(30, newContent.Length))}...");
            accumulatedText.Append(newContent);
        }
    }

    // Ellenőrzi, hogy új beszélgetésről van-e szó
    private bool IsNewConversation(string response)
    {
        // Ha nincs előzmény, biztosan új beszélgetés
        if (accumulatedText.Length == 0)
        {
            return true;
        }

        // Ha a válasz rövidebb, mint az eddigi tartalom, valószínűleg új beszélgetés
        if (response.Length < accumulatedText.Length / 2)
        {
            return true;
        }

        // Ha a válasz tartalmaz előtagot az eddigi tartalomból, de jelentősen eltér
        string currentText = accumulatedText.ToString();
        int commonPrefixLength = GetCommonPrefixLength(currentText, response);

        if (commonPrefixLength < currentText.Length * 0.7)
        {
            return true;
        }

        // Egyezik az aktuális beszélgetéssel
        return false;
    }

    // Két szöveg közös előtagjának hosszát adja vissza
    private int GetCommonPrefixLength(string a, string b)
    {
        int minLength = Math.Min(a.Length, b.Length);
        int i = 0;

        while (i < minLength && a[i] == b[i])
        {
            i++;
        }

        return i;
    }

    // Beszélgetés azonosító létrehozása
    private string CreateConversationId(string text)
    {
        // Egyszerű hash a tartalom alapján
        return text.Length + "-" + DateTime.Now.Ticks;
    }

    // Beszélgetés alaphelyzetbe állítása
    private void ResetConversation()
    {
        // Leállítjuk a beszédet, ha fut
        StopSpeaking();

        // Alaphelyzetbe állítjuk a változókat
        accumulatedText.Clear();
        lastReadPosition = 0;
        currentConversationId = "";
    }

    // Tisztítás
    private void OnDestroy()
    {
        processorActive = false;

        if (speechProcessorCoroutine != null)
        {
            StopCoroutine(speechProcessorCoroutine);
        }

        StopSpeaking();
    }
}