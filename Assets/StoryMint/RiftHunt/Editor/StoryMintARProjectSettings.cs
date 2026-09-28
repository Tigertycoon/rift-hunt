#if STORYMINT_IMAGINE
using System;
using System.Linq;
using UnityEditor;
using UnityEditor.Build;

namespace StoryMint.RiftHunt.Editor
{
    [InitializeOnLoad]
    internal static class StoryMintARProjectSettings
    {
        private const string ImagineTemplate = "PROJECT:iTracker";
        private const string LegacyStoryMintTemplate = "PROJECT:StoryMintAR";
        private const string ImagineUrpDefine = "IMAGINE_URP";

        static StoryMintARProjectSettings()
        {
            ApplyStoryMintTemplateIfNeeded();
        }

        [MenuItem("Tools/StoryMint/Rift Hunt/Apply Imagine WebGL Settings")]
        private static void ApplyStoryMintTemplateMenu()
        {
            ApplyStoryMintTemplate(true);
        }

        private static void ApplyStoryMintTemplateIfNeeded()
        {
            string currentTemplate = PlayerSettings.WebGL.template;
            if (currentTemplate == LegacyStoryMintTemplate ||
                string.IsNullOrEmpty(currentTemplate) ||
                HasDefine(NamedBuildTarget.WebGL, ImagineUrpDefine) ||
                HasDefine(NamedBuildTarget.Standalone, ImagineUrpDefine))
            {
                ApplyStoryMintTemplate(false);
            }
        }

        private static void ApplyStoryMintTemplate(bool verbose)
        {
            if (PlayerSettings.WebGL.template == ImagineTemplate)
            {
                if (verbose)
                    UnityEngine.Debug.Log("[StoryMint] WebGL template is already Imagine iTracker.");
            }
            else
            {
                PlayerSettings.WebGL.template = ImagineTemplate;
                if (verbose)
                    UnityEngine.Debug.Log("[StoryMint] WebGL template set to Imagine iTracker.");
            }

            bool definesChanged = RemoveDefine(NamedBuildTarget.WebGL, ImagineUrpDefine);
            definesChanged |= RemoveDefine(NamedBuildTarget.Standalone, ImagineUrpDefine);
            if (definesChanged && verbose)
                UnityEngine.Debug.Log("[StoryMint] Removed IMAGINE_URP from WebGL/Standalone scripting defines.");

            AssetDatabase.SaveAssets();
        }

        private static bool RemoveDefine(NamedBuildTarget target, string define)
        {
            string currentDefines = PlayerSettings.GetScriptingDefineSymbols(target);
            string[] filteredDefines = currentDefines
                .Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(symbol => symbol.Trim())
                .Where(symbol => !string.Equals(symbol, define, StringComparison.Ordinal))
                .ToArray();

            string nextDefines = string.Join(";", filteredDefines);
            if (string.Equals(currentDefines, nextDefines, StringComparison.Ordinal))
                return false;

            PlayerSettings.SetScriptingDefineSymbols(target, nextDefines);
            return true;
        }

        private static bool HasDefine(NamedBuildTarget target, string define)
        {
            string currentDefines = PlayerSettings.GetScriptingDefineSymbols(target);
            return currentDefines
                .Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(symbol => symbol.Trim())
                .Any(symbol => string.Equals(symbol, define, StringComparison.Ordinal));
        }
    }
}

#endif
