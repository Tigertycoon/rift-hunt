using System.Runtime.InteropServices;
using UnityEngine;

namespace StoryMint.RiftHunt
{
    /// <summary>
    /// Lightweight wrapper around browser localStorage for saving/restoring
    /// the current trail step so the game survives page reloads, phone locks,
    /// and browser tab suspensions.
    ///
    /// Stored keys:
    ///   sm_trail_{trailId}_step   – the step ID the player is currently on
    ///   sm_trail_{trailId}_ts     – unix-ms timestamp of last save (for staleness)
    ///
    /// In the Editor the values are stored in PlayerPrefs for testing.
    /// </summary>
    public static class StoryMintSaveState
    {
#if UNITY_WEBGL && !UNITY_EDITOR
        [DllImport("__Internal")] private static extern void   StoryMintSave_Set(string key, string value);
        [DllImport("__Internal")] private static extern string StoryMintSave_Get(string key);
        [DllImport("__Internal")] private static extern void   StoryMintSave_Remove(string key);
        [DllImport("__Internal")] private static extern int    StoryMintSave_Has(string key);
#else
        private static void   StoryMintSave_Set(string key, string value) => PlayerPrefs.SetString(key, value);
        private static string StoryMintSave_Get(string key) => PlayerPrefs.GetString(key, "");
        private static void   StoryMintSave_Remove(string key) => PlayerPrefs.DeleteKey(key);
        private static int    StoryMintSave_Has(string key) => PlayerPrefs.HasKey(key) ? 1 : 0;
#endif

        private const string Prefix = "sm_trail_";

        // -------------------------------------------------------------------
        //  Public API
        // -------------------------------------------------------------------

        /// <summary>
        /// Save the player's current step so it survives page reloads.
        /// Call this every time the step changes.
        /// </summary>
        public static void SaveStep(string trailId, string stepId)
        {
            if (string.IsNullOrEmpty(trailId) || string.IsNullOrEmpty(stepId))
                return;

            string key = StepKey(trailId);
            StoryMintSave_Set(key, stepId);
            StoryMintSave_Set(TimestampKey(trailId), CurrentTimestamp());
            Debug.Log("[SMSave] Saved step '" + stepId + "' for trail '" + trailId + "'");
        }

        /// <summary>
        /// Try to load a previously saved step ID. Returns null/empty if none.
        /// </summary>
        public static string LoadStep(string trailId)
        {
            if (string.IsNullOrEmpty(trailId))
                return null;

            string key = StepKey(trailId);
            if (StoryMintSave_Has(key) == 0)
                return null;

            string stepId = StoryMintSave_Get(key);
            if (string.IsNullOrEmpty(stepId))
                return null;

            Debug.Log("[SMSave] Loaded saved step '" + stepId + "' for trail '" + trailId + "'");
            return stepId;
        }

        /// <summary>
        /// Clear saved progress for a trail (call when the game is completed / finale).
        /// </summary>
        public static void ClearSave(string trailId)
        {
            if (string.IsNullOrEmpty(trailId))
                return;

            StoryMintSave_Remove(StepKey(trailId));
            StoryMintSave_Remove(TimestampKey(trailId));
            Debug.Log("[SMSave] Cleared save for trail '" + trailId + "'");
        }

        /// <summary>
        /// Check whether a saved step exists for this trail.
        /// </summary>
        public static bool HasSave(string trailId)
        {
            if (string.IsNullOrEmpty(trailId))
                return false;
            return StoryMintSave_Has(StepKey(trailId)) != 0;
        }

        /// <summary>
        /// Mark an external minigame as completed. External pages can write either
        /// the trail-scoped key or the global game key; RiftHunt checks both.
        /// </summary>
        public static void SaveGameCompleted(string trailId, string gameId)
        {
            if (string.IsNullOrEmpty(gameId))
                return;

            StoryMintSave_Set(GlobalGameCompleteKey(gameId), "1");

            if (!string.IsNullOrEmpty(trailId))
            {
                StoryMintSave_Set(TrailGameCompleteKey(trailId, gameId), "1");
                StoryMintSave_Set(TrailGameTimestampKey(trailId, gameId), CurrentTimestamp());
            }

            Debug.Log("[SMSave] Saved completed game '" + gameId + "' for trail '" + trailId + "'");
        }

        /// <summary>
        /// Returns true if an external minigame has been completed in localStorage.
        /// </summary>
        public static bool IsGameCompleted(string trailId, string gameId)
        {
            if (string.IsNullOrEmpty(gameId))
                return false;

            if (!string.IsNullOrEmpty(trailId))
                return IsTruthyKey(TrailGameCompleteKey(trailId, gameId));

            return IsTruthyKey(GlobalGameCompleteKey(gameId));
        }

        /// <summary>
        /// Clear an external minigame completion flag. Used when replaying a trail.
        /// </summary>
        public static void ClearGameCompleted(string trailId, string gameId)
        {
            if (string.IsNullOrEmpty(gameId))
                return;

            StoryMintSave_Remove(GlobalGameCompleteKey(gameId));

            if (!string.IsNullOrEmpty(trailId))
            {
                StoryMintSave_Remove(TrailGameCompleteKey(trailId, gameId));
                StoryMintSave_Remove(TrailGameTimestampKey(trailId, gameId));
            }
        }

        // -------------------------------------------------------------------
        //  Internals
        // -------------------------------------------------------------------

        private static string StepKey(string trailId) => Prefix + trailId + "_step";
        private static string TimestampKey(string trailId) => Prefix + trailId + "_ts";
        private static string GlobalGameCompleteKey(string gameId) => "sm_game_" + gameId + "_complete";
        private static string TrailGameCompleteKey(string trailId, string gameId) => Prefix + trailId + "_game_" + gameId + "_complete";
        private static string TrailGameTimestampKey(string trailId, string gameId) => Prefix + trailId + "_game_" + gameId + "_ts";

        private static bool IsTruthyKey(string key)
        {
            if (StoryMintSave_Has(key) == 0)
                return false;

            string value = StoryMintSave_Get(key);
            if (string.IsNullOrEmpty(value))
                return true;

            value = value.Trim().ToLowerInvariant();
            return value != "0" && value != "false" && value != "no";
        }

        private static string CurrentTimestamp()
        {
            return System.DateTimeOffset.UtcNow.ToUnixTimeMilliseconds().ToString();
        }
    }
}
