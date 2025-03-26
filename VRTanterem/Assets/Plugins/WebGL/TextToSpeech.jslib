mergeInto(LibraryManager.library, {
  InitializeSpeechSynthesis: function() {
    // Ellenőrizzük, hogy a beszédszintézis támogatott-e
    if ('speechSynthesis' in window) {
      console.log("A beszédszintézis támogatott ebben a böngészőben");
      return true;
    } else {
      console.log("A beszédszintézis nem támogatott ebben a böngészőben");
      return false;
    }
  },
  
  SpeakText: function(text, rate, pitch, volume, language) {
    var textToSpeak = UTF8ToString(text);
    var languageCode = UTF8ToString(language);
    
    // Leállítjuk az esetleges folyamatban lévő beszédet
    window.speechSynthesis.cancel();
    
    var utterance = new SpeechSynthesisUtterance(textToSpeak);
    
    // Beszéd paramétereinek beállítása
    utterance.rate = rate;
    utterance.pitch = pitch;
    utterance.volume = volume;
    utterance.lang = languageCode;
    
    // Próbáljunk meg magyar hangot találni, ha van
    var voices = window.speechSynthesis.getVoices();
    for (var i = 0; i < voices.length; i++) {
      if (voices[i].lang.includes(languageCode.split('-')[0])) {
        utterance.voice = voices[i];
        console.log("Magyar hang használata: " + voices[i].name);
        break;
      }
    }
    
    // Beszéd indítása
    window.speechSynthesis.speak(utterance);
    
    console.log("Beszélünk: " + textToSpeak);
  },
  
  CancelSpeech: function() {
    if ('speechSynthesis' in window) {
      window.speechSynthesis.cancel();
      console.log("Beszéd leállítva");
    }
  },
  
  PauseSpeech: function() {
    if ('speechSynthesis' in window) {
      window.speechSynthesis.pause();
      console.log("Beszéd szüneteltetve");
    }
  },
  
  ResumeSpeech: function() {
    if ('speechSynthesis' in window) {
      window.speechSynthesis.resume();
      console.log("Beszéd folytatva");
    }
  }
});