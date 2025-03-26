mergeInto(LibraryManager.library, {
  InitializeSpeechSynthesis: function() {
    try {
      // Beszédsor létrehozása a jobb kezelhetőségért
      window.speechQueue = [];
      window.isSpeaking = false;
      window.currentUtterance = null;
      
      // Beszéd eseménykezelők
      window.setupUtterance = function(utterance) {
        utterance.onstart = function() {
          window.isSpeaking = true;
          console.log("Speech started");
        };
        
        utterance.onend = function() {
          setTimeout(function() {
            window.isSpeaking = false;
            window.currentUtterance = null;
            console.log("Speech fully ended");
            
            if (window.speechQueue.length > 0) {
              setTimeout(function() {
                var nextSpeech = window.speechQueue.shift();
                window.speakInternal(nextSpeech);
              }, 300);
            }
          }, 300);
        };
        
        utterance.onerror = function(event) {
          console.error("Speech synthesis error:", event);
          window.isSpeaking = false;
          window.currentUtterance = null;
        };
        
        return utterance;
      };
      
      // Belső beszédfunkció
      window.speakInternal = function(speech) {
        try {
          // Egy kis késleltetés a beszéd indítása előtt a simább kezdésért
          setTimeout(function() {
            window.currentUtterance = speech;
            speechSynthesis.speak(speech);
          }, 80);  // 80ms késleltetés a beszéd kezdete előtt
        } catch(e) {
          console.error("Speech error:", e);
          window.isSpeaking = false;
        }
      };
      
      return (typeof speechSynthesis !== 'undefined' && typeof SpeechSynthesisUtterance !== 'undefined');
    } catch (e) {
      console.error("Speech synthesis initialization error:", e);
      return false;
    }
  },
  
  SpeakText: function(text, rate, pitch, volume, language) {
    var textString = UTF8ToString(text);
    var langString = UTF8ToString(language);
    
    try {
      if (window.isSpeaking && window.currentUtterance) {
        // Ha már beszélünk, töröljük a korábbi sorban lévő elemeket
        window.speechQueue = [];
      } else {
        // Biztonság kedvéért töröljük az esetleges korábbi beszédet
        speechSynthesis.cancel();
      }
      
      var utterance = new SpeechSynthesisUtterance(textString);
      utterance.rate = rate;
      utterance.pitch = pitch;
      utterance.volume = volume;
      utterance.lang = langString;
      
      // Eseménykezelők hozzáadása
      utterance = window.setupUtterance(utterance);
      
      // Ha már beszélünk, sorba tesszük
      if (window.isSpeaking) {
        window.speechQueue.push(utterance);
        console.log("Speech queued, length:", window.speechQueue.length);
      } else {
        window.speakInternal(utterance);
      }
    } catch (e) {
      console.error("SpeakText error:", e);
    }
  },
  
  CancelSpeech: function() {
    try {
      window.speechQueue = [];
      speechSynthesis.cancel();
      window.isSpeaking = false;
      window.currentUtterance = null;
    } catch (e) {
      console.error("CancelSpeech error:", e);
    }
  },
  
  PauseSpeech: function() {
    try {
      speechSynthesis.pause();
    } catch (e) {
      console.error("PauseSpeech error:", e);
    }
  },
  
  ResumeSpeech: function() {
    try {
      speechSynthesis.resume();
    } catch (e) {
      console.error("ResumeSpeech error:", e);
    }
  },
  
  IsSpeaking: function() {
    try {
      return window.isSpeaking ? 1 : 0;
    } catch (e) {
      console.error("IsSpeaking error:", e);
      return 0;
    }
  }
});