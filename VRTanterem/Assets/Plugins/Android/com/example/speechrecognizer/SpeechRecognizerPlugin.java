package com.example.speechrecognizer;

import android.app.Activity;
import android.content.Intent;
import android.speech.RecognizerIntent;
import android.speech.SpeechRecognizer;
import android.os.Bundle;
import android.util.Log;
import com.unity3d.player.UnityPlayer;

public class SpeechRecognizerPlugin {
    private static final int SPEECH_REQUEST_CODE = 10;

    public static void startSpeechRecognition() {
        Activity activity = UnityPlayer.currentActivity;
        Intent intent = new Intent(RecognizerIntent.ACTION_RECOGNIZE_SPEECH);
        intent.putExtra(RecognizerIntent.EXTRA_LANGUAGE_MODEL, RecognizerIntent.LANGUAGE_MODEL_FREE_FORM);
        intent.putExtra(RecognizerIntent.EXTRA_LANGUAGE, "hu-HU"); // Magyar nyelv
        activity.startActivityForResult(intent, SPEECH_REQUEST_CODE);
    }
}
