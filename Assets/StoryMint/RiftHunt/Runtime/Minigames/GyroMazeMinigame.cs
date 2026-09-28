using System;
using System.Collections;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

namespace StoryMint.RiftHunt.Minigames
{
    /// <summary>
    /// Spiel 3 – Gyro-Labyrinth.
    /// Tilt the phone to move an energy ball through a seal labyrinth to the goal.
    /// S-curve maze with verified open path and holes that respawn the ball.
    /// Uses StoryMintDeviceMotion jslib for WebGL DeviceMotion access.
    /// </summary>
    public sealed class GyroMazeMinigame : MonoBehaviour
    {
        public event Action OnCompleted;
        public event Action<string> OnStatusChanged;

        // ---------------------------------------------------------------
        //  JS bridge (WebGL only)
        // ---------------------------------------------------------------

#if UNITY_WEBGL && !UNITY_EDITOR
        [DllImport("__Internal")] private static extern void   StoryMintDM_RequestPermission();
        [DllImport("__Internal")] private static extern float  StoryMintDM_GetX();
        [DllImport("__Internal")] private static extern float  StoryMintDM_GetY();
        [DllImport("__Internal")] private static extern int    StoryMintDM_IsListening();
        [DllImport("__Internal")] private static extern int    StoryMintDM_HasData();
#else
        private static void  StoryMintDM_RequestPermission() { }
        private static float StoryMintDM_GetX() => Input.acceleration.x;
        private static float StoryMintDM_GetY() => Input.acceleration.y;
        private static int   StoryMintDM_IsListening() => 1;
        private static int   StoryMintDM_HasData() => 1;
#endif

        private RectTransform container;
        private RectTransform mazeArea;
        private RectTransform ball;
        private RectTransform goal;
        private Image ballImage;
        private Image goalImage;
        private TextMeshProUGUI instructionText;
        private TextMeshProUGUI deathCounterText;
        private Button startButton;
        private GameObject startButtonObj;
        private bool isCompleted;
        private bool motionReady;
        private bool respawning;
        private float motionWaitTimer;
        private int respawnCount;

        private Vector2 ballVelocity;
        private Vector2 spawnPosition;
        private const float BallRadius = 20f;
        private const float GoalRadius = 26f;
        private const float HoleRadius = 22f;
        private const float TiltSensitivity = 2200f;
        private const float Damping = 0.90f;
        private const float MaxSpeed = 700f;
        private const float WallThickness = 10f;

        private float mazeWidth;
        private float mazeHeight;
        private MazeWall[] walls;

        // Holes (traps)
        private Vector2[] holePositions;
        private readonly List<Image> holeImages = new List<Image>();

        // Trail effect
        private readonly RectTransform[] trailDots = new RectTransform[6];
        private int trailIndex;
        private float trailTimer;

        private static readonly Color BgColor       = new Color(0.03f, 0.05f, 0.10f, 0.94f);
        private static readonly Color MazeFloorColor= new Color(0.06f, 0.08f, 0.14f, 1f);
        private static readonly Color WallColor     = new Color(0.20f, 0.50f, 0.78f, 0.92f);
        private static readonly Color WallGlowColor = new Color(0.35f, 0.65f, 0.95f, 0.5f);
        private static readonly Color BallColor     = new Color(0.95f, 0.82f, 0.18f, 1f);
        private static readonly Color GoalColor     = new Color(0.15f, 0.85f, 0.45f, 1f);
        private static readonly Color HoleColor     = new Color(0.85f, 0.12f, 0.10f, 0.65f);
        private static readonly Color HoleGlowColor = new Color(0.95f, 0.18f, 0.15f, 0.25f);
        private static readonly Color TrailColor    = new Color(0.95f, 0.82f, 0.18f, 0.25f);

        private struct MazeWall
        {
            public float x, y, w, h;
            public MazeWall(float x, float y, float w, float h)
            { this.x = x; this.y = y; this.w = w; this.h = h; }
        }

        // ---------------------------------------------------------------
        //  Public API
        // ---------------------------------------------------------------

        public void Initialise(Canvas parentCanvas, int layoutIndex = 0)
        {
            isCompleted = false;
            respawning = false;
            motionReady = false;
            motionWaitTimer = 0f;
            respawnCount = 0;
            ballVelocity = Vector2.zero;
            BuildUI(parentCanvas);
            GenerateMaze(layoutIndex);
            PlaceBallGoalAndHoles();
            CreateTrailDots();

#if UNITY_WEBGL && !UNITY_EDITOR
            ShowStartButton();
#else
            motionReady = true;
            SetInstruction("Kippe dein Handy zum Ziel — meide die roten Loecher!");
#endif
        }

        public void Cleanup()
        {
            StopAllCoroutines();
            if (container != null)
                Destroy(container.gameObject);
        }

        // ---------------------------------------------------------------
        //  Permission flow (WebGL)
        // ---------------------------------------------------------------

        private void ShowStartButton()
        {
            SetInstruction("Bewegungssensor wird benoetigt.");

            startButtonObj = new GameObject("StartMotionBtn", typeof(RectTransform), typeof(Image), typeof(Button));
            RectTransform btnRect = startButtonObj.GetComponent<RectTransform>();
            btnRect.SetParent(container, false);
            btnRect.anchorMin = new Vector2(0.5f, 0.5f);
            btnRect.anchorMax = new Vector2(0.5f, 0.5f);
            btnRect.pivot = new Vector2(0.5f, 0.5f);
            btnRect.anchoredPosition = new Vector2(0f, 200f);
            btnRect.sizeDelta = new Vector2(500f, 100f);

            Image btnImg = startButtonObj.GetComponent<Image>();
            btnImg.color = new Color(0.18f, 0.55f, 0.82f, 1f);

            startButton = startButtonObj.GetComponent<Button>();
            startButton.targetGraphic = btnImg;
            startButton.onClick.AddListener(HandleStartMotionTapped);

            TextMeshProUGUI btnLabel = CreateText(btnRect, "Label", 34, FontStyles.Bold);
            btnLabel.rectTransform.anchorMin = Vector2.zero;
            btnLabel.rectTransform.anchorMax = Vector2.one;
            btnLabel.rectTransform.offsetMin = Vector2.zero;
            btnLabel.rectTransform.offsetMax = Vector2.zero;
            btnLabel.alignment = TextAlignmentOptions.Center;
            btnLabel.text = "SENSOR AKTIVIEREN";
            btnLabel.color = Color.white;
        }

        private void HandleStartMotionTapped()
        {
            StoryMintSFX.ResumeContext(); // unlock Web Audio from user gesture
            StoryMintSFX.Tap();
            StoryMintDM_RequestPermission();
            if (startButtonObj != null)
                Destroy(startButtonObj);
            SetInstruction("Warte auf Bewegungsdaten...");
            motionWaitTimer = 0f;
        }

        // ---------------------------------------------------------------
        //  Update loop
        // ---------------------------------------------------------------

        private void Update()
        {
            if (isCompleted || ball == null || mazeArea == null || respawning) return;

            // Wait for DeviceMotion data
            if (!motionReady)
            {
                if (startButtonObj != null) return;
                motionWaitTimer += Time.deltaTime;
                if (StoryMintDM_HasData() == 1)
                {
                    motionReady = true;
                    SetInstruction("Kippe dein Handy zum Ziel — meide die roten Loecher!");
                }
                else if (motionWaitTimer > 5f)
                {
                    motionReady = true;
                    SetInstruction("Sensor nicht verfuegbar — versuche es trotzdem.");
                }
                return;
            }

            // Read tilt
            float tiltX, tiltY;
#if UNITY_WEBGL && !UNITY_EDITOR
            tiltX = StoryMintDM_GetX();
            tiltY = StoryMintDM_GetY();
#else
            tiltX = Input.acceleration.x;
            tiltY = Input.acceleration.y;
#endif

            Vector2 tilt = new Vector2(tiltX, tiltY);
            ballVelocity += tilt * TiltSensitivity * Time.deltaTime;
            ballVelocity *= Damping;

            if (ballVelocity.magnitude > MaxSpeed)
                ballVelocity = ballVelocity.normalized * MaxSpeed;

            Vector2 newPos = ball.anchoredPosition + ballVelocity * Time.deltaTime;

            // Clamp to maze bounds
            float halfW = mazeWidth / 2f;
            float halfH = mazeHeight / 2f;
            newPos.x = Mathf.Clamp(newPos.x, -halfW + BallRadius, halfW - BallRadius);
            newPos.y = Mathf.Clamp(newPos.y, -halfH + BallRadius, halfH - BallRadius);

            // Wall collisions
            if (walls != null)
            {
                for (int i = 0; i < walls.Length; i++)
                    newPos = ResolveWallCollision(newPos, walls[i]);
            }

            ball.anchoredPosition = newPos;

            // Update trail
            UpdateTrail(newPos);

            // Pulse goal
            float pulse = 0.85f + 0.15f * Mathf.Sin(Time.time * 3.5f);
            if (goalImage != null)
                goalImage.color = new Color(GoalColor.r * pulse, GoalColor.g * pulse, GoalColor.b, 1f);

            // Pulse holes
            float holePulse = 0.55f + 0.15f * Mathf.Sin(Time.time * 4f);
            for (int i = 0; i < holeImages.Count; i++)
                holeImages[i].color = new Color(HoleColor.r, HoleColor.g, HoleColor.b, holePulse);

            // Check holes
            if (holePositions != null)
            {
                for (int i = 0; i < holePositions.Length; i++)
                {
                    float dist = Vector2.Distance(newPos, holePositions[i]);
                    if (dist < BallRadius + HoleRadius * 0.5f)
                    {
                        StoryMintSFX.Fall();
                        StartCoroutine(RespawnBall());
                        return;
                    }
                }
            }

            // Check goal
            float goalDist = Vector2.Distance(newPos, goal.anchoredPosition);
            if (goalDist < BallRadius + GoalRadius * 0.6f)
            {
                isCompleted = true;
                SetInstruction("Geschafft! Der letzte Riss ist versiegelt!");
                OnStatusChanged?.Invoke("Labyrinth gemeistert!");
                StoryMintSFX.Victory();
                StartCoroutine(DelayedComplete());
            }
        }

        private IEnumerator RespawnBall()
        {
            respawning = true;
            respawnCount++;
            UpdateDeathCounter();
            OnStatusChanged?.Invoke("In ein Loch gefallen!");

            // Flash ball red
            if (ballImage != null)
                ballImage.color = new Color(0.95f, 0.15f, 0.10f, 1f);

            SetInstruction("Achtung! Zurueck zum Start...");

            yield return new WaitForSeconds(0.6f);

            // Reset ball
            ballVelocity = Vector2.zero;
            ball.anchoredPosition = spawnPosition;
            if (ballImage != null)
                ballImage.color = BallColor;

            // Hide trail
            for (int i = 0; i < trailDots.Length; i++)
            {
                if (trailDots[i] != null)
                    trailDots[i].gameObject.SetActive(false);
            }

            SetInstruction("Kippe dein Handy zum Ziel — meide die roten Loecher!");
            respawning = false;
        }

        private void UpdateTrail(Vector2 pos)
        {
            trailTimer += Time.deltaTime;
            if (trailTimer < 0.04f) return;
            trailTimer = 0f;

            if (trailDots[trailIndex] != null)
            {
                trailDots[trailIndex].anchoredPosition = pos;
                trailDots[trailIndex].gameObject.SetActive(true);
                Image img = trailDots[trailIndex].GetComponent<Image>();
                if (img != null)
                    img.color = TrailColor;
            }
            trailIndex = (trailIndex + 1) % trailDots.Length;

            for (int i = 0; i < trailDots.Length; i++)
            {
                if (trailDots[i] == null) continue;
                Image dotImg = trailDots[i].GetComponent<Image>();
                if (dotImg != null)
                {
                    Color c = dotImg.color;
                    c.a *= 0.88f;
                    if (c.a < 0.03f) c.a = 0f;
                    dotImg.color = c;
                }
            }
        }

        private Vector2 ResolveWallCollision(Vector2 pos, MazeWall wall)
        {
            float wallLeft   = wall.x - wall.w / 2f;
            float wallRight  = wall.x + wall.w / 2f;
            float wallBottom = wall.y - wall.h / 2f;
            float wallTop    = wall.y + wall.h / 2f;

            float closestX = Mathf.Clamp(pos.x, wallLeft, wallRight);
            float closestY = Mathf.Clamp(pos.y, wallBottom, wallTop);

            float dx = pos.x - closestX;
            float dy = pos.y - closestY;
            float distSq = dx * dx + dy * dy;

            if (distSq < BallRadius * BallRadius && distSq > 0.001f)
            {
                float dist = Mathf.Sqrt(distSq);
                float overlap = BallRadius - dist;
                float nx = dx / dist;
                float ny = dy / dist;
                pos.x += nx * overlap;
                pos.y += ny * overlap;

                float dot = ballVelocity.x * nx + ballVelocity.y * ny;
                if (dot < 0)
                {
                    ballVelocity.x -= 2f * dot * nx * 0.5f;
                    ballVelocity.y -= 2f * dot * ny * 0.5f;
                    ballVelocity *= 0.35f;
                }
            }

            return pos;
        }

        // ---------------------------------------------------------------
        //  UI Construction
        // ---------------------------------------------------------------

        private void BuildUI(Canvas parentCanvas)
        {
            GameObject containerObj = new GameObject("GyroMazeContainer", typeof(RectTransform), typeof(Image));
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
            TextMeshProUGUI titleTmp = CreateText(container, "Title", 42, FontStyles.Bold);
            titleTmp.rectTransform.anchorMin = new Vector2(0f, 1f);
            titleTmp.rectTransform.anchorMax = new Vector2(1f, 1f);
            titleTmp.rectTransform.pivot = new Vector2(0.5f, 1f);
            titleTmp.rectTransform.anchoredPosition = new Vector2(0f, -36f);
            titleTmp.rectTransform.sizeDelta = new Vector2(-60f, 56f);
            titleTmp.alignment = TextAlignmentOptions.Center;
            titleTmp.text = "SIEGEL-LABYRINTH";
            titleTmp.color = new Color(0.7f, 0.88f, 1f, 0.9f);

            // Instruction text
            instructionText = CreateText(container, "Instruction", 28, FontStyles.Normal);
            instructionText.rectTransform.anchorMin = new Vector2(0f, 1f);
            instructionText.rectTransform.anchorMax = new Vector2(1f, 1f);
            instructionText.rectTransform.pivot = new Vector2(0.5f, 1f);
            instructionText.rectTransform.anchoredPosition = new Vector2(0f, -96f);
            instructionText.rectTransform.sizeDelta = new Vector2(-60f, 46f);
            instructionText.alignment = TextAlignmentOptions.Center;
            instructionText.enableWordWrapping = true;

            // Death counter (bottom)
            deathCounterText = CreateText(container, "DeathCounter", 24, FontStyles.Normal);
            deathCounterText.rectTransform.anchorMin = new Vector2(0f, 0f);
            deathCounterText.rectTransform.anchorMax = new Vector2(1f, 0f);
            deathCounterText.rectTransform.pivot = new Vector2(0.5f, 0f);
            deathCounterText.rectTransform.anchoredPosition = new Vector2(0f, 30f);
            deathCounterText.rectTransform.sizeDelta = new Vector2(-60f, 36f);
            deathCounterText.alignment = TextAlignmentOptions.Center;
            deathCounterText.color = new Color(0.85f, 0.35f, 0.30f, 0.7f);
            deathCounterText.text = "";

            // Maze area
            mazeWidth = 580f;
            mazeHeight = 580f;

            GameObject mazeObj = new GameObject("MazeArea", typeof(RectTransform), typeof(Image));
            mazeArea = mazeObj.GetComponent<RectTransform>();
            mazeArea.SetParent(container, false);
            mazeArea.anchorMin = new Vector2(0.5f, 0.5f);
            mazeArea.anchorMax = new Vector2(0.5f, 0.5f);
            mazeArea.pivot = new Vector2(0.5f, 0.5f);
            mazeArea.anchoredPosition = new Vector2(0f, -30f);
            mazeArea.sizeDelta = new Vector2(mazeWidth, mazeHeight);

            Image mazeFloor = mazeObj.GetComponent<Image>();
            mazeFloor.color = MazeFloorColor;
            mazeFloor.raycastTarget = false;
        }

        // ---------------------------------------------------------------
        //  Maze generation — verified S-curve with open path
        // ---------------------------------------------------------------
        //
        //  Layout (schematic):
        //
        //   S →→→→→→→→→→→→→ ↓  (gap right)
        //   ←←←←←←←←←←←←← ↓
        //   ↓ (gap left)
        //   ↓ →→→→→→→→→→→→→ ↓  (gap right)
        //   ←←←←←←←←←←←←← ↓
        //   ↓ (gap left)
        //   ↓ →→→→→→→→→→ G
        //

        private void GenerateMaze(int layoutIndex)
        {
            float hw = mazeWidth / 2f;   // 290
            float hh = mazeHeight / 2f;  // 290
            float t = WallThickness;

            // Gap width in pixels (must be > BallRadius*2 + margin)
            // Gap = 130px, ball diameter = 40px → plenty of room

            walls = new MazeWall[]
            {
                // 0-3: Outer walls
                new MazeWall(0f, hh - t / 2f, mazeWidth, t),          // top
                new MazeWall(0f, -hh + t / 2f, mazeWidth, t),         // bottom
                new MazeWall(-hw + t / 2f, 0f, t, mazeHeight),        // left
                new MazeWall(hw - t / 2f, 0f, t, mazeHeight),         // right

                // 4: horizontal at y=170, left side → gap on RIGHT
                //    center x = -65, width = 450  → spans -290 to 160
                new MazeWall(-65f, 170f, 450f, t),

                // 5: horizontal at y=60, right side → gap on LEFT
                //    center x = 65, width = 450   → spans -160 to 290
                new MazeWall(65f, 60f, 450f, t),

                // 6: horizontal at y=-60, left side → gap on RIGHT
                //    center x = -65, width = 450  → spans -290 to 160
                new MazeWall(-65f, -60f, 450f, t),

                // 7: horizontal at y=-170, right side → gap on LEFT
                //    center x = 65, width = 450   → spans -160 to 290
                new MazeWall(65f, -170f, 450f, t),
            };

            // Render walls with glow
            for (int i = 0; i < walls.Length; i++)
            {
                MazeWall w = walls[i];

                // Glow
                GameObject glowObj = new GameObject("WallGlow" + i, typeof(RectTransform), typeof(Image));
                RectTransform glowRect = glowObj.GetComponent<RectTransform>();
                glowRect.SetParent(mazeArea, false);
                glowRect.anchorMin = new Vector2(0.5f, 0.5f);
                glowRect.anchorMax = new Vector2(0.5f, 0.5f);
                glowRect.pivot = new Vector2(0.5f, 0.5f);
                glowRect.anchoredPosition = new Vector2(w.x, w.y);
                glowRect.sizeDelta = new Vector2(w.w + 6f, w.h + 6f);
                Image glowImg = glowObj.GetComponent<Image>();
                glowImg.color = WallGlowColor;
                glowImg.raycastTarget = false;

                // Wall
                GameObject wallObj = new GameObject("Wall" + i, typeof(RectTransform), typeof(Image));
                RectTransform wallRect = wallObj.GetComponent<RectTransform>();
                wallRect.SetParent(mazeArea, false);
                wallRect.anchorMin = new Vector2(0.5f, 0.5f);
                wallRect.anchorMax = new Vector2(0.5f, 0.5f);
                wallRect.pivot = new Vector2(0.5f, 0.5f);
                wallRect.anchoredPosition = new Vector2(w.x, w.y);
                wallRect.sizeDelta = new Vector2(w.w, w.h);
                Image wallImg = wallObj.GetComponent<Image>();
                wallImg.color = WallColor;
                wallImg.raycastTarget = false;
            }
        }

        private void PlaceBallGoalAndHoles()
        {
            // Spawn position: top-left corridor
            spawnPosition = new Vector2(-230f, 235f);

            // Holes — placed near turning gaps where overshooting is dangerous
            holePositions = new Vector2[]
            {
                new Vector2(220f, 120f),    // near right gap of wall at y=170, overshoot down
                new Vector2(-220f, -5f),    // near left gap of wall at y=60, overshoot down
                new Vector2(220f, -110f),   // near right gap of wall at y=-60, overshoot down
                new Vector2(-220f, -210f),  // near left gap of wall at y=-170
                new Vector2(0f, -230f),     // center of bottom corridor — surprise
            };

            // Create holes
            for (int i = 0; i < holePositions.Length; i++)
            {
                // Glow
                GameObject hGlowObj = new GameObject("HoleGlow" + i, typeof(RectTransform), typeof(Image));
                RectTransform hGlowRect = hGlowObj.GetComponent<RectTransform>();
                hGlowRect.SetParent(mazeArea, false);
                hGlowRect.anchorMin = new Vector2(0.5f, 0.5f);
                hGlowRect.anchorMax = new Vector2(0.5f, 0.5f);
                hGlowRect.pivot = new Vector2(0.5f, 0.5f);
                hGlowRect.anchoredPosition = holePositions[i];
                hGlowRect.sizeDelta = new Vector2(HoleRadius * 2f + 16f, HoleRadius * 2f + 16f);
                Image hGlowImg = hGlowObj.GetComponent<Image>();
                hGlowImg.color = HoleGlowColor;
                hGlowImg.raycastTarget = false;

                // Hole
                GameObject holeObj = new GameObject("Hole" + i, typeof(RectTransform), typeof(Image));
                RectTransform holeRect = holeObj.GetComponent<RectTransform>();
                holeRect.SetParent(mazeArea, false);
                holeRect.anchorMin = new Vector2(0.5f, 0.5f);
                holeRect.anchorMax = new Vector2(0.5f, 0.5f);
                holeRect.pivot = new Vector2(0.5f, 0.5f);
                holeRect.anchoredPosition = holePositions[i];
                holeRect.sizeDelta = new Vector2(HoleRadius * 2f, HoleRadius * 2f);
                Image holeImg = holeObj.GetComponent<Image>();
                holeImg.color = HoleColor;
                holeImg.raycastTarget = false;
                holeImages.Add(holeImg);

                // X label
                TextMeshProUGUI hLabel = CreateText(holeRect, "HLabel", 22, FontStyles.Bold);
                hLabel.rectTransform.anchorMin = Vector2.zero;
                hLabel.rectTransform.anchorMax = Vector2.one;
                hLabel.rectTransform.offsetMin = Vector2.zero;
                hLabel.rectTransform.offsetMax = Vector2.zero;
                hLabel.alignment = TextAlignmentOptions.Center;
                hLabel.text = "X";
                hLabel.color = new Color(1f, 1f, 1f, 0.5f);
            }

            // Goal at bottom-right
            GameObject goalObj = new GameObject("Goal", typeof(RectTransform), typeof(Image));
            goal = goalObj.GetComponent<RectTransform>();
            goal.SetParent(mazeArea, false);
            goal.anchorMin = new Vector2(0.5f, 0.5f);
            goal.anchorMax = new Vector2(0.5f, 0.5f);
            goal.pivot = new Vector2(0.5f, 0.5f);
            goal.anchoredPosition = new Vector2(220f, -240f);
            goal.sizeDelta = new Vector2(GoalRadius * 2f, GoalRadius * 2f);

            goalImage = goalObj.GetComponent<Image>();
            goalImage.color = GoalColor;
            goalImage.raycastTarget = false;

            TextMeshProUGUI goalLabel = CreateText(goal, "GoalLabel", 18, FontStyles.Bold);
            goalLabel.rectTransform.anchorMin = Vector2.zero;
            goalLabel.rectTransform.anchorMax = Vector2.one;
            goalLabel.rectTransform.offsetMin = Vector2.zero;
            goalLabel.rectTransform.offsetMax = Vector2.zero;
            goalLabel.alignment = TextAlignmentOptions.Center;
            goalLabel.text = "ZIEL";
            goalLabel.color = Color.white;

            // Ball at spawn
            GameObject ballObj = new GameObject("Ball", typeof(RectTransform), typeof(Image));
            ball = ballObj.GetComponent<RectTransform>();
            ball.SetParent(mazeArea, false);
            ball.anchorMin = new Vector2(0.5f, 0.5f);
            ball.anchorMax = new Vector2(0.5f, 0.5f);
            ball.pivot = new Vector2(0.5f, 0.5f);
            ball.anchoredPosition = spawnPosition;
            ball.sizeDelta = new Vector2(BallRadius * 2f, BallRadius * 2f);

            ballImage = ballObj.GetComponent<Image>();
            ballImage.color = BallColor;
            ballImage.raycastTarget = false;
        }

        private void CreateTrailDots()
        {
            for (int i = 0; i < trailDots.Length; i++)
            {
                GameObject dotObj = new GameObject("Trail" + i, typeof(RectTransform), typeof(Image));
                RectTransform dotRect = dotObj.GetComponent<RectTransform>();
                dotRect.SetParent(mazeArea, false);
                dotRect.anchorMin = new Vector2(0.5f, 0.5f);
                dotRect.anchorMax = new Vector2(0.5f, 0.5f);
                dotRect.pivot = new Vector2(0.5f, 0.5f);
                dotRect.sizeDelta = new Vector2(12f, 12f);
                dotRect.anchoredPosition = Vector2.zero;

                Image dotImg = dotObj.GetComponent<Image>();
                dotImg.color = new Color(TrailColor.r, TrailColor.g, TrailColor.b, 0f);
                dotImg.raycastTarget = false;

                dotObj.SetActive(false);
                trailDots[i] = dotRect;
            }

            if (ball != null)
                ball.SetAsLastSibling();
        }

        private void UpdateDeathCounter()
        {
            if (deathCounterText != null && respawnCount > 0)
                deathCounterText.text = "Gefallen: " + respawnCount;
        }

        // ---------------------------------------------------------------
        //  Helpers
        // ---------------------------------------------------------------

        private IEnumerator DelayedComplete()
        {
            yield return new WaitForSeconds(1.2f);
            OnCompleted?.Invoke();
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
