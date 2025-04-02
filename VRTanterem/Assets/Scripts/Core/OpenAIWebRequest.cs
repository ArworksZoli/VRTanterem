using UnityEngine;
using UnityEngine.Networking;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using System;
using Newtonsoft.Json.Linq;
using TMPro;
// using UnityEngine.UI; // Eltávolítva, mivel a Button már nem használatos

public class OpenAIWebRequest : MonoBehaviour
{
    // --- API és Asszisztens Beállítások ---
    // [SerializeField] attribútummal tesszük láthatóvá és szerkeszthetővé az Inspectorban,
    // miközben a változók privátak maradnak a kódban.
    [Header("OpenAI Configuration")] // Opcionális: Fejléc az Inspectorban a jobb átláthatóságért
    [Tooltip("Your OpenAI API Key (keep this secret!)")] // Opcionális: Segítő szöveg az Inspectorban
    [SerializeField] private string apiKey = ""; // Üresen hagyjuk, az értéket az Inspectorban kell megadni

    [Tooltip("The ID of the OpenAI Assistant to use")]
    [SerializeField] private string assistantID = ""; // Üresen hagyjuk, az értéket az Inspectorban kell megadni

    private string apiUrl = "https://api.openai.com/v1"; // Ezt itt hagyhatjuk, ha nem változik gyakran

    // --- UI Elemek ---
    public string userInput = "Hello!"; // Alapértelmezett első üzenet
    public TMP_Text TMPResponseText; // AI válasza
    [SerializeField] private TMP_InputField TMPInputField; // User szöveges üzenetének beviteli mező
    public TMP_Text TMPUserText; // User válasza elküldéskor

    // --- TTS Manager Referencia ---
    [Header("External Components")] // Opcionális fejléc
    [Tooltip("Reference to the TextToSpeechManager component for audio output.")]
    [SerializeField] private TextToSpeechManager textToSpeechManager;

    // --- Belső Változók ---
    private string assistantThreadId; // Az aktuális beszélgetési szál azonosítója
    private string currentRunId; // Az aktuális futtatás azonosítója (streaminghez)
    private StringBuilder messageBuilder = new StringBuilder(); // Üzenetek építéséhez (jelenleg kevésbé használt a streaming miatt)
    private StringBuilder buffer = new StringBuilder(); // Bejövő streaming adatok puffereléséhez
    // private string fullMessage = ""; // Eltávolítva vagy újragondolva, ha a teljes üzenet követése szükséges
    // private string lastProcessedContent = ""; // Eltávolítva vagy újragondolva

    // Whisper beállítások
    private const string WhisperApiUrl = "https://api.openai.com/v1/audio/transcriptions";
    private const string ModelName = "whisper-1";

    private void Start()
    {
        // SendButton listener eltávolítva
        // SendButton.onClick.AddListener(SendButtonClick);

        // --- KONFIGURÁCIÓ ELLENŐRZÉSE ---
        // Ellenőrizzük, hogy az API kulcs és az Asszisztens ID meg van-e adva az Inspectorban.
        bool configurationValid = true;
        if (string.IsNullOrEmpty(apiKey) || apiKey.Trim().Length < 10) // Egyszerű ellenőrzés
        {
            Debug.LogError("OpenAI API Key is not set or looks invalid in the Inspector! Please provide your API key on the OpenAIWebRequest component.");
            configurationValid = false;
        }
        if (string.IsNullOrEmpty(assistantID) || !assistantID.Trim().StartsWith("asst_")) // Egyszerű ellenőrzés
        {
            Debug.LogError("OpenAI Assistant ID is not set or looks invalid in the Inspector! Please provide the Assistant ID on the OpenAIWebRequest component.");
            configurationValid = false;
        }

        // Ha a konfiguráció hiányos, ne folytassuk a működést (vagy csak korlátozottan)
        if (!configurationValid)
        {
            Debug.LogError("OpenAI configuration is invalid. Disabling OpenAIWebRequest component.");
            enabled = false; // Letiltja a komponenst (nem fog futni az Update, stb.)
            // Opcionálisan visszajelzést adhatunk a UI-on is.
            if (TMPResponseText != null)
            {
                TMPResponseText.text = "ERROR: OpenAI Configuration Missing in Inspector!";
            }
            if (TMPInputField != null)
            {
                TMPInputField.interactable = false; // Letiltjuk a beviteli mezőt
            }
            return; // Kilépünk a Start metódusból, a korutinok nem indulnak el.
        }
        // --- KONFIGURÁCIÓ ELLENŐRZÉSE VÉGE ---

        // --- TTS Manager Inicializálása ---
        if (textToSpeechManager != null)
        {
            textToSpeechManager.Initialize(apiKey); // Átadjuk az API kulcsot
        }
        else
        {
            Debug.LogWarning("[OpenAIWebRequest] TextToSpeechManager reference is not set in the Inspector. TTS functionality will be disabled.");
            // Nem kell letiltani az egész komponenst, csak a TTS nem fog menni.
        }
        // --- TTS Manager Inicializálása VÉGE ---

        // InputField 'onSubmit' esemény (Enter lenyomására)
        if (TMPInputField != null)
        {
            TMPInputField.onSubmit.AddListener((value) => {
                if (!string.IsNullOrEmpty(value) && !string.IsNullOrWhiteSpace(assistantThreadId)) // Ellenőrizzük a thread ID-t is
                {
                    messageBuilder.Clear();
                    buffer.Clear();
                    StartCoroutine(SendMessageSequence(value));
                }
                else if (string.IsNullOrEmpty(assistantThreadId))
                {
                    Debug.LogWarning("Cannot send message yet, thread is not ready.");
                    // Esetleg visszajelzés a felhasználónak
                }
            });
        }
        else
        {
            Debug.LogWarning("TMPInputField nincs beállítva az Inspectorban!");
        }

        // Asszisztens adatainak lekérése és Thread létrehozása (csak ha a konfiguráció rendben van)
        StartCoroutine(GetAssistant());
        StartCoroutine(CreateThread());
    }

    // OpenAIWebRequest.cs-ben hozzáadandó metódus
    public void ProcessVoiceInput(string recognizedText)
    {
        Debug.Log($"[OpenAIWebRequest] Voice input received: '{recognizedText}'");

        // 1. Ellenőrzések (opcionális, de ajánlott)
        if (string.IsNullOrEmpty(recognizedText))
        {
            Debug.LogWarning("[OpenAIWebRequest] Received empty text from voice input. Ignoring.");
            // Esetleg visszajelzés a felhasználónak itt is?
            return;
        }
        if (string.IsNullOrEmpty(assistantThreadId))
        {
            Debug.LogError("[OpenAIWebRequest] Cannot process voice input: Assistant Thread ID is missing.");
            // Visszajelzés a felhasználónak
            if (TMPResponseText != null) TMPResponseText.text = "Error: Assistant connection not ready.";
            return;
        }

        // 2. Megjelenítés (Opcionális, de hasznos visszajelzés)
        // Kiírjuk, mit hallottunk a User szövegmezőbe
        if (TMPUserText != null)
        {
            TMPUserText.text = "User (Voice): " + recognizedText;
        }
        // Opcionálisan beletehetjük az InputField-be is, mintha begépelte volna
        // if (TMPInputField != null)
        // {
        //     TMPInputField.text = recognizedText;
        // }

        // 3. A meglévő üzenetküldési folyamat elindítása
        // Pontosan ugyanazt a korutint indítjuk, mint amit az InputField Enter lenyomása
        Debug.Log("[OpenAIWebRequest] Starting SendMessageSequence for voice input...");
        messageBuilder.Clear(); // Biztosítjuk a tiszta állapotot
        buffer.Clear();         // Biztosítjuk a tiszta állapotot
        StartCoroutine(SendMessageSequence(recognizedText));

        // Az InputField törlését a SendMessageSequence vége már kezeli,
        // így itt nem kell külön foglalkozni vele.
    }


    // Beállítja a közös HTTP fejléceket az OpenAI kérésekhez
    private void SetCommonHeaders(UnityWebRequest request)
    {
        // Most már az Inspectorban beállított apiKey változót használja
        request.SetRequestHeader("Content-Type", "application/json");
        request.SetRequestHeader("Authorization", $"Bearer {apiKey}");
        request.SetRequestHeader("OpenAI-Beta", "assistants=v2");
    }

    // Lekéri az asszisztens adatait (opcionális)
    private IEnumerator GetAssistant()
    {
        string url = $"{apiUrl}/assistants/{assistantID}";
        Debug.Log("Getting Assistant at URL: " + url);

        using (UnityWebRequest webRequest = UnityWebRequest.Get(url))
        {
            SetCommonHeaders(webRequest);
            yield return webRequest.SendWebRequest();

            if (webRequest.result != UnityWebRequest.Result.Success)
            {
                Debug.LogError($"Error while getting assistant: {webRequest.error} - {webRequest.downloadHandler.text}");
            }
            else
            {
                Debug.Log("Assistant Retrieved: " + webRequest.downloadHandler.text);
            }
        }
    }

    // Létrehoz egy új beszélgetési szálat (thread) az OpenAI-nál
    private IEnumerator CreateThread()
    {
        string url = $"{apiUrl}/threads";
        Debug.Log("Creating Thread at URL: " + url);

        // Kezdeti üzenet megadása (opcionális)
        JObject requestBody = new JObject();
        // A userInput változót az Inspectorban lehet beállítani, vagy itt marad az alapértelmezett "Hello!"
        if (!string.IsNullOrEmpty(userInput)) // Csak akkor adjuk hozzá, ha van kezdeti üzenet
        {
            requestBody["messages"] = new JArray
             {
                 new JObject
                 {
                     ["role"] = "user",
                     ["content"] = userInput // Az Inspectorban vagy kódban definiált kezdeti üzenet
                 }
             };
            Debug.Log($"Initial message included in thread creation: '{userInput}'");
        }
        else
        {
            Debug.Log("No initial message provided for thread creation.");
        }

        string jsonBody = requestBody.ToString();
        // Debug.Log("Thread creation JSON: " + jsonBody); // Csak szükség esetén

        using (UnityWebRequest webRequest = new UnityWebRequest(url, "POST"))
        {
            byte[] bodyRaw = Encoding.UTF8.GetBytes(jsonBody);
            webRequest.uploadHandler = new UploadHandlerRaw(bodyRaw);
            webRequest.downloadHandler = new DownloadHandlerBuffer();
            SetCommonHeaders(webRequest); // Beállítja az API kulcsot és egyéb fejléceket

            yield return webRequest.SendWebRequest(); // Elküldi a kérést és vár a válaszra

            // Ellenőrizzük a kérés sikerességét
            if (webRequest.result != UnityWebRequest.Result.Success)
            {
                // Hiba esetén logoljuk a részleteket
                Debug.LogError($"Error while creating thread: {webRequest.error} - Status Code: {webRequest.responseCode} - Response: {webRequest.downloadHandler.text}");
            }
            else
            {
                // Sikeres válasz esetén logoljuk
                Debug.Log("Thread Created Successfully. Response: " + webRequest.downloadHandler.text);
                try
                {
                    // Feldolgozzuk a JSON választ
                    JObject responseJson = JObject.Parse(webRequest.downloadHandler.text);
                    // Kinyerjük a thread ID-t
                    assistantThreadId = responseJson["id"]?.ToString();

                    // Ellenőrizzük, hogy kaptunk-e érvényes thread ID-t
                    if (!string.IsNullOrEmpty(assistantThreadId))
                    {
                        Debug.Log($"Thread created successfully with ID: {assistantThreadId}");

                        // --- MÓDOSÍTÁS ITT ---
                        // Ha a thread létrehozásakor küldtünk kezdeti üzenetet (userInput),
                        // akkor indítsunk el egy futtatást (run), hogy választ kapjunk rá.
                        if (!string.IsNullOrEmpty(userInput))
                        {
                            Debug.Log("Initial userInput was included in thread creation. Starting run for initial response.");

                            // Jelenítsük meg a kezdeti user üzenetet is a UI-on, ha van hova
                            if (TMPUserText != null)
                            {
                                TMPUserText.text = "User: " + userInput;
                            }
                            else
                            {
                                Debug.LogWarning("TMPUserText is not assigned in the Inspector. Cannot display initial user input.");
                            }

                            // Indítsuk el a futtatást (CreateAssistantRun),
                            // mivel az üzenet már a thread része, nem kell újra hozzáadni.
                            Debug.Log("Attempting to start CreateAssistantRun...");
                            StartCoroutine(CreateAssistantRun());
                        }
                        // --- MÓDOSÍTÁS VÉGE ---
                    }
                    else
                    {
                        // Hiba, ha nem sikerült kinyerni a thread ID-t a válaszból
                        Debug.LogError("Failed to retrieve assistantThreadId from thread creation response. Response JSON might be invalid or missing the 'id' field.");
                    }
                }
                catch (Exception e)
                {
                    // Hiba a JSON feldolgozása közben
                    Debug.LogError($"Error parsing thread creation response: {e.Message} - Raw Response: {webRequest.downloadHandler.text}");
                }
            }
        }
    }

    // Megpróbálja megszakítani az aktuálisan futó asszisztens futtatást (run)
    private IEnumerator CancelCurrentRun()
    {
        if (!string.IsNullOrEmpty(currentRunId) && !string.IsNullOrEmpty(assistantThreadId))
        {
            string cancelUrl = $"{apiUrl}/threads/{assistantThreadId}/runs/{currentRunId}/cancel";
            Debug.Log($"Attempting to cancel run: {currentRunId} on thread: {assistantThreadId}");

            using (UnityWebRequest webRequest = new UnityWebRequest(cancelUrl, "POST"))
            {
                webRequest.downloadHandler = new DownloadHandlerBuffer(); // Kell a válaszhoz
                SetCommonHeaders(webRequest);

                yield return webRequest.SendWebRequest();

                // A Cancel API általában 200 OK-t ad vissza akkor is, ha a run már nem törölhető (pl. befejeződött)
                if (webRequest.result == UnityWebRequest.Result.Success)
                {
                    Debug.Log($"Cancel request for run {currentRunId} sent. Response: {webRequest.downloadHandler.text}");
                }
                else
                {
                    // Hiba esetén logoljuk, de nem feltétlenül állítjuk le a folyamatot
                    Debug.LogWarning($"Run cancellation request failed for run {currentRunId}: {webRequest.error} - {webRequest.downloadHandler.text}");
                }
            }
            // Nullázzuk a run ID-t, hogy ne próbáljuk újra törölni ugyanazt
            currentRunId = null;
        }
        else
        {
            // Debug.Log("No current run to cancel or thread ID missing."); // Csak szükség esetén
        }
    }

    // --- SendButtonClick metódus törölve ---
    // public void SendButtonClick() { ... }

    // Elindítja az üzenetküldési folyamatot (megszakítás, új üzenet küldése)
    private IEnumerator SendMessageSequence(string input)
    {
        if (string.IsNullOrEmpty(assistantThreadId))
        {
            Debug.LogError("Cannot send message: Assistant Thread ID is missing. Ensure the thread was created.");
            yield break; // Kilépünk a coroutine-ból, ha nincs thread ID
        }

        // 1. Próbáljuk megszakítani az előző futást (ha volt)
        yield return StartCoroutine(CancelCurrentRun());

        // 2. Rövid várakozás (opcionális, de segíthet elkerülni a race condition-t)
        // yield return new WaitForSeconds(0.1f); // Szükség esetén visszakapcsolható

        // 3. Új üzenet hozzáadása és futtatás indítása
        yield return StartCoroutine(GetAssistantResponse(input)); // Ez indítja a run-t is

        // 4. UI frissítése: Kiírjuk a felhasználó üzenetét és töröljük az input mezőt
        if (TMPUserText != null)
        {
            TMPUserText.text = "User: " + input; // Megjelenítjük, mit küldött a felhasználó
        }
        if (TMPInputField != null)
        {
            TMPInputField.text = ""; // Töröljük az input mezőt
            TMPInputField.ActivateInputField(); // Opcionális: újra fókuszba helyezzük az input mezőt
        }
    }

    // Hozzáadja a felhasználó üzenetét a szálhoz és elindítja az asszisztens futtatását
    private IEnumerator GetAssistantResponse(string userMessageContent)
    {
        // Ellenőrizzük újra a thread ID-t
        if (string.IsNullOrEmpty(assistantThreadId))
        {
            Debug.LogError("Assistant Thread ID is invalid before adding message.");
            yield break;
        }

        // --- Üzenet hozzáadása a szálhoz ---
        string messageURL = $"{apiUrl}/threads/{assistantThreadId}/messages";
        Debug.Log($"Posting User Message to Thread: {assistantThreadId} at URL: {messageURL}");

        JObject messageBody = new JObject
        {
            ["role"] = "user",
            ["content"] = userMessageContent // A paraméterként kapott üzenetet használjuk
        };

        string messageJson = messageBody.ToString();
        // Debug.Log("Sending User Message JSON: " + messageJson); // Csak szükség esetén

        using (UnityWebRequest webRequest = new UnityWebRequest(messageURL, "POST"))
        {
            byte[] bodyRaw = Encoding.UTF8.GetBytes(messageJson);
            webRequest.uploadHandler = new UploadHandlerRaw(bodyRaw);
            webRequest.downloadHandler = new DownloadHandlerBuffer();
            SetCommonHeaders(webRequest);

            yield return webRequest.SendWebRequest();

            if (webRequest.result != UnityWebRequest.Result.Success)
            {
                Debug.LogError($"Error while posting user message: {webRequest.error} - {webRequest.downloadHandler.text}");
                // Itt nem indítjuk el a run-t, mert már az üzenet hozzáadása sem sikerült
                yield break; // Kilépés a coroutine-ból hiba esetén
            }
            else
            {
                Debug.Log("User message added to thread successfully. Response: " + webRequest.downloadHandler.text);
                // Az üzenet sikeresen hozzáadva, most indíthatjuk a futtatást (run)
                StartCoroutine(CreateAssistantRun());
            }
        }
    }

    // Létrehoz és elindít egy új asszisztens futtatást (run) streaming módban
    private IEnumerator CreateAssistantRun()
    {
        // Ellenőrizzük újra a thread ID-t
        if (string.IsNullOrEmpty(assistantThreadId))
        {
            Debug.LogError("Assistant Thread ID is invalid before creating run.");
            yield break;
        }

        // --- Run létrehozása streaminggel ---
        string runUrl = $"{apiUrl}/threads/{assistantThreadId}/runs"; // Stream paraméter a body-ban
        Debug.Log($"Creating assistant run with streaming for thread: {assistantThreadId} at URL: {runUrl}");

        var runBody = new JObject
        {
            ["assistant_id"] = assistantID,
            ["stream"] = true // Fontos: Streaming bekapcsolása
        };
        string runJson = runBody.ToString();
        // Debug.Log("Run creation JSON: " + runJson); // Csak szükség esetén

        using (UnityWebRequest webRequest = new UnityWebRequest(runUrl, "POST"))
        {
            webRequest.uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(runJson));
            webRequest.downloadHandler = new DownloadHandlerBuffer(); // Kell a kezdeti válaszhoz és a streamben érkező adatokhoz is.
            SetCommonHeaders(webRequest);

            // Aszinkron művelet indítása
            UnityWebRequestAsyncOperation asyncOp = webRequest.SendWebRequest();

            StringBuilder currentResponseChunk = new StringBuilder(); // A delta eseményekből összeálló válasz
            int lastProcessedIndex = 0; // Hol tartunk a letöltött adatok feldolgozásában
            buffer.Clear(); // Biztosítjuk, hogy a puffer tiszta legyen a stream kezdete előtt

            // Ciklus, amíg a kapcsolat él és adat érkezik
            while (!asyncOp.isDone)
            {
                // Hibakezelés a stream közben
                if (webRequest.result == UnityWebRequest.Result.ConnectionError || webRequest.result == UnityWebRequest.Result.ProtocolError)
                {
                    Debug.LogError($"Error during assistant run stream: {webRequest.error} - Status Code: {webRequest.responseCode} - Partial Response: {webRequest.downloadHandler.text}");
                    yield break; // Kilépés hiba esetén
                }

                // Van új adat a bufferben?
                if (webRequest.downloadHandler.data != null)
                {
                    int currentLength = webRequest.downloadHandler.data.Length;
                    if (currentLength > lastProcessedIndex)
                    {
                        // Csak az új adatokat olvassuk ki
                        string newTextChunk = Encoding.UTF8.GetString(webRequest.downloadHandler.data, lastProcessedIndex, currentLength - lastProcessedIndex);
                        lastProcessedIndex = currentLength; // Frissítjük a feldolgozott indexet

                        // Hozzáadjuk a pufferhez az új adatokat
                        buffer.Append(newTextChunk);
                        // Debug.Log($"Received chunk: {newTextChunk}"); // Részletes logolás

                        // Feldolgozzuk a puffert soronként (SSE formátum: "data: {...}\n\n")
                        string bufferContent = buffer.ToString();
                        int lastEventEndIndex = 0; // Hol ér véget az utolsó sikeresen feldolgozott esemény a bufferben

                        // Keressük a teljes eseményeket (általában \n\n választja el őket)
                        int eventSeparatorIndex;
                        while ((eventSeparatorIndex = bufferContent.IndexOf("\n\n", lastEventEndIndex)) != -1)
                        {
                            // Kivesszük az esemény blokkját
                            string eventBlock = bufferContent.Substring(lastEventEndIndex, eventSeparatorIndex - lastEventEndIndex);
                            lastEventEndIndex = eventSeparatorIndex + 2; // Ugrás a következő esemény elejére (\n\n hossza 2)

                            // Feldolgozzuk az esemény blokkját (ami több 'data:' sort is tartalmazhat)
                            string[] lines = eventBlock.Split('\n', StringSplitOptions.RemoveEmptyEntries);
                            foreach (string line in lines)
                            {
                                if (line.StartsWith("data:"))
                                {
                                    string jsonString = line.Substring(5).Trim(); // "data:" rész eltávolítása

                                    // A stream vége jelzés
                                    if (jsonString == "[DONE]")
                                    {
                                        Debug.Log("Streaming completed ([DONE] received). Final Assembled Response from Deltas: " + currentResponseChunk.ToString());
                                        // Ha a currentResponseChunk üres maradt, de a [DONE] megjött, az is információ
                                        if (currentResponseChunk.Length == 0)
                                        {
                                            Debug.LogWarning("Stream finished with [DONE], but no text content was received via delta events.");
                                            // Itt esetleg kiírhatnánk egy alapértelmezett üzenetet vagy hibát a UI-ra,
                                            // ha nem érkezett teljes üzenet objektum sem korábban.
                                            // if (TMPResponseText != null && string.IsNullOrEmpty(TMPResponseText.text))
                                            //      TMPResponseText.text = "[No response content received]";
                                        }
                                        yield break; // Kilépés a coroutine-ból, a stream véget ért
                                    }

                                    // JSON feldolgozása
                                    try
                                    {
                                        JObject eventOrMessageObject = JObject.Parse(jsonString);
                                        string eventType = eventOrMessageObject["event"]?.ToString();
                                        string objectType = eventOrMessageObject["object"]?.ToString();

                                        Debug.Log($"[STREAM DATA RECEIVED] EventType: '{eventType ?? "N/A"}', ObjectType: '{objectType ?? "N/A"}', RawJSON: {jsonString}");

                                        // --- ÚJ, RÉSZLETESEBB LOGIKAI SORREND ---

                                        // 1. Kezeljük a DELTA üzeneteket az OBJEKTUM TÍPUS alapján
                                        if (objectType == "thread.message.delta")
                                        {
                                            Debug.Log("[Delta Check] ObjectType is 'thread.message.delta'. Processing content..."); // Új log
                                            JArray contentDeltas = eventOrMessageObject["delta"]?["content"] as JArray;

                                            if (contentDeltas != null)
                                            {
                                                Debug.Log($"[Delta Check] Found {contentDeltas.Count} item(s) in content delta array."); // Új log
                                                foreach (var deltaItem in contentDeltas)
                                                {
                                                    string contentType = deltaItem["type"]?.ToString();
                                                    Debug.Log($"[Delta Check] Processing delta item. Type: '{contentType ?? "NULL"}'"); // Új log
                                                    if (contentType == "text")
                                                    {
                                                        var textToken = deltaItem["text"];
                                                        var valueToken = textToken?["value"]; // Külön a value token
                                                        string textDelta = valueToken?.ToString(); // Érték kinyerése

                                                        // Részletes log a kinyert értékről
                                                        Debug.Log($"[Delta Details] Text Token present: {textToken != null}. Value Token present: {valueToken != null}. Parsed textDelta: '{(textDelta == null ? "NULL" : (textDelta == "" ? "EMPTY_STRING" : textDelta))}'");

                                                        // A régi log is maradhat összehasonlításnak:
                                                        Debug.Log($"[MessageDelta - Handling by ObjectType] Received text delta: '{(textDelta ?? "NULL")}'");

                                                        // Csak akkor frissítjük, ha tényleg van tartalom
                                                        if (!string.IsNullOrEmpty(textDelta))
                                                        {
                                                            currentResponseChunk.Append(textDelta);
                                                            if (TMPResponseText != null)
                                                            {
                                                                Debug.Log($"[UI Update Delta - Handling by ObjectType] Updating TMPResponseText. Current length: {currentResponseChunk.Length}");
                                                                TMPResponseText.text = currentResponseChunk.ToString();
                                                            }
                                                            else { Debug.LogWarning("[UI Update Delta] TMPResponseText reference is NULL!"); } // Hiba, ha nincs UI elem

                                                            // --- ÚJ RÉSZ: Szöveg továbbítása a TTS Managernek ---
                                                            if (textToSpeechManager != null)
                                                            {
                                                                textToSpeechManager.AppendText(textDelta);
                                                            }
                                                            else { Debug.LogWarning("Kurvára nem sikerült továbbítani a TTS-nek a szöveget!"); }
                                                            // --- ÚJ RÉSZ VÉGE ---
                                                        }
                                                        else
                                                        {
                                                            Debug.LogWarning("[Delta Details] textDelta is null or empty, skipping UI update for this delta."); // Figyelmeztetés, ha üres
                                                        }
                                                    }
                                                    else
                                                    {
                                                        Debug.Log($"[MessageDelta - Handling by ObjectType] Received non-text delta part. Type: '{contentType ?? "NULL"}'");
                                                    }
                                                } // foreach deltaItem
                                            }
                                            else { Debug.LogWarning("[Delta Check] contentDeltas array is NULL!"); } // Hiba, ha nincs content tömb
                                        }
                                        // 2. Kezeljük a Run Step eseményeket (csendben, csak logoljuk)
                                        else if (objectType == "thread.run.step")
                                        {
                                            // Csak logoljuk, nem kell vele mást tenni a UI szempontjából
                                            Debug.Log($"[RunStepLifecycle] Run step update received. Status: {eventOrMessageObject["status"]?.ToString()} Type: {eventOrMessageObject["type"]?.ToString()}");
                                        }
                                        // 3. Kezeljük a specifikus EVENT TÍPUSOKAT (ha vannak és nem delta/step)
                                        else if (!string.IsNullOrEmpty(eventType))
                                        {
                                            // A switch (eventType) változatlan marad itt...
                                            switch (eventType)
                                            {
                                                case "thread.run.created":
                                                    currentRunId = eventOrMessageObject["data"]?["id"]?.ToString();
                                                    Debug.Log($"[RunLifecycle] Run created with ID: {currentRunId}");
                                                    break;
                                                // A "thread.message.delta" case innen kivehető, mert fentebb kezeljük objectType alapján
                                                case "thread.run.queued":
                                                case "thread.run.in_progress":
                                                    Debug.Log($"[RunLifecycle] Run status changed: {eventType}");
                                                    break;
                                                case "thread.run.completed":
                                                    Debug.Log($"[RunLifecycle] Run completed. Run ID: {eventOrMessageObject["data"]?["id"]?.ToString()}");
                                                    // Ellenőrizzük, hogy a végén a UI tükrözi-e a teljes választ
                                                    if (TMPResponseText != null && TMPResponseText.text != currentResponseChunk.ToString())
                                                    {
                                                        Debug.LogWarning("[Run Completed] Final UI text differs from assembled chunk. Forcing UI update.");
                                                        TMPResponseText.text = currentResponseChunk.ToString();
                                                    }
                                                    break;
                                                case "thread.run.failed":
                                                    Debug.LogError($"[RunLifecycle] Run failed! Run ID: {eventOrMessageObject["data"]?["id"]?.ToString()}, Error: {eventOrMessageObject["data"]?["last_error"]?.ToString()}");
                                                    break;
                                                case "thread.run.requires_action":
                                                    Debug.LogWarning($"[RunLifecycle] Run requires action! Details: {eventOrMessageObject["data"]?.ToString()}");
                                                    break;
                                                default:
                                                    Debug.Log($"[StreamEvent] Unhandled explicit event type: '{eventType}'");
                                                    break;
                                            }
                                        }
                                        // 4. Kezeljük a TELJES ÜZENET objektumokat (ha nem delta/step/explicit event)
                                        else if (objectType == "thread.message")
                                        {
                                            Debug.LogWarning("[StreamObject] Received a complete 'thread.message' object directly (not a delta). Checking content...");
                                            // A meglévő logika a teljes üzenet feldolgozására...
                                            // ... (fontos, hogy az üres content[] esetet is helyesen kezelje: nem csinál semmit)
                                            // ... (a meglévő kód erre jó volt)
                                            JArray contentArray = eventOrMessageObject["content"] as JArray;
                                            if (contentArray != null && contentArray.Count > 0) // Csak akkor próbálkozzunk, ha van tartalom
                                            {
                                                StringBuilder messageContentBuilder = new StringBuilder();
                                                foreach (var contentItem in contentArray)
                                                {
                                                    if (contentItem["type"]?.ToString() == "text")
                                                    {
                                                        string textValue = contentItem["text"]?["value"]?.ToString();
                                                        if (!string.IsNullOrEmpty(textValue))
                                                        {
                                                            Debug.Log($"[MessageObject] Extracted text from full assistant message: '{textValue}'");
                                                            messageContentBuilder.Append(textValue);
                                                        }
                                                    }
                                                }
                                                string finalMessage = messageContentBuilder.ToString();
                                                if (finalMessage.Length > 0)
                                                {
                                                    // Itt eldöntheted, hogy felülírod-e a deltákból összerakott szöveget
                                                    // Jobb lehet, ha csak logolod, és nem írod felül a UI-t,
                                                    // hacsak nem vagy biztos, hogy ez a végleges, teljes válasz.
                                                    Debug.LogWarning($"[MessageObject] Full message content received: '{finalMessage}'. Assembled chunk was: '{currentResponseChunk.ToString()}'");
                                                    // Optional: Force UI update if needed
                                                    // if (TMPResponseText != null) TMPResponseText.text = finalMessage;
                                                    // currentResponseChunk.Clear().Append(finalMessage);
                                                }
                                            }
                                            else
                                            {
                                                Debug.LogWarning("[StreamObject] Full 'thread.message' object received, but 'content' array is empty or null.");
                                            }
                                        }
                                        // 5. Minden más eset
                                        else
                                        {
                                            Debug.LogWarning($"[StreamData] Received data without recognizable delta, step, event type, or full message object: {jsonString}");
                                        }

                                    }
                                    catch (Exception e)
                                    {
                                        Debug.LogError($"Error processing stream data: {e.Message} - JSON: {jsonString}");
                                    }
                                } // if line.StartsWith("data:")
                            } // foreach line
                        } // while van feldolgozandó esemény a bufferben

                        // Ami nem lett feldolgozva (mert nem volt teljes \n\n végű esemény), az a buffer elején marad
                        // Nagyon fontos a puffer helyes kezelése az ismétlődés elkerülése érdekében!
                        if (lastEventEndIndex > 0)
                        {
                            buffer.Remove(0, lastEventEndIndex);
                            // Debug.Log($"Buffer trimmed. Remaining buffer: '{buffer.ToString()}'"); // Szükség esetén logolás
                        }

                    } // if van új adat (currentLength > lastProcessedIndex)
                } // if downloadhandler.data != null

                // Rövid várakozás, hogy ne pörögjön feleslegesen a CPU
                yield return null; // Várakozás a következő frame-ig

            } // while (!asyncOp.isDone)

            // --- A stream feldolgozása befejeződött (vagy hiba történt) ---

            // Ellenőrizzük a végső állapotot
            if (webRequest.result != UnityWebRequest.Result.Success)
            {
                // Ha a ciklusból hiba miatt léptünk ki, a hiba már logolva lett a ciklusban.
                // Itt esetleg egy végső hibaállapotot logolhatunk.
                Debug.LogError($"Assistant run network request finished with status: {webRequest.result}");
            }
            else
            {
                // Ha a ciklusból a [DONE] miatt léptünk ki, a logolás már megtörtént.
                // Ha valahogy máshogy fejeződött be sikeresen (pl. a kapcsolat bontása miatt?), itt logolhatunk.
                Debug.Log("Assistant run network request finished successfully.");
                // Ellenőrizzük még egyszer, hogy van-e valami a bufferben, ami nem lett feldolgozva
                if (buffer.Length > 0)
                {
                    Debug.LogWarning($"Stream finished, but buffer still contains unprocessed data: '{buffer.ToString()}'");
                    // Itt megpróbálhatnánk feldolgozni a maradékot, de ez bonyolult lehet.
                }

                // Végső UI frissítés (általában felesleges, ha a stream feldolgozás helyes volt)
                // Esetleg ellenőrizhetjük, hogy a UI tükrözi-e a 'currentResponseChunk' végső állapotát.
                if (TMPResponseText != null && TMPResponseText.text != currentResponseChunk.ToString())
                {
                    Debug.LogWarning("Final UI text differs from assembled response chunk. Forcing UI update.");
                    TMPResponseText.text = currentResponseChunk.ToString();
                }

                // --- ÚJ RÉSZ: Maradék puffer feldolgozása a TTS Managerben ---
                if (textToSpeechManager != null)
                {
                    Debug.Log("[Run End] Flushing remaining TTS buffer.");
                    textToSpeechManager.FlushBuffer();
                }
                // --- ÚJ RÉSZ VÉGE ---
            }
        } // using UnityWebRequest
    } // IEnumerator CreateAssistantRun

    public IEnumerator SendAudioToWhisper(byte[] audioData, Action<string> onCompleted)
    {
        if (string.IsNullOrEmpty(apiKey) || apiKey == "SK-xxxxxxxxxxxxxxxxxxxx")
        {
            Debug.LogError("OpenAI API Key is not set in OpenAIWebRequest Inspector!");
            onCompleted?.Invoke(null); // Visszajelzés a hívónak, hogy hiba történt
            yield break; // Leállítja a korutint
        }

        if (audioData == null || audioData.Length == 0)
        {
            Debug.LogError("Audio data is empty or null.");
            onCompleted?.Invoke(null);
            yield break;
        }

        Debug.Log($"Sending {audioData.Length} bytes of audio data to Whisper API...");

        // --- A Multipart Form Data összeállítása ---
        List<IMultipartFormSection> formData = new List<IMultipartFormSection>();

        // 1. A hangfájl adat
        // Fontos a fájlnév megadása (bármi lehet .wav kiterjesztéssel)
        formData.Add(new MultipartFormFileSection("file", audioData, "audio.wav", "audio/wav"));

        // 2. A modell neve
        formData.Add(new MultipartFormDataSection("model", ModelName));

        // Opcionális: Nyelv megadása (ha csak egy nyelvet vársz)
        formData.Add(new MultipartFormDataSection("language", "en"));

        // Opcionális: Válasz formátuma (alapértelmezetten json)
        // formData.Add(new MultipartFormDataSection("response_format", "json"));


        // --- UnityWebRequest létrehozása és konfigurálása ---
        UnityWebRequest request = UnityWebRequest.Post(WhisperApiUrl, formData);

        // API Kulcs hozzáadása a Headerhez
        request.SetRequestHeader("Authorization", $"Bearer {apiKey}");

        // FONTOS: NE állítsd be manuálisan a Content-Type-ot multipart kérésnél!
        // A UnityWebRequest.Post(url, formData) ezt automatikusan kezeli.
        // request.SetRequestHeader("Content-Type", "multipart/form-data"); // <<< EZT NE!

        // Várakozási idő növelése hosszabb hangfájlok esetén (opcionális)
        request.timeout = 60; // 60 másodperc

        // --- Kérés küldése és várakozás a válaszra ---
        yield return request.SendWebRequest();

        // --- Válasz feldolgozása ---
        if (request.result == UnityWebRequest.Result.ConnectionError || request.result == UnityWebRequest.Result.ProtocolError)
        {
            Debug.LogError($"Whisper API Error: {request.error}");
            Debug.LogError($"Response Code: {request.responseCode}");
            Debug.LogError($"Response Body: {request.downloadHandler?.text}"); // Próbáljuk kiírni a hibaüzenetet az API-tól
            onCompleted?.Invoke(null); // Hiba jelzése
        }
        else if (request.result == UnityWebRequest.Result.Success)
        {
            Debug.Log($"Whisper API Success! Response Code: {request.responseCode}");
            string responseJson = request.downloadHandler.text;
            Debug.Log($"Whisper Response JSON: {responseJson}");

            // JSON Parse-olás a transzkripció kinyeréséhez
            string transcription = ParseWhisperResponse(responseJson);

            onCompleted?.Invoke(transcription); // Visszaadjuk a sikeres transzkripciót
        }
        else
        {
            // Egyéb hiba (pl. DataProcessingError)
            Debug.LogError($"Whisper API Request failed with result: {request.result}");
            onCompleted?.Invoke(null);
        }

        // Erőforrások felszabadítása
        request.Dispose();
    }
    private string ParseWhisperResponse(string jsonResponse)
    {
        try
        {
            // Newtonsoft.Json használatával:
            JObject jsonObject = JObject.Parse(jsonResponse);
            string transcription = (string)jsonObject["text"];

            if (string.IsNullOrWhiteSpace(transcription))
            {
                Debug.LogWarning("Whisper returned an empty transcription.");
                return string.Empty; // Vagy null, ahogy preferálod
            }
            return transcription.Trim();

        }
        catch (Exception ex)
        {
            Debug.LogError($"Failed to parse Whisper JSON response: {ex.Message}");
            Debug.LogError($"JSON attempted to parse: {jsonResponse}");
            return null; // Hiba jelzése
        }
    }

}
