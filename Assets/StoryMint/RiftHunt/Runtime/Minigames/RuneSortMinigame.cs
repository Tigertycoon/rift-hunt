using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

namespace StoryMint.RiftHunt.Minigames
{
    /// <summary>
    /// Spiel 1 – Runen-Beschwörung.
    /// Large glowing rune orbs float on screen; player must tap them in the
    /// correct ritual order.  Correct taps trigger a satisfying seal animation,
    /// wrong taps shake the screen and reset.  Visual style: dark occult
    /// background, pulsing neon orbs, glowing slot ring at the top.
    /// </summary>
    public sealed class RuneSortMinigame : MonoBehaviour
    {
        public event Action OnCompleted;
        public event Action<string> OnStatusChanged;

        private List<string> correctSequence;
        private readonly List<string> playerInput = new List<string>();
        private readonly List<Button> orbButtons = new List<Button>();
        private readonly List<Image> orbImages = new List<Image>();
        private readonly List<Image> orbGlowImages = new List<Image>();
        private readonly List<TextMeshProUGUI> orbLabels = new List<TextMeshProUGUI>();
        private readonly List<Image> slotImages = new List<Image>();
        private readonly List<TextMeshProUGUI> slotLabels = new List<TextMeshProUGUI>();
        private readonly List<Image> slotGlowImages = new List<Image>();
        private RectTransform container;
        private TextMeshProUGUI titleText;
        private TextMeshProUGUI instructionText;
        private TextMeshProUGUI counterText;
        private bool isCompleted;

        // ---------------------------------------------------------------
        //  Colors — dark ritual theme
        // ---------------------------------------------------------------

        private static readonly Color BgColor       = new Color(0.03f, 0.04f, 0.09f, 0.95f);
        private static readonly Color OrbNormal     = new Color(0.08f, 0.45f, 0.78f, 1f);
        private static readonly Color OrbGlow       = new Color(0.20f, 0.55f, 0.95f, 0.40f);
        private static readonly Color OrbCorrect    = new Color(0.12f, 0.78f, 0.35f, 1f);
        private static readonly Color OrbCorrectGlow= new Color(0.15f, 0.85f, 0.40f, 0.50f);
        private static readonly Color OrbWrong      = new Color(0.92f, 0.18f, 0.14f, 1f);
        private static readonly Color OrbWrongGlow  = new Color(0.95f, 0.22f, 0.18f, 0.55f);
        private static readonly Color SlotEmpty     = new Color(0.15f, 0.18f, 0.26f, 0.55f);
        private static readonly Color SlotGlowEmpty = new Color(0.25f, 0.55f, 0.82f, 0.12f);
        private static readonly Color SlotFilled    = new Color(0.12f, 0.72f, 0.35f, 0.90f);
        private static readonly Color SlotGlowFilled= new Color(0.15f, 0.80f, 0.40f, 0.35f);

        // Rune symbols for visual flair (mapped by index, cycled)
        private static readonly string[] RuneGlyphs = { "ᚠ", "ᚢ", "ᚦ", "ᚱ", "ᚷ", "ᛁ" };

        // ---------------------------------------------------------------
        //  Public API
        // ---------------------------------------------------------------

        public void Initialise(Canvas parentCanvas, List<string> sequence)
        {
            if (sequence == null || sequence.Count < 2)
            {
                Debug.LogError("[RuneSort] Need at least 2 runes.");
                return;
            }

            correctSequence = new List<string>(sequence);
            playerInput.Clear();
            isCompleted = false;

            BuildUI(parentCanvas);
            ShuffleOrbs();
            UpdateSlots();
            UpdateCounter();
            SetInstruction("Tippe die Runen in der Reihenfolge des Rituals.");
        }

        public void Cleanup()
        {
            StopAllCoroutines();
            if (container != null)
                Destroy(container.gameObject);
        }

        // ---------------------------------------------------------------
        //  UI Construction
        // ---------------------------------------------------------------

        private void BuildUI(Canvas parentCanvas)
        {
            // Full-screen dark container
            GameObject containerObj = new GameObject("RuneSortContainer", typeof(RectTransform), typeof(Image));
            container = containerObj.GetComponent<RectTransform>();
            container.SetParent(parentCanvas.transform, false);
            container.anchorMin = Vector2.zero;
            container.anchorMax = Vector2.one;
            container.offsetMin = Vector2.zero;
            container.offsetMax = Vector2.zero;

            Image bgImage = containerObj.GetComponent<Image>();
            bgImage.color = BgColor;
            bgImage.raycastTarget = true;

            // Title
            titleText = CreateText(container, "Title", 44, FontStyles.Bold);
            titleText.rectTransform.anchorMin = new Vector2(0f, 1f);
            titleText.rectTransform.anchorMax = new Vector2(1f, 1f);
            titleText.rectTransform.pivot = new Vector2(0.5f, 1f);
            titleText.rectTransform.anchoredPosition = new Vector2(0f, -44f);
            titleText.rectTransform.sizeDelta = new Vector2(-80f, 58f);
            titleText.alignment = TextAlignmentOptions.Center;
            titleText.text = "RUNEN-BESCHWOERUNG";
            titleText.color = new Color(0.65f, 0.85f, 1f, 0.92f);

            // Instruction text
            instructionText = CreateText(container, "Instruction", 30, FontStyles.Normal);
            instructionText.rectTransform.anchorMin = new Vector2(0f, 1f);
            instructionText.rectTransform.anchorMax = new Vector2(1f, 1f);
            instructionText.rectTransform.pivot = new Vector2(0.5f, 1f);
            instructionText.rectTransform.anchoredPosition = new Vector2(0f, -106f);
            instructionText.rectTransform.sizeDelta = new Vector2(-60f, 50f);
            instructionText.alignment = TextAlignmentOptions.Center;
            instructionText.enableWordWrapping = true;
            instructionText.color = new Color(0.80f, 0.88f, 1f, 0.85f);

            // Slots row — ritual seal positions
            float slotY = -200f;
            float slotSize = 100f;
            float slotSpacing = 18f;
            float totalSlotWidth = correctSequence.Count * slotSize + (correctSequence.Count - 1) * slotSpacing;
            float slotStartX = -totalSlotWidth / 2f + slotSize / 2f;

            for (int i = 0; i < correctSequence.Count; i++)
            {
                // Slot glow
                GameObject slotGlowObj = new GameObject("SlotGlow" + i, typeof(RectTransform), typeof(Image));
                RectTransform slotGlowRect = slotGlowObj.GetComponent<RectTransform>();
                slotGlowRect.SetParent(container, false);
                slotGlowRect.anchorMin = new Vector2(0.5f, 1f);
                slotGlowRect.anchorMax = new Vector2(0.5f, 1f);
                slotGlowRect.pivot = new Vector2(0.5f, 0.5f);
                slotGlowRect.anchoredPosition = new Vector2(slotStartX + i * (slotSize + slotSpacing), slotY);
                slotGlowRect.sizeDelta = new Vector2(slotSize + 14f, slotSize + 14f);
                Image slotGlowImg = slotGlowObj.GetComponent<Image>();
                slotGlowImg.color = SlotGlowEmpty;
                slotGlowImg.raycastTarget = false;
                slotGlowImages.Add(slotGlowImg);

                // Slot
                GameObject slotObj = new GameObject("Slot" + i, typeof(RectTransform), typeof(Image));
                RectTransform slotRect = slotObj.GetComponent<RectTransform>();
                slotRect.SetParent(container, false);
                slotRect.anchorMin = new Vector2(0.5f, 1f);
                slotRect.anchorMax = new Vector2(0.5f, 1f);
                slotRect.pivot = new Vector2(0.5f, 0.5f);
                slotRect.anchoredPosition = new Vector2(slotStartX + i * (slotSize + slotSpacing), slotY);
                slotRect.sizeDelta = new Vector2(slotSize, slotSize);

                Image slotImg = slotObj.GetComponent<Image>();
                slotImg.color = SlotEmpty;
                slotImg.raycastTarget = false;
                slotImages.Add(slotImg);

                // Slot label (number or rune name)
                TextMeshProUGUI slotLabel = CreateText(slotRect, "SlotLabel", 22, FontStyles.Bold);
                slotLabel.rectTransform.anchorMin = Vector2.zero;
                slotLabel.rectTransform.anchorMax = Vector2.one;
                slotLabel.rectTransform.offsetMin = Vector2.zero;
                slotLabel.rectTransform.offsetMax = Vector2.zero;
                slotLabel.alignment = TextAlignmentOptions.Center;
                slotLabel.text = (i + 1).ToString();
                slotLabel.color = new Color(1f, 1f, 1f, 0.35f);
                slotLabels.Add(slotLabel);
            }

            // Counter text (e.g. "2 / 4")
            counterText = CreateText(container, "Counter", 26, FontStyles.Normal);
            counterText.rectTransform.anchorMin = new Vector2(0f, 1f);
            counterText.rectTransform.anchorMax = new Vector2(1f, 1f);
            counterText.rectTransform.pivot = new Vector2(0.5f, 1f);
            counterText.rectTransform.anchoredPosition = new Vector2(0f, -268f);
            counterText.rectTransform.sizeDelta = new Vector2(-60f, 34f);
            counterText.alignment = TextAlignmentOptions.Center;
            counterText.color = new Color(0.6f, 0.75f, 0.95f, 0.6f);

            // Orb buttons — large, spread vertically for drama
            int orbCount = correctSequence.Count;
            float orbSize = 130f;
            float orbSpacingV = 24f;
            float totalOrbHeight = orbCount * orbSize + (orbCount - 1) * orbSpacingV;
            // Center the orb column in the lower half of the screen
            float orbTopY = -340f;

            for (int i = 0; i < orbCount; i++)
            {
                float yPos = orbTopY - i * (orbSize + orbSpacingV);

                // Glow behind orb
                GameObject glowObj = new GameObject("OrbGlow" + i, typeof(RectTransform), typeof(Image));
                RectTransform glowRect = glowObj.GetComponent<RectTransform>();
                glowRect.SetParent(container, false);
                glowRect.anchorMin = new Vector2(0.5f, 1f);
                glowRect.anchorMax = new Vector2(0.5f, 1f);
                glowRect.pivot = new Vector2(0.5f, 0.5f);
                glowRect.anchoredPosition = new Vector2(0f, yPos);
                glowRect.sizeDelta = new Vector2(orbSize + 20f, orbSize + 20f);
                Image glowImg = glowObj.GetComponent<Image>();
                glowImg.color = OrbGlow;
                glowImg.raycastTarget = false;
                orbGlowImages.Add(glowImg);

                // Orb
                GameObject orbObj = new GameObject("Orb" + i, typeof(RectTransform), typeof(Image), typeof(Button));
                RectTransform orbRect = orbObj.GetComponent<RectTransform>();
                orbRect.SetParent(container, false);
                orbRect.anchorMin = new Vector2(0.5f, 1f);
                orbRect.anchorMax = new Vector2(0.5f, 1f);
                orbRect.pivot = new Vector2(0.5f, 0.5f);
                orbRect.anchoredPosition = new Vector2(0f, yPos);
                orbRect.sizeDelta = new Vector2(orbSize, orbSize);

                Image orbImg = orbObj.GetComponent<Image>();
                orbImg.color = OrbNormal;
                orbImages.Add(orbImg);

                Button orbBtn = orbObj.GetComponent<Button>();
                int capturedIndex = i;
                orbBtn.onClick.AddListener(() => HandleOrbTapped(capturedIndex));
                orbBtn.targetGraphic = orbImg;
                orbButtons.Add(orbBtn);

                // Rune glyph (decorative, top of orb)
                string glyph = RuneGlyphs[i % RuneGlyphs.Length];
                TextMeshProUGUI glyphLabel = CreateText(orbRect, "Glyph", 38, FontStyles.Bold);
                glyphLabel.rectTransform.anchorMin = new Vector2(0.5f, 1f);
                glyphLabel.rectTransform.anchorMax = new Vector2(0.5f, 1f);
                glyphLabel.rectTransform.pivot = new Vector2(0.5f, 1f);
                glyphLabel.rectTransform.anchoredPosition = new Vector2(0f, -8f);
                glyphLabel.rectTransform.sizeDelta = new Vector2(orbSize, 42f);
                glyphLabel.alignment = TextAlignmentOptions.Center;
                glyphLabel.color = new Color(1f, 1f, 1f, 0.25f);
                glyphLabel.text = glyph;

                // Rune name (main label)
                TextMeshProUGUI label = CreateText(orbRect, "OrbLabel", 32, FontStyles.Bold);
                label.rectTransform.anchorMin = new Vector2(0f, 0f);
                label.rectTransform.anchorMax = new Vector2(1f, 0.7f);
                label.rectTransform.offsetMin = new Vector2(8f, 0f);
                label.rectTransform.offsetMax = new Vector2(-8f, 0f);
                label.alignment = TextAlignmentOptions.Center;
                label.color = Color.white;
                orbLabels.Add(label);
            }
        }

        // ---------------------------------------------------------------
        //  Game Logic
        // ---------------------------------------------------------------

        private void Update()
        {
            if (isCompleted) return;

            // Pulse orb glows
            float pulse = 0.30f + 0.12f * Mathf.Sin(Time.time * 2.8f);
            for (int i = 0; i < orbGlowImages.Count; i++)
            {
                if (!orbButtons[i].interactable) continue;
                Color c = OrbGlow;
                c.a = pulse;
                orbGlowImages[i].color = c;
            }
        }

        private void ShuffleOrbs()
        {
            List<string> shuffled = new List<string>(correctSequence);

            // Fisher-Yates shuffle
            for (int i = shuffled.Count - 1; i > 0; i--)
            {
                int j = UnityEngine.Random.Range(0, i + 1);
                string temp = shuffled[i];
                shuffled[i] = shuffled[j];
                shuffled[j] = temp;
            }

            // Avoid identity
            if (IsIdenticalOrder(shuffled, correctSequence) && shuffled.Count >= 2)
            {
                string temp = shuffled[0];
                shuffled[0] = shuffled[1];
                shuffled[1] = temp;
            }

            for (int i = 0; i < shuffled.Count; i++)
            {
                orbLabels[i].text = shuffled[i];
                orbImages[i].color = OrbNormal;
                orbGlowImages[i].color = OrbGlow;
                orbButtons[i].interactable = true;
            }
        }

        private static bool IsIdenticalOrder(List<string> a, List<string> b)
        {
            if (a.Count != b.Count) return false;
            for (int i = 0; i < a.Count; i++)
            {
                if (!string.Equals(a[i], b[i], StringComparison.OrdinalIgnoreCase))
                    return false;
            }
            return true;
        }

        private void HandleOrbTapped(int orbIndex)
        {
            if (isCompleted) return;
            if (!orbButtons[orbIndex].interactable) return;

            string tappedRune = orbLabels[orbIndex].text;
            int currentSlot = playerInput.Count;
            string expectedRune = correctSequence[currentSlot];

            if (string.Equals(tappedRune, expectedRune, StringComparison.OrdinalIgnoreCase))
            {
                // Correct
                playerInput.Add(tappedRune);
                orbImages[orbIndex].color = OrbCorrect;
                orbGlowImages[orbIndex].color = OrbCorrectGlow;
                orbButtons[orbIndex].interactable = false;
                UpdateSlots();
                UpdateCounter();

                if (playerInput.Count >= correctSequence.Count)
                {
                    isCompleted = true;
                    SetInstruction("Alle Runen beschworen! Der Riss schliesst sich.");
                    OnStatusChanged?.Invoke("Runen-Ritual vollendet!");
                    StartCoroutine(VictorySequence());
                }
                else
                {
                    int remaining = correctSequence.Count - playerInput.Count;
                    SetInstruction("Richtig! Noch " + remaining + " Rune" + (remaining == 1 ? "" : "n") + ".");
                }
            }
            else
            {
                StartCoroutine(FlashWrongAndReset(orbIndex));
            }
        }

        private IEnumerator FlashWrongAndReset(int orbIndex)
        {
            // Flash wrong orb
            orbImages[orbIndex].color = OrbWrong;
            orbGlowImages[orbIndex].color = OrbWrongGlow;
            SetInstruction("Falsche Reihenfolge! Das Ritual bricht ab.");
            OnStatusChanged?.Invoke("Ritual unterbrochen!");

            // Disable all
            for (int i = 0; i < orbButtons.Count; i++)
                orbButtons[i].interactable = false;

            // Shake effect — move container slightly
            Vector2 originalPos = container.anchoredPosition;
            float shakeDuration = 0.4f;
            float elapsed = 0f;
            while (elapsed < shakeDuration)
            {
                float offsetX = UnityEngine.Random.Range(-12f, 12f) * (1f - elapsed / shakeDuration);
                float offsetY = UnityEngine.Random.Range(-8f, 8f) * (1f - elapsed / shakeDuration);
                container.anchoredPosition = originalPos + new Vector2(offsetX, offsetY);
                elapsed += Time.deltaTime;
                yield return null;
            }
            container.anchoredPosition = originalPos;

            yield return new WaitForSeconds(0.4f);

            // Reset
            playerInput.Clear();
            UpdateSlots();
            UpdateCounter();
            ShuffleOrbs();
            SetInstruction("Tippe die Runen in der Reihenfolge des Rituals.");
        }

        private IEnumerator VictorySequence()
        {
            // Flash all slots bright
            for (int i = 0; i < slotImages.Count; i++)
            {
                slotImages[i].color = new Color(0.18f, 0.85f, 0.45f, 1f);
                slotGlowImages[i].color = new Color(0.20f, 0.90f, 0.50f, 0.6f);
                yield return new WaitForSeconds(0.15f);
            }

            yield return new WaitForSeconds(0.6f);
            OnCompleted?.Invoke();
        }

        private void UpdateSlots()
        {
            for (int i = 0; i < slotImages.Count; i++)
            {
                if (i < playerInput.Count)
                {
                    slotImages[i].color = SlotFilled;
                    slotGlowImages[i].color = SlotGlowFilled;
                    slotLabels[i].text = playerInput[i];
                    slotLabels[i].color = Color.white;
                    slotLabels[i].fontSize = 20f;
                }
                else
                {
                    slotImages[i].color = SlotEmpty;
                    slotGlowImages[i].color = SlotGlowEmpty;
                    slotLabels[i].text = (i + 1).ToString();
                    slotLabels[i].color = new Color(1f, 1f, 1f, 0.35f);
                    slotLabels[i].fontSize = 22f;
                }
            }
        }

        private void UpdateCounter()
        {
            if (counterText != null)
                counterText.text = playerInput.Count + " / " + correctSequence.Count + " beschworen";
        }

        private void SetInstruction(string text)
        {
            if (instructionText != null)
                instructionText.text = text;
        }

        // ---------------------------------------------------------------
        //  Helpers
        // ---------------------------------------------------------------

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
