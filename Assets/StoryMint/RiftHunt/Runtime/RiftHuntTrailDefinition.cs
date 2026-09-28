using System;
using System.Collections.Generic;
using UnityEngine;

namespace StoryMint.RiftHunt
{
    public enum RiftHuntStepKind
    {
        Message,
        Geofence,
        Scan,
        ArInteraction,
        Riddle,
        PortalDeactivation,
        RuneSort,
        SimonSays,
        GyroMaze,
        RuneCatcher,
        SealTrace,
        WhackARune,
        RuneMemory,
        ExternalGame,
        Unknown
    }

    public static class StoryMintBuildVersion
    {
        // Increment this label after each Codex change batch so phone tests can confirm the active build.
        public const string Label = "v81";
    }

    [Serializable]
    public class RiftHuntTrailDefinition
    {
        public string trailId;
        public string title;
        public string sceneName;
        public string startStepId;
        public List<RiftHuntStepDefinition> steps = new List<RiftHuntStepDefinition>();

        public RiftHuntStepDefinition GetStartStep()
        {
            if (!string.IsNullOrWhiteSpace(startStepId))
            {
                var configuredStartStep = GetStepById(startStepId);
                if (configuredStartStep != null)
                    return configuredStartStep;
            }

            return steps != null && steps.Count > 0 ? steps[0] : null;
        }

        public RiftHuntStepDefinition GetStepById(string stepId)
        {
            if (steps == null || string.IsNullOrWhiteSpace(stepId))
                return null;

            for (int index = 0; index < steps.Count; index++)
            {
                if (steps[index] != null && steps[index].id == stepId)
                    return steps[index];
            }

            return null;
        }

        public int GetStepIndex(string stepId)
        {
            if (steps == null || string.IsNullOrWhiteSpace(stepId))
                return -1;

            for (int index = 0; index < steps.Count; index++)
            {
                if (steps[index] != null && steps[index].id == stepId)
                    return index;
            }

            return -1;
        }

        public bool HasGeofenceSteps()
        {
            if (steps == null)
                return false;

            for (int index = 0; index < steps.Count; index++)
            {
                if (steps[index] != null && steps[index].GetStepKind() == RiftHuntStepKind.Geofence)
                    return true;
            }

            return false;
        }
    }

    [Serializable]
    public class RiftHuntChoiceDefinition
    {
        public string id;
        public string label;
        public bool isCorrect;
        public string feedback;
    }

    [Serializable]
    public class RiftHuntStepDefinition
    {
        public string id;
        public string type;
        public string title;
        [TextArea] public string body;
        public string primaryActionLabel;
        public string actionUrl;
        public string targetId;
        public string locationName;
        public StoryMintGeoCoordinate location;
        public float geofenceRadiusMeters = 35f;
        public float requiredAccuracyMeters = 80f;
        public bool allowLocationFallback;
        public string qrFallbackCode;
        public string interactionType;
        public string nextStepId;
        public string successMessage;
        public float autoAdvanceDelay = 0.85f;
        public Vector3 interactionLocalPosition = new Vector3(0f, 0.18f, 0f);
        public float interactionScale = 0.08f;
        public string interactionPrefabResourcePath;
        public string interactionWaitingMessage;
        public string interactionVisibleMessage;
        public string interactionFallbackMessage;
        public string riddlePrompt;
        public List<RiftHuntChoiceDefinition> choices = new List<RiftHuntChoiceDefinition>();
        public string hintMessage;
        public List<string> runeOptions = new List<string>();
        public List<string> runeSequence = new List<string>();
        public string portalPrompt;
        public string portalProgressLabel;
        public string portalMistakeMessage;
        public string portalSolvedMessage;

        // ----- External game bridge fields -----
        public string gameId;
        public string gameUrl;
        public string gameWaitingMessage;
        public string gameCompletedMessage;

        // ----- Minigame fields (Spiel 1-3) -----

        /// <summary>Simon Says: how many rounds to win (default 3).</summary>
        public int simonRounds = 3;

        /// <summary>Simon Says: number of colors/pads (default 4).</summary>
        public int simonPadCount = 4;

        /// <summary>Gyro Maze: maze layout index (0 = default).</summary>
        public int mazeLayout;

        public RiftHuntStepKind GetStepKind()
        {
            switch ((type ?? string.Empty).Trim().ToLowerInvariant())
            {
                case "message":
                    return RiftHuntStepKind.Message;
                case "geofence":
                case "location":
                    return RiftHuntStepKind.Geofence;
                case "scan":
                    return RiftHuntStepKind.Scan;
                case "ar_interaction":
                    return RiftHuntStepKind.ArInteraction;
                case "riddle":
                case "raetsel":
                    return RiftHuntStepKind.Riddle;
                case "portal_deactivation":
                case "portal_deactivate":
                case "sequence":
                case "rune_sequence":
                    return RiftHuntStepKind.PortalDeactivation;
                case "rune_sort":
                case "runesort":
                    return RiftHuntStepKind.RuneSort;
                case "simon_says":
                case "simonsays":
                    return RiftHuntStepKind.SimonSays;
                case "gyro_maze":
                case "gyromaze":
                    return RiftHuntStepKind.GyroMaze;
                case "rune_catcher":
                case "runecatcher":
                    return RiftHuntStepKind.RuneCatcher;
                case "seal_trace":
                case "sealtrace":
                    return RiftHuntStepKind.SealTrace;
                case "whack_a_rune":
                case "whackarune":
                    return RiftHuntStepKind.WhackARune;
                case "rune_memory":
                case "runememory":
                case "memory":
                    return RiftHuntStepKind.RuneMemory;
                case "external_game":
                case "externalgame":
                case "game_link":
                case "gamelink":
                case "minigame_link":
                case "minigamelink":
                    return RiftHuntStepKind.ExternalGame;
                default:
                    return RiftHuntStepKind.Unknown;
            }
        }

        public string GetPrimaryActionLabel(string fallback)
        {
            return string.IsNullOrWhiteSpace(primaryActionLabel) ? fallback : primaryActionLabel;
        }

        public string GetInteractionWaitingMessage(string fallback)
        {
            return string.IsNullOrWhiteSpace(interactionWaitingMessage) ? fallback : interactionWaitingMessage;
        }

        public string GetInteractionVisibleMessage(string fallback)
        {
            return string.IsNullOrWhiteSpace(interactionVisibleMessage) ? fallback : interactionVisibleMessage;
        }

        public string GetInteractionFallbackMessage(string fallback)
        {
            return string.IsNullOrWhiteSpace(interactionFallbackMessage) ? fallback : interactionFallbackMessage;
        }

        public string GetRiddlePrompt()
        {
            return string.IsNullOrWhiteSpace(riddlePrompt) ? body : riddlePrompt;
        }

        public string GetHintMessage(string fallback)
        {
            return string.IsNullOrWhiteSpace(hintMessage) ? fallback : hintMessage;
        }

        public string GetPortalPrompt()
        {
            return string.IsNullOrWhiteSpace(portalPrompt) ? body : portalPrompt;
        }

        public string GetPortalProgressLabel()
        {
            return string.IsNullOrWhiteSpace(portalProgressLabel) ? "Eingabe" : portalProgressLabel;
        }

        public string GetPortalMistakeMessage()
        {
            return string.IsNullOrWhiteSpace(portalMistakeMessage) ? "Die Runen reagieren nicht. Versuch die Reihenfolge neu." : portalMistakeMessage;
        }

        public string GetPortalSolvedMessage()
        {
            return string.IsNullOrWhiteSpace(portalSolvedMessage) ? successMessage : portalSolvedMessage;
        }

        public string GetGameId()
        {
            return string.IsNullOrWhiteSpace(gameId) ? id : gameId;
        }

        public string GetGameWaitingMessage()
        {
            return string.IsNullOrWhiteSpace(gameWaitingMessage)
                ? "Starte das Spiel. Wenn du es geschafft hast, kommst du automatisch hierher zurueck."
                : gameWaitingMessage;
        }

        public string GetGameCompletedMessage()
        {
            if (!string.IsNullOrWhiteSpace(gameCompletedMessage))
                return gameCompletedMessage;

            return string.IsNullOrWhiteSpace(successMessage) ? "Spiel geschafft." : successMessage;
        }
    }
}
