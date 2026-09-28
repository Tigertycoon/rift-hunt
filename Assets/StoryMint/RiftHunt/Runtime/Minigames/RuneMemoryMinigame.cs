using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

namespace StoryMint.RiftHunt.Minigames
{
    /// <summary>
    /// Runen-Erinnerung (Memory) – a classic card-matching game.
    /// Cards are laid face-down in a grid. Player flips two at a time.
    /// Matching pairs stay revealed; mismatches flip back after a short delay.
    /// Win condition: all pairs found.
    /// </summary>
    public sealed class RuneMemoryMinigame : MonoBehaviour
    {
        public event Action OnCompleted;
        public event Action<string> OnStatusChanged;

        private bool isCompleted;
        private bool isBusy; // true while two non-matching cards are shown
        private int pairsFound;
        private int totalPairs;
        private int flipCount;

        private RectTransform container;
        private TextMeshProUGUI titleText;
        private TextMeshProUGUI progressText;

        private readonly List<MemoryCard> cards = new List<MemoryCard>();
        private MemoryCard firstFlipped;
        private MemoryCard secondFlipped;

        private static readonly Color BgColor       = new Color(0.03f, 0.04f, 0.09f, 0.95f);
        private static readonly Color CardBack      = new Color(0.10f, 0.16f, 0.28f, 1f);
        private static readonly Color CardFront     = new Color(0.14f, 0.46f, 0.78f, 1f);
        private static readonly Color CardMatched   = new Color(0.14f, 0.72f, 0.38f, 1f);
        private static readonly Color CardMismatch  = new Color(0.88f, 0.22f, 0.16f, 1f);
        private static readonly Color SymbolHidden  = new Color(0.20f, 0.30f, 0.50f, 0.6f);

        // Unicode symbols for rune cards (visually distinct)
        private static readonly string[] RuneSymbols = new string[]
        {
            "✦", // ✦
            "✶", // ✶
            "❀", // ❀
            "❖", // ❖
            "✺", // ✺
            "❂", // ❂
            "❈", // ❈
            "❉"  // ❉
        };

        private class MemoryCard
        {
            public RectTransform rect;
            public Image image;
            public Button button;
            public TextMeshProUGUI label;
            public TextMeshProUGUI nameLabel; // rune name under symbol
            public string runeName;
            public string symbol;
            public int pairIndex;
            public bool isRevealed;
            public bool isMatched;
        }

        // ---------------------------------------------------------------
        //  Public API
        // ---------------------------------------------------------------

        public void Initialise(Canvas parentCanvas, List<string> runeNames)
        {
            if (runeNames == null || runeNames.Count < 2)
            {
                Debug.LogError("[RuneMemory] Need at least 2 runes.");
                return;
            }

            // Use up to 6 runes (= 12 cards in 3x4 grid), minimum 3 (= 6 cards in 2x3)
            int pairCount = Mathf.Clamp(runeNames.Count, 3, 6);
            totalPairs = pairCount;

            // Build list of card data (two cards per rune)
            List<CardData> cardDataList = new List<CardData>();
            for (int i = 0; i < pairCount; i++)
            {
                string name = i < runeNames.Count ? runeNames[i] : runeNames[i % runeNames.Count];
                string symbol = RuneSymbols[i % RuneSymbols.Length];
                cardDataList.Add(new CardData { runeName = name, symbol = symbol, pairIndex = i });
                cardDataList.Add(new CardData { runeName = name, symbol = symbol, pairIndex = i });
            }

            // Shuffle using Fisher-Yates
            for (int i = cardDataList.Count - 1; i > 0; i--)
            {
                int j = UnityEngine.Random.Range(0, i + 1);
                var temp = cardDataList[i];
                cardDataList[i] = cardDataList[j];
                cardDataList[j] = temp;
            }

            BuildUi(parentCanvas, cardDataList);
            UpdateProgress();

            OnStatusChanged?.Invoke("Finde alle Runen-Paare!");
        }

        public void Cleanup()
        {
            if (container != null)
                Destroy(container.gameObject);
        }

        // ---------------------------------------------------------------
        //  UI construction
        // ---------------------------------------------------------------

        private struct CardData
        {
            public string runeName;
            public string symbol;
            public int pairIndex;
        }

        private void BuildUi(Canvas parentCanvas, List<CardData> cardDataList)
        {
            // Root container
            GameObject root = new GameObject("RuneMemoryRoot", typeof(RectTransform), typeof(Image));
            container = root.GetComponent<RectTransform>();
            container.SetParent(parentCanvas.transform, false);
            container.anchorMin = Vector2.zero;
            container.anchorMax = Vector2.one;
            container.offsetMin = Vector2.zero;
            container.offsetMax = Vector2.zero;
            root.GetComponent<Image>().color = BgColor;

            // Title
            titleText = CreateText(container, "Title", 46, FontStyles.Bold);
            titleText.text = "Runen-Erinnerung";
            titleText.rectTransform.anchorMin = new Vector2(0f, 1f);
            titleText.rectTransform.anchorMax = new Vector2(1f, 1f);
            titleText.rectTransform.pivot = new Vector2(0.5f, 1f);
            titleText.rectTransform.anchoredPosition = new Vector2(0f, -60f);
            titleText.rectTransform.sizeDelta = new Vector2(-80f, 60f);
            titleText.alignment = TextAlignmentOptions.Center;
            titleText.color = new Color(0.85f, 0.95f, 1f, 1f);

            // Progress
            progressText = CreateText(container, "Progress", 30, FontStyles.Normal);
            progressText.rectTransform.anchorMin = new Vector2(0f, 1f);
            progressText.rectTransform.anchorMax = new Vector2(1f, 1f);
            progressText.rectTransform.pivot = new Vector2(0.5f, 1f);
            progressText.rectTransform.anchoredPosition = new Vector2(0f, -130f);
            progressText.rectTransform.sizeDelta = new Vector2(-80f, 40f);
            progressText.alignment = TextAlignmentOptions.Center;
            progressText.color = new Color(0.65f, 0.80f, 0.95f, 1f);

            // Grid layout
            int cardCount = cardDataList.Count;
            int cols, rows;
            if (cardCount <= 6) { cols = 2; rows = 3; }
            else if (cardCount <= 8) { cols = 2; rows = 4; }
            else { cols = 3; rows = 4; }

            float gridWidth = 640f;
            float gridHeight = 720f;
            float spacing = 16f;
            float cardWidth = (gridWidth - spacing * (cols - 1)) / cols;
            float cardHeight = (gridHeight - spacing * (rows - 1)) / rows;

            // Grid container
            GameObject gridObj = new GameObject("Grid", typeof(RectTransform));
            RectTransform gridRect = gridObj.GetComponent<RectTransform>();
            gridRect.SetParent(container, false);
            gridRect.anchorMin = new Vector2(0.5f, 0.5f);
            gridRect.anchorMax = new Vector2(0.5f, 0.5f);
            gridRect.pivot = new Vector2(0.5f, 0.5f);
            gridRect.anchoredPosition = new Vector2(0f, -40f);
            gridRect.sizeDelta = new Vector2(gridWidth, gridHeight);

            float startX = -gridWidth / 2f + cardWidth / 2f;
            float startY = gridHeight / 2f - cardHeight / 2f;

            for (int i = 0; i < cardCount; i++)
            {
                int col = i % cols;
                int row = i / cols;
                float x = startX + col * (cardWidth + spacing);
                float y = startY - row * (cardHeight + spacing);

                MemoryCard card = CreateCard(gridRect, cardDataList[i], cardWidth, cardHeight);
                card.rect.anchoredPosition = new Vector2(x, y);
                cards.Add(card);
            }
        }

        private MemoryCard CreateCard(RectTransform parent, CardData data, float width, float height)
        {
            GameObject cardObj = new GameObject("Card_" + data.runeName, typeof(RectTransform), typeof(Image), typeof(Button));
            RectTransform rect = cardObj.GetComponent<RectTransform>();
            rect.SetParent(parent, false);
            rect.sizeDelta = new Vector2(width, height);

            Image img = cardObj.GetComponent<Image>();
            img.color = CardBack;

            // Rounded look (no actual rounding in code-built UI, but color conveys it)

            // Symbol text (hidden initially, shows "?")
            TextMeshProUGUI label = CreateText(rect, "Symbol", Mathf.Min(width * 0.45f, 72f), FontStyles.Bold);
            label.rectTransform.anchorMin = new Vector2(0f, 0.2f);
            label.rectTransform.anchorMax = new Vector2(1f, 1f);
            label.rectTransform.offsetMin = Vector2.zero;
            label.rectTransform.offsetMax = Vector2.zero;
            label.alignment = TextAlignmentOptions.Center;
            label.text = "?";
            label.color = SymbolHidden;

            // Name label (hidden until revealed)
            TextMeshProUGUI nameLabel = CreateText(rect, "Name", Mathf.Min(width * 0.18f, 24f), FontStyles.Normal);
            nameLabel.rectTransform.anchorMin = new Vector2(0f, 0f);
            nameLabel.rectTransform.anchorMax = new Vector2(1f, 0.25f);
            nameLabel.rectTransform.offsetMin = new Vector2(4f, 4f);
            nameLabel.rectTransform.offsetMax = new Vector2(-4f, 0f);
            nameLabel.alignment = TextAlignmentOptions.Center;
            nameLabel.text = "";
            nameLabel.color = new Color(1f, 1f, 1f, 0.8f);
            nameLabel.enableWordWrapping = false;
            nameLabel.overflowMode = TextOverflowModes.Ellipsis;

            Button btn = cardObj.GetComponent<Button>();
            btn.targetGraphic = img;

            MemoryCard card = new MemoryCard
            {
                rect = rect,
                image = img,
                button = btn,
                label = label,
                nameLabel = nameLabel,
                runeName = data.runeName,
                symbol = data.symbol,
                pairIndex = data.pairIndex,
                isRevealed = false,
                isMatched = false
            };

            btn.onClick.AddListener(() => OnCardTapped(card));
            return card;
        }

        // ---------------------------------------------------------------
        //  Game logic
        // ---------------------------------------------------------------

        private void OnCardTapped(MemoryCard card)
        {
            if (isCompleted || isBusy || card.isRevealed || card.isMatched)
                return;

            StoryMintSFX.ResumeContext();
            StoryMintSFX.Tap();

            FlipCard(card, true);
            flipCount++;

            if (firstFlipped == null)
            {
                firstFlipped = card;
                return;
            }

            secondFlipped = card;
            isBusy = true;

            if (firstFlipped.pairIndex == secondFlipped.pairIndex)
            {
                // Match!
                StartCoroutine(HandleMatch(firstFlipped, secondFlipped));
            }
            else
            {
                // Mismatch
                StartCoroutine(HandleMismatch(firstFlipped, secondFlipped));
            }
        }

        private IEnumerator HandleMatch(MemoryCard a, MemoryCard b)
        {
            yield return new WaitForSeconds(0.3f);

            a.isMatched = true;
            b.isMatched = true;
            a.image.color = CardMatched;
            b.image.color = CardMatched;
            StoryMintSFX.Success();
            pairsFound++;
            UpdateProgress();

            firstFlipped = null;
            secondFlipped = null;
            isBusy = false;

            if (pairsFound >= totalPairs)
            {
                yield return new WaitForSeconds(0.5f);
                HandleGameWon();
            }
        }

        private IEnumerator HandleMismatch(MemoryCard a, MemoryCard b)
        {
            a.image.color = CardMismatch;
            b.image.color = CardMismatch;
            StoryMintSFX.Error();

            yield return new WaitForSeconds(0.8f);

            FlipCard(a, false);
            FlipCard(b, false);

            firstFlipped = null;
            secondFlipped = null;
            isBusy = false;
        }

        private void FlipCard(MemoryCard card, bool reveal)
        {
            card.isRevealed = reveal;
            if (reveal)
            {
                card.image.color = CardFront;
                card.label.text = card.symbol;
                card.label.color = Color.white;
                card.nameLabel.text = card.runeName;
            }
            else
            {
                card.image.color = CardBack;
                card.label.text = "?";
                card.label.color = SymbolHidden;
                card.nameLabel.text = "";
            }
        }

        private void HandleGameWon()
        {
            isCompleted = true;
            titleText.text = "Alle Paare gefunden!";

            string stars = flipCount <= totalPairs * 3 ? "★★★"
                         : flipCount <= totalPairs * 4 ? "★★"
                         : "★";
            progressText.text = flipCount / 2 + " Versuche  " + stars;

            StoryMintSFX.Victory();
            OnStatusChanged?.Invoke("Runen-Erinnerung abgeschlossen!");
            OnCompleted?.Invoke();
        }

        private void UpdateProgress()
        {
            if (progressText != null)
                progressText.text = pairsFound + " / " + totalPairs + " Paare";
        }

        // ---------------------------------------------------------------
        //  Helpers
        // ---------------------------------------------------------------

        private static TextMeshProUGUI CreateText(RectTransform parent, string name, float fontSize, FontStyles style)
        {
            GameObject obj = new GameObject(name, typeof(RectTransform));
            obj.GetComponent<RectTransform>().SetParent(parent, false);
            TextMeshProUGUI tmp = obj.AddComponent<TextMeshProUGUI>();
            tmp.fontSize = fontSize;
            tmp.fontStyle = style;
            tmp.color = Color.white;
            tmp.font = TMP_Settings.defaultFontAsset;
            return tmp;
        }
    }
}
