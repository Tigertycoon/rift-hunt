var StoryMintPersistencePlugin = {

    StoryMintSave_Set: function(keyPtr, valuePtr) {
        var key = UTF8ToString(keyPtr);
        var val = UTF8ToString(valuePtr);
        try { localStorage.setItem(key, val); } catch(e) { console.warn('[SMSave] set failed:', e); }
    },

    StoryMintSave_Get: function(keyPtr) {
        var key = UTF8ToString(keyPtr);
        var val = null;
        try { val = localStorage.getItem(key); } catch(e) { console.warn('[SMSave] get failed:', e); }
        if (!val) val = "";
        var bufSize = lengthBytesUTF8(val) + 1;
        var buf = _malloc(bufSize);
        stringToUTF8(val, buf, bufSize);
        return buf;
    },

    StoryMintSave_Remove: function(keyPtr) {
        var key = UTF8ToString(keyPtr);
        try { localStorage.removeItem(key); } catch(e) { console.warn('[SMSave] remove failed:', e); }
    },

    StoryMintSave_Has: function(keyPtr) {
        var key = UTF8ToString(keyPtr);
        try { return localStorage.getItem(key) !== null ? 1 : 0; } catch(e) { return 0; }
    }
};

mergeInto(LibraryManager.library, StoryMintPersistencePlugin);
