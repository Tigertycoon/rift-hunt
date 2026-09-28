using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

namespace StoryMint.RiftHunt.Minigames
{
    /// <summary>
    /// Runen-Jagd (Whack-a-Rune) – 6 slots on screen.
    /// Runes pop up randomly and disappear after a short window.
    /// Player must tap the correct rune (next in ritual sequence).
    /// Wrong tap = time penalty. Missed target = sequence resets.
    /// Pop-up duration shrinks as you progress.
    /// </summary>
    public sealed class WhackARuneMinigame : MonoBehaviour
    {
        public event Action OnCompleted;
        public event Action<string> OnStatusChanged;

        private List<string> correctSequence;
        private int currentTarget;
        private bool isCompleted;
        private bool isRunning;
        private int mistakeCount;

        private RectTransform container;
        private TextMeshProUGUI titleText;
        private TextMeshProUGUI instructionText;
        private TextMeshProUGUI progressText;
        private TextMeshProUGUI nextRuneText;

        // Slots
        private const int SlotCount = 6;
        private readonly RuneSlot[] slots = new RuneSlot[SlotCount];

        // Timing
        private float popupDuration = 1.8f;
        private float spawnInterval = 0.9f;
        private float spawnTimer;
        private float penaltyTimer; // blocks spawning during penalty

        private const float MinPopupDuration = 0.7f;
        private const float MinSpawnInterval = 0.45f;
        private const float SpeedUp = 0.08f;

        // All rune names for spawning
        private readonly List<string> allRuneNames = new List<string>();

        private static readonly Color BgColor       = new Color(0.03f, 0.04f, 0.09f, 0.95f);
        private static readonly Color SlotEmpty     = new Color(0.08f, 0.10f, 0.16f, 0.6f);
        private static readonly Color SlotActive    = new Color(0.12f, 0.50f, 0.85f, 1f);
        private static readonly Color SlotTarget    = new Color(0.18f, 0.82f, 0.45f, 1f);
        private static readonly Color SlotWrong     = new Color(0.92f, 0.18f, 0.14f, 1f);
        private static readonly Color SlotGlowColor = new Color(0.25f, 0.55f, 0.85f, 0.25f);

        private class RuneSlot
        {
            public RectTransform rect;
            public Image image;
            public Image glowImage;
            public Button button;
            public TextMeshProUGUI label;
            public string currentRune;
            public bool isActive;
            public float activeTimer;
            public float activeDuration;
        }

        // ---------------------------------------------------------------
        //  Public API
        // ---------------------------------------------------------------

        public void Initialise(Canvas parentCanvas, List<string> sequence)
        {
            if (sequence == null || sequence.Count < 2)
            {
                Debug.LogError("[WhackARune] Need at least 2 runes.");
                return;
            }

            correctSequence = new List<string>(sequence);
            currentTarget = 0;
            isCompleted = false;
            isRunning = false;
            mistakeCount = 0;
            spawnTimer = 0f;
            penaltyTimer = 0f;
            popupDuration = 1.8f;
            spawnInterval = 0.9f;

            allRuneNames.Clear();
            allRuneNames.AddRange(correctSequence);
            AddDistractorIfMissing("IGNIS");
            AddDistractorIfMissing("AQUA");
            AddDistractorIfMissing("VITA");
            AddDistractorIfMissing("NOX");

            BuildUI(parentCanvas);
            UpdateProgress();
            UpdateNextRune();
            SetInstruction("Erwische die richtige Rune wenn sie aufpoppt!");

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

            // Penalty cooldown
            if (penaltyTimer > 0f)
            {
                penaltyTimer -= dt;
                return;
            }

            // Update active slots
            for (int i = 0; i < SlotCount; i++)
            {
                RuneSlot slot = slots[i];
                if (!slot.isActive) continue;

                slot.activeTimer += dt;

                // Fade pulse
                float life = slot.activeTimer / slot.activeDuration;
                float alpha = life < 0.1f ? life * 10f : (life > 0.7f ? (1f - life) / 0.3f : 1f);
                Color c = slot.image.color;
                c.a = alpha;
                slot.image.color = c;
                slot.label.alpha = alpha;

                // Expired?
                if (slot.activeTimer >= slot.activeDuration)
                {
                    bool wasTarget = string.Equals(slot.currentRune,
                        correctSequence[currentTarget], StringComparison.OrdinalIgnoreCase);

                    DeactivateSlot(i);

                    if (wasTarget)
                    {
                        // Missed the target rune
                        mistakeCount++;
                        OnStatusChanged?.Invoke("Rune verpasst!");
                        StoryMintSFX.Reset();
                        StartCoroutine(ResetAfterMiss());
                        return;
                    }
                }
            }

            // Spawn
            spawnTimer += dt;
            if (spawnTimer >= spawnInterval)
            {
                spawnTimer = 0f;
                TrySpawnRune();
            }
        }

        // ---------------------------------------------------------------
        //  Game Logic
        // ---------------------------------------------------------------

        private void TrySpawnRune()
        {
            // Find an empty slot
            List<int> emptySlots = new List<int>();
            for (int i = 0; i < SlotCount; i++)
            {
                if (!slots[i].isActive)
                    emptySlots.Add(i);
            }

            if (emptySlots.Count == 0) return;

            int slotIdx = emptySlots[UnityEngine.Random.Range(0, emptySlots.Count)];

            // Decide rune — bias toward target
            string runeName;
            if (UnityEngine.Random.value < 0.45f)
                runeName = correctSequence[currentTarget];
            else
                runeName = allRuneNames[UnityEngine.Random.Range(0, allRuneNames.Count)];

            ActivateSlot(slotIdx, runeName);
            StoryMintSFX.PopUp();
        }

        private void ActivateSlot(int idx, string runeName)
        {
            RuneSlot slot = slots[idx];
            slot.currentRune = runeName;
            slot.isActive = true;
            slot.activeTimer = 0f;
            slot.activeDuration = popupDuration;
            slot.image.color = SlotActive;
            slot.glowImage.color = SlotGlowColor;
            slot.label.text = runeName;
            slot.label.alpha = 1f;
            slot.button.interactable = true;
        }

        private void DeactivateSlot(int idx)
        {
            RuneSlot slot = slots[idx];
            slot.isActive = false;
            slot.currentRune = null;
            slot.image.color = SlotEmpty;
            slot.glowImage.color = new Color(0f, 0f, 0f, 0f);
            slot.label.text = "";
            slot.button.interactable = false;
        }

        private void HandleSlotTapped(int idx)
        {
            if (!isRunning || isCompleted) return;

            RuneSlot slot = slots[idx];
            if (!slot.isActive) return;

            string expected = correctSequence[currentTarget];

            if (string.Equals(slot.currentRune, expected, StringComparison.OrdinalIgnoreCase))
            {
                // Correct!
                slot.image.color = SlotTarget;
                slot.glowImage.color = new Color(0.18f, 0.85f, 0.45f, 0.5f);
                slot.button.interactable = false;
                currentTarget++;

                // Speed up
                popupDuration = Mathf.Max(MinPopupDuration, popupDuration - SpeedUp);
                spawnInterval = Mathf.Max(MinSpawnInterval, spawnInterval - SpeedUp * 0.5f);

                UpdateProgress();
                UpdateNextRune();
                OnStatusChanged?.Invoke(slot.currentRune + " erwischt!");
                StoryMintSFX.Catch();

                StartCoroutine(FadeSlotOut(idx));

                if (currentTarget >= correctSequence.Count)
                {
                    isCompleted = true;
                    isRunning = false;
                    SetInstruction("Alle Runen erwischt! Ritual vollendet!");
                    StoryMintSFX.Victory();
                    StartCoroutine(VictoryFlash());
                }
                else
                {
                    int remaining = correctSequence.Count - currentTarget;
                    SetInstruction("Richtig! Noch " + remaining + ".");
                }
            }
            else
            {
                // Wrong!
                mistakeCount++;
                slot.image.color = SlotWrong;
                OnStatusChanged?.Invoke("Falsche Rune!");
                StoryMintSFX.Penalty();
                StartCoroutine(PenaltyFlash(idx));
            }
        }

        private IEnumerator FadeSlotOut(int idx)
        {
            yield return new WaitForSeconds(0.3f);
            DeactivateSlot(idx);
        }

        private IEnumerator PenaltyFlash(int idx)
        {
            isRunning = false;
            SetInstruction("Falsche Rune! Kurze Strafe...");

            yield return new WaitForSeconds(0.5f);
            DeactivateSlot(idx);

            penaltyTimer = 0.8f;
            isRunning = true;
            SetInstruction("Erwische die richtige Rune wenn sie aufpoppt!");
        }

        private IEnumerator ResetAfterMiss()
        {
            isRunning = false;
            SetInstruction("Rune verpasst! Sequenz startet neu...");

            // Deactivate all
            for (int i = 0; i < SlotCount; i++)
                DeactivateSlot(i);

            yield return new WaitForSeconds(1.0f);

            currentTarget = 0;
            UpdateProgress();
            UpdateNextRune();
            spawnTimer = 0f;
            isRunning = true;
            SetInstruction("Erwische die richtige Rune wenn sie aufpoppt!");
        }

        private IEnumerator VictoryFlash()
        {
            // Flash all slots green
            for (int r = 0; r < 2; r++)
            {
                for (int i = 0; i < SlotCount; i++)
                {
                    slots[i].image.color = SlotTarget;
                    slots[i].glowImage.color = new Color(0.18f, 0.85f, 0.45f, 0.5f);
                    slots[i].label.text = "";
                }
                yield return new WaitForSeconds(0.2f);
                for (int i = 0; i < SlotCount; i++)
                {
                    slots[i].image.color = SlotEmpty;
                    slots[i].glowImage.color = new Color(0f, 0f, 0f, 0f);
                }
                yield return new WaitForSeconds(0.15f);
            }

            yield return new WaitForSeconds(0.4f);
            OnCompleted?.Invoke();
        }

        // ---------------------------------------------------------------
        //  UI
        // ---------------------------------------------------------------

        private void BuildUI(Canvas parentCanvas)
        {
            GameObject containerObj = new GameObject("WhackARuneContainer", typeof(RectTransform), typeof(Image));
            container = containerObj.GetComponent<RectTransform>();
            container.SetParent(parentCanvas.transform, false);
            container.anchorMin = Vector2.zero;
            container.anchorMax = Vector2.one;
            container.offsetMin = Vector2.zero;
            container.offsetMax = Vector2.zero;

            Image bg = containerObj.GetComponent<Image>();
            bg.color = BgColor;
            bg.raycastTarget = true;

            // Title
            titleText = CreateText(container, "Title", 42, FontStyles.Bold);
            titleText.rectTransform.anchorMin = new Vector2(0f, 1f);
            titleText.rectTransform.anchorMax = new Vector2(1f, 1f);
            titleText.rectTransform.pivot = new Vector2(0.5f, 1f);
            titleText.rectTransform.anchoredPosition = new Vector2(0f, -44f);
            titleText.rectTransform.sizeDelta = new Vector2(-80f, 56f);
            titleText.alignment = TextAlignmentOptions.Center;
            titleText.text = "RUNEN-JAGD";
            titleText.color = new Color(0.65f, 0.85f, 1f, 0.92f);

            // Next rune indicator
            nextRuneText = CreateText(container, "NextRune", 34, FontStyles.Bold);
            nextRuneText.rectTransform.anchorMin = new Vector2(0f, 1f);
            nextRuneText.rectTransform.anchorMax = new Vector2(1f, 1f);
            nextRuneText.rectTransform.pivot = new Vector2(0.5f, 1f);
            nextRuneText.rectTransform.anchoredPosition = new Vector2(0f, -104f);
            nextRuneText.rectTransform.sizeDelta = new Vector2(-80f, 46f);
            nextRuneText.alignment = TextAlignmentOptions.Center;
            nextRuneText.color = new Color(0.18f, 0.85f, 0.45f, 1f);

            // Instruction
            instructionText = CreateText(container, "Instruction", 28, FontStyles.Normal);
            instructionText.rectTransform.anchorMin = new Vector2(0f, 1f);
            instructionText.rectTransform.anchorMax = new Vector2(1f, 1f);
            instructionText.rectTransform.pivot = new Vector2(0.5f, 1f);
            instructionText.rectTransform.anchoredPosition = new Vector2(0f, -155f);
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

            // Create 6 slots in a 3x2 grid
            float slotSize = 160f;
            float gapX = 24f;
            float gapY = 28f;
            float gridWidth = 3f * slotSize + 2f * gapX;
            float gridHeight = 2f * slotSize + gapY;
            float startX = -gridWidth / 2f + slotSize / 2f;
            float startY = gridHeight / 2f - slotSize / 2f;

            for (int i = 0; i < SlotCount; i++)
            {
                int col = i % 3;
                int row = i / 3;
                float x = startX + col * (slotSize + gapX);
                float y = startY - row * (slotSize + gapY);

                // Glow
                GameObject glowObj = new GameObject("SlotGlow" + i, typeof(RectTransform), typeof(Image));
                RectTransform glowRect = glowObj.GetComponent<RectTransform>();
                glowRect.SetParent(container, false);
                glowRect.anchorMin = new Vector2(0.5f, 0.5f);
                glowRect.anchorMax = new Vector2(0.5f, 0.5f);
                glowRect.pivot = new Vector2(0.5f, 0.5f);
                glowRect.anchoredPosition = new Vector2(x, y - 60f);
                glowRect.sizeDelta = new Vector2(slotSize + 14f, slotSize + 14f);
                Image glowImg = glowObj.GetComponent<Image>();
                glowImg.color = new Color(0f, 0f, 0f, 0f);
                glowImg.raycastTarget = false;

                // Slot
                GameObject slotObj = new GameObject("Slot" + i, typeof(RectTransform), typeof(Image), typeof(Button));
                RectTransform slotRect = slotObj.GetComponent<RectTransform>();
                slotRect.SetParent(container, false);
                slotRect.anchorMin = new Vector2(0.5f, 0.5f);
                slotRect.anchorMax = new Vector2(0.5f, 0.5f);
                slotRect.pivot = new Vector2(0.5f, 0.5f);
                slotRect.anchoredPosition = new Vector2(x, y - 60f);
                slotRect.sizeDelta = new Vector2(slotSize, slotSize);

                Image slotImg = slotObj.GetComponent<Image>();
                slotImg.color = SlotEmpty;

                Button slotBtn = slotObj.GetComponent<Button>();
                slotBtn.targetGraphic = slotImg;
                slotBtn.interactable = false;

                TextMeshProUGUI label = CreateText(slotRect, "Label", 30, FontStyles.Bold);
                label.rectTransform.anchorMin = Vector2.zero;
                label.rectTransform.anchorMax = Vector2.one;
                label.rectTransform.offsetMin = Vector2.zero;
                label.rectTransform.offsetMax = Vector2.zero;
                label.alignment = TextAlignmentOptions.Center;
                label.text = "";
                label.color = Color.white;

                int capturedIdx = i;
                slotBtn.onClick.AddListener(() => HandleSlotTapped(capturedIdx));

                slots[i] = new RuneSlot
                {
                    rect = slotRect,
                    image = slotImg,
                    glowImage = glowImg,
                    button = slotBtn,
                    label = label,
                    currentRune = null,
                    isActive = false,
                    activeTimer = 0f,
                    activeDuration = 0f
                };
            }
        }

        private void UpdateProgress()
        {
            if (progressText != null)
                progressText.text = currentTarget + " / " + correctSequence.Count + " erwischt" +
                    (mistakeCount > 0 ? "  |  Fehler: " + mistakeCount : "");
        }

        private void UpdateNextRune()
        {
            if (nextRuneText == null) return;
            if (currentTarget < correctSequence.Count)
                nextRuneText.text = "Suche: " + correctSequence[currentTarget];
            else
                nextRuneText.text = "";
        }

        private void SetInstruction(string text)
        {
            if (instructionText != null)
                instructionText.text = text;
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
