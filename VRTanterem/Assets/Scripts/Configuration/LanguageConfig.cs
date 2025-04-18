﻿using UnityEngine;
using System.Collections.Generic;

[CreateAssetMenu(fileName = "NewLanguageConfig", menuName = "Configuration/Language Config", order = 1)]
public class LanguageConfig : ScriptableObject
{
    [Tooltip("A nyelv neve, ami megjelenik a felhasználói felületen.")]
    public string displayName = "Új Nyelv";

    [Tooltip("A nyelv ISO 639-1 kódja (pl. 'hu', 'en'). Ezt használhatjuk az API hívásoknál.")]
    public string languageCode = "en";

    [Tooltip("Az ezen a nyelven elérhető tantárgyak konfigurációs fájljai.")]
    public List<SubjectConfig> availableSubjects = new List<SubjectConfig>();

    [Tooltip("A prompt, ami a felhasználó kérdését kéri (pl. 'Mi a kérdésed?')")]
    public string AskQuestionPrompt;
}
