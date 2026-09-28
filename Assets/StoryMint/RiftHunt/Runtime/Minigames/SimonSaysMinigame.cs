using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

namespace StoryMint.RiftHunt.Minigames
{
    /// <summary>
    /// Spiel 2 – Simon Says (Erinnerungs-Ritual).
    /// Coloured pads flash in a sequence the player must repeat.
    /// Each successful round adds one more element AND speeds up.
    /// Wrong input replays the FULL current sequence (no shortcut).
    /// Runs entirely in screen-space UI.
    /// </summary>
    public sealed class SimonSaysMinigame : MonoBehaviour
    {
        public event Action OnCompleted;
        public event Action<string> OnStatusChanged;

        private int totalRounds = 5;
        private int padCount = 4;
        private readonly List<int> sequence = new List<int>();
        private int playerIndex;
        private int currentRound;
        private bool acceptingInput;
        private bool isCompleted;
        private int mistakeCount;

        private RectTransform container;
        private TextMeshProUGUI instructionText;
        private TextMeshProUGUI roundText;
        private TextMeshProUGUI titleText;
        private readonly List<Button> padButtons = new List<Button>();
        private readonly List<Image> padImages = new List<Image>();
        private readonly List<Image> padGlowImages = new List<Image>();
        private readonly List<RectTransform> progressDots = new List<RectTransform>();

        // ---------------------------------------------------------------
        //  Pad colors
        // ---------------------------------------------------------------

        private static readonly Color[] PadColors = new Color[]
        {
            new Color(0.85f, 0.18f, 0.18f, 1f),   // Red
            new Color(0.10f, 0.52f, 0.90f, 1f),   // Blue
            new Color(0.22f, 0.65f, 0.25f, 1f),   // Green
            new Color(0.92f, 0.72f, 0.05f, 1f),   // Yellow
        };

        private static readonly Color[] PadFlashColors = new Color[]
        {
            new Color(1.00f, 0.55f, 0.55f, 1f),
            new Color(0.50f, 0.75f, 1.00f, 1f),
            new Color(0.55f, 0.88f, 0.55f, 1f),
            new Color(1.00f, 0.90f, 0.45f, 1f),
        };

        private static readonly Color PadDim = new Color(0.12f, 0.14f, 0.18f, 0.75f);
        private static readonly Color BgColor = new Color(0.03f, 0.05f, 0.10f, 0.94f);
        private static readonly Color WrongFlash = new Color(0.92f, 0.18f, 0.14f, 1f);
        private static readonly Color DotEmpty = new Color(0.3f, 0.35f, 0.45f, 0.5f);
        private static readonly Color DotFilled = new Color(0.18f, 0.82f, 0.45f, 1f);

        // Timing: gets faster each round
        private float FlashOnDuration  => Mathf.Max(0.22f, 0.55f - (currentRound - 1) * 0.055f);
        private float FlashGapDuration => Mathf.Max(0.10f, 0.25f - (currentRound - 1) * 0.025f);

        // ---------------------------------------------------------------
        //  Public API
        // ---------------------------------------------------------------

        public void Initialise(Canvas parentCanvas, int rounds = 5, int pads = 4)
        {
            totalRounds = Mathf.Max(2, rounds);
            padCount = Mathf.Clamp(pads, 2, 4);
            currentRound = 0;
            mistakeCount = 0;
            sequence.Clear();
            isCompleted = false;
            acceptingInput = false;

            BuildUI(parentCanvas);
            StartNextRound();
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
            GameObject containerObj = new GameObject("SimonSaysContainer", typeof(RectTransform), typeof(Image));
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
            titleText.rectTransform.anchoredPosition = new Vector2(0f, -50f);
            titleText.rectTransform.sizeDelta = new Vector2(-80f, 56f);
            titleText.alignment = TextAlignmentOptions.Center;
            titleText.text = "ERINNERUNGS-RITUAL";
            titleText.color = new Color(0.7f, 0.88f, 1f, 0.9f);

            // Instruction
            instructionText = CreateText(container, "Instruction", 32, FontStyles.Bold);
            instructionText.rectTransform.anchorMin = new Vector2(0f, 1f);
            instructionText.rectTransform.anchorMax = new Vector2(1f, 1f);
            instructionText.rectTransform.pivot = new Vector2(0.5f, 1f);
            instructionText.rectTransform.anchoredPosition = new Vector2(0f, -110f);
            instructionText.rectTransform.sizeDelta = new Vector2(-80f, 52f);
            instructionText.alignment = TextAlignmentOptions.Center;
            instructionText.enableWordWrapping = true;

            // Round counter
            roundText = CreateText(container, "Round", 26, FontStyles.Normal);
            roundText.rectTransform.anchorMin = new Vector2(0f, 1f);
            roundText.rectTransform.anchorMax = new Vector2(1f, 1f);
            roundText.rectTransform.pivot = new Vector2(0.5f, 1f);
            roundText.rectTransform.anchoredPosition = new Vector2(0f, -168f);
            roundText.rectTransform.sizeDelta = new Vector2(-80f, 36f);
            roundText.alignment = TextAlignmentOptions.Center;
            roundText.color = new Color(0.65f, 0.80f, 1f, 0.7f);

            // Progress dots
            float dotSize = 20f;
            float dotGap = 12f;
            float totalDotsWidth = totalRounds * dotSize + (totalRounds - 1) * dotGap;
            float dotsStartX = -totalDotsWidth / 2f + dotSize / 2f;

            for (int i = 0; i < totalRounds; i++)
            {
                GameObject dotObj = new GameObject("Dot" + i, typeof(RectTransform), typeof(Image));
                RectTransform dotRect = dotObj.GetComponent<RectTransform>();
                dotRect.SetParent(container, false);
                dotRect.anchorMin = new Vector2(0.5f, 1f);
                dotRect.anchorMax = new Vector2(0.5f, 1f);
                dotRect.pivot = new Vector2(0.5f, 0.5f);
                dotRect.anchoredPosition = new Vector2(dotsStartX + i * (dotSize + dotGap), -210f);
                dotRect.sizeDelta = new Vector2(dotSize, dotSize);

                Image dotImg = dotObj.GetComponent<Image>();
                dotImg.color = DotEmpty;
                dotImg.raycastTarget = false;
                progressDots.Add(dotRect);
            }

            // Pads — 2x2 grid
            float padSize = 180f;
            float gap = 20f;
            Vector2[] positions;
            if (padCount <= 2)
            {
                positions = new Vector2[]
                {
                    new Vector2(-padSize / 2f - gap / 2f, 0f),
                    new Vector2(padSize / 2f + gap / 2f, 0f),
                };
            }
            else
            {
                positions = new Vector2[]
                {
                    new Vector2(-padSize / 2f - gap / 2f, padSize / 2f + gap / 2f),
                    new Vector2(padSize / 2f + gap / 2f, padSize / 2f + gap / 2f),
                    new Vector2(-padSize / 2f - gap / 2f, -padSize / 2f - gap / 2f),
                    new Vector2(padSize / 2f + gap / 2f, -padSize / 2f - gap / 2f),
                };
            }

            for (int i = 0; i < padCount; i++)
            {
                // Glow (slightly larger, behind pad)
                GameObject glowObj = new GameObject("PadGlow" + i, typeof(RectTransform), typeof(Image));
                RectTransform glowRect = glowObj.GetComponent<RectTransform>();
                glowRect.SetParent(container, false);
                glowRect.anchorMin = new Vector2(0.5f, 0.5f);
                glowRect.anchorMax = new Vector2(0.5f, 0.5f);
                glowRect.pivot = new Vector2(0.5f, 0.5f);
                glowRect.anchoredPosition = positions[i] + new Vector2(0f, -50f);
                glowRect.sizeDelta = new Vector2(padSize + 12f, padSize + 12f);
                Image glowImg = glowObj.GetComponent<Image>();
                glowImg.color = new Color(PadColors[i].r, PadColors[i].g, PadColors[i].b, 0f);
                glowImg.raycastTarget = false;
                padGlowImages.Add(glowImg);

                // Pad
                GameObject padObj = new GameObject("Pad" + i, typeof(RectTransform), typeof(Image), typeof(Button));
                RectTransform padRect = padObj.GetComponent<RectTransform>();
                padRect.SetParent(container, false);
                padRect.anchorMin = new Vector2(0.5f, 0.5f);
                padRect.anchorMax = new Vector2(0.5f, 0.5f);
                padRect.pivot = new Vector2(0.5f, 0.5f);
                padRect.anchoredPosition = positions[i] + new Vector2(0f, -50f);
                padRect.sizeDelta = new Vector2(padSize, padSize);

                Image padImg = padObj.GetComponent<Image>();
                padImg.color = PadDim;
                padImages.Add(padImg);

                Button padBtn = padObj.GetComponent<Button>();
                padBtn.targetGraphic = padImg;
                int capturedIndex = i;
                padBtn.onClick.AddListener(() => HandlePadTapped(capturedIndex));
                padBtn.interactable = false;
                padButtons.Add(padBtn);
            }
        }

        // ---------------------------------------------------------------
        //  Game Logic
        // ---------------------------------------------------------------

        private void StartNextRound()
        {
            currentRound++;
            playerIndex = 0;
            acceptingInput = false;

            // Round 1 starts with 3 elements, each subsequent round adds 1
            int toAdd = currentRound == 1 ? 3 : 1;
            for (int a = 0; a < toAdd; a++)
                sequence.Add(UnityEngine.Random.Range(0, padCount));

            UpdateRoundLabel();
            UpdateProgressDots();
            SetInstruction("Praege dir die Sequenz ein...");
            SetAllPadsInteractable(false);

            StartCoroutine(PlaySequenceThenWait());
        }

        private IEnumerator PlaySequenceThenWait()
        {
            yield return new WaitForSeconds(0.7f);

            // Dim all
            for (int i = 0; i < padImages.Count; i++)
            {
                padImages[i].color = PadDim;
                SetGlow(i, 0f);
            }

            float onTime = FlashOnDuration;
            float gapTime = FlashGapDuration;

            // Play each step
            for (int s = 0; s < sequence.Count; s++)
            {
                int padIndex = sequence[s];
                padImages[padIndex].color = PadFlashColors[padIndex];
                SetGlow(padIndex, 0.45f);
                StoryMintSFX.SimonBlink(padIndex);
                yield return new WaitForSeconds(onTime);
                padImages[padIndex].color = PadDim;
                SetGlow(padIndex, 0f);
                yield return new WaitForSeconds(gapTime);
            }

            // Light up pads and accept input
            for (int i = 0; i < padImages.Count; i++)
            {
                padImages[i].color = PadColors[i];
                SetGlow(i, 0.15f);
            }

            SetInstruction("Jetzt du! Wiederhole die Sequenz.");
            SetAllPadsInteractable(true);
            acceptingInput = true;
        }

        private void HandlePadTapped(int padIndex)
        {
            if (!acceptingInput || isCompleted) return;

            int expected = sequence[playerIndex];

            if (padIndex == expected)
            {
                StoryMintSFX.SimonBlink(padIndex);
                StartCoroutine(FlashPad(padIndex, true));
                playerIndex++;

                if (playerIndex >= sequence.Count)
                {
                    acceptingInput = false;
                    SetAllPadsInteractable(false);

                    if (currentRound >= totalRounds)
                    {
                        isCompleted = true;
                        UpdateProgressDots();
                        SetInstruction("Perfekt! Das Erinnerungs-Ritual ist vollendet!");
                        OnStatusChanged?.Invoke("Ritual gemeistert!");
                        StoryMintSFX.Victory();
                        StartCoroutine(VictoryFlash());
                    }
                    else
                    {
                        UpdateProgressDots();
                        SetInstruction("Runde geschafft!");
                        OnStatusChanged?.Invoke("Runde " + currentRound + "/" + totalRounds);
                        StoryMintSFX.Success();
                        StartCoroutine(DelayedNextRound());
                    }
                }
            }
            else
            {
                acceptingInput = false;
                mistakeCount++;
                StoryMintSFX.Error();
                StartCoroutine(HandleWrongInput(padIndex));
            }
        }

        private IEnumerator FlashPad(int padIndex, bool correct)
        {
            Color flashColor = correct ? PadFlashColors[padIndex] : WrongFlash;
            padImages[padIndex].color = flashColor;
            SetGlow(padIndex, correct ? 0.5f : 0.6f);
            yield return new WaitForSeconds(0.18f);
            if (!isCompleted)
            {
                padImages[padIndex].color = PadColors[padIndex];
                SetGlow(padIndex, 0.15f);
            }
        }

        private IEnumerator HandleWrongInput(int padIndex)
        {
            SetAllPadsInteractable(false);

            // Flash all pads red briefly
            for (int i = 0; i < padImages.Count; i++)
            {
                padImages[i].color = WrongFlash;
                SetGlow(i, 0.5f);
            }

            SetInstruction("Falsch! Schau nochmal genau hin.");
            OnStatusChanged?.Invoke("Falsche Eingabe!");

            yield return new WaitForSeconds(1.0f);

            // Dim all
            for (int i = 0; i < padImages.Count; i++)
            {
                padImages[i].color = PadDim;
                SetGlow(i, 0f);
            }

            playerIndex = 0;
            UpdateRoundLabel();
            SetInstruction("Praege dir die Sequenz ein...");

            yield return new WaitForSeconds(0.5f);
            yield return StartCoroutine(PlaySequenceThenWait());
        }

        private IEnumerator VictoryFlash()
        {
            for (int r = 0; r < 2; r++)
            {
                for (int i = 0; i < padImages.Count; i++)
                {
                    padImages[i].color = PadFlashColors[i];
                    SetGlow(i, 0.6f);
                }
                yield return new WaitForSeconds(0.25f);
                for (int i = 0; i < padImages.Count; i++)
                {
                    padImages[i].color = PadDim;
                    SetGlow(i, 0f);
                }
                yield return new WaitForSeconds(0.15f);
            }

            for (int i = 0; i < padImages.Count; i++)
            {
                padImages[i].color = PadColors[i];
                SetGlow(i, 0.3f);
            }

            yield return new WaitForSeconds(0.3f);
            OnCompleted?.Invoke();
        }

        private IEnumerator DelayedNextRound()
        {
            yield return new WaitForSeconds(1.0f);
            StartNextRound();
        }

        private void SetAllPadsInteractable(bool interactable)
        {
            for (int i = 0; i < padButtons.Count; i++)
                padButtons[i].interactable = interactable;
        }

        private void SetGlow(int padIndex, float alpha)
        {
            if (padIndex < 0 || padIndex >= padGlowImages.Count) return;
            Color c = padGlowImages[padIndex].color;
            c.a = alpha;
            padGlowImages[padIndex].color = c;
        }

        private void UpdateRoundLabel()
        {
            if (roundText != null)
                roundText.text = "Runde " + currentRound + " / " + totalRounds +
                    (mistakeCount > 0 ? "  |  Fehler: " + mistakeCount : "");
        }

        private void UpdateProgressDots()
        {
            for (int i = 0; i < progressDots.Count; i++)
            {
                Image dotImg = progressDots[i].GetComponent<Image>();
                if (dotImg != null)
                    dotImg.color = i < currentRound ? DotFilled : DotEmpty;
            }
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
