public class UnityPlayerActivity extends Activity {
    public void onActivityResult(int requestCode, int resultCode, Intent data) {
        if (requestCode == 1001 && resultCode == RESULT_OK) {
            ArrayList<String> matches = data.getStringArrayListExtra(RecognizerIntent.EXTRA_RESULTS);
            if (matches != null && !matches.isEmpty()) {
                // Az első találat átadása Unity-nak
                UnityPlayer.UnitySendMessage("SpeechToTextManager", "OnActivityResult", matches.get(0));
            }
        }
    }
}