using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System;

public class SpeechToTextManager : MonoBehaviour
{
    [Header("UI References")]
    [SerializeField] private OpenAIWebRequest associatedTerminal;
    [SerializeField] private TMP_InputField inputField;
    [SerializeField] private Button micButton;
    [SerializeField] private Image micIcon;
    [SerializeField] private TMP_Dropdown microphoneDropdown;

    [Header("Speech Recognition Settings")]
    [SerializeField] private float micSensitivity = 0.1f;
    [SerializeField] private int recordingDuration = 5; // másodperc

    private AudioClip recordedClip;
    private bool isRecording = false;
    private string terminalId;

    void Start()
    {
        // Terminal azonosítás
        terminalId = transform.parent.name;
        Debug.Log($"SpeechToTextManager inicializálva: {terminalId}");

        // Mikrofon engedély kérése
        RequestMicrophonePermission();

        // Mikrofon gomb beállítása
        if (micButton != null)
        {
            micButton.onClick.RemoveAllListeners();
            micButton.onClick.AddListener(ToggleSpeechRecognition);
        }

        // Mikrofon dropdown inicializálása
        InitializeMicrophoneDropdown();
    }

    void RequestMicrophonePermission()
    {
#if UNITY_ANDROID && !UNITY_EDITOR
    if (!UnityEngine.Android.Permission.HasUserAuthorizedPermission(UnityEngine.Android.Permission.Microphone))
    {
        UnityEngine.Android.Permission.RequestUserPermission(UnityEngine.Android.Permission.Microphone);
    }
#endif
    }

    void InitializeMicrophoneDropdown()
    {
        if (microphoneDropdown != null)
        {
            microphoneDropdown.options.Clear();

            // Elérhető mikrofonok listázása
            string[] devices = Microphone.devices;
            if (devices.Length > 0)
            {
                foreach (string device in devices)
                {
                    microphoneDropdown.options.Add(new TMP_Dropdown.OptionData(device));
                }
            }
            else
            {
                microphoneDropdown.options.Add(new TMP_Dropdown.OptionData("Nincs mikrofon"));
            }

            microphoneDropdown.RefreshShownValue();
            microphoneDropdown.onValueChanged.AddListener(OnMicrophoneSelected);
        }
    }

    void OnMicrophoneSelected(int index)
    {
        Debug.Log($"Kiválasztott mikrofon: {Microphone.devices[index]}");
    }

    void ToggleSpeechRecognition()
    {
        if (!isRecording)
        {
            StartRecording();
        }
        else
        {
            StopRecording();
        }
    }

    void StartRecording()
    {
        // Mikrofon aktiválása
        string selectedMic = GetSelectedMicrophone();
        recordedClip = Microphone.Start(selectedMic, false, recordingDuration, 44100);

        isRecording = true;
        micIcon.color = Color.red;

        // Felvétel leállítása megadott idő után
        Invoke("StopRecording", recordingDuration);

        Debug.Log("Hangfelvétel elindítva");
    }

    void StopRecording()
    {
        if (!isRecording) return;

        // Felvétel leállítása
        Microphone.End(GetSelectedMicrophone());
        isRecording = false;
        micIcon.color = Color.white;

        // Hanganyag átalakítása szöveggé (egyszerűsített változat)
        ProcessRecordedAudio();
    }

    string GetSelectedMicrophone()
    {
        if (Microphone.devices.Length == 0) return null;

        return microphoneDropdown != null && microphoneDropdown.value < Microphone.devices.Length
            ? Microphone.devices[microphoneDropdown.value]
            : Microphone.devices[0];
    }

    void ProcessRecordedAudio()
    {
        // FONTOS: Ez egy egyszerűsített megoldás. 
        // Valós alkalmazásban speech-to-text API-t kell használni
        float[] samples = new float[recordedClip.samples];
        recordedClip.GetData(samples, 0);

        // Nagyon egyszerűsített szöveg kinyerés
        string detectedText = DetectSpeechSimple(samples);

        if (!string.IsNullOrEmpty(detectedText))
        {
            UpdateUIWithText(detectedText);
        }
    }

    string DetectSpeechSimple(float[] samples)
    {
        // Egyszerű hangerősség alapú detektálás
        float maxVolume = 0;
        foreach (float sample in samples)
        {
            maxVolume = Mathf.Max(maxVolume, Mathf.Abs(sample));
        }

        // Ha van értelmezhető hangerősség, adjunk vissza egy alapértelmezett szöveget
        return maxVolume > micSensitivity
            ? "Hangfelvétel érzékelve"
            : string.Empty;
    }

    void UpdateUIWithText(string text)
    {
        // Beviteli mező frissítése
        if (inputField != null)
        {
            inputField.text = text;
        }

        // Üzenet küldése
        if (associatedTerminal != null)
        {
            try
            {
                associatedTerminal.SendButtonClick();
                Debug.Log($"Üzenet elküldve a terminálon: {terminalId}");
            }
            catch (Exception e)
            {
                Debug.LogError($"Hiba az üzenet küldésekor: {e.Message}");
            }
        }
    }

    void OnApplicationFocus(bool hasFocus)
    {
        // Mikrofonok újraellenőrzése, ha visszatér az alkalmazás fókuszba
        if (hasFocus)
        {
            InitializeMicrophoneDropdown();
        }
    }
}