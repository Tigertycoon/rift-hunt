using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

namespace StoryMint.RiftHunt.Minigames
{
    /// <summary>
    /// Siegel-Zeichnen v2 – Player traces rune shapes with their finger.
    /// A template glyph is shown as a faded guide; the player draws over it.
    /// Accuracy is measured on finger-lift. Pass → next rune. Fail → retry.
    /// </summary>
    public sealed class SealTraceMinigame : MonoBehaviour
    {
        public event Action OnCompleted;
        public event Action<string> OnStatusChanged;

        private List<string> correctSequence;
        private int currentRuneIndex;
        private bool isCompleted;
        private int mistakeCount;

        private RectTransform container;
        private RectTransform drawArea;
        private TextMeshProUGUI titleText;
        private TextMeshProUGUI instructionText;
        private TextMeshProUGUI progressText;
        private TextMeshProUGUI runeNameText;
        private TextMeshProUGUI accuracyText;
        private Canvas parentCanvasRef;

        // Template rendering
        private readonly List<Image> templateDots = new List<Image>();
        private readonly List<Image> templateLines = new List<Image>();

        // Player stroke
        private readonly List<Vector2> drawnPoints = new List<Vector2>();
        private readonly List<Image> strokeSegments = new List<Image>();
        private bool isDrawing;
        private Vector2 lastDrawnPoint;

        // Completed rune check marks
        private readonly List<Image> completedMarks = new List<Image>();

        private const float DrawAreaSize = 520f;
        private const float TemplateDotSize = 20f;
        private const float StrokeWidth = 8f;
        private const float TemplateLineWidth = 14f;
        private const float AccuracyThreshold = 0.90f; // 90% accuracy to pass
        private const float SampleInterval = 8f; // min px between recorded points

        private static readonly Color BgColor           = new Color(0.03f, 0.04f, 0.09f, 0.95f);
        private static readonly Color DrawAreaColor     = new Color(0.06f, 0.08f, 0.14f, 1f);
        private static readonly Color TemplateColor     = new Color(0.25f, 0.55f, 0.85f, 0.25f);
        private static readonly Color TemplateDotColor  = new Color(0.30f, 0.60f, 0.90f, 0.50f);
        private static readonly Color StrokeColor       = new Color(0.15f, 0.82f, 0.40f, 0.85f);
        private static readonly Color StrokeFailColor   = new Color(0.92f, 0.18f, 0.14f, 0.85f);
        private static readonly Color StrokePassColor   = new Color(0.20f, 0.90f, 0.50f, 1f);
        private static readonly Color StartDotColor     = new Color(0.30f, 0.90f, 0.55f, 0.80f);

        // ---------------------------------------------------------------
        //  Rune templates – normalised [0,1] coordinates
        //  Each template is a list of keypoints; the player traces the path
        //  connecting them in order.
        // ---------------------------------------------------------------

        private static readonly Dictionary<string, List<Vector2>> RuneTemplates = new Dictionary<string, List<Vector2>>
        {
            // LUX (light) – upward triangle
            { "LUX", new List<Vector2> {
                new Vector2(0.50f, 0.88f),
                new Vector2(0.15f, 0.18f),
                new Vector2(0.85f, 0.18f),
                new Vector2(0.50f, 0.88f)
            }},

            // AER (air) – zigzag / wind
            { "AER", new List<Vector2> {
                new Vector2(0.10f, 0.50f),
                new Vector2(0.30f, 0.15f),
                new Vector2(0.50f, 0.85f),
                new Vector2(0.70f, 0.15f),
                new Vector2(0.90f, 0.50f)
            }},

            // TERRA (earth) – diamond
            { "TERRA", new List<Vector2> {
                new Vector2(0.50f, 0.10f),
                new Vector2(0.90f, 0.50f),
                new Vector2(0.50f, 0.90f),
                new Vector2(0.10f, 0.50f),
                new Vector2(0.50f, 0.10f)
            }},

            // UMBRA (shadow) – inverted V with horizontal bar (like a rune)
            { "UMBRA", new List<Vector2> {
                new Vector2(0.15f, 0.85f),
                new Vector2(0.50f, 0.12f),
                new Vector2(0.85f, 0.85f),
                new Vector2(0.65f, 0.55f),
                new Vector2(0.35f, 0.55f)
            }}
        };

        // Fallback generic shape for unknown rune names
        private static readonly List<Vector2> FallbackTemplate = new List<Vector2>
        {
            new Vector2(0.50f, 0.10f),
            new Vector2(0.85f, 0.35f),
            new Vector2(0.70f, 0.85f),
            new Vector2(0.30f, 0.85f),
            new Vector2(0.15f, 0.35f),
            new Vector2(0.50f, 0.10f)
        };

        // ---------------------------------------------------------------
        //  Public API
        // ---------------------------------------------------------------

        public void Initialise(Canvas parentCanvas, List<string> sequence)
        {
            if (sequence == null || sequence.Count < 1)
            {
                Debug.LogError("[SealTrace] Need at least 1 rune.");
                return;
            }

            parentCanvasRef = parentCanvas;
            correctSequence = new List<string>(sequence);
            currentRuneIndex = 0;
            isCompleted = false;
            mistakeCount = 0;
            isDrawing = false;

            BuildUI(parentCanvas);
            ShowCurrentRune();
            UpdateProgress();
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
            // Container
            GameObject containerObj = new GameObject("SealTraceContainer", typeof(RectTransform), typeof(Image));
            container = containerObj.GetComponent<RectTransform>();
            container.SetParent(parentCanvas.transform, false);
            container.anchorMin = Vector2.zero;
            container.anchorMax = Vector2.one;
            container.offsetMin = Vector2.zero;
            container.offsetMax = Vector2.zero;
            containerObj.GetComponent<Image>().color = BgColor;
            containerObj.GetComponent<Image>().raycastTarget = true;

            // Title
            titleText = CreateText(container, "Title", 38, FontStyles.Bold);
            SetAnchors(titleText.rectTransform, 0f, 1f, 1f, 1f, new Vector2(0.5f, 1f));
            titleText.rectTransform.anchoredPosition = new Vector2(0f, -36f);
            titleText.rectTransform.sizeDelta = new Vector2(-60f, 50f);
            titleText.alignment = TextAlignmentOptions.Center;
            titleText.text = "SIEGEL-ZEICHNEN";
            titleText.color = new Color(0.65f, 0.85f, 1f, 0.92f);

            // Rune name (big, above the draw area)
            runeNameText = CreateText(container, "RuneName", 52, FontStyles.Bold);
            SetAnchors(runeNameText.rectTransform, 0f, 1f, 1f, 1f, new Vector2(0.5f, 1f));
            runeNameText.rectTransform.anchoredPosition = new Vector2(0f, -90f);
            runeNameText.rectTransform.sizeDelta = new Vector2(-60f, 64f);
            runeNameText.alignment = TextAlignmentOptions.Center;
            runeNameText.color = new Color(0.40f, 0.75f, 1f, 0.95f);

            // Instruction
            instructionText = CreateText(container, "Instruction", 26, FontStyles.Normal);
            SetAnchors(instructionText.rectTransform, 0f, 1f, 1f, 1f, new Vector2(0.5f, 1f));
            instructionText.rectTransform.anchoredPosition = new Vector2(0f, -148f);
            instructionText.rectTransform.sizeDelta = new Vector2(-40f, 40f);
            instructionText.alignment = TextAlignmentOptions.Center;
            instructionText.enableWordWrapping = true;
            instructionText.color = new Color(0.8f, 0.88f, 1f, 0.75f);

            // Draw area
            GameObject drawObj = new GameObject("DrawArea", typeof(RectTransform), typeof(Image));
            drawArea = drawObj.GetComponent<RectTransform>();
            drawArea.SetParent(container, false);
            SetAnchors(drawArea, 0.5f, 0.5f, 0.5f, 0.5f, new Vector2(0.5f, 0.5f));
            drawArea.anchoredPosition = new Vector2(0f, -20f);
            drawArea.sizeDelta = new Vector2(DrawAreaSize, DrawAreaSize);
            drawObj.GetComponent<Image>().color = DrawAreaColor;
            drawObj.GetComponent<Image>().raycastTarget = true;

            // Accuracy feedback (shown after each attempt)
            accuracyText = CreateText(container, "Accuracy", 30, FontStyles.Bold);
            SetAnchors(accuracyText.rectTransform, 0f, 0f, 1f, 0f, new Vector2(0.5f, 0f));
            accuracyText.rectTransform.anchoredPosition = new Vector2(0f, 74f);
            accuracyText.rectTransform.sizeDelta = new Vector2(-40f, 44f);
            accuracyText.alignment = TextAlignmentOptions.Center;
            accuracyText.color = new Color(0.8f, 0.9f, 1f, 0f); // hidden initially

            // Progress
            progressText = CreateText(container, "Progress", 22, FontStyles.Normal);
            SetAnchors(progressText.rectTransform, 0f, 0f, 1f, 0f, new Vector2(0.5f, 0f));
            progressText.rectTransform.anchoredPosition = new Vector2(0f, 30f);
            progressText.rectTransform.sizeDelta = new Vector2(-60f, 32f);
            progressText.alignment = TextAlignmentOptions.Center;
            progressText.color = new Color(0.6f, 0.75f, 0.95f, 0.6f);
        }

        // ---------------------------------------------------------------
        //  Template Display
        // ---------------------------------------------------------------

        private List<Vector2> GetTemplate(string runeName)
        {
            string key = (runeName ?? "").Trim().ToUpperInvariant();
            if (RuneTemplates.TryGetValue(key, out List<Vector2> tmpl))
                return tmpl;
            return FallbackTemplate;
        }

        /// <summary>
        /// Converts normalised [0,1] template coordinate to local position
        /// within drawArea (origin at center, Y-up).
        /// </summary>
        private Vector2 TemplateToLocal(Vector2 normalised)
        {
            float half = DrawAreaSize * 0.5f;
            float margin = DrawAreaSize * 0.08f;
            float usable = DrawAreaSize - margin * 2f;
            float x = -half + margin + normalised.x * usable;
            float y = -half + margin + normalised.y * usable;
            return new Vector2(x, y);
        }

        private void ShowCurrentRune()
        {
            ClearTemplate();
            ClearStroke();
            HideAccuracy();

            if (currentRuneIndex >= correctSequence.Count) return;

            string runeName = correctSequence[currentRuneIndex];
            runeNameText.text = runeName;
            SetInstruction("Zeichne das Symbol nach! Starte beim gruenen Punkt.");

            List<Vector2> template = GetTemplate(runeName);
            List<Vector2> localPts = new List<Vector2>();

            for (int i = 0; i < template.Count; i++)
                localPts.Add(TemplateToLocal(template[i]));

            // Draw template lines (faded)
            for (int i = 0; i < localPts.Count - 1; i++)
                DrawTemplateLine(localPts[i], localPts[i + 1]);

            // Draw template dots
            for (int i = 0; i < localPts.Count; i++)
            {
                bool isStart = (i == 0);
                float dotSize = isStart ? 34f : TemplateDotSize;
                Color dotCol = isStart ? StartDotColor : TemplateDotColor;
                DrawTemplateDot(localPts[i], dotSize, dotCol);
            }
        }

        private void DrawTemplateLine(Vector2 from, Vector2 to)
        {
            GameObject lineObj = new GameObject("TmplLine", typeof(RectTransform), typeof(Image));
            RectTransform lr = lineObj.GetComponent<RectTransform>();
            lr.SetParent(drawArea, false);
            SetAnchors(lr, 0.5f, 0.5f, 0.5f, 0.5f, new Vector2(0.5f, 0.5f));

            Vector2 mid = (from + to) * 0.5f;
            float length = Vector2.Distance(from, to);
            float angle = Mathf.Atan2(to.y - from.y, to.x - from.x) * Mathf.Rad2Deg;

            lr.anchoredPosition = mid;
            lr.sizeDelta = new Vector2(length, TemplateLineWidth);
            lr.localRotation = Quaternion.Euler(0, 0, angle);

            Image img = lineObj.GetComponent<Image>();
            img.color = TemplateColor;
            img.raycastTarget = false;

            templateLines.Add(img);
        }

        private void DrawTemplateDot(Vector2 pos, float size, Color color)
        {
            GameObject dotObj = new GameObject("TmplDot", typeof(RectTransform), typeof(Image));
            RectTransform dr = dotObj.GetComponent<RectTransform>();
            dr.SetParent(drawArea, false);
            SetAnchors(dr, 0.5f, 0.5f, 0.5f, 0.5f, new Vector2(0.5f, 0.5f));
            dr.anchoredPosition = pos;
            dr.sizeDelta = new Vector2(size, size);

            Image img = dotObj.GetComponent<Image>();
            img.color = color;
            img.raycastTarget = false;

            templateDots.Add(img);
        }

        private void ClearTemplate()
        {
            for (int i = templateDots.Count - 1; i >= 0; i--)
            {
                if (templateDots[i] != null) Destroy(templateDots[i].gameObject);
            }
            templateDots.Clear();

            for (int i = templateLines.Count - 1; i >= 0; i--)
            {
                if (templateLines[i] != null) Destroy(templateLines[i].gameObject);
            }
            templateLines.Clear();
        }

        // ---------------------------------------------------------------
        //  Player Stroke
        // ---------------------------------------------------------------

        private void ClearStroke()
        {
            drawnPoints.Clear();
            for (int i = strokeSegments.Count - 1; i >= 0; i--)
            {
                if (strokeSegments[i] != null) Destroy(strokeSegments[i].gameObject);
            }
            strokeSegments.Clear();
            isDrawing = false;
        }

        private void AddStrokeSegment(Vector2 from, Vector2 to)
        {
            GameObject segObj = new GameObject("Stroke", typeof(RectTransform), typeof(Image));
            RectTransform sr = segObj.GetComponent<RectTransform>();
            sr.SetParent(drawArea, false);
            SetAnchors(sr, 0.5f, 0.5f, 0.5f, 0.5f, new Vector2(0.5f, 0.5f));

            Vector2 mid = (from + to) * 0.5f;
            float length = Vector2.Distance(from, to);
            float angle = Mathf.Atan2(to.y - from.y, to.x - from.x) * Mathf.Rad2Deg;

            sr.anchoredPosition = mid;
            sr.sizeDelta = new Vector2(length + StrokeWidth, StrokeWidth);
            sr.localRotation = Quaternion.Euler(0, 0, angle);

            Image img = segObj.GetComponent<Image>();
            img.color = StrokeColor;
            img.raycastTarget = false;

            strokeSegments.Add(img);
        }

        private void ColorStroke(Color c)
        {
            for (int i = 0; i < strokeSegments.Count; i++)
            {
                if (strokeSegments[i] != null) strokeSegments[i].color = c;
            }
        }

        // ---------------------------------------------------------------
        //  Touch / Input Handling
        // ---------------------------------------------------------------

        private void Update()
        {
            if (isCompleted) return;

            // Pulse the start dot
            PulseStartDot();

            // Handle touch / mouse input
            if (Input.touchCount > 0)
            {
                Touch t = Input.GetTouch(0);
                switch (t.phase)
                {
                    case TouchPhase.Began:
                        TryBeginDraw(t.position);
                        break;
                    case TouchPhase.Moved:
                    case TouchPhase.Stationary:
                        TryContinueDraw(t.position);
                        break;
                    case TouchPhase.Ended:
                    case TouchPhase.Canceled:
                        EndDraw();
                        break;
                }
            }
            else
            {
                // Mouse fallback for editor
                if (Input.GetMouseButtonDown(0))
                    TryBeginDraw(Input.mousePosition);
                else if (Input.GetMouseButton(0))
                    TryContinueDraw(Input.mousePosition);
                else if (Input.GetMouseButtonUp(0))
                    EndDraw();
            }
        }

        private void PulseStartDot()
        {
            if (templateDots.Count == 0 || isDrawing) return;
            // First dot in templateDots is the start dot
            Image startDot = templateDots[templateDots.Count > 0 ? templateDots.Count - templateDots.Count : 0];
            // Actually the start dot is added after template lines + normal dots.
            // Start dot is the last-added dot for index 0, but we added them in order.
            // The first dot (index 0) is the start dot.
            if (templateDots[0] != null)
            {
                float a = 0.55f + 0.35f * Mathf.Sin(Time.time * 4.5f);
                Color c = StartDotColor;
                c.a = a;
                templateDots[0].color = c;
                float s = 34f + 6f * Mathf.Sin(Time.time * 4.5f);
                templateDots[0].rectTransform.sizeDelta = new Vector2(s, s);
            }
        }

        private bool ScreenToDrawAreaLocal(Vector2 screenPos, out Vector2 localPos)
        {
            Camera cam = null;
            if (parentCanvasRef != null && parentCanvasRef.renderMode != RenderMode.ScreenSpaceOverlay)
                cam = parentCanvasRef.worldCamera;

            return RectTransformUtility.ScreenPointToLocalPointInRectangle(
                drawArea, screenPos, cam, out localPos);
        }

        private bool IsInsideDrawArea(Vector2 localPos)
        {
            float half = DrawAreaSize * 0.5f;
            return localPos.x >= -half && localPos.x <= half &&
                   localPos.y >= -half && localPos.y <= half;
        }

        private void TryBeginDraw(Vector2 screenPos)
        {
            if (isDrawing) return;

            if (!ScreenToDrawAreaLocal(screenPos, out Vector2 localPos)) return;
            if (!IsInsideDrawArea(localPos)) return;

            ClearStroke();
            isDrawing = true;
            drawnPoints.Add(localPos);
            lastDrawnPoint = localPos;
        }

        private void TryContinueDraw(Vector2 screenPos)
        {
            if (!isDrawing) return;

            if (!ScreenToDrawAreaLocal(screenPos, out Vector2 localPos)) return;

            // Clamp to draw area
            float half = DrawAreaSize * 0.5f;
            localPos.x = Mathf.Clamp(localPos.x, -half, half);
            localPos.y = Mathf.Clamp(localPos.y, -half, half);

            float dist = Vector2.Distance(localPos, lastDrawnPoint);
            if (dist < SampleInterval) return;

            AddStrokeSegment(lastDrawnPoint, localPos);
            drawnPoints.Add(localPos);
            lastDrawnPoint = localPos;

            // Chirp every ~5th sample for tactile audio feedback
            if (drawnPoints.Count % 5 == 0)
                StoryMintSFX.TraceDot();
        }

        private void EndDraw()
        {
            if (!isDrawing) return;
            isDrawing = false;

            if (drawnPoints.Count < 5)
            {
                // Too short, just clear
                ClearStroke();
                return;
            }

            StartCoroutine(EvaluateDrawing());
        }

        // ---------------------------------------------------------------
        //  Accuracy Evaluation
        // ---------------------------------------------------------------

        private IEnumerator EvaluateDrawing()
        {
            string runeName = correctSequence[currentRuneIndex];
            List<Vector2> template = GetTemplate(runeName);

            // Build the template path as densely sampled local-space points
            List<Vector2> templatePath = ResamplePath(
                ConvertTemplateToLocal(template), 80);
            List<Vector2> drawnPath = ResamplePath(drawnPoints, 80);

            if (templatePath.Count < 2 || drawnPath.Count < 2)
            {
                ClearStroke();
                yield break;
            }

            // Calculate bidirectional accuracy:
            // 1) How close is each drawn point to the nearest template point
            // 2) What fraction of the template path was "covered" by the drawing
            float avgDrawnDist = AverageMinDistance(drawnPath, templatePath);
            float coverage = CalculateCoverage(templatePath, drawnPath, DrawAreaSize * 0.12f);

            // Normalise drawn distance to a 0-1 score (closer = higher)
            float maxAllowedDist = DrawAreaSize * 0.15f;
            float distScore = Mathf.Clamp01(1f - avgDrawnDist / maxAllowedDist);

            float finalScore = distScore * 0.5f + coverage * 0.5f;

            int pct = Mathf.RoundToInt(finalScore * 100f);
            bool passed = finalScore >= AccuracyThreshold;

            if (passed)
            {
                ColorStroke(StrokePassColor);
                ShowAccuracy(pct, true);
                SetInstruction("Perfekt gezeichnet!");
                OnStatusChanged?.Invoke("Rune " + runeName + " versiegelt!");
                StoryMintSFX.Success();

                yield return new WaitForSeconds(0.8f);

                AddCompletedMark();
                currentRuneIndex++;
                UpdateProgress();

                if (currentRuneIndex >= correctSequence.Count)
                {
                    isCompleted = true;
                    SetInstruction("Alle Siegel gezeichnet! Der Riss schliesst sich.");
                    runeNameText.text = "";
                    StoryMintSFX.Victory();
                    StartCoroutine(VictoryAnimation());
                }
                else
                {
                    yield return new WaitForSeconds(0.3f);
                    ShowCurrentRune();
                }
            }
            else
            {
                mistakeCount++;
                ColorStroke(StrokeFailColor);
                ShowAccuracy(pct, false);
                SetInstruction("Nicht genau genug — zeichne nochmal!");
                StoryMintSFX.Error();
                OnStatusChanged?.Invoke("Siegel ungenuegend!");

                yield return new WaitForSeconds(1.0f);

                ClearStroke();
                HideAccuracy();
                UpdateProgress();
                SetInstruction("Zeichne das Symbol nach! Starte beim gruenen Punkt.");
            }
        }

        private List<Vector2> ConvertTemplateToLocal(List<Vector2> template)
        {
            List<Vector2> pts = new List<Vector2>(template.Count);
            for (int i = 0; i < template.Count; i++)
                pts.Add(TemplateToLocal(template[i]));
            return pts;
        }

        /// <summary>
        /// Resample a polyline to N evenly spaced points.
        /// </summary>
        private List<Vector2> ResamplePath(List<Vector2> points, int targetCount)
        {
            if (points == null || points.Count < 2 || targetCount < 2)
                return points ?? new List<Vector2>();

            // Calculate total length
            float totalLen = 0f;
            for (int i = 1; i < points.Count; i++)
                totalLen += Vector2.Distance(points[i - 1], points[i]);

            if (totalLen < 0.001f)
                return new List<Vector2>(points);

            float segLen = totalLen / (targetCount - 1);
            List<Vector2> result = new List<Vector2>(targetCount) { points[0] };

            float carry = 0f;
            int srcIdx = 1;

            while (result.Count < targetCount && srcIdx < points.Count)
            {
                float d = Vector2.Distance(points[srcIdx - 1], points[srcIdx]);
                if (carry + d >= segLen)
                {
                    float remaining = segLen - carry;
                    float t = remaining / d;
                    Vector2 newPt = Vector2.Lerp(points[srcIdx - 1], points[srcIdx], t);
                    result.Add(newPt);

                    // Insert new point so we keep walking from there
                    points.Insert(srcIdx, newPt);
                    carry = 0f;
                    srcIdx++;
                }
                else
                {
                    carry += d;
                    srcIdx++;
                }
            }

            // Pad if we came up short
            while (result.Count < targetCount)
                result.Add(points[points.Count - 1]);

            return result;
        }

        private float AverageMinDistance(List<Vector2> from, List<Vector2> to)
        {
            if (from.Count == 0) return float.MaxValue;

            float sum = 0f;
            for (int i = 0; i < from.Count; i++)
            {
                float minD = float.MaxValue;
                for (int j = 0; j < to.Count; j++)
                {
                    float d = Vector2.Distance(from[i], to[j]);
                    if (d < minD) minD = d;
                }
                sum += minD;
            }
            return sum / from.Count;
        }

        /// <summary>
        /// What fraction of template points are "covered" (have a drawn point
        /// within maxDist pixels).
        /// </summary>
        private float CalculateCoverage(List<Vector2> template, List<Vector2> drawn, float maxDist)
        {
            if (template.Count == 0) return 0f;
            int covered = 0;
            for (int i = 0; i < template.Count; i++)
            {
                for (int j = 0; j < drawn.Count; j++)
                {
                    if (Vector2.Distance(template[i], drawn[j]) <= maxDist)
                    {
                        covered++;
                        break;
                    }
                }
            }
            return (float)covered / template.Count;
        }

        // ---------------------------------------------------------------
        //  Accuracy Display
        // ---------------------------------------------------------------

        private void ShowAccuracy(int pct, bool passed)
        {
            if (accuracyText == null) return;
            accuracyText.text = pct + "% Genauigkeit";
            accuracyText.color = passed
                ? new Color(0.20f, 0.90f, 0.50f, 1f)
                : new Color(0.92f, 0.35f, 0.25f, 1f);
        }

        private void HideAccuracy()
        {
            if (accuracyText != null)
                accuracyText.color = new Color(0.8f, 0.9f, 1f, 0f);
        }

        // ---------------------------------------------------------------
        //  Completed Rune Marks (small dots at bottom)
        // ---------------------------------------------------------------

        private void AddCompletedMark()
        {
            float dotSize = 18f;
            float spacing = 28f;
            int total = correctSequence.Count;
            float totalWidth = (total - 1) * spacing;
            float startX = -totalWidth * 0.5f;

            // Rebuild all marks
            for (int i = completedMarks.Count - 1; i >= 0; i--)
            {
                if (completedMarks[i] != null) Destroy(completedMarks[i].gameObject);
            }
            completedMarks.Clear();

            for (int i = 0; i < total; i++)
            {
                GameObject mObj = new GameObject("Mark" + i, typeof(RectTransform), typeof(Image));
                RectTransform mr = mObj.GetComponent<RectTransform>();
                mr.SetParent(container, false);
                SetAnchors(mr, 0.5f, 0f, 0.5f, 0f, new Vector2(0.5f, 0f));
                mr.anchoredPosition = new Vector2(startX + i * spacing, 110f);
                mr.sizeDelta = new Vector2(dotSize, dotSize);

                Image img = mObj.GetComponent<Image>();
                bool done = i < currentRuneIndex + 1; // +1 because we just completed one
                img.color = done
                    ? new Color(0.20f, 0.90f, 0.50f, 0.9f)
                    : new Color(0.3f, 0.4f, 0.5f, 0.3f);
                img.raycastTarget = false;

                completedMarks.Add(img);
            }
        }

        // ---------------------------------------------------------------
        //  Victory
        // ---------------------------------------------------------------

        private IEnumerator VictoryAnimation()
        {
            ClearTemplate();
            ClearStroke();

            // Flash green bursts
            for (int r = 0; r < 3; r++)
            {
                for (int i = 0; i < completedMarks.Count; i++)
                {
                    if (completedMarks[i] != null)
                        completedMarks[i].color = new Color(0.3f, 1f, 0.6f, 1f);
                }
                if (accuracyText != null)
                {
                    accuracyText.text = "SIEGEL VOLLENDET!";
                    accuracyText.color = new Color(0.3f, 1f, 0.6f, 1f);
                }
                yield return new WaitForSeconds(0.25f);

                for (int i = 0; i < completedMarks.Count; i++)
                {
                    if (completedMarks[i] != null)
                        completedMarks[i].color = new Color(0.20f, 0.90f, 0.50f, 0.9f);
                }
                if (accuracyText != null)
                    accuracyText.color = new Color(0.20f, 0.90f, 0.50f, 0.8f);
                yield return new WaitForSeconds(0.2f);
            }

            yield return new WaitForSeconds(0.5f);
            OnCompleted?.Invoke();
        }

        // ---------------------------------------------------------------
        //  Helpers
        // ---------------------------------------------------------------

        private void UpdateProgress()
        {
            if (progressText != null)
                progressText.text = currentRuneIndex + " / " + correctSequence.Count + " Runen" +
                    (mistakeCount > 0 ? "  |  Fehler: " + mistakeCount : "");
        }

        private void SetInstruction(string text)
        {
            if (instructionText != null) instructionText.text = text;
        }

        private static void SetAnchors(RectTransform rt, float minX, float minY, float maxX, float maxY, Vector2 pivot)
        {
            rt.anchorMin = new Vector2(minX, minY);
            rt.anchorMax = new Vector2(maxX, maxY);
            rt.pivot = pivot;
        }

        private static TextMeshProUGUI CreateText(RectTransform parent, string name, float fontSize, FontStyles style)
        {
            GameObject obj = new GameObject(name, typeof(RectTransform));
            obj.transform.SetParent(parent, false);
            TextMeshProUGUI tmp = obj.AddComponent<TextMeshProUGUI>();
            tmp.fontSize = fontSize;
            tmp.fontStyle = style;
            tmp.color = Color.white;
            tmp.font = TMP_Settings.defaultFontAsset;
            return tmp;
        }
    }
}
