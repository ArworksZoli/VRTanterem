using UnityEngine;
using UnityEngine.Networking;
using System.Collections;
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
    // [SerializeField] private TextToSpeechManager textToSpeechManager; // Eltávolítva

    // --- Belső Változók ---
    private string assistantThreadId; // Az aktuális beszélgetési szál azonosítója
    private string currentRunId; // Az aktuális futtatás azonosítója (streaminghez)
    private StringBuilder messageBuilder = new StringBuilder(); // Üzenetek építéséhez (jelenleg kevésbé használt a streaming miatt)
    private StringBuilder buffer = new StringBuilder(); // Bejövő streaming adatok puffereléséhez
    // private string fullMessage = ""; // Eltávolítva vagy újragondolva, ha a teljes üzenet követése szükséges
    // private string lastProcessedContent = ""; // Eltávolítva vagy újragondolva

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
                                        string eventType = eventOrMessageObject["event"]?.ToString(); // Lehet null/üres
                                        string objectType = eventOrMessageObject["object"]?.ToString(); // Pl. "thread.message", "thread.run", stb.

                                        // Logoljuk, mit kaptunk
                                        Debug.Log($"[STREAM DATA RECEIVED] EventType: '{eventType ?? "N/A"}', ObjectType: '{objectType ?? "N/A"}', RawJSON: {jsonString}");

                                        // 1. Elsődleges kezelés: Standard Stream Események
                                        if (!string.IsNullOrEmpty(eventType))
                                        {
                                            switch (eventType)
                                            {
                                                case "thread.run.created":
                                                    currentRunId = eventOrMessageObject["data"]?["id"]?.ToString();
                                                    Debug.Log($"[RunLifecycle] Run created with ID: {currentRunId}");
                                                    break;

                                                case "thread.message.delta":
                                                    JArray contentDeltas = eventOrMessageObject["data"]?["delta"]?["content"] as JArray;
                                                    if (contentDeltas != null)
                                                    {
                                                        foreach (var deltaItem in contentDeltas)
                                                        {
                                                            if (deltaItem["type"]?.ToString() == "text")
                                                            {
                                                                string textDelta = deltaItem["text"]?["value"]?.ToString();
                                                                Debug.Log($"[MessageDelta] Received text delta: '{(textDelta ?? "NULL")}'");
                                                                if (!string.IsNullOrEmpty(textDelta))
                                                                {
                                                                    currentResponseChunk.Append(textDelta); // Építjük a választ
                                                                    if (TMPResponseText != null)
                                                                    {
                                                                        TMPResponseText.text = currentResponseChunk.ToString(); // Frissítjük a UI-t
                                                                        // Opcionális: Logoljuk a UI frissítést is
                                                                        // Debug.Log($"[UI Update from Delta] TMPResponseText set to: '{currentResponseChunk.ToString()}'");
                                                                    }
                                                                }
                                                            }
                                                            else { Debug.Log($"[MessageDelta] Received non-text delta part. Type: '{deltaItem["type"]?.ToString()}'"); }
                                                        }
                                                    }
                                                    break;

                                                // Run állapotváltozások logolása
                                                case "thread.run.queued":
                                                case "thread.run.in_progress":
                                                    Debug.Log($"[RunLifecycle] Run status changed: {eventType}");
                                                    break;
                                                case "thread.run.completed":
                                                    Debug.Log($"[RunLifecycle] Run completed. Run ID: {eventOrMessageObject["data"]?["id"]?.ToString()}");
                                                    break;
                                                case "thread.run.failed":
                                                    Debug.LogError($"[RunLifecycle] Run failed! Run ID: {eventOrMessageObject["data"]?["id"]?.ToString()}, Error: {eventOrMessageObject["data"]?["last_error"]?.ToString()}");
                                                    break;
                                                case "thread.run.requires_action":
                                                    Debug.LogWarning($"[RunLifecycle] Run requires action! Details: {eventOrMessageObject["data"]?.ToString()}");
                                                    break;
                                                default:
                                                    Debug.Log($"[StreamEvent] Unhandled event type: '{eventType}'");
                                                    break;
                                            }
                                        }
                                        // 2. Másodlagos kezelés: Ha nincs eventType, de objectType igen (pl. teljes üzenet)
                                        else if (!string.IsNullOrEmpty(objectType))
                                        {
                                            switch (objectType)
                                            {
                                                case "thread.message":
                                                    Debug.LogWarning("[StreamObject] Received a complete 'thread.message' object directly in the stream.");
                                                    // Próbáljuk meg kinyerni a tartalmat ebből a teljes üzenetből
                                                    // Ellenőrizzük, hogy ez assistant üzenet-e
                                                    if (eventOrMessageObject["role"]?.ToString() == "assistant")
                                                    {
                                                        JArray contentArray = eventOrMessageObject["content"] as JArray;
                                                        if (contentArray != null)
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

                                                            // Frissítsük a UI-t a teljes üzenet tartalmával
                                                            string finalMessage = messageContentBuilder.ToString();
                                                            if (finalMessage.Length > 0)
                                                            {
                                                                // Elmentjük a végleges választ (felülírva a deltákat, ha voltak)
                                                                currentResponseChunk.Clear().Append(finalMessage);
                                                                if (TMPResponseText != null)
                                                                {
                                                                    TMPResponseText.text = finalMessage;
                                                                    Debug.Log($"[UI Update from MessageObject] TMPResponseText set to: '{finalMessage}'");
                                                                }
                                                            }
                                                        }
                                                    }
                                                    else
                                                    {
                                                        Debug.Log($"[StreamObject] Received 'thread.message' object, but role is not 'assistant' (Role: {eventOrMessageObject["role"]?.ToString()}). Ignoring for response display.");
                                                    }
                                                    break;
                                                // Kezelhetnénk más objektumtípusokat is, ha szükséges
                                                // case "thread.run": ...
                                                default:
                                                    Debug.Log($"[StreamObject] Received object type '{objectType}' without a specific event type.");
                                                    break;
                                            }
                                        }
                                        else
                                        {
                                            // Nem volt se eventType, se objectType?
                                            Debug.LogWarning($"[StreamData] Received data without recognizable event or object type: {jsonString}");
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
            }
        } // using UnityWebRequest
    } // IEnumerator CreateAssistantRun

}
