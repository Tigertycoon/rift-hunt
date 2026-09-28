mergeInto(LibraryManager.library, {
    $StoryMintGeoInternal: {
        ensure: function() {
            if (!window.storyMintGeo) {
                window.storyMintGeo = { permissionState: "unknown", watchId: null, permissionHooked: false };
            }
        },

        sendMessage: function(callbackObjectName, methodName, payload) {
            if (!callbackObjectName || !window.unityInstance || typeof window.unityInstance.SendMessage !== "function") {
                return;
            }

            window.unityInstance.SendMessage(callbackObjectName, methodName, payload || "");
        },

        updatePermission: function(callbackObjectName) {
            StoryMintGeoInternal.ensure();

            if (!navigator.permissions || typeof navigator.permissions.query !== "function") {
                return;
            }

            navigator.permissions.query({ name: "geolocation" }).then(function(result) {
                window.storyMintGeo.permissionState = result.state || "unknown";
                StoryMintGeoInternal.sendMessage(callbackObjectName, "OnWebGlPermissionState", window.storyMintGeo.permissionState);

                if (!window.storyMintGeo.permissionHooked) {
                    window.storyMintGeo.permissionHooked = true;
                    result.onchange = function() {
                        window.storyMintGeo.permissionState = result.state || "unknown";
                        StoryMintGeoInternal.sendMessage(callbackObjectName, "OnWebGlPermissionState", window.storyMintGeo.permissionState);
                    };
                }
            }).catch(function() {
                window.storyMintGeo.permissionState = "unknown";
            });
        },

        sendFix: function(callbackObjectName, position) {
            var coords = position.coords || {};
            var payload = {
                latitude: Number(coords.latitude) || 0,
                longitude: Number(coords.longitude) || 0,
                accuracy: Number(coords.accuracy) || 0,
                hasAltitude: coords.altitude !== null && coords.altitude !== undefined && isFinite(coords.altitude),
                altitude: coords.altitude !== null && coords.altitude !== undefined && isFinite(coords.altitude) ? Number(coords.altitude) : 0,
                hasHeading: coords.heading !== null && coords.heading !== undefined && isFinite(coords.heading),
                heading: coords.heading !== null && coords.heading !== undefined && isFinite(coords.heading) ? Number(coords.heading) : 0,
                hasSpeed: coords.speed !== null && coords.speed !== undefined && isFinite(coords.speed),
                speed: coords.speed !== null && coords.speed !== undefined && isFinite(coords.speed) ? Number(coords.speed) : 0,
                timestamp: position.timestamp ? Number(position.timestamp) / 1000 : Date.now() / 1000
            };

            StoryMintGeoInternal.sendMessage(callbackObjectName, "OnWebGlLocationUpdate", JSON.stringify(payload));
        },

        sendError: function(callbackObjectName, code, message) {
            StoryMintGeoInternal.ensure();

            var payload = {
                code: code || 0,
                message: message || "GPS-Fehler.",
                permissionState: window.storyMintGeo.permissionState || "unknown"
            };

            StoryMintGeoInternal.sendMessage(callbackObjectName, "OnWebGlLocationError", JSON.stringify(payload));
        },

        requestInternal: function(callbackObjectName, highAccuracy, timeoutMs, maximumAgeMs, watch) {
            StoryMintGeoInternal.ensure();
            StoryMintGeoInternal.updatePermission(callbackObjectName);

            if (!navigator.geolocation) {
                window.storyMintGeo.permissionState = "unsupported";
                StoryMintGeoInternal.sendError(callbackObjectName, 2, "Browser-Geolocation ist nicht verfuegbar.");
                return -1;
            }

            if (window.isSecureContext === false && window.location.hostname !== "localhost" && window.location.hostname !== "127.0.0.1") {
                StoryMintGeoInternal.sendError(callbackObjectName, 2, "Geolocation erfordert HTTPS oder localhost.");
                return -1;
            }

            var options = {
                enableHighAccuracy: highAccuracy,
                timeout: Math.max(1000, timeoutMs || 12000),
                maximumAge: Math.max(0, maximumAgeMs || 5000)
            };

            var onSuccess = function(position) {
                window.storyMintGeo.permissionState = "granted";
                StoryMintGeoInternal.sendFix(callbackObjectName, position);
            };

            var onError = function(error) {
                StoryMintGeoInternal.updatePermission(callbackObjectName);
                StoryMintGeoInternal.sendError(callbackObjectName, error && error.code ? error.code : 0, error && error.message ? error.message : "GPS-Fehler.");
            };

            if (!watch) {
                navigator.geolocation.getCurrentPosition(onSuccess, onError, options);
                return 0;
            }

            if (window.storyMintGeo.watchId !== null && window.storyMintGeo.watchId !== undefined) {
                navigator.geolocation.clearWatch(window.storyMintGeo.watchId);
            }

            window.storyMintGeo.watchId = navigator.geolocation.watchPosition(onSuccess, onError, options);
            return window.storyMintGeo.watchId;
        }
    },

    StoryMintGeoIsSupported: function() {
        return (typeof navigator !== "undefined" && !!navigator.geolocation) ? 1 : 0;
    },

    StoryMintGeoGetCachedPermissionState__deps: ["$StoryMintGeoInternal"],
    StoryMintGeoGetCachedPermissionState: function() {
        StoryMintGeoInternal.ensure();

        var state = window.storyMintGeo.permissionState || "unknown";
        var bufferSize = lengthBytesUTF8(state) + 1;
        var mallocFn = (typeof _malloc !== "undefined")
            ? _malloc
            : (typeof Module !== "undefined" && Module._malloc ? Module._malloc : null);
        if (!mallocFn && typeof unityInstance !== "undefined" && unityInstance.Module && unityInstance.Module._malloc) {
            mallocFn = unityInstance.Module._malloc;
        }
        if (!mallocFn) {
            return 0;
        }

        var buffer = mallocFn(bufferSize);
        stringToUTF8(state, buffer, bufferSize);
        return buffer;
    },

    StoryMintGeoRequestPosition__deps: ["$StoryMintGeoInternal"],
    StoryMintGeoRequestPosition: function(callbackObjectName, highAccuracy, timeoutMs, maximumAgeMs) {
        StoryMintGeoInternal.requestInternal(UTF8ToString(callbackObjectName), !!highAccuracy, timeoutMs || 12000, maximumAgeMs || 5000, false);
    },

    StoryMintGeoStartWatch__deps: ["$StoryMintGeoInternal"],
    StoryMintGeoStartWatch: function(callbackObjectName, highAccuracy, timeoutMs, maximumAgeMs) {
        return StoryMintGeoInternal.requestInternal(UTF8ToString(callbackObjectName), !!highAccuracy, timeoutMs || 12000, maximumAgeMs || 5000, true);
    },

    StoryMintGeoStopWatch__deps: ["$StoryMintGeoInternal"],
    StoryMintGeoStopWatch: function() {
        StoryMintGeoInternal.ensure();

        if (window.storyMintGeo.watchId !== null && window.storyMintGeo.watchId !== undefined && navigator.geolocation) {
            navigator.geolocation.clearWatch(window.storyMintGeo.watchId);
        }

        window.storyMintGeo.watchId = null;
    }
});
