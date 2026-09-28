var StoryMintNavigationPlugin = {
    StoryMintNavigate_To: function(urlPtr) {
        var url = UTF8ToString(urlPtr);
        try {
            window.location.assign(url);
        } catch (e) {
            console.warn('[SMNav] same-tab navigation failed:', e);
            try { window.location.replace(url); } catch (fallbackError) {}
        }
    },

    StoryMintNavigate_ReplaceAddress: function(urlPtr) {
        var url = UTF8ToString(urlPtr);
        try {
            if (window.history && window.history.replaceState) {
                window.history.replaceState(null, document.title, url);
            }
        } catch (e) {
            console.warn('[SMNav] address cleanup failed:', e);
        }
    }
};

mergeInto(LibraryManager.library, StoryMintNavigationPlugin);
