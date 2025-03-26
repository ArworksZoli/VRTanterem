using UnityEngine;
using UnityEngine.Networking;
using System.Collections;
using System.Text;
using System;
using Newtonsoft.Json.Linq;
using TMPro;
using UnityEngine.UI;


public class OpenAIWebRequest : MonoBehaviour
{
    private string apiKey = "APIKEY";
    private string assistantID = "ASSISTANID";
    private string apiUrl = "https://api.openai.com/v1";

    public string userInput = "Hello!";  // Alapértelmezett elsõ üzenet
    public TMP_Text TMPResponseText;
    [SerializeField] private TMP_InputField TMPInputField;
    public Button SendButton;
    public TMP_Text TMPUserText;

    [SerializeField] private TextToSpeechManager textToSpeechManager;
    private string assistantThreadId;
    private string currentRunId;
    private StringBuilder messageBuilder = new StringBuilder();
    private StringBuilder buffer = new StringBuilder();
    private string fullMessage = "";
    private string lastProcessedContent = "";

    private void Start()
    {
        SendButton.onClick.AddListener(SendButtonClick);

        if (TMPInputField != null)
        {
            TMPInputField.onSubmit.AddListener((value) => {
                if (!string.IsNullOrEmpty(value))
                {
                    SendButtonClick();
                }
            });
        }

        StartCoroutine(GetAssistant());
        StartCoroutine(CreateThread());
    }

    private void SetCommonHeaders(UnityWebRequest request)
    {
        request.SetRequestHeader("Content-Type", "application/json");
        request.SetRequestHeader("Authorization", $"Bearer {apiKey}");
        request.SetRequestHeader("OpenAI-Beta", "assistants=v2");
        Debug.Log("Headers set: Content-Type, Authorization, OpenAI-Beta");
    }

    private IEnumerator GetAssistant()
    {
        string url = $"{apiUrl}/assistants/{assistantID}";
        Debug.Log("Getting Assistant at URL: " + url);

        using (UnityWebRequest webRequest = UnityWebRequest.Get(url))
        {
            SetCommonHeaders(webRequest);

            yield return webRequest.SendWebRequest();

            if (webRequest.result == UnityWebRequest.Result.ConnectionError || webRequest.result == UnityWebRequest.Result.ProtocolError)
            {
                Debug.LogError("Error while getting assistant: " + webRequest.error);
            }
            else
            {
                Debug.Log("Assistant Retrieved: " + webRequest.downloadHandler.text);
                // Process JSON response if needed
            }
        }
    }

    private IEnumerator CreateThread()
    {
        string url = $"{apiUrl}/threads";
        Debug.Log("Creating Thread at URL: " + url);

        JObject requestBody = new JObject
        {
            ["messages"] = new JArray
            {
                new JObject
                {
                    ["role"] = "user",
                    ["content"] = userInput
                }
            }
        };

        string jsonBody = requestBody.ToString();
        Debug.Log("Thread creation JSON: " + jsonBody);

        using (UnityWebRequest webRequest = new UnityWebRequest(url, "POST"))
        {
            byte[] bodyRaw = Encoding.UTF8.GetBytes(jsonBody);
            webRequest.uploadHandler = new UploadHandlerRaw(bodyRaw);
            webRequest.downloadHandler = new DownloadHandlerBuffer();

            SetCommonHeaders(webRequest);

            yield return webRequest.SendWebRequest();

            if (webRequest.result == UnityWebRequest.Result.ConnectionError || webRequest.result == UnityWebRequest.Result.ProtocolError)
            {
                Debug.LogError("Error while creating thread: " + webRequest.error);
                Debug.LogError("Response: " + webRequest.downloadHandler.text);
            }
            else
            {
                Debug.Log("Thread Created: " + webRequest.downloadHandler.text);
                JObject responseJson = JObject.Parse(webRequest.downloadHandler.text);
                assistantThreadId = responseJson["id"]?.ToString();

                if (!string.IsNullOrEmpty(assistantThreadId))
                {
                    StartCoroutine(GetAssistantResponse(userInput));
                }
                else
                {
                    Debug.LogError("Failed to retrieve assistantThreadId.");
                }
            }
        }
    }

    private IEnumerator CancelCurrentRun()
    {
        if (!string.IsNullOrEmpty(currentRunId) && !string.IsNullOrEmpty(assistantThreadId))
        {
            string cancelUrl = $"{apiUrl}/threads/{assistantThreadId}/runs/{currentRunId}/cancel";
            Debug.Log($"Attempting to cancel run: {currentRunId}");

            using (UnityWebRequest webRequest = new UnityWebRequest(cancelUrl, "POST"))
            {
                webRequest.downloadHandler = new DownloadHandlerBuffer();
                SetCommonHeaders(webRequest);

                yield return webRequest.SendWebRequest();

                if (webRequest.result == UnityWebRequest.Result.Success)
                {
                    Debug.Log($"Run {currentRunId} cancelled successfully");
                }
                else
                {
                    Debug.LogWarning($"Run cancellation failed or was not necessary: {webRequest.error}");
                }
            }
            currentRunId = null;
        }
    }
    public void SendButtonClick()
    {
        if (!string.IsNullOrEmpty(assistantThreadId))
        {
            string input = TMPInputField.text;
            if (!string.IsNullOrEmpty(input))
            {
                messageBuilder.Clear();
                buffer.Clear();
                fullMessage = "";
                lastProcessedContent = "";

                StartCoroutine(SendMessageSequence(input));
            }
        }
        else
        {
            Debug.LogError("Failed to retrieve assistantThreadId.");
        }
    }

    private IEnumerator SendMessageSequence(string input)
    {
        // Először próbáljuk meg törölni a futó kérést
        yield return StartCoroutine(CancelCurrentRun());

        // Kis szünet a biztonságos feldolgozáshoz
        yield return new WaitForSeconds(0.1f);

        // Új kérés indítása
        yield return StartCoroutine(GetAssistantResponse(input));

        if (TMPUserText != null)
        {
            TMPUserText.text = "User: " + input;
        }
        TMPInputField.text = "";
    }

    private IEnumerator GetAssistantResponse(string userInput)
    {
        string messageURL = $"{apiUrl}/threads/{assistantThreadId}/messages";
        Debug.Log("Posting User Message to URL: " + messageURL);

        if (string.IsNullOrEmpty(assistantThreadId))
        {
            Debug.LogError("Assistant Thread ID is invalid. Ensure the thread is created successfully.");
            yield break;
        }

        JObject messageBody = new JObject
        {
            ["role"] = "user",
            ["content"] = userInput
        };

        string messageJson = messageBody.ToString();
        Debug.Log("Sending User Message JSON: " + messageJson);

        using (UnityWebRequest webRequest = new UnityWebRequest(messageURL, "POST"))
        {
            byte[] bodyRaw = Encoding.UTF8.GetBytes(messageJson);
            webRequest.uploadHandler = new UploadHandlerRaw(bodyRaw);
            webRequest.downloadHandler = new DownloadHandlerBuffer();

            SetCommonHeaders(webRequest);

            yield return webRequest.SendWebRequest();

            if (webRequest.result == UnityWebRequest.Result.ConnectionError || webRequest.result == UnityWebRequest.Result.ProtocolError)
            {
                Debug.LogError("Error while posting user message: " + webRequest.error);
                Debug.LogError("Response: " + webRequest.downloadHandler.text);
            }
            else
            {
                Debug.Log("User message added to thread successfully.");
                StartCoroutine(CreateAssistantRun());
            }
        }
    }

    private IEnumerator CreateAssistantRun()
    {
        string runUrl = $"{apiUrl}/threads/{assistantThreadId}/runs?stream=true";
        Debug.Log("Creating assistant run with streaming at URL: " + runUrl);

        var runBody = new JObject
        {
            ["assistant_id"] = assistantID,
            ["stream"] = true
        };
        string runJson = runBody.ToString();
        Debug.Log("Run creation JSON: " + runJson);

        using (UnityWebRequest webRequest = new UnityWebRequest(runUrl, "POST"))
        {
            webRequest.uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(runJson));
            webRequest.downloadHandler = new DownloadHandlerBuffer();

            SetCommonHeaders(webRequest);
            UnityWebRequestAsyncOperation asyncOp = webRequest.SendWebRequest();

            // A run ID kinyerése a válaszból
            if (webRequest.result == UnityWebRequest.Result.Success)
            {
                try
                {
                    JObject responseJson = JObject.Parse(webRequest.downloadHandler.text);
                    currentRunId = responseJson["id"]?.ToString();
                    Debug.Log($"New run created with ID: {currentRunId}");
                }
                catch (Exception e)
                {
                    Debug.LogError($"Error parsing run response: {e.Message}");
                }
            }

            StringBuilder currentMessageBuilder = new StringBuilder();
            StringBuilder buffer = new StringBuilder(); // Buffer for incoming data

            while (!asyncOp.isDone)
            {
                if (webRequest.result == UnityWebRequest.Result.ConnectionError || webRequest.result == UnityWebRequest.Result.ProtocolError)
                {
                    Debug.LogError("Error during assistant run: " + webRequest.error);
                    Debug.LogError("Response: " + webRequest.downloadHandler.text);
                    yield break;
                }

                byte[] receivedBytes = webRequest.downloadHandler.data;
                if (receivedBytes != null && receivedBytes.Length > 0)
                {
                    string newText = Encoding.UTF8.GetString(receivedBytes);
                    Debug.Log("New Text Received: " + newText);

                    buffer.Append(newText);

                    // Process each buffer content
                    StringBuilder blockUpdate = new StringBuilder(); // To track what's added in this block
                    string[] lines = buffer.ToString().Split(new char[] { '\n' }, StringSplitOptions.RemoveEmptyEntries);

                    buffer.Clear(); // Clear buffer for new iteration

                    foreach (string line in lines)
                    {
                        if (line.StartsWith("data:"))
                        {
                            string jsonString = line.Substring(5).Trim();
                            if (jsonString == "[DONE]")
                            {
                                Debug.Log("Streaming completed.");
                                yield break;
                            }

                            try
                            {
                                JObject eventObject = JObject.Parse(jsonString);
                                if (eventObject["object"]?.ToString() == "thread.message.delta")
                                {
                                    JArray deltas = eventObject["delta"]["content"] as JArray;
                                    if (deltas != null)
                                    {
                                        foreach (JObject delta in deltas)
                                        {
                                            if (delta["type"]?.ToString() == "text")
                                            {
                                                string contentDelta = delta["text"]["value"]?.ToString() ?? "";
                                                Debug.Log("Streaming Update: " + contentDelta); // Log each text delta

                                                blockUpdate.Append(contentDelta); // Track current block's update
                                            }
                                        }
                                    }
                                }
                            }
                            catch (Exception e)
                            {
                                Debug.LogError("Error parsing JSON: " + e.Message);
                            }
                        }
                    }

                    // Log block-wise update
                    if (blockUpdate.Length > 0)
                    {
                        Debug.Log("Block Appended: " + blockUpdate.ToString());

                        // Update answerText with only the content from the current block
                        if (TMPResponseText != null)
                        {
                            Debug.Log("Setting answerText with block content.");
                            TMPResponseText.text = blockUpdate.ToString();

                            if (textToSpeechManager != null)
                            {
                                // A teljes üzenetet küldjük a beszédszintézishez, ne csak a frissítést
                                textToSpeechManager.ReadAIResponse(TMPResponseText.text);
                            }

                        }
                    }
                }

                yield return new WaitForSeconds(0.1f);
                Debug.Log("FREQUENCY CHEEECK");
            }

            // Final update if any data left
            if (currentMessageBuilder.Length > 0 && TMPResponseText != null)
            {
                TMPResponseText.text = currentMessageBuilder.ToString();

            }
        }
    }
}