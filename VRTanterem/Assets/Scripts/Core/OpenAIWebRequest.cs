using UnityEngine;
using UnityEngine.Networking;
using System.Collections;
using System.Text;
using System;
using Newtonsoft.Json.Linq; // Requires Newtonsoft.Json package (e.g., via Unity Package Manager)
using TMPro; // Keep if using TextMeshPro for display
using Meta.WitAi.TTS.Utilities; // Required for TTSWit

public class OpenAIWebRequest : MonoBehaviour
{
    // --- Configuration ---
    [Header("API Configuration")]
    [Tooltip("Your OpenAI API Key. DO NOT HARDCODE IN PRODUCTION! Load securely.")]
    [SerializeField] private string apiKey = "YOUR_API_KEY"; // <- TODO: Set Your Key Here (or load securely)
    [Tooltip("Your OpenAI Assistant ID.")]
    [SerializeField] private string assistantID = "YOUR_ASSISTANT_ID"; // <- TODO: Set Your Assistant ID Here
    private string apiUrl = "https://api.openai.com/v1";

    [Header("Conversation Setup")]
    [Tooltip("The initial message sent by the user to start the conversation thread.")]
    [SerializeField] private string initialMessage = "Hello!";

    // --- UI Display (Optional) ---
    [Header("UI Display (Optional)")]
    [Tooltip("Text element to display the AI's response.")]
    [SerializeField] private TMP_Text TMPResponseText;
    [Tooltip("Text element to display the User's recognized speech.")]
    [SerializeField] private TMP_Text TMPUserText;

    // --- Meta Voice SDK TTS Reference ---
    [Header("Speech Synthesis")]
    [Tooltip("Reference to the TTSWit component for speaking the AI's response.")]
    [SerializeField] private TTSSpeaker ttsSpeaker; // <- Itt változott a típus TTSSpeaker-re


    // --- Internal State ---
    private string assistantThreadId;
    private string currentRunId;
    private StringBuilder streamingBuffer = new StringBuilder(); // Buffer for incoming stream data
    private bool isProcessing = false; // Flag to prevent concurrent requests

    //--------------------------------------------------------------------------
    // UNITY LIFECYCLE
    //--------------------------------------------------------------------------

    private void Start()
    {
        // --- Validate essential references ---
        if (string.IsNullOrEmpty(apiKey) || apiKey == "YOUR_API_KEY")
        {
            Debug.LogError($"[{GetType().Name}] OpenAI API Key is not set in the Inspector!", this);
            enabled = false; return;
        }
        if (string.IsNullOrEmpty(assistantID) || assistantID == "YOUR_ASSISTANT_ID")
        {
            Debug.LogError($"[{GetType().Name}] OpenAI Assistant ID is not set in the Inspector!", this);
            enabled = false; return;
        }
        if (ttsSpeaker == null)
        {
            // This might be acceptable if TTS is optional
            Debug.LogWarning($"[{GetType().Name}] TTSWit component is not assigned in the Inspector. Text-to-speech will not function.", this);
        }
        if (TMPResponseText == null)
        {
            // Optional display
            Debug.LogWarning($"[{GetType().Name}] TMPResponseText not assigned. AI responses won't be displayed visually.", this);
        }
        if (TMPUserText == null)
        {
            // Optional display
            Debug.LogWarning($"[{GetType().Name}] TMPUserText not assigned. User speech won't be displayed visually.", this);
        }


        // Start the assistant setup and thread creation
        StartCoroutine(InitializeAssistant());
    }

    //--------------------------------------------------------------------------
    // PUBLIC INTERFACE
    //--------------------------------------------------------------------------

    /// <summary>
    /// Public method called by an external system (e.g., VoiceInputHandler)
    /// to process recognized speech text.
    /// </summary>
    /// <param name="recognizedText">The text transcribed from user speech.</param>
    public void ProcessVoiceInput(string recognizedText)
    {
        if (isProcessing)
        {
            Debug.LogWarning($"[{GetType().Name}] Already processing a request. Ignoring new input: '{recognizedText}'");
            return;
        }
        if (string.IsNullOrEmpty(assistantThreadId))
        {
            Debug.LogError($"[{GetType().Name}] Cannot process voice input: Assistant Thread ID is not set. Was initialization successful?", this);
            return;
        }
        if (string.IsNullOrWhiteSpace(recognizedText))
        {
            Debug.LogWarning($"[{GetType().Name}] Cannot process voice input: Recognized text is empty or whitespace.", this);
            return;
        }

        Debug.Log($"[{GetType().Name}] Processing Voice Input: '{recognizedText}'");
        isProcessing = true; // Set processing flag

        // Display user's input (optional)
        if (TMPUserText != null)
        {
            TMPUserText.text = $"User: {recognizedText}";
        }

        // Clear or update response text to indicate processing (optional)
        if (TMPResponseText != null)
        {
            TMPResponseText.text = "..."; // Or "Listening...", "Thinking..."
        }

        // Stop any ongoing TTS from the previous turn
        if (ttsSpeaker != null && ttsSpeaker.IsSpeaking)
        {
            Debug.Log($"[{GetType().Name}] Stopping previous TTS utterance.");
            ttsSpeaker.Stop();
        }

        // Start the sequence: Cancel (if needed) -> Add Message -> Create Run
        StartCoroutine(SendMessageSequence(recognizedText));
    }

    //--------------------------------------------------------------------------
    // CORE ASSISTANT COROUTINES
    //--------------------------------------------------------------------------

    private IEnumerator InitializeAssistant()
    {
        Debug.Log($"[{GetType().Name}] Initializing Assistant...");
        yield return StartCoroutine(GetAssistant()); // Verify assistant exists (optional but good practice)
        yield return StartCoroutine(CreateThread()); // Create the conversation thread
        if (!string.IsNullOrEmpty(assistantThreadId))
        {
            Debug.Log($"[{GetType().Name}] Assistant Initialized. Thread ID: {assistantThreadId}");
        }
        else
        {
            Debug.LogError($"[{GetType().Name}] Assistant Initialization Failed. Could not create thread.", this);
        }
    }

    private IEnumerator SendMessageSequence(string userInput)
    {
        // 1. Attempt to cancel any potentially lingering run (might not exist)
        yield return StartCoroutine(CancelCurrentRun());

        // Optional brief pause if cancellation needs time server-side
        // yield return new WaitForSeconds(0.1f);

        // 2. Add the new user message to the thread
        bool messageAdded = false;
        yield return StartCoroutine(AddMessageToThread(userInput, success => messageAdded = success));

        // 3. Create a new run ONLY if the message was added successfully
        if (messageAdded && !string.IsNullOrEmpty(assistantThreadId))
        {
            yield return StartCoroutine(CreateAssistantRun());
        }
        else
        {
            Debug.LogError($"[{GetType().Name}] Failed to add message to thread, skipping run creation.", this);
            // Reset UI or provide error feedback if needed
            if (TMPResponseText != null) TMPResponseText.text = "Error sending message.";
            isProcessing = false; // Ensure flag is reset even if run doesn't start
        }

        // 4. Reset processing flag AFTER the entire sequence (including run) is complete or has failed
        // isProcessing = false; // Moved the reset to the end of CreateAssistantRun and the error case above

        Debug.Log($"[{GetType().Name}] SendMessageSequence finished for input: '{userInput}'");
    }


    private IEnumerator AddMessageToThread(string userInput, System.Action<bool> callback)
    {
        if (string.IsNullOrEmpty(assistantThreadId))
        {
            Debug.LogError($"[{GetType().Name}] Cannot add message: Assistant Thread ID is invalid.", this);
            callback?.Invoke(false);
            yield break;
        }

        string messageURL = $"{apiUrl}/threads/{assistantThreadId}/messages";
        // Debug.Log($"[{GetType().Name}] Posting User Message to URL: {messageURL}"); // Reduce log spam

        JObject messageBody = new JObject
        {
            ["role"] = "user",
            ["content"] = userInput
        };
        string messageJson = messageBody.ToString();
        // Debug.Log($"[{GetType().Name}] Sending User Message JSON: {messageJson}"); // Reduce log spam

        using (UnityWebRequest webRequest = new UnityWebRequest(messageURL, "POST"))
        {
            byte[] bodyRaw = Encoding.UTF8.GetBytes(messageJson);
            webRequest.uploadHandler = new UploadHandlerRaw(bodyRaw);
            webRequest.downloadHandler = new DownloadHandlerBuffer();
            SetCommonHeaders(webRequest);

            yield return webRequest.SendWebRequest();

            if (webRequest.result != UnityWebRequest.Result.Success)
            {
                Debug.LogError($"[{GetType().Name}] Error posting user message: {webRequest.error} (Code: {webRequest.responseCode}) - Response: {webRequest.downloadHandler?.text}", this);
                callback?.Invoke(false);
            }
            else
            {
                Debug.Log($"[{GetType().Name}] User message added to thread successfully.");
                callback?.Invoke(true);
            }
        }
    }

    private IEnumerator CreateAssistantRun()
    {
        if (string.IsNullOrEmpty(assistantThreadId))
        {
            Debug.LogError($"[{GetType().Name}] Cannot create run: Assistant Thread ID is invalid.", this);
            isProcessing = false; // Reset flag on error before run starts
            yield break;
        }

        string runUrl = $"{apiUrl}/threads/{assistantThreadId}/runs";
        Debug.Log($"[{GetType().Name}] Creating assistant run with streaming...");

        var runBody = new JObject
        {
            ["assistant_id"] = assistantID,
            ["stream"] = true // Request Server-Sent Events (SSE)
        };
        string runJson = runBody.ToString();

        using (UnityWebRequest webRequest = new UnityWebRequest(runUrl, "POST"))
        {
            webRequest.uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(runJson));
            webRequest.downloadHandler = new DownloadHandlerBuffer(); // Process data as it arrives
            SetCommonHeaders(webRequest);
            // Optional: Set Accept header, though usually not needed with stream=true
            // webRequest.SetRequestHeader("Accept", "text/event-stream");

            UnityWebRequestAsyncOperation asyncOp = webRequest.SendWebRequest();

            StringBuilder fullResponseBuilder = new StringBuilder(); // Collects the complete text response
            streamingBuffer.Clear(); // Clear buffer for this run
            long processedBytes = 0;
            bool runIdReceived = false;
            bool doneReceived = false;

            // --- Stream Processing Loop ---
            while (!asyncOp.isDone)
            {
                // --- Error Check ---
                if (webRequest.result == UnityWebRequest.Result.ConnectionError || webRequest.result == UnityWebRequest.Result.ProtocolError)
                {
                    Debug.LogError($"[{GetType().Name}] Error during assistant run stream: {webRequest.error} (Code: {webRequest.responseCode}) - Response: {webRequest.downloadHandler?.text}", this);
                    currentRunId = null; // Clear potentially invalid run ID
                    if (TMPResponseText != null) TMPResponseText.text = "Error receiving response.";
                    isProcessing = false; // Reset flag on stream error
                    yield break; // Exit coroutine on error
                }

                // --- Process New Data ---
                var handler = webRequest.downloadHandler as DownloadHandlerBuffer;
                if (handler?.data != null)
                {
                    long currentLength = handler.data.Length;
                    if (currentLength > processedBytes)
                    {
                        // Get only the new chunk
                        byte[] newBytes = new byte[currentLength - processedBytes];
                        Array.Copy(handler.data, processedBytes, newBytes, 0, newBytes.Length);
                        string newTextChunk = Encoding.UTF8.GetString(newBytes);
                        // Debug.Log($"Chunk: {newTextChunk}"); // Very verbose

                        streamingBuffer.Append(newTextChunk);
                        processedBytes = currentLength;

                        // --- Parse SSE Events ---
                        // Process complete events (separated by "\n\n") in the buffer
                        string bufferContent = streamingBuffer.ToString();
                        int eventEndIndex;
                        while ((eventEndIndex = bufferContent.IndexOf("\n\n")) >= 0)
                        {
                            string eventBlock = bufferContent.Substring(0, eventEndIndex);
                            streamingBuffer.Remove(0, eventEndIndex + 2); // Remove processed event + "\n\n"
                            bufferContent = streamingBuffer.ToString(); // Update buffer content for next iteration

                            string eventType = null;
                            string eventData = null;

                            // Parse lines within the event block
                            string[] lines = eventBlock.Split(new[] { '\n' }, StringSplitOptions.RemoveEmptyEntries);
                            foreach (string line in lines)
                            {
                                if (line.StartsWith("event:"))
                                {
                                    eventType = line.Substring(6).Trim();
                                }
                                else if (line.StartsWith("data:"))
                                {
                                    eventData = line.Substring(5).Trim();
                                }
                            }

                            // --- Handle Parsed Event ---
                            if (eventData == "[DONE]")
                            {
                                Debug.Log($"[{GetType().Name}] Streaming: [DONE] received.");
                                doneReceived = true;
                                // Don't break loop immediately, let asyncOp finish naturally
                                continue;
                            }

                            if (!string.IsNullOrEmpty(eventType) && !string.IsNullOrEmpty(eventData))
                            {
                                // Debug.Log($"Type: {eventType}, Data: {eventData}"); // Verbose
                                try
                                {
                                    JObject dataJson = JObject.Parse(eventData);

                                    switch (eventType)
                                    {
                                        case "thread.run.created":
                                            currentRunId = dataJson["id"]?.ToString();
                                            runIdReceived = true;
                                            Debug.Log($"[{GetType().Name}] Run started with ID: {currentRunId}");
                                            break;

                                        case "thread.message.delta":
                                            // Extract text delta: data.delta.content[0].text.value
                                            JToken deltaToken = dataJson.SelectToken("delta.content[0].text.value");
                                            if (deltaToken != null)
                                            {
                                                string contentDelta = deltaToken.ToString();
                                                fullResponseBuilder.Append(contentDelta);

                                                // Update display text incrementally
                                                if (TMPResponseText != null)
                                                {
                                                    TMPResponseText.text = fullResponseBuilder.ToString();
                                                }
                                            }
                                            break;

                                        // --- Handle other events if needed ---
                                        case "thread.run.queued":
                                        case "thread.run.in_progress":
                                        case "thread.run.requires_action": // Important for function calling
                                            Debug.Log($"[{GetType().Name}] Run Status: {eventType}");
                                            // TODO: Implement handling for requires_action if using tools/functions
                                            break;

                                        case "thread.run.completed":
                                            Debug.Log($"[{GetType().Name}] Run Status: {eventType}");
                                            // Final status, loop will end soon.
                                            break;

                                        case "thread.run.failed":
                                        case "thread.run.cancelling":
                                        case "thread.run.cancelled":
                                        case "thread.run.expired":
                                            Debug.LogWarning($"[{GetType().Name}] Run Status: {eventType} - Run may not complete successfully. Data: {eventData}");
                                            // Handle failure/cancellation states if necessary
                                            break;

                                        case "error":
                                            Debug.LogError($"[{GetType().Name}] Streaming Error Event: {eventData}", this);
                                            break;

                                        default:
                                            // Debug.Log($"[{GetType().Name}] Received unhandled event type: {eventType}");
                                            break;
                                    }
                                }
                                catch (Exception e)
                                {
                                    Debug.LogWarning($"[{GetType().Name}] Error parsing stream JSON for event '{eventType}': {e.Message} - JSON: {eventData}", this);
                                }
                            }
                        } // End while processing buffer
                    }
                }

                yield return null; // Wait for the next frame before checking again
            } // End of while (!asyncOp.isDone) loop

            // --- Request Finished ---
            if (webRequest.result != UnityWebRequest.Result.Success && !doneReceived) // If loop exited due to error before [DONE]
            {
                // Error should have been logged in the loop. Ensure final state.
                Debug.LogError($"[{GetType().Name}] Assistant run request finished with error: {webRequest.error}", this);
                if (TMPResponseText != null && fullResponseBuilder.Length == 0) TMPResponseText.text = "Error getting response.";
            }
            else
            {
                // --- Success Case ---
                Debug.Log($"[{GetType().Name}] Assistant run stream finished processing.");
                string finalResponse = fullResponseBuilder.ToString();
                Debug.Log($"[{GetType().Name}] Final Assistant Response: {finalResponse}");

                // Update final text display (might be redundant if updated incrementally)
                if (TMPResponseText != null)
                {
                    TMPResponseText.text = finalResponse;
                }

                // --- Text-to-Speech Call ---
                if (ttsSpeaker != null && !string.IsNullOrWhiteSpace(finalResponse))
                {
                    // Stop again just in case something triggered TTS while processing
                    if (ttsSpeaker.IsSpeaking)
                    {
                        Debug.LogWarning($"[{GetType().Name}] TTS was speaking when run finished, stopping it before new utterance.");
                        ttsSpeaker.Stop();
                        // yield return new WaitForSeconds(0.1f); // Optional small delay
                    }

                    Debug.Log($"[{GetType().Name}] Sending final response to TTSWit for speech synthesis.");
                    ttsSpeaker.Speak(finalResponse); // Use the Meta Voice SDK TTS component
                }
                else if (ttsSpeaker == null)
                {
                    Debug.LogWarning($"[{GetType().Name}] TTSWit component not assigned, cannot speak the response.", this);
                }
                else // TTS is assigned, but response is empty/whitespace
                {
                    Debug.LogWarning($"[{GetType().Name}] Final response was empty or whitespace, nothing to speak.", this);
                    if (TMPResponseText != null && string.IsNullOrWhiteSpace(finalResponse)) TMPResponseText.text = "[No text in response]";
                }
            }

            // --- Cleanup ---
            currentRunId = null; // Run is completed or failed
            isProcessing = false; // Reset processing flag for the next input
            Debug.Log($"[{GetType().Name}] CreateAssistantRun finished. isProcessing = false.");

        } // End of using UnityWebRequest
    }


    //--------------------------------------------------------------------------
    // UTILITY & API HELPER COROUTINES
    //--------------------------------------------------------------------------

    private IEnumerator GetAssistant()
    {
        // Optional: Verify the assistant ID is valid on start
        string url = $"{apiUrl}/assistants/{assistantID}";
        Debug.Log($"[{GetType().Name}] Verifying Assistant ID: {assistantID}...");

        using (UnityWebRequest webRequest = UnityWebRequest.Get(url))
        {
            SetCommonHeaders(webRequest);
            yield return webRequest.SendWebRequest();

            if (webRequest.result != UnityWebRequest.Result.Success)
            {
                Debug.LogError($"[{GetType().Name}] Error getting assistant details: {webRequest.error} (Code: {webRequest.responseCode}) - Response: {webRequest.downloadHandler?.text}", this);
                // Consider disabling the component or preventing thread creation if assistant is invalid
            }
            else
            {
                Debug.Log($"[{GetType().Name}] Assistant details retrieved successfully.");
                // Optional: Parse response to confirm assistant name, etc.
                // JObject responseJson = JObject.Parse(webRequest.downloadHandler.text);
                // Debug.Log($"Assistant Name: {responseJson["name"]}");
            }
        }
    }

    private IEnumerator CreateThread()
    {
        if (!string.IsNullOrEmpty(assistantThreadId))
        {
            Debug.LogWarning($"[{GetType().Name}] Thread already exists ({assistantThreadId}). Skipping creation.", this);
            yield break;
        }

        string url = $"{apiUrl}/threads";
        Debug.Log($"[{GetType().Name}] Creating new Thread...");

        // Create thread with the initial user message
        JObject requestBody = new JObject
        {
            ["messages"] = new JArray
            {
                new JObject { ["role"] = "user", ["content"] = initialMessage }
            }
        };
        string jsonBody = requestBody.ToString();

        using (UnityWebRequest webRequest = new UnityWebRequest(url, "POST"))
        {
            byte[] bodyRaw = Encoding.UTF8.GetBytes(jsonBody);
            webRequest.uploadHandler = new UploadHandlerRaw(bodyRaw);
            webRequest.downloadHandler = new DownloadHandlerBuffer();
            SetCommonHeaders(webRequest);

            yield return webRequest.SendWebRequest();

            if (webRequest.result != UnityWebRequest.Result.Success)
            {
                Debug.LogError($"[{GetType().Name}] Error creating thread: {webRequest.error} (Code: {webRequest.responseCode}) - Response: {webRequest.downloadHandler?.text}", this);
                assistantThreadId = null;
            }
            else
            {
                try
                {
                    JObject responseJson = JObject.Parse(webRequest.downloadHandler.text);
                    assistantThreadId = responseJson["id"]?.ToString();

                    if (!string.IsNullOrEmpty(assistantThreadId))
                    {
                        Debug.Log($"[{GetType().Name}] Thread created successfully. ID: {assistantThreadId}");
                        // Optionally, trigger a run immediately to get a response to the initial message
                        // isProcessing = true; yield return StartCoroutine(CreateAssistantRun());
                    }
                    else
                    {
                        Debug.LogError($"[{GetType().Name}] Failed to parse 'id' from thread creation response: {webRequest.downloadHandler.text}", this);
                        assistantThreadId = null;
                    }
                }
                catch (Exception e)
                {
                    Debug.LogError($"[{GetType().Name}] Error parsing thread creation JSON response: {e.Message} - Response: {webRequest.downloadHandler.text}", this);
                    assistantThreadId = null;
                }
            }
        }
    }

    private IEnumerator CancelCurrentRun()
    {
        if (string.IsNullOrEmpty(currentRunId) || string.IsNullOrEmpty(assistantThreadId))
        {
            // No active run to cancel or thread doesn't exist
            yield break;
        }

        string cancelUrl = $"{apiUrl}/threads/{assistantThreadId}/runs/{currentRunId}/cancel";
        Debug.Log($"[{GetType().Name}] Attempting to cancel run: {currentRunId}");
        string runIdToCancel = currentRunId; // Store it in case currentRunId gets reset elsewhere
        currentRunId = null; // Assume cancellation will succeed or run becomes irrelevant

        using (UnityWebRequest webRequest = new UnityWebRequest(cancelUrl, "POST"))
        {
            // Some APIs require Content-Length: 0 even for empty POST bodies
            webRequest.uploadHandler = new UploadHandlerRaw(new byte[0]);
            webRequest.downloadHandler = new DownloadHandlerBuffer();
            SetCommonHeaders(webRequest);

            yield return webRequest.SendWebRequest();

            // Success (200 OK) means cancellation was accepted.
            // Other codes (e.g., 404, 400) might mean it was already done/invalid.
            if (webRequest.result == UnityWebRequest.Result.Success)
            {
                Debug.Log($"[{GetType().Name}] Request to cancel run {runIdToCancel} sent successfully.");
            }
            else
            {
                Debug.LogWarning($"[{GetType().Name}] Run cancellation request for {runIdToCancel} failed or run was likely already completed/invalid: {webRequest.error} (Code: {webRequest.responseCode})", this);
            }
        }
    }

    //--------------------------------------------------------------------------
    // HELPER METHODS
    //--------------------------------------------------------------------------

    private void SetCommonHeaders(UnityWebRequest request)
    {
        request.SetRequestHeader("Content-Type", "application/json");
        request.SetRequestHeader("Authorization", $"Bearer {apiKey}");
        request.SetRequestHeader("OpenAI-Beta", "assistants=v2");
    }
}
