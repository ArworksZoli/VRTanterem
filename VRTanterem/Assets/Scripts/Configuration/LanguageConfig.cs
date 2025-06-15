using UnityEngine;
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

    [Tooltip("A 'Kérem várjon...' üzenet, ami egy készenléti panelen jelenik meg.")]
    public string PleaseWaitPrompt;

    [Tooltip("Mielőtt visszatérne az előadásra, az applikáció által beszélt promptnyelv.")]
    public string ResumeLecturePrompt;

    [Header("AI Instrukció Promptjai")]
    [Tooltip("Instrukció az AI számára, hogyan viselkedjen, miután válaszolt egy közbevetett felhasználói kérdésre/megjegyzésre. Ezt az 'additional_instructions' mezőben használjuk az OpenAI API hívásakor.")]
    public string AIInstructionOnInterruptionResponse;

    [Header("Kvíz Detektálási Kulcsszavak")]
    [Tooltip("Kifejezések, amelyekkel az AI egyértelműen kvízkérdést vezet be. Kisbetűvel, írásjelek nélkül.")]
    public List<string> ExplicitQuizIntroducers = new List<string>();

    [Tooltip("Általános kérdésfeltevő promptok, amelyek NEM kvízkérdések, még ha kérdőjellel is végződnek. Kisbetűvel, írásjelek nélkül.")]
    public List<string> GeneralQuestionPrompts = new List<string>();

    [Tooltip("Természetes AI megállás kérdése")]
    public string PromptForGeneralInquiry;
}
