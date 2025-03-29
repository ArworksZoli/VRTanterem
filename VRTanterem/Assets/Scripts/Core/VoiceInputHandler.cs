using UnityEngine;
using Meta.WitAi;
using Meta.WitAi.Events;
using Oculus.Voice;

public class VoiceInputHandler : MonoBehaviour
{
    [SerializeField] private AppVoiceExperience appVoiceExperience;
    [SerializeField] private OpenAIWebRequest openAIWebRequest; // H�zd be az Inspectorban
    [SerializeField] private bool logRawTranscription = true;

    private void OnEnable()
    {
        if (appVoiceExperience != null)
        {
            appVoiceExperience.VoiceEvents.OnFullTranscription.AddListener(HandleFullTranscription);
            // Esetleg tov�bbi esem�nyek kezel�se (opcion�lis)
            appVoiceExperience.VoiceEvents.OnError.AddListener(HandleError);
            appVoiceExperience.VoiceEvents.OnRequestCompleted.AddListener(HandleRequestCompleted);
        }
        else
        {
            Debug.LogError("AppVoiceExperience is not assigned!");
        }

        if (openAIWebRequest == null)
        {
            Debug.LogError("OpenAIWebRequest is not assigned!");
        }
    }

    private void OnDisable()
    {
        if (appVoiceExperience != null)
        {
            appVoiceExperience.VoiceEvents.OnFullTranscription.RemoveListener(HandleFullTranscription);
            appVoiceExperience.VoiceEvents.OnError.RemoveListener(HandleError);
            appVoiceExperience.VoiceEvents.OnRequestCompleted.RemoveListener(HandleRequestCompleted);
        }
    }

    private void HandleFullTranscription(string text)
    {
        if (logRawTranscription)
        {
            Debug.Log($"Full Transcription: {text}");
        }

        if (!string.IsNullOrWhiteSpace(text) && openAIWebRequest != null)
        {
            openAIWebRequest.ProcessVoiceInput(text);
        }
    }

    private void HandleError(string error, string message)
    {
        Debug.LogError($"Voice SDK Error: {error} - {message}");
    }

    private void HandleRequestCompleted()
    {
        Debug.Log("Voice SDK Request Completed.");
        // Itt lehet pl. �jraaktiv�lni a figyel�st, ha sz�ks�ges
    }

    // Hozz�adhatsz egy met�dust, amit VR kontroller gombj�ra k�thetsz
    // az AppVoiceExperience Activate() �s Deactivate() met�dusainak h�v�s�hoz
    public void ActivateListening()
    {
        if (appVoiceExperience != null && !appVoiceExperience.Active)
        {
            Debug.Log("Activating Voice Input...");
            appVoiceExperience.Activate();
        }
    }

    public void DeactivateListening()
    {
        if (appVoiceExperience != null && appVoiceExperience.Active)
        {
            Debug.Log("Deactivating Voice Input...");
            appVoiceExperience.Deactivate(); // Vagy DeactivateAndSend() ha az utols� hangot is fel akarod dolgozni
        }
    }
}
