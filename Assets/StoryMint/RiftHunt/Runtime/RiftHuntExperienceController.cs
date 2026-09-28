using System;
using System.Collections;
using System.Collections.Generic;
using StoryMint.RiftHunt.Minigames;
using TMPro;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace StoryMint.RiftHunt
{
    public class RiftHuntExperienceController : MonoBehaviour
    {
        private struct CachedTargetPose
        {
            public Vector3 position;
            public Quaternion rotation;
        }

        private RiftHuntTrailDefinition trail;
        private RiftHuntStepDefinition currentStep;
        private IRiftHuntARProvider arProvider;
        private Camera mainCamera;
        private StoryMintLocationService locationService;

        private Canvas overlayCanvas;
        private TextMeshProUGUI titleText;
        private TextMeshProUGUI bodyText;
        private TextMeshProUGUI statusText;
        private TextMeshProUGUI progressText;
        private TextMeshProUGUI versionText;
        private Button primaryButton;
        private TextMeshProUGUI primaryButtonLabel;
        private RectTransform panelRect;

        private readonly Dictionary<string, RiftHuntTapHotspot> hotspotsByStepId = new Dictionary<string, RiftHuntTapHotspot>();
        private readonly Dictionary<string, CachedTargetPose> cachedTargetPosesById = new Dictionary<string, CachedTargetPose>();
        private readonly List<Button> choiceButtons = new List<Button>();
        private readonly List<TextMeshProUGUI> choiceButtonLabels = new List<TextMeshProUGUI>();
        private readonly List<string> portalSequenceInput = new List<string>();
        private Coroutine pendingAdvance;
        private RuneSortMinigame activeRuneSort;
        private SimonSaysMinigame activeSimonSays;
        private GyroMazeMinigame activeGyroMaze;
        private RuneCatcherMinigame activeRuneCatcher;
        private SealTraceMinigame activeSealTrace;
        private WhackARuneMinigame activeWhackARune;
        private RuneMemoryMinigame activeRuneMemory;
        private bool isInitialized;
        private bool demoMode;
        private bool geofenceRequestStarted;
        private bool geofenceAdvanceScheduled;
        private float nextGeofenceRefreshTime;
        private const float DefaultPanelHeight = 520f;
        private const float PuzzlePanelHeight = 720f;

        private IEnumerator Start()
        {
            RiftHuntTrailDefinition loadedTrail = null;
            string loadError = null;

            yield return RiftHuntTrailLoader.LoadActiveTrail(
                definition => loadedTrail = definition,
                error => loadError = error);

            if (loadedTrail == null)
            {
                if (!string.IsNullOrWhiteSpace(loadError))
                    Debug.LogWarning("[StoryMint] Rift Hunt bootstrap skipped: " + loadError);

                Destroy(gameObject);
                yield break;
            }

            trail = loadedTrail;
            if (!string.IsNullOrWhiteSpace(trail.sceneName) && trail.sceneName != SceneManager.GetActiveScene().name)
            {
                Destroy(gameObject);
                yield break;
            }

            if (!RiftHuntRuntimeSceneBuilder.EnsureSceneReady(trail, out arProvider, out mainCamera, out string runtimeError))
            {
                Debug.LogError("[StoryMint] Failed to prepare Rift Hunt runtime: " + runtimeError);
                Destroy(gameObject);
                yield break;
            }

            // --- Demo mode: add ?demo to the URL to skip geofence steps ---
#if UNITY_WEBGL && !UNITY_EDITOR
            demoMode = Application.absoluteURL.Contains("demo");
#else
            demoMode = false;
#endif
            if (demoMode)
                Debug.Log("[StoryMint] Demo mode active — geofence steps will be skipped.");

            BuildUi();
            if (trail.HasGeofenceSteps() && !demoMode)
                EnsureLocationService();

            string requestedDebugStepId = demoMode ? FirstNonEmpty(
                GetQueryParameter(Application.absoluteURL, "step"),
                GetQueryParameter(Application.absoluteURL, "debugStep")) : null;

            bool resetRequested = ApplyResetUrlSignal();
            if (!resetRequested)
                ApplyExternalGameUrlSignals();

            arProvider.ImageFound += HandleImageFound;
            arProvider.ImageLost += HandleImageLost;
            isInitialized = true;

            // --- Resume from saved progress if the page was reloaded ---
            string savedStepId = resetRequested ? null : StoryMintSaveState.LoadStep(trail.trailId);
            RiftHuntStepDefinition debugStep = !string.IsNullOrWhiteSpace(requestedDebugStepId)
                ? trail.GetStepById(requestedDebugStepId)
                : null;
            RiftHuntStepDefinition resumeStep = null;

            if (!string.IsNullOrEmpty(savedStepId))
                resumeStep = trail.GetStepById(savedStepId);

            if (debugStep != null)
            {
                Debug.Log("[StoryMint] Demo debug step requested: " + requestedDebugStepId);
                ShowStep(debugStep);
            }
            else if (!string.IsNullOrWhiteSpace(requestedDebugStepId))
            {
                Debug.LogWarning("[StoryMint] Demo debug step not found: " + requestedDebugStepId);
                ShowStep(resumeStep != null ? resumeStep : trail.GetStartStep());
            }
            else if (resumeStep != null)
            {
                Debug.Log("[StoryMint] Resuming trail at saved step: " + savedStepId);
                ShowStep(resumeStep);
            }
            else
            {
                ShowStep(trail.GetStartStep());
            }
        }

        private void OnDestroy()
        {
            CleanupActiveMinigame();

            if (arProvider != null)
            {
                arProvider.ImageFound -= HandleImageFound;
                arProvider.ImageLost -= HandleImageLost;
            }

            if (locationService != null)
            {
                locationService.LocationUpdated -= HandleLocationUpdated;
                locationService.StatusChanged -= HandleLocationStatusChanged;
            }
        }

        private void Update()
        {
            if (!isInitialized || currentStep == null)
                return;

            if (!string.IsNullOrWhiteSpace(currentStep.targetId) && arProvider != null && arProvider.IsImageTracked(currentStep.targetId))
                CacheTargetPose(currentStep.targetId);

            if (currentStep.GetStepKind() == RiftHuntStepKind.Geofence && !demoMode)
            {
                UpdateGeofenceStep(false);
                return;
            }

            if (currentStep.GetStepKind() == RiftHuntStepKind.PortalDeactivation)
            {
                UpdateHotspotVisibility(false);
                return;
            }

            if (currentStep.GetStepKind() != RiftHuntStepKind.ArInteraction)
                return;

            UpdateHotspotVisibility(true);

            if (!TryGetPrimaryPointerDown(out Vector2 pointerPosition, out int pointerId))
                return;

            if (IsPointerOverUi(pointerId))
                return;

            Ray ray = mainCamera.ScreenPointToRay(pointerPosition);
            if (!Physics.Raycast(ray, out RaycastHit hit))
                return;

            RiftHuntTapHotspot hotspot = hit.collider.GetComponentInParent<RiftHuntTapHotspot>();
            if (hotspot == null || hotspot.StepId != currentStep.id)
                return;

            HandleInteractionCompleted();
        }

        private void HandlePrimaryAction()
        {
            StoryMintSFX.ResumeContext(); // unlock Web Audio on first user gesture
            StoryMintSFX.Tap();

            if (currentStep == null)
                return;

            if (!string.IsNullOrWhiteSpace(currentStep.actionUrl))
            {
                StoryMintBrowserNavigation.NavigateTo(currentStep.actionUrl.Trim());
                return;
            }

            if (currentStep.GetStepKind() == RiftHuntStepKind.ExternalGame)
            {
                LaunchExternalGame(currentStep);
                return;
            }

            AdvanceToStep(currentStep.nextStepId);
        }

        private void HandleImageFound(string targetId)
        {
            if (currentStep == null)
                return;

            CacheTargetPose(targetId);

            if (currentStep.GetStepKind() == RiftHuntStepKind.Scan && currentStep.targetId == targetId)
            {
                string successText = string.IsNullOrWhiteSpace(currentStep.successMessage) ? "Ziel erkannt." : currentStep.successMessage;
                SetStatus(successText, true);
                StoryMintSFX.Success();
                ScheduleAdvance(currentStep.autoAdvanceDelay);
            }
            else if (demoMode && currentStep.GetStepKind() == RiftHuntStepKind.Scan)
            {
                SetStatus("[DEMO] Erkannt: " + targetId + " | erwartet: " + currentStep.targetId, true);
            }

            UpdateHotspotVisibility(true);
        }

        private void HandleImageLost(string targetId)
        {
            if (currentStep == null)
                return;

            UpdateHotspotVisibility(true);
        }

        private void ShowStep(RiftHuntStepDefinition step)
        {
            if (step == null)
            {
                Debug.LogError("[StoryMint] Tried to show a missing step.");
                return;
            }

            if (currentStep != null && currentStep.GetStepKind() == RiftHuntStepKind.Geofence && step.GetStepKind() != RiftHuntStepKind.Geofence && locationService != null)
                locationService.StopWatching();

            currentStep = step;
            geofenceRequestStarted = false;
            geofenceAdvanceScheduled = false;
            nextGeofenceRefreshTime = 0f;

            // --- Persist progress so the game survives page reloads ---
            if (trail != null && !string.IsNullOrEmpty(step.id))
            {
                // If this step loops back to the start (= finale), clear save
                // so "Nochmal spielen" begins fresh.
                bool isFinale = !string.IsNullOrEmpty(step.nextStepId)
                    && step.nextStepId == trail.startStepId
                    && step.id != trail.startStepId;

                if (isFinale)
                {
                    StoryMintSaveState.ClearSave(trail.trailId);
                    ClearExternalGameCompletionsForTrail();
                }
                else
                {
                    StoryMintSaveState.SaveStep(trail.trailId, step.id);
                }
            }

            titleText.text = string.IsNullOrWhiteSpace(step.title) ? trail.title : step.title;
            bodyText.text = step.body ?? string.Empty;
            progressText.text = BuildProgressLabel(step.id);

            StopPendingAdvance();
            HideAllHotspots();
            HideChoiceButtons();
            CleanupActiveMinigame();
            portalSequenceInput.Clear();
            SetPuzzleLayout(false);

            switch (step.GetStepKind())
            {
                case RiftHuntStepKind.Message:
                    SetPrimaryAction(step.GetPrimaryActionLabel("Weiter"), true);
                    SetStatus(string.Empty, false);
                    break;

                case RiftHuntStepKind.Geofence:
                    if (demoMode)
                    {
                        // In demo mode, show geofence as a simple message with a skip button
                        SetPrimaryAction(step.GetPrimaryActionLabel("Weiter"), true);
                        SetStatus("[DEMO] GPS deaktiviert", true);
                    }
                    else
                    {
                        SetPrimaryAction(null, false);
                        SetStatus(BuildGeofenceWaitingMessage(step), true);
                        EnsureLocationService();
                        UpdateGeofenceStep(true);
                    }
                    break;

                case RiftHuntStepKind.Scan:
                    SetPrimaryAction(null, false);
                    SetStatus(demoMode
                        ? "[DEMO] Scan erwartet: " + step.targetId
                        : "Halte das Motiv stabil im Bild.", true);
                    if (arProvider != null && arProvider.IsImageTracked(step.targetId))
                    {
                        string successText = string.IsNullOrWhiteSpace(step.successMessage) ? "Ziel erkannt." : step.successMessage;
                        SetStatus(successText, true);
                        ScheduleAdvance(step.autoAdvanceDelay);
                    }
                    break;

                case RiftHuntStepKind.ArInteraction:
                    SetPrimaryAction(null, false);
                    SetStatus(BuildInteractionWaitingMessage(step), true);
                    CreateOrUpdateHotspot(step);
                    UpdateHotspotVisibility(true);
                    break;

                case RiftHuntStepKind.Riddle:
                    SetPrimaryAction(null, false);
                    ShowRiddleStep(step);
                    break;

                case RiftHuntStepKind.PortalDeactivation:
                    SetPrimaryAction(null, false);
                    ShowPortalDeactivationStep(step);
                    break;

                case RiftHuntStepKind.RuneSort:
                    SetPrimaryAction(null, false);
                    SetStatus(string.Empty, false);
                    LaunchRuneSortMinigame(step);
                    break;

                case RiftHuntStepKind.SimonSays:
                    SetPrimaryAction(null, false);
                    SetStatus(string.Empty, false);
                    LaunchSimonSaysMinigame(step);
                    break;

                case RiftHuntStepKind.GyroMaze:
                    SetPrimaryAction(null, false);
                    SetStatus(string.Empty, false);
                    LaunchGyroMazeMinigame(step);
                    break;

                case RiftHuntStepKind.RuneCatcher:
                    SetPrimaryAction(null, false);
                    SetStatus(string.Empty, false);
                    LaunchRuneCatcherMinigame(step);
                    break;

                case RiftHuntStepKind.SealTrace:
                    SetPrimaryAction(null, false);
                    SetStatus(string.Empty, false);
                    LaunchSealTraceMinigame(step);
                    break;

                case RiftHuntStepKind.WhackARune:
                    SetPrimaryAction(null, false);
                    SetStatus(string.Empty, false);
                    LaunchWhackARuneMinigame(step);
                    break;

                case RiftHuntStepKind.RuneMemory:
                    SetPrimaryAction(null, false);
                    SetStatus(string.Empty, false);
                    LaunchRuneMemoryMinigame(step);
                    break;

                case RiftHuntStepKind.ExternalGame:
                    SetPrimaryAction(null, false);
                    ShowExternalGameStep(step);
                    break;

                default:
                    SetPrimaryAction("Weiter", true);
                    SetStatus("Unbekannter Step-Typ. Weiter mit Vorsicht.", true);
                    break;
            }
        }

        private void ShowRiddleStep(RiftHuntStepDefinition step)
        {
            SetPuzzleLayout(true);
            bodyText.text = step.GetRiddlePrompt() ?? string.Empty;

            if (step.choices == null || step.choices.Count == 0)
            {
                SetStatus("Dieses Raetsel hat noch keine Antwortoptionen.", true);
                return;
            }

            SetStatus(step.GetHintMessage("Waehle die Antwort, die den Riss beruhigt."), true);
            ConfigureChoiceButtonsForRiddle(step);
        }

        private void ShowPortalDeactivationStep(RiftHuntStepDefinition step)
        {
            SetPuzzleLayout(true);
            bodyText.text = step.GetPortalPrompt() ?? string.Empty;

            if (!string.IsNullOrWhiteSpace(step.targetId))
            {
                CreateOrUpdateHotspot(step);
                UpdateHotspotVisibility(false);
            }

            if (step.runeSequence == null || step.runeSequence.Count == 0)
            {
                SetStatus("Dieses Portal-Schloss hat noch keine Sequenz.", true);
                return;
            }

            ConfigureChoiceButtonsForRunes(step);
            SetStatus(BuildPortalSequenceStatus(step), true);
        }

        private void HandleInteractionCompleted()
        {
            string successText = string.IsNullOrWhiteSpace(currentStep.successMessage) ? "Interaktion bestaetigt." : currentStep.successMessage;
            SetStatus(successText, true);
            HideAllHotspots();
            ScheduleAdvance(Mathf.Max(currentStep.autoAdvanceDelay, 0.5f));
        }

        private void AdvanceToStep(string nextStepId)
        {
            RiftHuntStepDefinition nextStep = trail.GetStepById(nextStepId);
            if (nextStep == null)
            {
                Debug.LogWarning("[StoryMint] Step '" + nextStepId + "' was not found. Restarting trail.");
                nextStep = trail.GetStartStep();
            }

            ShowStep(nextStep);
        }

        private void ShowExternalGameStep(RiftHuntStepDefinition step)
        {
            string gameId = step.GetGameId();
            if (StoryMintSaveState.IsGameCompleted(trail.trailId, gameId))
            {
                SetPrimaryAction(null, false);
                SetStatus(step.GetGameCompletedMessage(), true);
                StoryMintSFX.Success();
                ScheduleAdvance(Mathf.Max(step.autoAdvanceDelay, 0.75f));
                return;
            }

            if (string.IsNullOrWhiteSpace(step.gameUrl))
            {
                SetPrimaryAction(null, false);
                SetStatus("Dieses externe Spiel hat noch keine gameUrl.", true);
                return;
            }

            SetPrimaryAction(step.GetPrimaryActionLabel("Spiel starten"), true);
            SetStatus(step.GetGameWaitingMessage(), true);
        }

        private void LaunchExternalGame(RiftHuntStepDefinition step)
        {
            if (string.IsNullOrWhiteSpace(step.gameUrl))
            {
                SetStatus("Dieses externe Spiel hat noch keine gameUrl.", true);
                return;
            }

            StoryMintSaveState.SaveStep(trail.trailId, step.id);
            string launchUrl = BuildExternalGameUrl(step);
            SetStatus("Spiel wird geoeffnet ...", true);
            StoryMintBrowserNavigation.NavigateTo(launchUrl);
        }

        private string BuildExternalGameUrl(RiftHuntStepDefinition step)
        {
            string launchUrl = step.gameUrl.Trim();
            string gameId = step.GetGameId();

            launchUrl = AppendQueryParameter(launchUrl, "gameId", gameId);
            launchUrl = AppendQueryParameter(launchUrl, "trailId", trail.trailId);
            launchUrl = AppendQueryParameter(launchUrl, "stepId", step.id);

            string returnUrl = BuildExternalGameReturnUrl(step);
            if (!string.IsNullOrWhiteSpace(returnUrl))
                launchUrl = AppendQueryParameter(launchUrl, "returnUrl", returnUrl);

            return launchUrl;
        }

        private bool ApplyResetUrlSignal()
        {
#if UNITY_WEBGL && !UNITY_EDITOR
            string url = Application.absoluteURL;
#else
            string url = string.Empty;
#endif
            if (string.IsNullOrWhiteSpace(url) || trail == null || !HasQueryParameter(url, "reset"))
                return false;

            StoryMintSaveState.ClearSave(trail.trailId);
            ClearExternalGameCompletionsForTrail();
            StoryMintBrowserNavigation.ReplaceCurrentAddress(BuildCleanRiftHuntUrl(url, HasQueryParameter(url, "demo")));
            Debug.Log("[StoryMint] Reset URL signal applied for trail '" + trail.trailId + "'.");
            return true;
        }

        private string BuildExternalGameReturnUrl(RiftHuntStepDefinition step)
        {
#if UNITY_WEBGL && !UNITY_EDITOR
            string currentUrl = Application.absoluteURL;
#else
            string currentUrl = string.Empty;
#endif
            if (string.IsNullOrWhiteSpace(currentUrl) || step == null)
                return string.Empty;

            string returnUrl = BuildCleanRiftHuntUrl(currentUrl, HasQueryParameter(currentUrl, "demo"));
            return AppendQueryParameter(returnUrl, "gameComplete", step.GetGameId());
        }

        private void ApplyExternalGameUrlSignals()
        {
#if UNITY_WEBGL && !UNITY_EDITOR
            string url = Application.absoluteURL;
#else
            string url = string.Empty;
#endif
            if (string.IsNullOrWhiteSpace(url) || trail == null)
                return;

            bool shouldCleanUrl = false;
            string completedGameId = FirstNonEmpty(
                GetQueryParameter(url, "gameComplete"),
                GetQueryParameter(url, "completeGame"));

            if (string.IsNullOrWhiteSpace(completedGameId) && IsTruthyQueryValue(GetQueryParameter(url, "completed")))
                completedGameId = GetQueryParameter(url, "gameId");

            if (!string.IsNullOrWhiteSpace(completedGameId))
            {
                RiftHuntStepDefinition completedStep = FindExternalGameStepByGameId(completedGameId);
                if (completedStep != null)
                    MarkExternalGameCompleted(completedStep, saveNextStep: true);
                else
                    StoryMintSaveState.SaveGameCompleted(trail.trailId, completedGameId.Trim());

                shouldCleanUrl = true;
            }

            string skipGameId = FirstNonEmpty(
                GetQueryParameter(url, "skipGame"),
                GetQueryParameter(url, "skip"));
            string skipStepId = GetQueryParameter(url, "skipStep");

            if (string.IsNullOrWhiteSpace(skipGameId) && string.IsNullOrWhiteSpace(skipStepId))
            {
                if (shouldCleanUrl)
                    CleanExternalGameUrlSignals(url);
                return;
            }

            string providedCode = FirstNonEmpty(
                GetQueryParameter(url, "code"),
                GetQueryParameter(url, "fallbackCode"));

            RiftHuntStepDefinition skipStep = !string.IsNullOrWhiteSpace(skipStepId)
                ? trail.GetStepById(skipStepId.Trim())
                : FindExternalGameStepByGameId(skipGameId);

            if (skipStep == null || skipStep.GetStepKind() != RiftHuntStepKind.ExternalGame)
            {
                Debug.LogWarning("[StoryMint] External game skip ignored: step/game was not found.");
                return;
            }

            if (!DoesFallbackCodeMatch(skipStep.qrFallbackCode, providedCode))
            {
                Debug.LogWarning("[StoryMint] External game skip ignored: fallback code did not match.");
                if (shouldCleanUrl)
                    CleanExternalGameUrlSignals(url);
                return;
            }

            MarkExternalGameCompleted(skipStep, saveNextStep: true);
            CleanExternalGameUrlSignals(url);
        }

        private void MarkExternalGameCompleted(RiftHuntStepDefinition step, bool saveNextStep)
        {
            if (step == null)
                return;

            StoryMintSaveState.SaveGameCompleted(trail.trailId, step.GetGameId());
            if (saveNextStep && !string.IsNullOrWhiteSpace(step.nextStepId))
                StoryMintSaveState.SaveStep(trail.trailId, step.nextStepId);
        }

        private RiftHuntStepDefinition FindExternalGameStepByGameId(string gameId)
        {
            if (trail == null || trail.steps == null || string.IsNullOrWhiteSpace(gameId))
                return null;

            string trimmedGameId = gameId.Trim();
            for (int index = 0; index < trail.steps.Count; index++)
            {
                RiftHuntStepDefinition step = trail.steps[index];
                if (step == null || step.GetStepKind() != RiftHuntStepKind.ExternalGame)
                    continue;

                if (string.Equals(step.GetGameId(), trimmedGameId, StringComparison.OrdinalIgnoreCase))
                    return step;
            }

            return null;
        }

        private void ClearExternalGameCompletionsForTrail()
        {
            if (trail == null || trail.steps == null)
                return;

            for (int index = 0; index < trail.steps.Count; index++)
            {
                RiftHuntStepDefinition step = trail.steps[index];
                if (step != null && step.GetStepKind() == RiftHuntStepKind.ExternalGame)
                    StoryMintSaveState.ClearGameCompleted(trail.trailId, step.GetGameId());
            }
        }

        private void ScheduleAdvance(float delay)
        {
            StopPendingAdvance();
            pendingAdvance = StartCoroutine(AdvanceAfterDelay(delay));
        }

        private IEnumerator AdvanceAfterDelay(float delay)
        {
            yield return new WaitForSeconds(delay);
            AdvanceToStep(currentStep.nextStepId);
        }

        private static string AppendQueryParameter(string url, string key, string value)
        {
            if (string.IsNullOrWhiteSpace(url) || string.IsNullOrWhiteSpace(key) || string.IsNullOrWhiteSpace(value))
                return url;

            int fragmentIndex = url.IndexOf('#');
            string fragment = fragmentIndex >= 0 ? url.Substring(fragmentIndex) : string.Empty;
            string baseUrl = fragmentIndex >= 0 ? url.Substring(0, fragmentIndex) : url;
            string separator = baseUrl.Contains("?")
                ? (baseUrl.EndsWith("?") || baseUrl.EndsWith("&") ? string.Empty : "&")
                : "?";

            return baseUrl + separator + Uri.EscapeDataString(key) + "=" + Uri.EscapeDataString(value) + fragment;
        }

        private void CleanExternalGameUrlSignals(string currentUrl)
        {
            string cleanUrl = BuildCleanRiftHuntUrl(currentUrl, HasQueryParameter(currentUrl, "demo"));
            StoryMintBrowserNavigation.ReplaceCurrentAddress(cleanUrl);
        }

        private static string BuildCleanRiftHuntUrl(string currentUrl, bool preserveDemo)
        {
            string cleanUrl = StripQueryAndFragment(currentUrl);

            try
            {
                Uri uri = new Uri(currentUrl);
                string path = uri.AbsolutePath;
                string lowerPath = path.ToLowerInvariant();
                int riftHuntIndex = lowerPath.IndexOf("/rifthunt/", StringComparison.Ordinal);

                if (riftHuntIndex >= 0)
                    path = path.Substring(0, riftHuntIndex + "/RiftHunt/".Length);
                else if (lowerPath.EndsWith("/rifthunt", StringComparison.Ordinal))
                    path += "/";
                else if (lowerPath.EndsWith("/index.html", StringComparison.Ordinal))
                    path = path.Substring(0, path.Length - "index.html".Length);

                UriBuilder builder = new UriBuilder(uri.Scheme, uri.Host, uri.IsDefaultPort ? -1 : uri.Port, path)
                {
                    Query = string.Empty,
                    Fragment = string.Empty
                };
                cleanUrl = builder.Uri.AbsoluteUri;
            }
            catch (UriFormatException)
            {
                cleanUrl = StripQueryAndFragment(currentUrl);
            }

            return preserveDemo ? AppendQueryParameter(cleanUrl, "demo", "1") : cleanUrl;
        }

        private static string StripQueryAndFragment(string url)
        {
            if (string.IsNullOrWhiteSpace(url))
                return url;

            int end = url.Length;
            int queryIndex = url.IndexOf('?');
            int fragmentIndex = url.IndexOf('#');
            if (queryIndex >= 0)
                end = Mathf.Min(end, queryIndex);
            if (fragmentIndex >= 0)
                end = Mathf.Min(end, fragmentIndex);

            return url.Substring(0, end);
        }

        private static string GetQueryParameter(string url, string key)
        {
            if (string.IsNullOrWhiteSpace(url) || string.IsNullOrWhiteSpace(key))
                return null;

            int queryStart = url.IndexOf('?');
            if (queryStart < 0 || queryStart >= url.Length - 1)
                return null;

            int fragmentStart = url.IndexOf('#', queryStart);
            string query = fragmentStart >= 0
                ? url.Substring(queryStart + 1, fragmentStart - queryStart - 1)
                : url.Substring(queryStart + 1);

            string[] pairs = query.Split('&');
            for (int index = 0; index < pairs.Length; index++)
            {
                string pair = pairs[index];
                int equalsIndex = pair.IndexOf('=');
                string pairKey = equalsIndex >= 0 ? pair.Substring(0, equalsIndex) : pair;
                if (!string.Equals(Uri.UnescapeDataString(pairKey), key, StringComparison.OrdinalIgnoreCase))
                    continue;

                string value = equalsIndex >= 0 ? pair.Substring(equalsIndex + 1) : string.Empty;
                return Uri.UnescapeDataString(value.Replace("+", " "));
            }

            return null;
        }

        private static bool HasQueryParameter(string url, string key)
        {
            return GetQueryParameter(url, key) != null;
        }

        private static string FirstNonEmpty(params string[] values)
        {
            if (values == null)
                return null;

            for (int index = 0; index < values.Length; index++)
            {
                if (!string.IsNullOrWhiteSpace(values[index]))
                    return values[index];
            }

            return null;
        }

        private static bool IsTruthyQueryValue(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return false;

            value = value.Trim().ToLowerInvariant();
            return value == "1" || value == "true" || value == "yes" || value == "done" || value == "complete";
        }

        private static bool DoesFallbackCodeMatch(string expectedCode, string providedCode)
        {
            return !string.IsNullOrWhiteSpace(expectedCode)
                && !string.IsNullOrWhiteSpace(providedCode)
                && string.Equals(expectedCode.Trim(), providedCode.Trim(), StringComparison.OrdinalIgnoreCase);
        }

        private void StopPendingAdvance()
        {
            if (pendingAdvance == null)
                return;

            StopCoroutine(pendingAdvance);
            pendingAdvance = null;
        }

        private void EnsureLocationService()
        {
            if (locationService != null)
                return;

            locationService = StoryMintLocationService.GetOrCreate();
            locationService.LocationUpdated += HandleLocationUpdated;
            locationService.StatusChanged += HandleLocationStatusChanged;
        }

        private void HandleLocationUpdated(StoryMintLocationFix fix)
        {
            if (currentStep != null && currentStep.GetStepKind() == RiftHuntStepKind.Geofence)
                UpdateGeofenceStep(false);
        }

        private void HandleLocationStatusChanged(StoryMintLocationStatus status, string message)
        {
            if (currentStep == null || currentStep.GetStepKind() != RiftHuntStepKind.Geofence)
                return;

            if (!string.IsNullOrWhiteSpace(message))
                SetStatus(message, true);

            UpdateGeofenceStep(false);
        }

        private void UpdateGeofenceStep(bool forceRequest)
        {
            if (currentStep == null || currentStep.GetStepKind() != RiftHuntStepKind.Geofence || geofenceAdvanceScheduled)
                return;

            EnsureLocationService();

            if (!currentStep.location.IsValid)
            {
                SetStatus("Dieser Story-Ort hat noch keine gueltige GPS-Position.", true);
                SetGeofenceFallbackAction(currentStep, true);
                return;
            }

            if (!geofenceRequestStarted || forceRequest)
            {
                geofenceRequestStarted = true;
                locationService.StartWatching();
            }

            if (!forceRequest && Time.unscaledTime < nextGeofenceRefreshTime)
                return;

            nextGeofenceRefreshTime = Time.unscaledTime + 0.5f;

            StoryMintGeofenceResult result = locationService.EvaluateGeofence(
                currentStep.location,
                currentStep.geofenceRadiusMeters,
                currentStep.requiredAccuracyMeters);

            if (result.isInside)
            {
                string successText = string.IsNullOrWhiteSpace(currentStep.successMessage) ? "Story-Ort erreicht." : currentStep.successMessage;
                SetStatus(successText, true);
                SetPrimaryAction(null, false);
                StoryMintSFX.Success();
                geofenceAdvanceScheduled = true;
                locationService.StopWatching();
                ScheduleAdvance(Mathf.Max(currentStep.autoAdvanceDelay, 0.5f));
                return;
            }

            if (!string.IsNullOrWhiteSpace(result.BuildStatusText(currentStep.locationName)))
                SetStatus(result.BuildStatusText(currentStep.locationName), true);

            SetGeofenceFallbackAction(currentStep, ShouldOfferLocationFallback(currentStep, locationService.Status));
        }

        private void SetGeofenceFallbackAction(RiftHuntStepDefinition step, bool visible)
        {
            if (!visible)
            {
                SetPrimaryAction(null, false);
                return;
            }

            string fallbackLabel = string.IsNullOrWhiteSpace(step.qrFallbackCode)
                ? "Manuell bestaetigen"
                : "QR-Fallback";
            SetPrimaryAction(step.GetPrimaryActionLabel(fallbackLabel), true);
        }

        private static bool ShouldOfferLocationFallback(RiftHuntStepDefinition step, StoryMintLocationStatus status)
        {
            if (!step.allowLocationFallback)
                return false;

            switch (status)
            {
                case StoryMintLocationStatus.PermissionDenied:
                case StoryMintLocationStatus.Unavailable:
                case StoryMintLocationStatus.Timeout:
                case StoryMintLocationStatus.Error:
                    return true;
                default:
                    return false;
            }
        }

        private void ConfigureChoiceButtonsForRiddle(RiftHuntStepDefinition step)
        {
            int visibleCount = Mathf.Min(step.choices.Count, choiceButtons.Count);
            for (int index = 0; index < choiceButtons.Count; index++)
            {
                bool visible = index < visibleCount;
                choiceButtons[index].gameObject.SetActive(visible);
                if (!visible)
                    continue;

                RiftHuntChoiceDefinition choice = step.choices[index];
                string label = choice != null && !string.IsNullOrWhiteSpace(choice.label)
                    ? choice.label
                    : "Antwort " + (index + 1);
                choiceButtonLabels[index].text = label;
            }
        }

        private void ConfigureChoiceButtonsForRunes(RiftHuntStepDefinition step)
        {
            List<string> runeOptions = BuildRuneOptions(step);
            int visibleCount = Mathf.Min(runeOptions.Count, choiceButtons.Count);

            for (int index = 0; index < choiceButtons.Count; index++)
            {
                bool visible = index < visibleCount;
                choiceButtons[index].gameObject.SetActive(visible);
                if (visible)
                    choiceButtonLabels[index].text = runeOptions[index];
            }
        }

        private List<string> BuildRuneOptions(RiftHuntStepDefinition step)
        {
            List<string> runeOptions = new List<string>();
            if (step.runeOptions != null)
            {
                for (int index = 0; index < step.runeOptions.Count; index++)
                    AddUniqueRune(runeOptions, step.runeOptions[index]);
            }

            if (runeOptions.Count == 0 && step.runeSequence != null)
            {
                for (int index = 0; index < step.runeSequence.Count; index++)
                    AddUniqueRune(runeOptions, step.runeSequence[index]);
            }

            if (runeOptions.Count == 0)
            {
                AddUniqueRune(runeOptions, "LUX");
                AddUniqueRune(runeOptions, "AER");
                AddUniqueRune(runeOptions, "TERRA");
                AddUniqueRune(runeOptions, "UMBRA");
            }

            return runeOptions;
        }

        private static void AddUniqueRune(List<string> runeOptions, string rune)
        {
            if (string.IsNullOrWhiteSpace(rune))
                return;

            string trimmedRune = rune.Trim();
            for (int index = 0; index < runeOptions.Count; index++)
            {
                if (string.Equals(runeOptions[index], trimmedRune, System.StringComparison.OrdinalIgnoreCase))
                    return;
            }

            runeOptions.Add(trimmedRune);
        }

        private void HandleChoiceButtonPressed(int index)
        {
            if (currentStep == null)
                return;

            switch (currentStep.GetStepKind())
            {
                case RiftHuntStepKind.Riddle:
                    HandleRiddleChoice(index);
                    break;
                case RiftHuntStepKind.PortalDeactivation:
                    HandleRuneChoice(index);
                    break;
            }
        }

        private void HandleRiddleChoice(int index)
        {
            if (currentStep.choices == null || index < 0 || index >= currentStep.choices.Count)
                return;

            RiftHuntChoiceDefinition choice = currentStep.choices[index];
            if (choice == null)
                return;

            if (choice.isCorrect)
            {
                string successText = !string.IsNullOrWhiteSpace(choice.feedback)
                    ? choice.feedback
                    : (string.IsNullOrWhiteSpace(currentStep.successMessage) ? "Raetsel geloest." : currentStep.successMessage);
                SetStatus(successText, true);
                HideChoiceButtons();
                ScheduleAdvance(Mathf.Max(currentStep.autoAdvanceDelay, 0.5f));
                return;
            }

            string feedback = !string.IsNullOrWhiteSpace(choice.feedback)
                ? choice.feedback
                : currentStep.GetHintMessage("Noch nicht. Schau genau auf den Hinweis.");
            SetStatus(feedback, true);
        }

        private void HandleRuneChoice(int index)
        {
            List<string> runeOptions = BuildRuneOptions(currentStep);
            if (index < 0 || index >= runeOptions.Count)
                return;

            portalSequenceInput.Add(runeOptions[index]);

            if (!IsPortalSequencePrefixValid(currentStep))
            {
                portalSequenceInput.Clear();
                SetStatus(currentStep.GetPortalMistakeMessage(), true);
                return;
            }

            if (portalSequenceInput.Count >= currentStep.runeSequence.Count)
            {
                string solvedText = currentStep.GetPortalSolvedMessage();
                if (string.IsNullOrWhiteSpace(solvedText))
                    solvedText = "Portal deaktiviert.";

                SetStatus(solvedText, true);
                HideChoiceButtons();
                HideAllHotspots();
                ScheduleAdvance(Mathf.Max(currentStep.autoAdvanceDelay, 0.65f));
                return;
            }

            SetStatus(BuildPortalSequenceStatus(currentStep), true);
        }

        private bool IsPortalSequencePrefixValid(RiftHuntStepDefinition step)
        {
            if (step.runeSequence == null || portalSequenceInput.Count > step.runeSequence.Count)
                return false;

            for (int index = 0; index < portalSequenceInput.Count; index++)
            {
                if (!string.Equals(portalSequenceInput[index], step.runeSequence[index], System.StringComparison.OrdinalIgnoreCase))
                    return false;
            }

            return true;
        }

        private string BuildPortalSequenceStatus(RiftHuntStepDefinition step)
        {
            string inputText = portalSequenceInput.Count == 0 ? "-" : string.Join(" ", portalSequenceInput.ToArray());
            int remaining = step.runeSequence == null ? 0 : Mathf.Max(0, step.runeSequence.Count - portalSequenceInput.Count);
            return step.GetPortalProgressLabel() + ": " + inputText + " | noch " + remaining;
        }

        private void HideChoiceButtons()
        {
            for (int index = 0; index < choiceButtons.Count; index++)
                choiceButtons[index].gameObject.SetActive(false);
        }

        private void CreateOrUpdateHotspot(RiftHuntStepDefinition step)
        {
            if (arProvider == null)
                return;

            Transform targetTransform = arProvider.GetTargetTransform(step.targetId);
            if (targetTransform == null)
            {
                SetStatus("Target '" + step.targetId + "' ist in dieser Szene nicht konfiguriert.", true);
                return;
            }

            if (!hotspotsByStepId.TryGetValue(step.id, out RiftHuntTapHotspot hotspot) || hotspot == null)
            {
                GameObject hotspotObject = CreateInteractionVisual(step);
                hotspotObject.name = "StoryMintInteraction_" + step.id;
                hotspotObject.transform.SetParent(transform, false);

                hotspot = hotspotObject.GetComponent<RiftHuntTapHotspot>();
                if (hotspot == null)
                    hotspot = hotspotObject.AddComponent<RiftHuntTapHotspot>();

                EnsureTapColliders(hotspotObject);
                hotspotsByStepId[step.id] = hotspot;
            }

            hotspot.transform.SetParent(transform, false);
            hotspot.Configure(step.id, step.targetId, Mathf.Max(step.interactionScale, 0.03f));
            hotspot.gameObject.SetActive(false);
        }

        private void UpdateHotspotVisibility(bool updateStatus)
        {
            HideAllHotspots();

            if (currentStep == null)
                return;

            RiftHuntStepKind stepKind = currentStep.GetStepKind();
            if (stepKind != RiftHuntStepKind.ArInteraction && stepKind != RiftHuntStepKind.PortalDeactivation)
                return;

            if (!hotspotsByStepId.TryGetValue(currentStep.id, out RiftHuntTapHotspot hotspot) || hotspot == null)
                return;

            Transform targetTransform = arProvider != null ? arProvider.GetTargetTransform(currentStep.targetId) : null;
            bool isTracked = targetTransform != null && arProvider != null && arProvider.IsImageTracked(currentStep.targetId);
            if (isTracked)
            {
                CacheTargetPose(currentStep.targetId);
                AttachHotspotToTarget(hotspot, targetTransform, currentStep.interactionLocalPosition);
                hotspot.gameObject.SetActive(true);
                if (updateStatus)
                    SetStatus(BuildInteractionVisibleMessage(currentStep), true);
                return;
            }

            if (TryPositionHotspotFromCachedPose(hotspot, currentStep))
            {
                hotspot.gameObject.SetActive(true);
                if (updateStatus)
                    SetStatus(BuildInteractionFallbackMessage(currentStep), true);
                return;
            }

            hotspot.gameObject.SetActive(false);
            if (updateStatus)
                SetStatus(BuildInteractionWaitingMessage(currentStep), true);
        }

        private void HideAllHotspots()
        {
            foreach (var entry in hotspotsByStepId)
            {
                if (entry.Value != null)
                    entry.Value.gameObject.SetActive(false);
            }
        }

        private string BuildProgressLabel(string stepId)
        {
            int stepIndex = trail.GetStepIndex(stepId);
            int totalSteps = trail.steps != null ? trail.steps.Count : 0;

            if (stepIndex < 0 || totalSteps <= 0)
                return trail.title;

            return "Schritt " + (stepIndex + 1) + " / " + totalSteps;
        }

        private void BuildUi()
        {
            EnsureEventSystem();

            GameObject canvasObject = new GameObject("StoryMintCanvas", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            overlayCanvas = canvasObject.GetComponent<Canvas>();
            overlayCanvas.renderMode = RenderMode.ScreenSpaceOverlay;
            overlayCanvas.sortingOrder = 200;

            CanvasScaler scaler = canvasObject.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1080f, 1920f);
            scaler.matchWidthOrHeight = 1f;

            GameObject versionBadgeObject = CreateUiObject("VersionBadge", canvasObject.transform);
            Image versionBadgeImage = versionBadgeObject.AddComponent<Image>();
            versionBadgeImage.color = new Color(0.05f, 0.08f, 0.14f, 0.8f);

            RectTransform versionBadgeRect = versionBadgeObject.GetComponent<RectTransform>();
            versionBadgeRect.anchorMin = new Vector2(0f, 1f);
            versionBadgeRect.anchorMax = new Vector2(0f, 1f);
            versionBadgeRect.pivot = new Vector2(0f, 1f);
            versionBadgeRect.anchoredPosition = new Vector2(24f, -(GetTopSafeInset() + 24f));
            versionBadgeRect.sizeDelta = new Vector2(120f, 46f);

            versionText = CreateText(versionBadgeObject.transform, "Version", 24, FontStyles.Bold);
            versionText.rectTransform.anchorMin = Vector2.zero;
            versionText.rectTransform.anchorMax = Vector2.one;
            versionText.rectTransform.offsetMin = new Vector2(14f, 0f);
            versionText.rectTransform.offsetMax = new Vector2(-14f, 0f);
            versionText.alignment = TextAlignmentOptions.MidlineLeft;
            versionText.color = new Color(0.82f, 0.95f, 1f, 1f);
            versionText.text = StoryMintBuildVersion.Label;

            GameObject panelObject = CreateUiObject("Panel", canvasObject.transform);
            Image panelImage = panelObject.AddComponent<Image>();
            panelImage.color = new Color(0.05f, 0.08f, 0.14f, 0.84f);

            panelRect = panelObject.GetComponent<RectTransform>();
            panelRect.anchorMin = new Vector2(0f, 0f);
            panelRect.anchorMax = new Vector2(1f, 0f);
            panelRect.pivot = new Vector2(0.5f, 0f);
            panelRect.anchoredPosition = new Vector2(0f, GetBottomSafeInset());
            panelRect.sizeDelta = new Vector2(-56f, 480f);

            progressText = CreateText(panelObject.transform, "Progress", 32, FontStyles.Bold);
            progressText.rectTransform.anchorMin = new Vector2(0f, 1f);
            progressText.rectTransform.anchorMax = new Vector2(1f, 1f);
            progressText.rectTransform.pivot = new Vector2(0.5f, 1f);
            progressText.rectTransform.anchoredPosition = new Vector2(0f, -24f);
            progressText.rectTransform.sizeDelta = new Vector2(-64f, 44f);
            progressText.alignment = TextAlignmentOptions.Center;

            titleText = CreateText(panelObject.transform, "Title", 48, FontStyles.Bold);
            titleText.rectTransform.anchorMin = new Vector2(0f, 1f);
            titleText.rectTransform.anchorMax = new Vector2(1f, 1f);
            titleText.rectTransform.pivot = new Vector2(0.5f, 1f);
            titleText.rectTransform.anchoredPosition = new Vector2(0f, -72f);
            titleText.rectTransform.sizeDelta = new Vector2(-84f, 100f);
            titleText.alignment = TextAlignmentOptions.Center;
            titleText.enableWordWrapping = true;
            titleText.overflowMode = TextOverflowModes.Ellipsis;

            bodyText = CreateText(panelObject.transform, "Body", 36, FontStyles.Normal);
            bodyText.rectTransform.anchorMin = new Vector2(0f, 1f);
            bodyText.rectTransform.anchorMax = new Vector2(1f, 1f);
            bodyText.rectTransform.pivot = new Vector2(0.5f, 1f);
            bodyText.rectTransform.anchoredPosition = new Vector2(0f, -182f);
            bodyText.rectTransform.sizeDelta = new Vector2(-96f, 120f);
            bodyText.alignment = TextAlignmentOptions.Center;
            bodyText.enableWordWrapping = true;

            statusText = CreateText(panelObject.transform, "Status", 28, FontStyles.Italic);
            statusText.rectTransform.anchorMin = new Vector2(0f, 0f);
            statusText.rectTransform.anchorMax = new Vector2(1f, 0f);
            statusText.rectTransform.pivot = new Vector2(0.5f, 0f);
            statusText.rectTransform.anchoredPosition = new Vector2(0f, 120f);
            statusText.rectTransform.sizeDelta = new Vector2(-110f, 44f);
            statusText.alignment = TextAlignmentOptions.Center;
            statusText.color = new Color(0.76f, 0.92f, 1f, 1f);
            statusText.enableWordWrapping = true;
            statusText.gameObject.SetActive(false);

            GameObject buttonObject = CreateUiObject("PrimaryButton", panelObject.transform);
            Image buttonImage = buttonObject.AddComponent<Image>();
            buttonImage.color = new Color(1f, 0.43f, 0.25f, 1f);

            primaryButton = buttonObject.AddComponent<Button>();
            primaryButton.targetGraphic = buttonImage;
            primaryButton.onClick.AddListener(HandlePrimaryAction);

            RectTransform buttonRect = buttonObject.GetComponent<RectTransform>();
            buttonRect.anchorMin = new Vector2(0.5f, 0f);
            buttonRect.anchorMax = new Vector2(0.5f, 0f);
            buttonRect.pivot = new Vector2(0.5f, 0f);
            buttonRect.anchoredPosition = new Vector2(0f, 28f);
            buttonRect.sizeDelta = new Vector2(420f, 80f);

            primaryButtonLabel = CreateText(buttonObject.transform, "Label", 36, FontStyles.Bold);
            primaryButtonLabel.rectTransform.anchorMin = Vector2.zero;
            primaryButtonLabel.rectTransform.anchorMax = Vector2.one;
            primaryButtonLabel.rectTransform.offsetMin = Vector2.zero;
            primaryButtonLabel.rectTransform.offsetMax = Vector2.zero;
            primaryButtonLabel.alignment = TextAlignmentOptions.Center;
            primaryButtonLabel.color = new Color(1f, 1f, 1f, 1f);

            CreateChoiceButtons(panelObject.transform);
        }

        private void CreateChoiceButtons(Transform parent)
        {
            for (int index = 0; index < 4; index++)
            {
                GameObject choiceObject = CreateUiObject("ChoiceButton" + (index + 1), parent);
                Image choiceImage = choiceObject.AddComponent<Image>();
                choiceImage.color = new Color(0.1f, 0.26f, 0.34f, 0.96f);

                Button choiceButton = choiceObject.AddComponent<Button>();
                choiceButton.targetGraphic = choiceImage;

                int capturedIndex = index;
                choiceButton.onClick.AddListener(() => HandleChoiceButtonPressed(capturedIndex));

                RectTransform choiceRect = choiceObject.GetComponent<RectTransform>();
                choiceRect.anchorMin = new Vector2(0.5f, 0f);
                choiceRect.anchorMax = new Vector2(0.5f, 0f);
                choiceRect.pivot = new Vector2(0.5f, 0f);
                choiceRect.anchoredPosition = new Vector2(0f, 28f + index * 68f);
                choiceRect.sizeDelta = new Vector2(620f, 58f);

                TextMeshProUGUI choiceLabel = CreateText(choiceObject.transform, "Label", 28, FontStyles.Bold);
                choiceLabel.rectTransform.anchorMin = Vector2.zero;
                choiceLabel.rectTransform.anchorMax = Vector2.one;
                choiceLabel.rectTransform.offsetMin = new Vector2(20f, 0f);
                choiceLabel.rectTransform.offsetMax = new Vector2(-20f, 0f);
                choiceLabel.alignment = TextAlignmentOptions.Center;
                choiceLabel.enableWordWrapping = true;

                choiceButtons.Add(choiceButton);
                choiceButtonLabels.Add(choiceLabel);
                choiceObject.SetActive(false);
            }
        }

        private void SetPuzzleLayout(bool isPuzzle)
        {
            if (panelRect == null || bodyText == null || statusText == null)
                return;

            panelRect.sizeDelta = new Vector2(-56f, isPuzzle ? PuzzlePanelHeight : DefaultPanelHeight);
            bodyText.rectTransform.sizeDelta = new Vector2(-96f, isPuzzle ? 150f : 104f);
            statusText.rectTransform.anchoredPosition = new Vector2(0f, isPuzzle ? 326f : 120f);
            statusText.rectTransform.sizeDelta = new Vector2(-110f, isPuzzle ? 58f : 44f);
        }

        private static void EnsureEventSystem()
        {
            if (EventSystem.current != null)
                return;

            GameObject eventSystemObject = new GameObject("EventSystem", typeof(EventSystem), typeof(StandaloneInputModule));
            UnityEngine.Object.DontDestroyOnLoad(eventSystemObject);
        }

        private TextMeshProUGUI CreateText(Transform parent, string name, float fontSize, FontStyles fontStyle)
        {
            GameObject textObject = CreateUiObject(name, parent);
            TextMeshProUGUI text = textObject.AddComponent<TextMeshProUGUI>();
            text.fontSize = fontSize;
            text.fontStyle = fontStyle;
            text.color = Color.white;
            text.font = TMP_Settings.defaultFontAsset;
            return text;
        }

        private static GameObject CreateUiObject(string name, Transform parent)
        {
            GameObject uiObject = new GameObject(name, typeof(RectTransform));
            RectTransform rectTransform = uiObject.GetComponent<RectTransform>();
            rectTransform.SetParent(parent, false);
            rectTransform.localScale = Vector3.one;
            return uiObject;
        }

        private void AttachHotspotToTarget(RiftHuntTapHotspot hotspot, Transform targetTransform, Vector3 localPosition)
        {
            hotspot.transform.SetParent(targetTransform, false);
            hotspot.transform.localPosition = localPosition;
            OrientHotspotToCamera(hotspot);
        }

        private void OrientHotspotToCamera(RiftHuntTapHotspot hotspot)
        {
            if (hotspot == null || mainCamera == null)
                return;

            // Keep flat VFX portals screen-facing while their position follows the tracked image.
            hotspot.transform.rotation = mainCamera.transform.rotation;
        }

        private void CacheTargetPose(string targetId)
        {
            if (arProvider == null || string.IsNullOrWhiteSpace(targetId))
                return;

            Transform targetTransform = arProvider.GetTargetTransform(targetId);
            if (targetTransform == null)
                return;

            cachedTargetPosesById[targetId] = new CachedTargetPose
            {
                position = targetTransform.position,
                rotation = targetTransform.rotation
            };
        }

        private bool TryPositionHotspotFromCachedPose(RiftHuntTapHotspot hotspot, RiftHuntStepDefinition step)
        {
            if (!cachedTargetPosesById.TryGetValue(step.targetId, out CachedTargetPose cachedPose))
                return false;

            hotspot.transform.SetParent(transform, true);
            hotspot.transform.position = cachedPose.position + cachedPose.rotation * step.interactionLocalPosition;
            OrientHotspotToCamera(hotspot);
            return true;
        }

        private GameObject CreateInteractionVisual(RiftHuntStepDefinition step)
        {
            if (!string.IsNullOrWhiteSpace(step.interactionPrefabResourcePath))
            {
                GameObject interactionPrefab = Resources.Load<GameObject>(step.interactionPrefabResourcePath);
                if (interactionPrefab != null)
                    return Instantiate(interactionPrefab);

                Debug.LogWarning("[StoryMint] Could not load interaction prefab at Resources path '" + step.interactionPrefabResourcePath + "'. Falling back to sphere.");
            }

            if ((step.interactionType ?? string.Empty).Trim().ToLowerInvariant() == "tap")
                return CreateRiftAnchorVisual();

            GameObject hotspotObject = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            Renderer hotspotRenderer = hotspotObject.GetComponent<Renderer>();
            if (hotspotRenderer != null)
                hotspotRenderer.material = CreateInteractionMaterial("StoryMintFallbackHotspotMat", "StoryMint/Materials/mat-red", new Color(1f, 0.34f, 0.18f, 1f));

            return hotspotObject;
        }

        private GameObject CreateRiftAnchorVisual()
        {
            GameObject root = new GameObject("StoryMintRiftPortal");
            root.AddComponent<RiftPortalVfx>();
            return root;
        }

        private Transform CreatePrimitiveChild(Transform parent, PrimitiveType primitiveType, string name)
        {
            GameObject primitive = GameObject.CreatePrimitive(primitiveType);
            primitive.name = name;
            primitive.transform.SetParent(parent, false);

            Collider primitiveCollider = primitive.GetComponent<Collider>();
            if (primitiveCollider != null)
                Destroy(primitiveCollider);

            return primitive.transform;
        }

        private void CreateStaticShard(Transform parent, string name, Vector3 localPosition, Vector3 localScale, float zRotation, string materialResourcePath, Color color)
        {
            Transform shard = CreatePrimitiveChild(parent, PrimitiveType.Cube, name);
            shard.localPosition = localPosition;
            shard.localRotation = Quaternion.Euler(0f, 0f, zRotation);
            shard.localScale = localScale;
            SetRendererMaterial(shard, CreateInteractionMaterial("StoryMint" + name + "Mat", materialResourcePath, color));
        }

        private void CreateOrbitShard(Transform orbitRoot, string name, Vector3 localPosition, Vector3 localScale, string materialResourcePath, Color color)
        {
            Transform shard = CreatePrimitiveChild(orbitRoot, PrimitiveType.Cube, name);
            shard.localPosition = localPosition;
            shard.localRotation = Quaternion.Euler(25f, 0f, 35f);
            shard.localScale = localScale;
            SetRendererMaterial(shard, CreateInteractionMaterial("StoryMintRift" + name + "Mat", materialResourcePath, color));
        }

        private static void SetRendererMaterial(Transform targetTransform, Material material)
        {
            Renderer targetRenderer = targetTransform.GetComponent<Renderer>();
            if (targetRenderer != null)
            {
                targetRenderer.material = material;
                targetRenderer.shadowCastingMode = ShadowCastingMode.Off;
                targetRenderer.receiveShadows = false;
            }
        }

        private static void EnsureTapColliders(GameObject interactionObject)
        {
            if (interactionObject.GetComponentsInChildren<Collider>(true).Length > 0)
                return;

            Renderer[] renderers = interactionObject.GetComponentsInChildren<Renderer>(true);
            for (int index = 0; index < renderers.Length; index++)
            {
                Renderer renderer = renderers[index];
                if (renderer != null && renderer.GetComponent<Collider>() == null)
                    renderer.gameObject.AddComponent<BoxCollider>();
            }

            if (interactionObject.GetComponentsInChildren<Collider>(true).Length == 0)
                interactionObject.AddComponent<SphereCollider>();
        }

        private static Material CreateInteractionMaterial(string materialName, string materialResourcePath, Color fallbackColor)
        {
            Material resourceMaterial = string.IsNullOrWhiteSpace(materialResourcePath)
                ? null
                : Resources.Load<Material>(materialResourcePath);

            Material material = resourceMaterial != null
                ? new Material(resourceMaterial)
                : CreateDynamicInteractionMaterial(materialName);
            if (material == null)
                return null;

            material.name = materialName;
            ConfigureInteractionMaterial(material, fallbackColor);
            return material;
        }

        private static Material CreateDynamicInteractionMaterial(string materialName)
        {
            Material resourceMaterial = Resources.Load<Material>("StoryMint/Materials/mat-white");
            if (resourceMaterial != null)
            {
                return new Material(resourceMaterial)
                {
                    name = materialName
                };
            }

            Shader shader = Shader.Find("Universal Render Pipeline/Unlit");
            if (shader == null)
                shader = Shader.Find("Unlit/Color");
            if (shader == null)
                shader = Shader.Find("Standard");
            if (shader == null)
            {
                Debug.LogError("[StoryMint] Could not create interaction material '" + materialName + "' because no compatible shader was found.");
                return null;
            }

            Material material = new Material(shader)
            {
                name = materialName
            };

            return material;
        }

        private static void ConfigureInteractionMaterial(Material material, Color color)
        {
            if (material == null)
                return;

            if (material.HasProperty("_BaseColor"))
                material.SetColor("_BaseColor", color);
            if (material.HasProperty("_Color"))
                material.color = color;
            if (material.HasProperty("_EmissionColor"))
                material.SetColor("_EmissionColor", new Color(color.r * 2f, color.g * 2f, color.b * 2f, 1f));
            if (material.HasProperty("_Cull"))
                material.SetFloat("_Cull", 0f);

            material.EnableKeyword("_EMISSION");

        }

        private void SetPrimaryAction(string label, bool visible)
        {
            primaryButton.gameObject.SetActive(visible);
            if (visible)
                primaryButtonLabel.text = label;
        }

        private void SetStatus(string text, bool visible)
        {
            statusText.gameObject.SetActive(visible);
            if (visible)
                statusText.text = text;
        }

        private static float GetBottomSafeInset()
        {
            Rect safeArea = Screen.safeArea;
            return Mathf.Max(0f, safeArea.yMin);
        }

        private static float GetTopSafeInset()
        {
            Rect safeArea = Screen.safeArea;
            return Mathf.Max(0f, Screen.height - safeArea.yMax);
        }

        private static string BuildInteractionWaitingMessage(RiftHuntStepDefinition step)
        {
            string fallback = string.IsNullOrWhiteSpace(step.interactionPrefabResourcePath)
                ? "Halte das Motiv im Bild, dann erscheint der Punkt."
                : "Halte das Motiv im Bild, dann erscheint das Testmodell.";
            return step.GetInteractionWaitingMessage(fallback);
        }

        private static string BuildInteractionVisibleMessage(RiftHuntStepDefinition step)
        {
            string fallback = string.IsNullOrWhiteSpace(step.interactionPrefabResourcePath)
                ? "Punkt sichtbar. Tippe ihn an."
                : "Testmodell sichtbar. Tippe es an.";
            return step.GetInteractionVisibleMessage(fallback);
        }

        private static string BuildInteractionFallbackMessage(RiftHuntStepDefinition step)
        {
            string fallback = string.IsNullOrWhiteSpace(step.interactionPrefabResourcePath)
                ? "Tracking kurz verloren. Der Punkt bleibt an der letzten Position sichtbar."
                : "Tracking kurz verloren. Das Testmodell bleibt an der letzten Position sichtbar.";
            return step.GetInteractionFallbackMessage(fallback);
        }

        private static string BuildGeofenceWaitingMessage(RiftHuntStepDefinition step)
        {
            if (!string.IsNullOrWhiteSpace(step.locationName))
                return "Naehere dich dem Story-Ort: " + step.locationName + ".";

            return "Naehere dich dem Story-Ort. GPS wird gesucht.";
        }

        private static bool TryGetPrimaryPointerDown(out Vector2 pointerPosition, out int pointerId)
        {
            if (Input.touchCount > 0)
            {
                Touch firstTouch = Input.GetTouch(0);
                if (firstTouch.phase == TouchPhase.Began)
                {
                    pointerPosition = firstTouch.position;
                    pointerId = firstTouch.fingerId;
                    return true;
                }
            }

            if (Input.GetMouseButtonDown(0))
            {
                pointerPosition = Input.mousePosition;
                pointerId = -1;
                return true;
            }

            pointerPosition = default;
            pointerId = -1;
            return false;
        }

        private static bool IsPointerOverUi(int pointerId)
        {
            if (EventSystem.current == null)
                return false;

            return pointerId >= 0
                ? EventSystem.current.IsPointerOverGameObject(pointerId)
                : EventSystem.current.IsPointerOverGameObject();
        }

        // -------------------------------------------------------------------
        //  Minigame launchers
        // -------------------------------------------------------------------

        private void LaunchRuneSortMinigame(RiftHuntStepDefinition step)
        {
            List<string> sequence = step.runeSequence != null && step.runeSequence.Count >= 2
                ? step.runeSequence
                : new List<string> { "LUX", "AER", "TERRA", "UMBRA" };

            activeRuneSort = gameObject.AddComponent<RuneSortMinigame>();
            activeRuneSort.OnCompleted += HandleMinigameCompleted;
            activeRuneSort.OnStatusChanged += HandleMinigameStatus;
            activeRuneSort.Initialise(overlayCanvas, sequence);

            // Hide panel while minigame is active
            if (panelRect != null)
                panelRect.gameObject.SetActive(false);
        }

        private void LaunchSimonSaysMinigame(RiftHuntStepDefinition step)
        {
            int rounds = step.simonRounds > 0 ? step.simonRounds : 3;
            int pads = step.simonPadCount > 0 ? step.simonPadCount : 4;

            activeSimonSays = gameObject.AddComponent<SimonSaysMinigame>();
            activeSimonSays.OnCompleted += HandleMinigameCompleted;
            activeSimonSays.OnStatusChanged += HandleMinigameStatus;
            activeSimonSays.Initialise(overlayCanvas, rounds, pads);

            if (panelRect != null)
                panelRect.gameObject.SetActive(false);
        }

        private void LaunchGyroMazeMinigame(RiftHuntStepDefinition step)
        {
            activeGyroMaze = gameObject.AddComponent<GyroMazeMinigame>();
            activeGyroMaze.OnCompleted += HandleMinigameCompleted;
            activeGyroMaze.OnStatusChanged += HandleMinigameStatus;
            activeGyroMaze.Initialise(overlayCanvas, step.mazeLayout);

            if (panelRect != null)
                panelRect.gameObject.SetActive(false);
        }

        private void LaunchRuneCatcherMinigame(RiftHuntStepDefinition step)
        {
            List<string> sequence = step.runeSequence != null && step.runeSequence.Count >= 2
                ? step.runeSequence
                : new List<string> { "LUX", "AER", "TERRA", "UMBRA" };

            activeRuneCatcher = gameObject.AddComponent<RuneCatcherMinigame>();
            activeRuneCatcher.OnCompleted += HandleMinigameCompleted;
            activeRuneCatcher.OnStatusChanged += HandleMinigameStatus;
            activeRuneCatcher.Initialise(overlayCanvas, sequence);

            if (panelRect != null)
                panelRect.gameObject.SetActive(false);
        }

        private void LaunchSealTraceMinigame(RiftHuntStepDefinition step)
        {
            List<string> sequence = step.runeSequence != null && step.runeSequence.Count >= 2
                ? step.runeSequence
                : new List<string> { "LUX", "AER", "TERRA", "UMBRA" };

            activeSealTrace = gameObject.AddComponent<SealTraceMinigame>();
            activeSealTrace.OnCompleted += HandleMinigameCompleted;
            activeSealTrace.OnStatusChanged += HandleMinigameStatus;
            activeSealTrace.Initialise(overlayCanvas, sequence);

            if (panelRect != null)
                panelRect.gameObject.SetActive(false);
        }

        private void LaunchWhackARuneMinigame(RiftHuntStepDefinition step)
        {
            List<string> sequence = step.runeSequence != null && step.runeSequence.Count >= 2
                ? step.runeSequence
                : new List<string> { "LUX", "AER", "TERRA", "UMBRA" };

            activeWhackARune = gameObject.AddComponent<WhackARuneMinigame>();
            activeWhackARune.OnCompleted += HandleMinigameCompleted;
            activeWhackARune.OnStatusChanged += HandleMinigameStatus;
            activeWhackARune.Initialise(overlayCanvas, sequence);

            if (panelRect != null)
                panelRect.gameObject.SetActive(false);
        }

        private void LaunchRuneMemoryMinigame(RiftHuntStepDefinition step)
        {
            List<string> sequence = step.runeSequence != null && step.runeSequence.Count >= 2
                ? step.runeSequence
                : new List<string> { "LUX", "AER", "TERRA", "UMBRA" };

            activeRuneMemory = gameObject.AddComponent<RuneMemoryMinigame>();
            activeRuneMemory.OnCompleted += HandleMinigameCompleted;
            activeRuneMemory.OnStatusChanged += HandleMinigameStatus;
            activeRuneMemory.Initialise(overlayCanvas, sequence);

            if (panelRect != null)
                panelRect.gameObject.SetActive(false);
        }

        private void HandleMinigameCompleted()
        {
            CleanupActiveMinigame();

            if (panelRect != null)
                panelRect.gameObject.SetActive(true);

            string successText = string.IsNullOrWhiteSpace(currentStep.successMessage)
                ? "Spiel geschafft!"
                : currentStep.successMessage;
            SetStatus(successText, true);
            ScheduleAdvance(Mathf.Max(currentStep.autoAdvanceDelay, 1f));
        }

        private void HandleMinigameStatus(string status)
        {
            // Optionally forward to status text (not shown while minigame is fullscreen)
            Debug.Log("[StoryMint] Minigame: " + status);
        }

        private void CleanupActiveMinigame()
        {
            if (activeRuneSort != null)
            {
                activeRuneSort.OnCompleted -= HandleMinigameCompleted;
                activeRuneSort.OnStatusChanged -= HandleMinigameStatus;
                activeRuneSort.Cleanup();
                Destroy(activeRuneSort);
                activeRuneSort = null;
            }

            if (activeSimonSays != null)
            {
                activeSimonSays.OnCompleted -= HandleMinigameCompleted;
                activeSimonSays.OnStatusChanged -= HandleMinigameStatus;
                activeSimonSays.Cleanup();
                Destroy(activeSimonSays);
                activeSimonSays = null;
            }

            if (activeGyroMaze != null)
            {
                activeGyroMaze.OnCompleted -= HandleMinigameCompleted;
                activeGyroMaze.OnStatusChanged -= HandleMinigameStatus;
                activeGyroMaze.Cleanup();
                Destroy(activeGyroMaze);
                activeGyroMaze = null;
            }

            if (activeRuneCatcher != null)
            {
                activeRuneCatcher.OnCompleted -= HandleMinigameCompleted;
                activeRuneCatcher.OnStatusChanged -= HandleMinigameStatus;
                activeRuneCatcher.Cleanup();
                Destroy(activeRuneCatcher);
                activeRuneCatcher = null;
            }

            if (activeSealTrace != null)
            {
                activeSealTrace.OnCompleted -= HandleMinigameCompleted;
                activeSealTrace.OnStatusChanged -= HandleMinigameStatus;
                activeSealTrace.Cleanup();
                Destroy(activeSealTrace);
                activeSealTrace = null;
            }

            if (activeWhackARune != null)
            {
                activeWhackARune.OnCompleted -= HandleMinigameCompleted;
                activeWhackARune.OnStatusChanged -= HandleMinigameStatus;
                activeWhackARune.Cleanup();
                Destroy(activeWhackARune);
                activeWhackARune = null;
            }

            if (activeRuneMemory != null)
            {
                activeRuneMemory.OnCompleted -= HandleMinigameCompleted;
                activeRuneMemory.OnStatusChanged -= HandleMinigameStatus;
                activeRuneMemory.Cleanup();
                Destroy(activeRuneMemory);
                activeRuneMemory = null;
            }
        }
    }
}
