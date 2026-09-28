var StoryMintDeviceMotionLib = {

    $SMDeviceMotion: {
        ax: 0, ay: 0, az: 0,
        listening: false,
        permitted: false,
        requestPending: false
    },

    StoryMintDM_RequestPermission: function () {
        if (SMDeviceMotion.listening) return;

        function startListening() {
            window.addEventListener('devicemotion', function (e) {
                var a = e.accelerationIncludingGravity;
                if (a) {
                    // Normalise to g (÷ 9.81) so values match Unity Input.acceleration range [-1,1]
                    SMDeviceMotion.ax = (a.x || 0) / 9.81;
                    SMDeviceMotion.ay = (a.y || 0) / 9.81;
                    SMDeviceMotion.az = (a.z || 0) / 9.81;
                }
                SMDeviceMotion.permitted = true;
            }, true);
            SMDeviceMotion.listening = true;
            console.log('[StoryMint] DeviceMotion listener active.');
        }

        if (SMDeviceMotion.requestPending) return;
        SMDeviceMotion.requestPending = true;

        // iOS 13+ requires explicit permission request triggered by user gesture
        if (typeof DeviceMotionEvent !== 'undefined' &&
            typeof DeviceMotionEvent.requestPermission === 'function') {
            DeviceMotionEvent.requestPermission()
                .then(function (state) {
                    if (state === 'granted') {
                        startListening();
                    } else {
                        console.warn('[StoryMint] DeviceMotion permission denied: ' + state);
                    }
                })
                .catch(function (err) {
                    console.warn('[StoryMint] DeviceMotion permission error', err);
                });
        } else {
            // Android Chrome, Firefox — no permission needed
            startListening();
        }
    },

    StoryMintDM_GetX: function () { return SMDeviceMotion.ax; },
    StoryMintDM_GetY: function () { return SMDeviceMotion.ay; },
    StoryMintDM_GetZ: function () { return SMDeviceMotion.az; },
    StoryMintDM_IsListening: function () { return SMDeviceMotion.listening ? 1 : 0; },
    StoryMintDM_HasData: function () { return SMDeviceMotion.permitted ? 1 : 0; }
};

autoAddDeps(StoryMintDeviceMotionLib, '$SMDeviceMotion');
mergeInto(LibraryManager.library, StoryMintDeviceMotionLib);
