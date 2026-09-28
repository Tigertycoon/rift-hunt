using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

namespace StoryMint.RiftHunt.Minigames
{
    /// <summary>
    /// Runen-Fang – Runes fall from the top of the screen.
    /// The player must tap the correct rune (next in the ritual sequence)
    /// before it reaches the bottom. Wrong tap or missed rune = reset sequence.
    /// Speed increases each time a rune is caught correctly.
    /// </summary>
    public sealed class RuneCatcherMinigame : MonoBehaviour
    {
        public event Action OnCompleted;
        public event Action<string> OnStatusChanged;

        private List<string> correctSequence;
        private int currentTarget;
        private bool isCompleted;
        private bool isRunning;

        private RectTransform container;
        private TextMeshProUGUI titleText;
        private TextMeshProUGUI instructionText;
        private TextMeshProUGUI progressText;
        private TextMeshProUGUI nextRuneText;
        private RectTransform catchZone;

        // Falling runes
        private readonly List<FallingRune> activeRunes = new List<FallingRune>();
        private float spawnTimer;
        private float spawnInterval = 1.4f;
        private float fallSpeed = 320f;
        private float containerHeight;
        private float containerWidth;
        private int missCount;

        // Speed ramp
        private const float MinSpawnInterval = 0.5f;
        private const float MaxFallSpeed = 700f;
        private const float SpeedUpPerCatch = 0.12f;
        private const float IntervalReduction = 0.15f;

        private static readonly Color BgColor       = new Color(0.03f, 0.04f, 0.09f, 0.95f);
        private static readonly Color RuneNormal    = new Color(0.12f, 0.50f, 0.85f, 1f);
        private static readonly Color RuneTarget    = new Color(0.18f, 0.82f, 0.45f, 1f);
        private static readonly Color RuneWrong     = new Color(0.92f, 0.18f, 0.14f, 1f);
        private static readonly Color CatchZoneColor= new Color(0.85f, 0.15f, 0.10f, 0.15f);

        private class FallingRune
        {
            public RectTransform rect;
            public Image image;
            public Button button;
            public TextMeshProUGUI label;
            public string runeName;
            public bool caught;
        }

        // Rune pool of all possible names (shuffled for spawning)
        private readonly List<string> allRuneNames = new List<string>();

        // ---------------------------------------------------------------
        //  Public API
        // ---------------------------------------------------------------

        public void Initialise(Canvas parentCanvas, List<string> sequence)
        {
            if (sequence == null || sequence.Count < 2)
            {
                Debug.LogError("[RuneCatcher] Need at least 2 runes.");
                return;
            }

            correctSequence = new List<string>(sequence);
            currentTarget = 0;
            isCompleted = false;
            isRunning = false;
            missCount = 0;
            spawnTimer = 0f;
            spawnInterval = 1.4f;
            fallSpeed = 320f;

            // Build rune pool: all sequence runes + some distractors
            allRuneNames.Clear();
            allRuneNames.AddRange(correctSequence);
            AddDistractorIfMissing("IGNIS");
            AddDistractorIfMissing("AQUA");
            AddDistractorIfMissing("VITA");
            AddDistractorIfMissing("NOX");

            BuildUI(parentCanvas);
            UpdateProgress();
            UpdateNextRune();
            SetInstruction("Fange die Runen in der richtigen Reihenfolge!");

            isRunning = true;
        }

        private void AddDistractorIfMissing(string name)
        {
            for (int i = 0; i < allRuneNames.Count; i++)
            {
                if (string.Equals(allRuneNames[i], name, StringComparison.OrdinalIgnoreCase))
                    return;
            }
            allRuneNames.Add(name);
        }

        public void Cleanup()
        {
            isRunning = false;
            StopAllCoroutines();
            if (container != null)
                Destroy(container.gameObject);
        }

        // ---------------------------------------------------------------
        //  Update
        // ---------------------------------------------------------------

        private void Update()
        {
            if (!isRunning || isCompleted) return;

            float dt = Time.deltaTime;

            // Spawn new runes
            spawnTimer += dt;
            if (spawnTimer >= spawnInterval)
            {
                spawnTimer = 0f;
                SpawnRune();
            }

            // Move runes down, check for misses
            for (int i = activeRunes.Count - 1; i >= 0; i--)
            {
                FallingRune r = activeRunes[i];
                if (r.caught) continue;

                Vector2 pos = r.rect.anchoredPosition;
                pos.y -= fallSpeed * dt;
                r.rect.anchoredPosition = pos;

                // Fell off bottom?
                if (pos.y < -containerHeight / 2f - 80f)
                {
                    // If this was the target rune, that's a miss
                    if (string.Equals(r.runeName, correctSequence[currentTarget], StringComparison.OrdinalIgnoreCase))
                    {
                        HandleMiss();
                    }
                    DestroyRune(r);
                    activeRunes.RemoveAt(i);
                }
            }
        }

        // ---------------------------------------------------------------
        //  Game Logic
        // ---------------------------------------------------------------

        private void SpawnRune()
        {
            // Decide what rune to spawn — bias toward target rune
            string runeName;
            float targetChance = 0.4f;
            if (UnityEngine.Random.value < targetChance && currentTarget < correctSequence.Count)
            {
                runeName = correctSequence[currentTarget];
            }
            else
            {
                runeName = allRuneNames[UnityEngine.Random.Range(0, allRuneNames.Count)];
            }

            // Random horizontal position
            float margin = 80f;
            float xMin = -containerWidth / 2f + margin;
            float xMax = containerWidth / 2f - margin;
            float x = UnityEngine.Random.Range(xMin, xMax);
            float y = containerHeight / 2f + 60f;

            // Create rune UI
            float size = 90f;
            GameObject runeObj = new GameObject("FallingRune", typeof(RectTransform), typeof(Image), typeof(Button));
            RectTransform runeRect = runeObj.GetComponent<RectTransform>();
            runeRect.SetParent(container, false);
            runeRect.anchorMin = new Vector2(0.5f, 0.5f);
            runeRect.anchorMax = new Vector2(0.5f, 0.5f);
            runeRect.pivot = new Vector2(0.5f, 0.5f);
            runeRect.anchoredPosition = new Vector2(x, y);
            runeRect.sizeDelta = new Vector2(size, size);

            Image runeImg = runeObj.GetComponent<Image>();
            bool isTarget = string.Equals(runeName, correctSequence[currentTarget], StringComparison.OrdinalIgnoreCase);
            runeImg.color = RuneNormal;

            Button runeBtn = runeObj.GetComponent<Button>();
            runeBtn.targetGraphic = runeImg;

            TextMeshProUGUI label = CreateText(runeRect, "Label", 26, FontStyles.Bold);
            label.rectTransform.anchorMin = Vector2.zero;
            label.rectTransform.anchorMax = Vector2.one;
            label.rectTransform.offsetMin = Vector2.zero;
            label.rectTransform.offsetMax = Vector2.zero;
            label.alignment = TextAlignmentOptions.Center;
            label.text = runeName;
            label.color = Color.white;

            FallingRune fr = new FallingRune
            {
                rect = runeRect,
                image = runeImg,
                button = runeBtn,
                label = label,
                runeName = runeName,
                caught = false
            };

            runeBtn.onClick.AddListener(() => HandleRuneTapped(fr));
            activeRunes.Add(fr);
        }

        private void HandleRuneTapped(FallingRune rune)
        {
            if (isCompleted || rune.caught) return;

            string expected = correctSequence[currentTarget];
            if (string.Equals(rune.runeName, expected, StringComparison.OrdinalIgnoreCase))
            {
                // Correct!
                rune.caught = true;
                rune.image.color = RuneTarget;
                rune.button.interactable = false;
                currentTarget++;

                // Speed up
                fallSpeed = Mathf.Min(MaxFallSpeed, fallSpeed + fallSpeed * SpeedUpPerCatch);
                spawnInterval = Mathf.Max(MinSpawnInterval, spawnInterval - IntervalReduction);

                UpdateProgress();
                UpdateNextRune();
                OnStatusChanged?.Invoke(rune.runeName + " gefangen!");
                StoryMintSFX.Catch();

                StartCoroutine(FadeOutRune(rune));

                if (currentTarget >= correctSequence.Count)
                {
                    isCompleted = true;
                    isRunning = false;
                    SetInstruction("Alle Runen gefangen! Ritual vollendet!");
                    StoryMintSFX.Victory();
                    StartCoroutine(DelayedComplete());
                }
                else
                {
                    int remaining = correctSequence.Count - currentTarget;
                    SetInstruction("Richtig! Noch " + remaining + " Rune" + (remaining == 1 ? "" : "n") + ".");
                }
            }
            else
            {
                // Wrong rune tapped
                rune.image.color = RuneWrong;
                StoryMintSFX.Error();
                StartCoroutine(FlashWrongAndReset(rune));
            }
        }

        private void HandleMiss()
        {
            missCount++;
            OnStatusChanged?.Invoke("Rune verpasst!");
            StoryMintSFX.Reset();
            StartCoroutine(ResetSequence("Rune verpasst! Von vorne..."));
        }

        private IEnumerator FlashWrongAndReset(FallingRune rune)
        {
            isRunning = false;
            yield return new WaitForSeconds(0.4f);
            yield return ResetSequence("Falsche Rune! Von vorne...");
        }

        private IEnumerator ResetSequence(string message)
        {
            isRunning = false;
            SetInstruction(message);

            yield return new WaitForSeconds(0.8f);

            // Clear all active runes
            for (int i = activeRunes.Count - 1; i >= 0; i--)
            {
                DestroyRune(activeRunes[i]);
            }
            activeRunes.Clear();

            currentTarget = 0;
            spawnTimer = 0f;
            // Don't reset speed — keep the challenge

            UpdateProgress();
            UpdateNextRune();
            SetInstruction("Fange die Runen in der richtigen Reihenfolge!");
            isRunning = true;
        }

        private IEnumerator FadeOutRune(FallingRune rune)
        {
            float elapsed = 0f;
            float duration = 0.4f;
            Vector2 startPos = rune.rect.anchoredPosition;
            while (elapsed < duration)
            {
                elapsed += Time.deltaTime;
                float t = elapsed / duration;
                rune.rect.anchoredPosition = startPos + new Vector2(0f, 60f * t);
                rune.rect.localScale = Vector3.one * (1f - t * 0.5f);
                Color c = rune.image.color;
                c.a = 1f - t;
                rune.image.color = c;
                rune.label.alpha = 1f - t;
                yield return null;
            }
            DestroyRune(rune);
            activeRunes.Remove(rune);
        }

        private void DestroyRune(FallingRune rune)
        {
            if (rune.rect != null)
                Destroy(rune.rect.gameObject);
        }

        // ---------------------------------------------------------------
        //  UI Construction
        // ---------------------------------------------------------------

        private void BuildUI(Canvas parentCanvas)
        {
            GameObject containerObj = new GameObject("RuneCatcherContainer", typeof(RectTransform), typeof(Image));
            container = containerObj.GetComponent<RectTransform>();
            container.SetParent(parentCanvas.transform, false);
            container.anchorMin = Vector2.zero;
            container.anchorMax = Vector2.one;
            container.offsetMin = Vector2.zero;
            container.offsetMax = Vector2.zero;

            Image bg = containerObj.GetComponent<Image>();
            bg.color = BgColor;
            bg.raycastTarget = true;

            // Cache size (reference resolution based)
            containerWidth = 1080f;
            containerHeight = 1920f;

            // Title
            titleText = CreateText(container, "Title", 40, FontStyles.Bold);
            titleText.rectTransform.anchorMin = new Vector2(0f, 1f);
            titleText.rectTransform.anchorMax = new Vector2(1f, 1f);
            titleText.rectTransform.pivot = new Vector2(0.5f, 1f);
            titleText.rectTransform.anchoredPosition = new Vector2(0f, -44f);
            titleText.rectTransform.sizeDelta = new Vector2(-80f, 52f);
            titleText.alignment = TextAlignmentOptions.Center;
            titleText.text = "RUNEN-FANG";
            titleText.color = new Color(0.65f, 0.85f, 1f, 0.92f);

            // Next rune indicator
            nextRuneText = CreateText(container, "NextRune", 34, FontStyles.Bold);
            nextRuneText.rectTransform.anchorMin = new Vector2(0f, 1f);
            nextRuneText.rectTransform.anchorMax = new Vector2(1f, 1f);
            nextRuneText.rectTransform.pivot = new Vector2(0.5f, 1f);
            nextRuneText.rectTransform.anchoredPosition = new Vector2(0f, -100f);
            nextRuneText.rectTransform.sizeDelta = new Vector2(-80f, 46f);
            nextRuneText.alignment = TextAlignmentOptions.Center;
            nextRuneText.color = new Color(0.18f, 0.85f, 0.45f, 1f);

            // Instruction
            instructionText = CreateText(container, "Instruction", 28, FontStyles.Normal);
            instructionText.rectTransform.anchorMin = new Vector2(0f, 1f);
            instructionText.rectTransform.anchorMax = new Vector2(1f, 1f);
            instructionText.rectTransform.pivot = new Vector2(0.5f, 1f);
            instructionText.rectTransform.anchoredPosition = new Vector2(0f, -150f);
            instructionText.rectTransform.sizeDelta = new Vector2(-60f, 42f);
            instructionText.alignment = TextAlignmentOptions.Center;
            instructionText.enableWordWrapping = true;
            instructionText.color = new Color(0.8f, 0.88f, 1f, 0.8f);

            // Progress
            progressText = CreateText(container, "Progress", 24, FontStyles.Normal);
            progressText.rectTransform.anchorMin = new Vector2(0f, 0f);
            progressText.rectTransform.anchorMax = new Vector2(1f, 0f);
            progressText.rectTransform.pivot = new Vector2(0.5f, 0f);
            progressText.rectTransform.anchoredPosition = new Vector2(0f, 30f);
            progressText.rectTransform.sizeDelta = new Vector2(-60f, 36f);
            progressText.alignment = TextAlignmentOptions.Center;
            progressText.color = new Color(0.6f, 0.75f, 0.95f, 0.6f);

            // Danger zone at bottom
            GameObject czObj = new GameObject("CatchZone", typeof(RectTransform), typeof(Image));
            RectTransform czRect = czObj.GetComponent<RectTransform>();
            czRect.SetParent(container, false);
            czRect.anchorMin = new Vector2(0f, 0f);
            czRect.anchorMax = new Vector2(1f, 0f);
            czRect.pivot = new Vector2(0.5f, 0f);
            czRect.anchoredPosition = new Vector2(0f, 70f);
            czRect.sizeDelta = new Vector2(0f, 6f);
            Image czImg = czObj.GetComponent<Image>();
            czImg.color = new Color(0.85f, 0.15f, 0.10f, 0.4f);
            czImg.raycastTarget = false;
        }

        private void UpdateProgress()
        {
            if (progressText != null)
                progressText.text = currentTarget + " / " + correctSequence.Count + " gefangen" +
                    (missCount > 0 ? "  |  Verpasst: " + missCount : "");
        }

        private void UpdateNextRune()
        {
            if (nextRuneText == null) return;
            if (currentTarget < correctSequence.Count)
                nextRuneText.text = "Naechste: " + correctSequence[currentTarget];
            else
                nextRuneText.text = "";
        }

        private void SetInstruction(string text)
        {
            if (instructionText != null)
                instructionText.text = text;
        }

        private IEnumerator DelayedComplete()
        {
            yield return new WaitForSeconds(1.0f);
            OnCompleted?.Invoke();
        }

        private static TextMeshProUGUI CreateText(RectTransform parent, string name, float fontSize, FontStyles style)
        {
            GameObject textObj = new GameObject(name, typeof(RectTransform));
            textObj.transform.SetParent(parent, false);
            TextMeshProUGUI tmp = textObj.AddComponent<TextMeshProUGUI>();
            tmp.fontSize = fontSize;
            tmp.fontStyle = style;
            tmp.color = Color.white;
            tmp.font = TMP_Settings.defaultFontAsset;
            return tmp;
        }
    }
}
