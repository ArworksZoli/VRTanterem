using UnityEngine;
using System.Collections;
using TMPro;
using UnityEngine.UI;
#if PLATFORM_ANDROID
using UnityEngine.Android;
#endif

public class MicrophoneTester : MonoBehaviour
{
    [Header("UI References")]
    public TextMeshProUGUI statusText;
    public TextMeshProUGUI deviceText;
    public TextMeshProUGUI levelText;
    public Image levelMeter;

    [Header("Settings")]
    public bool autoStartRecording = true;
    public int sampleWindow = 64;
    public float updateInterval = 0.1f;

    // Private variables
    private AudioClip microphoneClip;
    private string selectedDevice;
    private bool isRecording = false;
    private float[] samples;
    private float currentLevel = 0f;
    private float peakLevel = 0f;

    void Start()
    {
        // Inicializálás
        samples = new float[sampleWindow];

        // Mikrofon engedély kérése Androidon
#if PLATFORM_ANDROID
        if (!Permission.HasUserAuthorizedPermission(Permission.Microphone))
        {
            Debug.Log("Mikrofon engedély kérése...");
            Permission.RequestUserPermission(Permission.Microphone);
            StartCoroutine(WaitForPermission());
            return; // Várjunk az engedélyre, mielőtt továbblépünk
        }
#endif

        // Mikrofon eszközök ellenőrzése
        ListAvailableMicrophones();

        // Automatikus indítás ha be van állítva
        if (autoStartRecording && Microphone.devices.Length > 0)
        {
            StartRecording();
        }
    }

    IEnumerator WaitForPermission()
    {
        yield return new WaitForSeconds(0.5f);

        // Újra ellenőrizzük az engedélyt
#if PLATFORM_ANDROID
        if (Permission.HasUserAuthorizedPermission(Permission.Microphone))
        {
            Debug.Log("Mikrofon engedély megadva!");
            ListAvailableMicrophones();

            if (autoStartRecording && Microphone.devices.Length > 0)
            {
                StartRecording();
            }
        }
        else
        {
            Debug.LogError("A mikrofon engedély nem lett megadva!");
            if (statusText != null)
                statusText.text = "HIBA: Mikrofon engedély hiányzik!";
        }
#endif
    }

    void ListAvailableMicrophones()
    {
        string[] devices = Microphone.devices;

        if (devices.Length == 0)
        {
            Debug.LogError("Nem található mikrofon az eszközön!");
            if (statusText != null)
                statusText.text = "HIBA: Nem található mikrofon!";
            if (deviceText != null)
                deviceText.text = "Elérhető mikrofonok: Nincs";
            return;
        }

        // Logoljuk az összes eszközt és kiválasztjuk az elsőt
        string deviceList = "Elérhető mikrofonok:\n";
        for (int i = 0; i < devices.Length; i++)
        {
            deviceList += $"- {devices[i]}\n";
            Debug.Log($"Mikrofon #{i}: {devices[i]}");
        }

        if (deviceText != null)
            deviceText.text = deviceList;

        selectedDevice = devices[0]; // Kiválasztjuk az első mikrofont
        Debug.Log($"Kiválasztott mikrofon: {selectedDevice}");
    }

    public void StartRecording()
    {
        if (isRecording || string.IsNullOrEmpty(selectedDevice)) return;

        Debug.Log($"Mikrofon felvétel indítása: {selectedDevice}");

        // 44.1 kHz mintavételezési frekvencia, 1 másodperces buffer, loopban
        microphoneClip = Microphone.Start(selectedDevice, true, 1, 44100);
        isRecording = true;

        if (statusText != null)
            statusText.text = "Mikrofon: Felvétel folyamatban...";

        // Hangszint monitorozás indítása
        StartCoroutine(MonitorAudioLevel());
    }

    public void StopRecording()
    {
        if (!isRecording) return;

        Debug.Log("Mikrofon felvétel leállítása");
        Microphone.End(selectedDevice);
        isRecording = false;

        if (statusText != null)
            statusText.text = "Mikrofon: Nincs felvétel";
    }

    IEnumerator MonitorAudioLevel()
    {
        while (isRecording)
        {
            // Hangszint mérése
            currentLevel = GetMicrophoneLevel();
            if (currentLevel > peakLevel)
            {
                peakLevel = currentLevel;
                Debug.Log($"Új csúcsszint: {peakLevel}");
            }

            // UI frissítése
            if (levelText != null)
                levelText.text = $"Jelenlegi szint: {currentLevel:F4}\nCsúcsszint: {peakLevel:F4}";

            if (levelMeter != null)
                levelMeter.fillAmount = Mathf.Clamp01(currentLevel * 5); // 5x szorzó a láthatóságért

            // Kis várakozás a következő mérésig
            yield return new WaitForSeconds(updateInterval);
        }
    }

    float GetMicrophoneLevel()
    {
        if (!isRecording || microphoneClip == null) return 0f;

        // A mikrofon jelenlegi pozíciójának lekérése
        int micPosition = Microphone.GetPosition(selectedDevice);
        if (micPosition <= sampleWindow) return 0f; // Ha még nincs elég minta

        // Hangminták lekérése
        microphoneClip.GetData(samples, micPosition - sampleWindow);

        // Maximum keresése a mintákban
        float levelMax = 0f;
        for (int i = 0; i < samples.Length; i++)
        {
            float abs = Mathf.Abs(samples[i]);
            if (abs > levelMax) levelMax = abs;
        }

        return levelMax;
    }

    void OnGUI()
    {
        // Egyszerű vezérlőgombok megjelenítése
        GUILayout.BeginArea(new Rect(10, 10, 300, 100));

        if (!isRecording)
        {
            if (GUILayout.Button("Mikrofon Indítása"))
            {
                StartRecording();
            }
        }
        else
        {
            if (GUILayout.Button("Mikrofon Leállítása"))
            {
                StopRecording();
            }
        }

        if (GUILayout.Button("Mikrofon Engedély Kérése"))
        {
#if PLATFORM_ANDROID
            Permission.RequestUserPermission(Permission.Microphone);
#endif
        }

        GUILayout.EndArea();
    }

    void OnDestroy()
    {
        // Tiszta leállítás
        if (isRecording)
        {
            StopRecording();
        }
    }
}
