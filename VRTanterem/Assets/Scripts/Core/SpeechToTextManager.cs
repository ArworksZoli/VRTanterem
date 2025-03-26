using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using TMPro;
using UnityEngine.UI;
using System;

#if UNITY_WEBGL && !UNITY_EDITOR
using System.Runtime.InteropServices;
#endif

public class SpeechToTextManager : MonoBehaviour
{
    [SerializeField] private OpenAIWebRequest associatedTerminal;
    [SerializeField] private TMP_InputField inputField;
    [SerializeField] private Button micButton;
    [SerializeField] private Image micIcon;
    [SerializeField] private TMP_Dropdown microphoneDropdown;
    private bool isListening = false;
    private string terminalId;
    private List<string> availableMicrophones = new List<string>();
    private Dictionary<string, string> microphoneDeviceIds = new Dictionary<string, string>();

    void Start()
    {
        // Szülő terminal azonosítás
        terminalId = transform.parent.name;
        Debug.Log($"SpeechToTextManager initialized for terminal: {terminalId}");
        
        // Diagnosztika
        Debug.Log("[Diagnosztika] Spatial Build Ellenőrzés");

        // Részletes rendszerinfo logolása
        Debug.Log($"Unity Platform: {Application.platform}");

        // Platform specifikus WebGL ellenőrzés
#if UNITY_WEBGL
        Debug.Log("WebGL platform detected");

        // Modern JavaScript kommunikáció
        try
        {
            // Biztonságos JavaScript kommunikáció
#pragma warning disable CS0618
            Application.ExternalEval("console.log('JavaScript kommunikáció teszt');");
#pragma warning restore CS0618
        }
        catch (Exception e)
        {
            Debug.LogError($"JavaScript kommunikáció hiba: {e.Message}");
        }
#endif

        // Mikrofon dropdown inicializálása
        if (microphoneDropdown != null)
        {
            // Először töröljük az összes alapértelmezett opciót
            microphoneDropdown.options.Clear();

            // Adjunk hozzá egy átmeneti üzenetet
            microphoneDropdown.options.Add(new TMP_Dropdown.OptionData("Mikrofonok betöltése..."));
            microphoneDropdown.RefreshShownValue(); // Azonnal frissítjük a megjelenített értéket
            microphoneDropdown.onValueChanged.AddListener(OnMicrophoneSelected);
            microphoneDropdown.interactable = false;
        }

        // Mikrofon gomb inicializálása
        if (micButton != null)
        {
            micButton.onClick.RemoveAllListeners();
            micButton.onClick.AddListener(ToggleSpeechRecognition);
        }

        InjectJavaScript();
    }

    void ToggleSpeechRecognition()
    {
        Debug.Log($"=======================================");
        Debug.Log($"DIAGNOSZTIKA: Mikrofon gomb megnyomva a {terminalId} terminálon");
        Debug.Log($"DIAGNOSZTIKA: Aktuális állapot: {isListening}");
        Debug.Log($"DIAGNOSZTIKA: Kiválasztott mikrofon: {GetSelectedMicrophoneName()}");
        Debug.Log($"=======================================");

        if (!isListening)
        {
            StartListening();
        }
        else
        {
            StopListening();
        }
    }

    // Segédmetódus a kiválasztott mikrofon nevének lekérdezéséhez
    private string GetSelectedMicrophoneName()
    {
        if (microphoneDropdown != null && microphoneDropdown.value >= 0 &&
            microphoneDropdown.value < availableMicrophones.Count)
        {
            return availableMicrophones[microphoneDropdown.value];
        }
        return "Ismeretlen mikrofon";
    }

    void StartListening()
    {
        try
        {
            Debug.Log($"[DIAGNOSZTIKA] StartListening metódus meghívva");
            isListening = true;

            if (micIcon != null)
            {
                micIcon.color = Color.red;
                Debug.Log($"[DIAGNOSZTIKA] Mikrofon ikon színe piros lett");
            }
            else
            {
                Debug.LogError($"[DIAGNOSZTIKA] HIBA: micIcon null!");
            }

#if UNITY_WEBGL && !UNITY_EDITOR
        Debug.Log($"[DIAGNOSZTIKA] WEBGL kódág végrehajtása");
        
        // Küldünk egy egyszerű, garantáltan működő JavaScript konzol üzenetet
        string testCall = $"console.log('DIAGNOSZTIKA: JavaScript hívás tesztelése')";
        Application.ExternalEval(testCall);
        
        // Most próbáljuk meg a tényleges beszédfelismerést indítani
        string jsCall = $"if(window.terminals['{terminalId}']) {{ " +
                        $"console.log('DIAGNOSZTIKA: Hangfelismerés indítása'); " +
                        $"try {{ " +
                        $"  window.terminals['{terminalId}'].recognition.start(); " +
                        $"  console.log('DIAGNOSZTIKA: Recognition.start() meghívva'); " +
                        $"}} catch(e) {{ " +
                        $"  console.error('DIAGNOSZTIKA HIBA:', e.toString()); " +
                        $"}} " +
                        $"}}";
                        
        Application.ExternalEval(jsCall);
#endif

            Debug.Log($"[DIAGNOSZTIKA] Hangfelismerés elindítva a {terminalId} terminálon");
        }
        catch (Exception e)
        {
            Debug.LogError($"[DIAGNOSZTIKA] SÚLYOS HIBA a hangfelismerés indításakor: {e.Message}");
            Debug.LogError($"[DIAGNOSZTIKA] Stack trace: {e.StackTrace}");
            StopListening();
        }
    }

    void StopListening()
    {
        try
        {
            isListening = false;
            if (micIcon != null) micIcon.color = Color.white;

#if UNITY_WEBGL && !UNITY_EDITOR
        string jsCall = $"if(window.terminals['{terminalId}'] && window.terminals['{terminalId}'].recognition) {{ " +
                        $"window.terminals['{terminalId}'].recognition.stop(); }}";
        Application.ExternalEval(jsCall);
#endif

            Debug.Log($"Hangfelismerés leállítva a {terminalId} terminálon");
        }
        catch (Exception e)
        {
            Debug.LogError($"Hangfelismerés leállítási hiba a {terminalId} terminálon: {e.Message}");
        }
    }

    public void OnSpeechResult(string result)
    {
        Debug.Log($"Felismert szöveg a {terminalId} terminálon: {result}");

        if (inputField != null)
        {
            inputField.text = result;
        }

        if (associatedTerminal != null)
        {
            try
            {
                associatedTerminal.SendButtonClick();
                Debug.Log($"Hang alapján felismert üzenet elküldve a {terminalId} terminálon");
            }
            catch (Exception e)
            {
                Debug.LogError($"Üzenet küldési hiba a {terminalId} terminálon: {e.Message}");
            }
        }
        else
        {
            Debug.LogWarning($"Nem található OpenAIWebRequest szkript a {terminalId} terminálon");
        }

        StopListening();
    }

    // Mikrofonok listájának fogadása JavaScript-től
    public void OnMicrophonesEnumerated(string jsonData)
    {
        Debug.Log($"[SpeechToTextManager] Mikrofonok listája érkezett: {jsonData}");

        try
        {
            // JSON feldolgozása
            var micDevices = JsonUtility.FromJson<MicrophoneDeviceList>(
                "{\"devices\":" + jsonData + "}"
            );

            if (micDevices != null && micDevices.devices != null)
            {
                // Mikrofonok tárolása
                availableMicrophones.Clear();
                microphoneDeviceIds.Clear();

                // Duplikációk szűrésére használt halmaz
                HashSet<string> uniqueLabels = new HashSet<string>();

                // Dropdown lista frissítése
                if (microphoneDropdown != null)
                {
                    microphoneDropdown.options.Clear();

                    // Első legyen mindig az alapértelmezett mikrofon
                    string defaultLabel = "Alapértelmezett mikrofon";
                    availableMicrophones.Add(defaultLabel);
                    microphoneDeviceIds.Add(defaultLabel, "default");
                    microphoneDropdown.options.Add(new TMP_Dropdown.OptionData(defaultLabel));
                    uniqueLabels.Add(defaultLabel);

                    // A többi mikrofon hozzáadása (duplikációk szűrésével)
                    foreach (var device in micDevices.devices)
                    {
                        // Az alapértelmezett mikrofont már hozzáadtuk, ezt kihagyjuk
                        if (device.deviceId == "default" || device.label == defaultLabel)
                            continue;

                        // Ha már van ilyen nevű mikrofon, kihagyjuk
                        if (uniqueLabels.Contains(device.label))
                            continue;

                        // Hozzáadjuk a mikrofonokat a listához
                        availableMicrophones.Add(device.label);
                        microphoneDeviceIds.Add(device.label, device.deviceId);
                        uniqueLabels.Add(device.label);

                        // Rövidített nevet használunk a megjelenítéshez, ha túl hosszú
                        string displayLabel = device.label;
                        if (displayLabel.Length > 30)
                        {
                            displayLabel = displayLabel.Substring(0, 27) + "...";
                        }

                        // Hozzáadjuk a dropdown opcióhoz
                        microphoneDropdown.options.Add(new TMP_Dropdown.OptionData(displayLabel));
                    }

                    // Frissítjük a dropdown-t és kiválasztjuk az alapértelmezett mikrofont
                    microphoneDropdown.value = 0;
                    microphoneDropdown.RefreshShownValue();
                    microphoneDropdown.interactable = true;

                    // Explicit módon meghívjuk a kiválasztó metódust
                    OnMicrophoneSelected(0);
                }
            }
        }
        catch (Exception e)
        {
            Debug.LogError($"[SpeechToTextManager] Hiba a mikrofonok feldolgozásakor: {e.Message}");
        }
    }

    // Mikrofon választás kezelése
    private void OnMicrophoneSelected(int index)
    {
        if (index >= 0 && index < availableMicrophones.Count)
        {
            string selectedLabel = availableMicrophones[index];
            string deviceId = microphoneDeviceIds[selectedLabel];

            Debug.Log($"[SpeechToTextManager] Mikrofon kiválasztva: {selectedLabel} (ID: {deviceId})");

            // JavaScript hívás a kiválasztott mikrofon beállításához
#if UNITY_WEBGL && !UNITY_EDITOR
        string jsCall = $"if(window.terminals['{terminalId}']) {{ window.terminals['{terminalId}'].setMicrophone('{deviceId}'); }}";
        Application.ExternalEval(jsCall);
#endif
        }
    }

    // Hiba callback
    public void OnMicrophoneEnumerationError(string errorMessage)
    {
        Debug.LogError($"[SpeechToTextManager] Hiba a mikrofonok lekérdezésekor: {errorMessage}");

        if (microphoneDropdown != null)
        {
            microphoneDropdown.options.Clear();
            microphoneDropdown.options.Add(new TMP_Dropdown.OptionData("Hiba történt"));
            microphoneDropdown.interactable = false;
        }
    }

    // Mikrofon hiba kezelése
    public void OnMicrophoneError(string errorMessage)
    {
        Debug.LogError($"[SpeechToTextManager] Mikrofon hiba: {errorMessage}");

        // Állítsuk vissza az alaphelyzetbe a mikrofon ikont és állapotot
        if (micIcon != null)
        {
            micIcon.color = Color.white;
        }

        isListening = false;
    }

    // JSON Segédosztályok a deszerializációhoz
    [Serializable]
    private class MicrophoneDevice
    {
        public string deviceId;
        public string label;
    }

    [Serializable]
    private class MicrophoneDeviceList
    {
        public MicrophoneDevice[] devices;
    }

    void InjectJavaScript()
    {
#if UNITY_WEBGL && !UNITY_EDITOR
    string terminalIdentifier = terminalId;
    string script = @"
        console.log('DIAGNOSZTIKA: JavaScript inicializálás kezdődik: " + terminalIdentifier + @"');
        
        if (!window.terminals) {
            window.terminals = {};
            console.log('DIAGNOSZTIKA: terminals objektum létrehozva');
        }

        // Tiszta állapotból indítjuk az objektumot
        window.terminals['" + terminalIdentifier + @"'] = {
            recognition: null,
            microphoneDevices: [],
            selectedMicrophoneId: 'default'
        };
        console.log('DIAGNOSZTIKA: terminal objektum inicializálva: " + terminalIdentifier + @"');
        
        // A recognition objektumot csak egyszer hozzuk létre
        try {
            window.terminals['" + terminalIdentifier + @"'].recognition = new webkitSpeechRecognition();
            console.log('DIAGNOSZTIKA: webkitSpeechRecognition sikeresen létrehozva');
            
            var recognition = window.terminals['" + terminalIdentifier + @"'].recognition;
            recognition.continuous = false;
            recognition.interimResults = false;
            recognition.lang = 'hu-HU';
            console.log('DIAGNOSZTIKA: recognition beállítások alkalmazva');

            recognition.onresult = function(event) {
                console.log('DIAGNOSZTIKA: onresult esemény meghívva');
                try {
                    var result = event.results[0][0].transcript;
                    console.log('DIAGNOSZTIKA: Felismert szöveg: ' + result);
                    SendMessage('" + terminalIdentifier + @"/SpeechToTextManager', 'OnSpeechResult', result);
                } catch(e) {
                    console.error('DIAGNOSZTIKA: Hiba az eredmény feldolgozásakor:', e);
                }
            };

            recognition.onerror = function(event) {
                console.error('DIAGNOSZTIKA: Speech recognition error:', event.error);
                SendMessage('" + terminalIdentifier + @"/SpeechToTextManager', 'OnMicrophoneError', event.error);
            };

            recognition.onend = function() {
                console.log('DIAGNOSZTIKA: onend esemény meghívva');
                SendMessage('" + terminalIdentifier + @"/SpeechToTextManager', 'StopListening');
            };
            
            console.log('DIAGNOSZTIKA: Eseménykezelők sikeresen beállítva');
        } catch(e) {
            console.error('DIAGNOSZTIKA: HIBA a recognition objektum létrehozásakor:', e);
        }
        
        // EGYSZERŰSÍTETT mikrofon lekérdezés
        window.terminals['" + terminalIdentifier + @"'].enumerateMicrophones = function() {
            console.log('DIAGNOSZTIKA: enumerateMicrophones meghívva');
            
            if (!navigator.mediaDevices || !navigator.mediaDevices.enumerateDevices) {
                console.error('DIAGNOSZTIKA: mediaDevices API nem támogatott');
                SendMessage('" + terminalIdentifier + @"/SpeechToTextManager', 'OnMicrophoneEnumerationError', 'API nem támogatott');
                return;
            }

            navigator.mediaDevices.getUserMedia({audio: true})
                .then(function(stream) {
                    console.log('DIAGNOSZTIKA: Mikrofon engedély megadva');
                    stream.getTracks().forEach(track => track.stop());
                    return navigator.mediaDevices.enumerateDevices();
                })
                .then(function(devices) {
                    console.log('DIAGNOSZTIKA: eszközök lekérdezve:', devices.length);
                    
                    var mics = [];
                    mics.push({
                        deviceId: 'default',
                        label: 'Alapértelmezett mikrofon'
                    });
                    
                    devices.forEach(function(device) {
                        if (device.kind === 'audioinput' && device.deviceId !== 'default') {
                            var label = device.label || ('Mikrofon ' + mics.length);
                            mics.push({
                                deviceId: device.deviceId,
                                label: label
                            });
                        }
                    });
                    
                    console.log('DIAGNOSZTIKA: Mikrofonok száma:', mics.length);
                    window.terminals['" + terminalIdentifier + @"'].microphoneDevices = mics;
                    
                    var result = JSON.stringify(mics);
                    SendMessage('" + terminalIdentifier + @"/SpeechToTextManager', 'OnMicrophonesEnumerated', result);
                })
                .catch(function(err) {
                    console.error('DIAGNOSZTIKA: Hiba a mikrofonok lekérdezésekor:', err);
                    SendMessage('" + terminalIdentifier + @"/SpeechToTextManager', 'OnMicrophoneEnumerationError', err.toString());
                });
        };
        
        // Mikrofon kiválasztása
        window.terminals['" + terminalIdentifier + @"'].setMicrophone = function(deviceId) {
            console.log('DIAGNOSZTIKA: setMicrophone meghívva:', deviceId);
            window.terminals['" + terminalIdentifier + @"'].selectedMicrophoneId = deviceId;
        };
        
        console.log('DIAGNOSZTIKA: Mikrofonok lekérdezése 1 másodperc múlva...');
        setTimeout(function() {
            window.terminals['" + terminalIdentifier + @"'].enumerateMicrophones();
        }, 1000);
    ";
    
    Debug.Log($"[DIAGNOSZTIKA] JavaScript kód injektálása: {script.Substring(0, 100)}...");
    Application.ExternalEval(script);
    Debug.Log($"[DIAGNOSZTIKA] JavaScript kód injektálása befejeződött");
#endif
    }
}