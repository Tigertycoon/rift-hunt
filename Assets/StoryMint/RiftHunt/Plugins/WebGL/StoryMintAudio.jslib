var StoryMintAudioPlugin = {

    $SMAudio: {
        ctx: null,
        masterGain: null,
        _ensureCtx: function() {
            if (SMAudio.ctx) return;
            try {
                SMAudio.ctx = new (window.AudioContext || window.webkitAudioContext)();
                SMAudio.masterGain = SMAudio.ctx.createGain();
                SMAudio.masterGain.gain.value = 0.35;
                SMAudio.masterGain.connect(SMAudio.ctx.destination);
            } catch(e) { console.warn('[SMAudio] No AudioContext:', e); }
        },
        _play: function(freq, dur, type, vol, attack, decay) {
            SMAudio._ensureCtx();
            if (!SMAudio.ctx) return;
            var now = SMAudio.ctx.currentTime;
            var osc = SMAudio.ctx.createOscillator();
            var g = SMAudio.ctx.createGain();
            osc.type = type || 'sine';
            osc.frequency.setValueAtTime(freq, now);
            g.gain.setValueAtTime(0, now);
            g.gain.linearRampToValueAtTime(vol || 0.5, now + (attack || 0.01));
            g.gain.linearRampToValueAtTime(0, now + dur);
            osc.connect(g);
            g.connect(SMAudio.masterGain);
            osc.start(now);
            osc.stop(now + dur + 0.05);
        },
        _playChord: function(freqs, dur, type, vol) {
            for (var i = 0; i < freqs.length; i++) {
                SMAudio._play(freqs[i], dur, type, (vol || 0.3) / freqs.length, 0.01, dur);
            }
        }
    },

    StoryMintAudio_ResumeContext: function() {
        SMAudio._ensureCtx();
        if (SMAudio.ctx && SMAudio.ctx.state === 'suspended') {
            SMAudio.ctx.resume();
        }
    },

    // ---- Individual sound effects ----

    // Generic UI tap — short click
    StoryMintAudio_Tap: function() {
        SMAudio._play(800, 0.08, 'sine', 0.3, 0.005, 0.08);
    },

    // Correct / success ping — rising two-tone
    StoryMintAudio_Success: function() {
        SMAudio._play(523, 0.12, 'sine', 0.35, 0.01, 0.12);
        setTimeout(function() { SMAudio._play(784, 0.18, 'sine', 0.35, 0.01, 0.18); }, 100);
    },

    // Error / wrong — low buzz
    StoryMintAudio_Error: function() {
        SMAudio._play(180, 0.25, 'sawtooth', 0.2, 0.01, 0.25);
    },

    // Victory fanfare — ascending chord
    StoryMintAudio_Victory: function() {
        var base = 523;
        var delays = [0, 120, 240, 360];
        var freqs = [base, base * 1.25, base * 1.5, base * 2];
        for (var i = 0; i < freqs.length; i++) {
            (function(f, d) {
                setTimeout(function() { SMAudio._play(f, 0.35, 'sine', 0.3, 0.02, 0.35); }, d);
            })(freqs[i], delays[i]);
        }
        // Final chord
        setTimeout(function() { SMAudio._playChord([523, 659, 784, 1047], 0.6, 'sine', 0.4); }, 500);
    },

    // Countdown / timer tick
    StoryMintAudio_Tick: function() {
        SMAudio._play(1000, 0.04, 'square', 0.15, 0.003, 0.04);
    },

    // Sequence blink (Simon Says) — pitch-mapped
    // padIndex: 0-3 mapped to different pitches
    StoryMintAudio_SimonBlink: function(padIndex) {
        var pitches = [261, 329, 392, 523]; // C4 E4 G4 C5
        var idx = padIndex % pitches.length;
        SMAudio._play(pitches[idx], 0.3, 'sine', 0.4, 0.01, 0.3);
    },

    // Rune catch
    StoryMintAudio_Catch: function() {
        SMAudio._play(660, 0.1, 'sine', 0.3, 0.005, 0.1);
        setTimeout(function() { SMAudio._play(880, 0.15, 'sine', 0.25, 0.01, 0.15); }, 60);
    },

    // Ball hole fall (GyroMaze respawn)
    StoryMintAudio_Fall: function() {
        SMAudio._play(400, 0.08, 'sine', 0.3, 0.005, 0.08);
        setTimeout(function() { SMAudio._play(200, 0.3, 'sine', 0.25, 0.01, 0.3); }, 70);
    },

    // Seal trace — drawing sound (short chirp)
    StoryMintAudio_TraceDot: function() {
        SMAudio._play(700, 0.06, 'triangle', 0.2, 0.005, 0.06);
    },

    // Penalty buzz (WhackARune wrong tap)
    StoryMintAudio_Penalty: function() {
        SMAudio._play(150, 0.15, 'sawtooth', 0.18, 0.01, 0.15);
        setTimeout(function() { SMAudio._play(120, 0.2, 'sawtooth', 0.15, 0.01, 0.2); }, 100);
    },

    // Whack pop-up appear
    StoryMintAudio_PopUp: function() {
        SMAudio._play(500, 0.06, 'sine', 0.15, 0.005, 0.06);
    },

    // Reset / sequence broken
    StoryMintAudio_Reset: function() {
        SMAudio._play(300, 0.1, 'sawtooth', 0.2, 0.01, 0.1);
        setTimeout(function() { SMAudio._play(200, 0.15, 'sawtooth', 0.18, 0.01, 0.15); }, 80);
        setTimeout(function() { SMAudio._play(130, 0.25, 'sawtooth', 0.15, 0.01, 0.25); }, 180);
    }
};

autoAddDeps(StoryMintAudioPlugin, '$SMAudio');
mergeInto(LibraryManager.library, StoryMintAudioPlugin);
