package com.ArWorks.VRTanterem;

import android.os.Bundle;
import android.speech.RecognitionListener;
import android.speech.SpeechRecognizer;
import android.util.Log;
import com.unity3d.player.UnityPlayer;
import java.util.ArrayList;

public class UnityRecognitionListener implements RecognitionListener {
    private String gameObjectName;
    private static final String TAG = "UnityRecognitionListener";

    public UnityRecognitionListener(String gameObjectName) {
        this.gameObjectName = gameObjectName;
        Log.d(TAG, "Listener initialized for GameObject: " + gameObjectName);
    }

    @Override
    public void onReadyForSpeech(Bundle params) {
        Log.d(TAG, "Ready for speech");
        UnityPlayer.UnitySendMessage(gameObjectName, "OnSpeechReadyForSpeech", "");
    }

    @Override
    public void onBeginningOfSpeech() {
        Log.d(TAG, "Beginning of speech");
        UnityPlayer.UnitySendMessage(gameObjectName, "OnSpeechBegin", "");
    }

    @Override
    public void onRmsChanged(float rmsdB) {
        // Túl sűrű logolást okozna, ezért ezt nem küldjük
    }

    @Override
    public void onBufferReceived(byte[] buffer) {
        Log.d(TAG, "Buffer received");
    }

    @Override
    public void onEndOfSpeech() {
        Log.d(TAG, "End of speech");
        UnityPlayer.UnitySendMessage(gameObjectName, "OnSpeechEnd", "");
    }

    @Override
    public void onError(int error) {
        String errorMessage;
        switch (error) {
            case SpeechRecognizer.ERROR_AUDIO:
                errorMessage = "Audio error";
                break;
            case SpeechRecognizer.ERROR_CLIENT:
                errorMessage = "Client error";
                break;
            case SpeechRecognizer.ERROR_INSUFFICIENT_PERMISSIONS:
                errorMessage = "Insufficient permissions";
                break;
            case SpeechRecognizer.ERROR_NETWORK:
                errorMessage = "Network error";
                break;
            case SpeechRecognizer.ERROR_NETWORK_TIMEOUT:
                errorMessage = "Network timeout";
                break;
            case SpeechRecognizer.ERROR_NO_MATCH:
                errorMessage = "No match found";
                break;
            case SpeechRecognizer.ERROR_RECOGNIZER_BUSY:
                errorMessage = "Recognizer busy";
                break;
            case SpeechRecognizer.ERROR_SERVER:
                errorMessage = "Server error";
                break;
            case SpeechRecognizer.ERROR_SPEECH_TIMEOUT:
                errorMessage = "Speech timeout";
                break;
            default:
                errorMessage = "Unknown error (" + error + ")";
                break;
        }
        Log.e(TAG, "Speech error: " + errorMessage);
        UnityPlayer.UnitySendMessage(gameObjectName, "OnSpeechError", errorMessage);
    }

    @Override
    public void onResults(Bundle results) {
        ArrayList<String> matches = results.getStringArrayList(SpeechRecognizer.RESULTS_RECOGNITION);
        if (matches != null && !matches.isEmpty()) {
            String text = matches.get(0);
            Log.d(TAG, "Speech results: " + text);
            UnityPlayer.UnitySendMessage(gameObjectName, "OnSpeechResults", text);
        } else {
            Log.w(TAG, "No speech results found");
            UnityPlayer.UnitySendMessage(gameObjectName, "OnSpeechResults", "");
        }
    }

    @Override
    public void onPartialResults(Bundle partialResults) {
        ArrayList<String> matches = partialResults.getStringArrayList(SpeechRecognizer.RESULTS_RECOGNITION);
        if (matches != null && !matches.isEmpty()) {
            String text = matches.get(0);
            Log.d(TAG, "Partial results: " + text);
            UnityPlayer.UnitySendMessage(gameObjectName, "OnSpeechPartialResults", text);
        }
    }

    @Override
    public void onEvent(int eventType, Bundle params) {
        Log.d(TAG, "Speech event: " + eventType);
    }
}
