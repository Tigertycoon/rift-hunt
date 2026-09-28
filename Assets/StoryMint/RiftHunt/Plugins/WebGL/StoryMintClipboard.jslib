mergeInto(LibraryManager.library, {
    StoryMintClipboardCopy: function(textPtr) {
        var text = UTF8ToString(textPtr || 0);

        function fallbackCopy(value) {
            if (typeof document === "undefined") {
                return false;
            }

            var textarea = document.createElement("textarea");
            textarea.value = value;
            textarea.setAttribute("readonly", "");
            textarea.style.position = "fixed";
            textarea.style.left = "-9999px";
            textarea.style.top = "0";
            document.body.appendChild(textarea);
            textarea.focus();
            textarea.select();

            var copied = false;
            try {
                copied = document.execCommand("copy");
            } catch (error) {
                copied = false;
            }

            document.body.removeChild(textarea);
            return copied;
        }

        if (typeof navigator !== "undefined" && navigator.clipboard && navigator.clipboard.writeText) {
            navigator.clipboard.writeText(text).catch(function() {
                fallbackCopy(text);
            });
            return 1;
        }

        return fallbackCopy(text) ? 1 : 0;
    }
});
