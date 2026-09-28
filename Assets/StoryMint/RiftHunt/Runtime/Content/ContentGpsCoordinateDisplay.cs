using System;
using System.Globalization;
using System.Text;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace StoryMint.RiftHunt
{
    [DisallowMultipleComponent]
    public sealed class ContentGpsCoordinateDisplay : MonoBehaviour
    {
        [SerializeField] private bool startWatchingOnEnable = true;
        [SerializeField] private bool stopWatchingOnDisable;
        [SerializeField, Range(5, 10)] private int coordinateDecimals = 8;
        [SerializeField] private float refreshIntervalSeconds = 0.25f;

        private StoryMintLocationService locationService;
        private TextMeshProUGUI statusText;
        private TextMeshProUGUI latitudeText;
        private TextMeshProUGUI longitudeText;
        private TextMeshProUGUI accuracyText;
        private TextMeshProUGUI detailsText;
        private TextMeshProUGUI jsonText;
        private TMP_InputField targetIdInput;
        private TMP_InputField locationNameInput;
        private TMP_InputField radiusInput;
        private TMP_InputField requiredAccuracyInput;
        private TextMeshProUGUI contentJsonText;
        private TextMeshProUGUI photoHintText;
        private TextMeshProUGUI copyFeedbackText;
        private float nextRefreshTime;
        private string lastStatusMessage = string.Empty;

        private static readonly CultureInfo Invariant = CultureInfo.InvariantCulture;
        private const float MinimumGeofenceRadiusMeters = 20f;

        private void OnEnable()
        {
            locationService = StoryMintLocationService.GetOrCreate();
            locationService.LocationUpdated += HandleLocationUpdated;
            locationService.StatusChanged += HandleStatusChanged;

            BuildUi();

            if (startWatchingOnEnable)
                locationService.StartWatching();
            else
                locationService.RequestSingleUpdate();

            RefreshText();
        }

        private void OnDisable()
        {
            if (locationService != null)
            {
                locationService.LocationUpdated -= HandleLocationUpdated;
                locationService.StatusChanged -= HandleStatusChanged;

                if (stopWatchingOnDisable)
                    locationService.StopWatching();
            }
        }

        private void Update()
        {
            if (Time.unscaledTime < nextRefreshTime)
                return;

            nextRefreshTime = Time.unscaledTime + Mathf.Max(0.05f, refreshIntervalSeconds);
            RefreshText();
        }

        private void HandleLocationUpdated(StoryMintLocationFix fix)
        {
            RefreshText();
        }

        private void HandleStatusChanged(StoryMintLocationStatus status, string message)
        {
            lastStatusMessage = message ?? string.Empty;
            RefreshText();
        }

        private void RequestFreshGpsFix()
        {
            copyFeedbackText.text = "GPS wird aktualisiert...";
            locationService.RequestSingleUpdate();
            RefreshText();
        }

        private void RestartGpsWatch()
        {
            copyFeedbackText.text = "GPS-Watch neu gestartet.";
            locationService.StopWatching();
            locationService.StartWatching();
            RefreshText();
        }

        private void CopyJsonLocation()
        {
            string textToCopy = locationService != null && locationService.HasFix
                ? BuildJsonLocationSnippet(locationService.LastFix)
                : "Noch keine GPS-Koordinaten verfuegbar.";

            StoryMintWebGlClipboardBridge.Copy(textToCopy);
            copyFeedbackText.text = locationService != null && locationService.HasFix
                ? "Location-Block kopiert."
                : "Noch keine Koordinaten zum Kopieren.";
        }

        private void CopyContentStepJson()
        {
            bool copied = StoryMintWebGlClipboardBridge.Copy(BuildContentStepSnippet());
            copyFeedbackText.text = copied
                ? (locationService != null && locationService.HasFix
                    ? "Geofence- und Scan-Steps kopiert."
                    : "Snippet kopiert, aber GPS wartet noch.")
                : "Kopieren wurde vom Browser blockiert.";
        }

        private void RefreshText()
        {
            if (locationService == null || statusText == null)
                return;

            statusText.text = BuildStatusLine();

            if (!locationService.HasFix)
            {
                latitudeText.text = "Latitude: wartet auf GPS...";
                longitudeText.text = "Longitude: wartet auf GPS...";
                accuracyText.text = "Genauigkeit: -";
                detailsText.text = "Erlaube Standortzugriff im Browser. Fuer echte Werte braucht WebGL HTTPS oder localhost.";
                jsonText.text = "\"location\": {\n  \"latitude\": 0,\n  \"longitude\": 0\n}";
                RefreshContentAssistant();
                return;
            }

            StoryMintLocationFix fix = locationService.LastFix;
            string latitude = FormatCoordinate(fix.coordinate.latitude);
            string longitude = FormatCoordinate(fix.coordinate.longitude);

            latitudeText.text = "Latitude:  " + latitude;
            longitudeText.text = "Longitude: " + longitude;
            accuracyText.text = "Genauigkeit: +/- " + FormatMeters(fix.accuracyMeters)
                + (fix.isMock ? " (Editor-Mock)" : string.Empty);
            detailsText.text = BuildDetailsLine(fix);
            jsonText.text = BuildJsonLocationSnippet(fix);
            RefreshContentAssistant();
        }

        private string BuildStatusLine()
        {
            string status = locationService.Status.ToString();
            string permission = locationService.PermissionState.ToString();
            string message = !string.IsNullOrWhiteSpace(lastStatusMessage)
                ? " | " + lastStatusMessage
                : string.Empty;

            if (!string.IsNullOrWhiteSpace(locationService.LastError))
                message = " | " + locationService.LastError;

            return "Status: " + status + " | Permission: " + permission + message;
        }

        private string BuildDetailsLine(StoryMintLocationFix fix)
        {
            string timestamp = FormatTimestamp(fix);
            string altitude = fix.hasAltitude
                ? " | Altitude: " + fix.altitudeMeters.ToString("F1", Invariant) + " m"
                : string.Empty;
            string heading = fix.hasHeading
                ? " | Heading: " + fix.headingDegrees.ToString("F0", Invariant) + " deg"
                : string.Empty;
            string speed = fix.hasSpeed
                ? " | Speed: " + fix.speedMetersPerSecond.ToString("F1", Invariant) + " m/s"
                : string.Empty;

            return "Letzter Fix: " + timestamp + altitude + heading + speed;
        }

        private string BuildJsonLocationSnippet(StoryMintLocationFix fix)
        {
            return "\"location\": {\n"
                + "  \"latitude\": " + FormatCoordinate(fix.coordinate.latitude) + ",\n"
                + "  \"longitude\": " + FormatCoordinate(fix.coordinate.longitude) + "\n"
                + "}";
        }

        private void RefreshContentAssistant()
        {
            if (contentJsonText == null)
                return;

            string targetId = GetTargetId();
            contentJsonText.text = BuildContentStepSnippet();
            photoHintText.text = "Triggerbild-Datei: Assets/Resources/StoryMint/Targets/" + targetId
                + ".jpg oder .png  |  Foto extern aufnehmen: scharf, frontal, detailreich, keine Spiegelung.";
        }

        private string BuildContentStepSnippet()
        {
            string targetId = GetTargetId();
            string locationName = GetLocationName();
            string geofenceStepId = targetId + "-gps";
            string scanStepId = targetId + "-scan";
            float radiusMeters = Mathf.Max(MinimumGeofenceRadiusMeters, GetInputFloat(radiusInput, 35f));
            float requiredAccuracyMeters = GetInputFloat(requiredAccuracyInput, 80f);

            double latitude = 0d;
            double longitude = 0d;
            if (locationService != null && locationService.HasFix)
            {
                StoryMintLocationFix fix = locationService.LastFix;
                latitude = fix.coordinate.latitude;
                longitude = fix.coordinate.longitude;
            }

            return "{\n"
                + "  \"id\": \"" + EscapeJson(geofenceStepId) + "\",\n"
                + "  \"type\": \"geofence\",\n"
                + "  \"title\": \"Story-Ort erreichen\",\n"
                + "  \"body\": \"Geh zum Story-Ort und warte, bis GPS genau genug ist.\",\n"
                + "  \"locationName\": \"" + EscapeJson(locationName) + "\",\n"
                + "  \"location\": {\n"
                + "    \"latitude\": " + FormatCoordinate(latitude) + ",\n"
                + "    \"longitude\": " + FormatCoordinate(longitude) + "\n"
                + "  },\n"
                + "  \"geofenceRadiusMeters\": " + FormatJsonFloat(radiusMeters) + ",\n"
                + "  \"requiredAccuracyMeters\": " + FormatJsonFloat(requiredAccuracyMeters) + ",\n"
                + "  \"allowLocationFallback\": true,\n"
                + "  \"primaryActionLabel\": \"Fallback nutzen\",\n"
                + "  \"nextStepId\": \"" + EscapeJson(scanStepId) + "\"\n"
                + "},\n"
                + "{\n"
                + "  \"id\": \"" + EscapeJson(scanStepId) + "\",\n"
                + "  \"type\": \"scan\",\n"
                + "  \"title\": \"Triggerbild scannen\",\n"
                + "  \"body\": \"Richte die Kamera auf das Triggerbild.\",\n"
                + "  \"targetId\": \"" + EscapeJson(targetId) + "\",\n"
                + "  \"successMessage\": \"Signal gefunden.\",\n"
                + "  \"nextStepId\": \"\"\n"
                + "}";
        }

        private string FormatCoordinate(double value)
        {
            int decimals = Mathf.Clamp(coordinateDecimals, 5, 10);
            return value.ToString("F" + decimals, Invariant);
        }

        private string GetTargetId()
        {
            string input = targetIdInput != null ? targetIdInput.text : string.Empty;
            return SanitizeTargetId(input);
        }

        private string GetLocationName()
        {
            string input = locationNameInput != null ? locationNameInput.text : string.Empty;
            return string.IsNullOrWhiteSpace(input) ? "Neuer Story-Ort" : input.Trim();
        }

        private static string SanitizeTargetId(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return "neues-triggerbild";

            StringBuilder builder = new StringBuilder(value.Length);
            bool previousSeparator = false;
            string lower = value.Trim().ToLowerInvariant();

            for (int index = 0; index < lower.Length; index++)
            {
                char character = lower[index];
                bool isAsciiLetter = character >= 'a' && character <= 'z';
                bool isDigit = character >= '0' && character <= '9';

                if (AppendGermanAsciiReplacement(builder, character))
                {
                    previousSeparator = false;
                    continue;
                }

                if (isAsciiLetter || isDigit)
                {
                    builder.Append(character);
                    previousSeparator = false;
                    continue;
                }

                if (character == '-' || character == '_' || char.IsWhiteSpace(character))
                {
                    if (builder.Length > 0 && !previousSeparator)
                    {
                        builder.Append('-');
                        previousSeparator = true;
                    }
                }
            }

            string sanitized = builder.ToString().Trim('-');
            return string.IsNullOrWhiteSpace(sanitized) ? "neues-triggerbild" : sanitized;
        }

        private static bool AppendGermanAsciiReplacement(StringBuilder builder, char character)
        {
            switch (character)
            {
                case 'ä':
                    builder.Append("ae");
                    return true;
                case 'ö':
                    builder.Append("oe");
                    return true;
                case 'ü':
                    builder.Append("ue");
                    return true;
                case 'ß':
                    builder.Append("ss");
                    return true;
                default:
                    return false;
            }
        }

        private static float GetInputFloat(TMP_InputField input, float fallback)
        {
            if (input == null || string.IsNullOrWhiteSpace(input.text))
                return fallback;

            string normalized = input.text.Trim().Replace(',', '.');
            if (float.TryParse(normalized, NumberStyles.Float, Invariant, out float value) && value > 0f)
                return value;

            return fallback;
        }

        private static string FormatJsonFloat(float value)
        {
            return value.ToString("0.##", Invariant);
        }

        private static string EscapeJson(string value)
        {
            if (string.IsNullOrEmpty(value))
                return string.Empty;

            StringBuilder builder = new StringBuilder(value.Length);
            for (int index = 0; index < value.Length; index++)
            {
                char character = value[index];
                switch (character)
                {
                    case '\\':
                        builder.Append("\\\\");
                        break;
                    case '"':
                        builder.Append("\\\"");
                        break;
                    case '\n':
                        builder.Append("\\n");
                        break;
                    case '\r':
                        builder.Append("\\r");
                        break;
                    case '\t':
                        builder.Append("\\t");
                        break;
                    default:
                        builder.Append(character);
                        break;
                }
            }

            return builder.ToString();
        }

        private static string FormatMeters(float meters)
        {
            if (meters <= 0f)
                return "unbekannt";

            return meters.ToString(meters < 10f ? "F1" : "F0", Invariant) + " m";
        }

        private static string FormatTimestamp(StoryMintLocationFix fix)
        {
            if (fix.timestampSeconds <= 0d)
                return "-";

            if (fix.isMock || fix.timestampSeconds < 100000000d)
                return fix.timestampSeconds.ToString("F1", Invariant) + " s";

            try
            {
                long unixMilliseconds = (long)Math.Round(fix.timestampSeconds * 1000d);
                return DateTimeOffset.FromUnixTimeMilliseconds(unixMilliseconds)
                    .ToLocalTime()
                    .ToString("HH:mm:ss", Invariant);
            }
            catch (ArgumentOutOfRangeException)
            {
                return fix.timestampSeconds.ToString("F1", Invariant) + " s";
            }
        }

        private void BuildUi()
        {
            if (statusText != null)
                return;

            EnsureEventSystem();

            GameObject canvasObject = new GameObject("StoryMintContentGpsCanvas", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            canvasObject.transform.SetParent(transform, false);

            Canvas canvas = canvasObject.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 3000;

            CanvasScaler scaler = canvasObject.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1080f, 1920f);
            scaler.matchWidthOrHeight = 0.5f;

            RectTransform canvasRect = canvasObject.GetComponent<RectTransform>();
            CreateBackground(canvasRect);
            RectTransform panel = CreatePanel(canvasRect);

            CreateText("Title", panel, "Content GPS " + StoryMintBuildVersion.Label, 34f, FontStyles.Bold, new Color(1f, 1f, 1f, 1f), 48f);
            statusText = CreateText("Status", panel, string.Empty, 20f, FontStyles.Normal, new Color(0.78f, 0.88f, 1f, 1f), 56f);
            latitudeText = CreateText("Latitude", panel, string.Empty, 32f, FontStyles.Bold, new Color(0.62f, 1f, 0.82f, 1f), 48f);
            longitudeText = CreateText("Longitude", panel, string.Empty, 32f, FontStyles.Bold, new Color(0.62f, 1f, 0.82f, 1f), 48f);
            accuracyText = CreateText("Accuracy", panel, string.Empty, 22f, FontStyles.Normal, Color.white, 36f);
            detailsText = CreateText("Details", panel, string.Empty, 18f, FontStyles.Normal, new Color(0.88f, 0.92f, 0.96f, 1f), 44f);
            jsonText = CreateText("JsonSnippet", panel, string.Empty, 21f, FontStyles.Normal, new Color(1f, 0.93f, 0.68f, 1f), 118f);

            RectTransform buttonRow = CreateButtonRow(panel);
            CreateButton(buttonRow, "GPS neu", RequestFreshGpsFix);
            CreateButton(buttonRow, "Watch neu", RestartGpsWatch);
            CreateButton(buttonRow, "Location", CopyJsonLocation);

            CreateText("AssistantTitle", panel, "Triggerbild-Assistent", 26f, FontStyles.Bold, Color.white, 38f);
            targetIdInput = CreateInputField(panel, "TargetId", "targetId", "neues-triggerbild", "z.B. karlstor", 62f);
            locationNameInput = CreateInputField(panel, "LocationName", "Ort", "Neuer Story-Ort", "z.B. Karlstor", 62f);
            radiusInput = CreateInputField(panel, "RadiusMeters", "Radius m", "35", "35", 62f);
            requiredAccuracyInput = CreateInputField(panel, "AccuracyMeters", "GPS max m", "80", "80", 62f);
            contentJsonText = CreateText("ContentSnippet", panel, string.Empty, 17f, FontStyles.Normal, new Color(1f, 0.93f, 0.68f, 1f), 318f);
            photoHintText = CreateText("PhotoHint", panel, string.Empty, 17f, FontStyles.Normal, new Color(0.88f, 0.92f, 0.96f, 1f), 58f);

            RectTransform contentButtonRow = CreateButtonRow(panel);
            CreateButton(contentButtonRow, "Content kopieren", CopyContentStepJson);

            copyFeedbackText = CreateText("CopyFeedback", panel, string.Empty, 18f, FontStyles.Normal, new Color(0.78f, 0.88f, 1f, 1f), 30f);
        }

        private static RectTransform CreatePanel(RectTransform canvasRect)
        {
            GameObject panelObject = new GameObject("GpsPanel", typeof(RectTransform), typeof(Image), typeof(VerticalLayoutGroup));
            panelObject.transform.SetParent(canvasRect, false);

            RectTransform rect = panelObject.GetComponent<RectTransform>();
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = new Vector2(24f, 24f);
            rect.offsetMax = new Vector2(-24f, -24f);

            Image image = panelObject.GetComponent<Image>();
            image.color = new Color(0.035f, 0.04f, 0.06f, 0.95f);

            VerticalLayoutGroup layout = panelObject.GetComponent<VerticalLayoutGroup>();
            layout.padding = new RectOffset(26, 26, 22, 22);
            layout.spacing = 9f;
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = false;

            return rect;
        }

        private static TextMeshProUGUI CreateText(
            string name,
            RectTransform parent,
            string text,
            float fontSize,
            FontStyles style,
            Color color,
            float preferredHeight)
        {
            GameObject textObject = new GameObject(name, typeof(RectTransform), typeof(TextMeshProUGUI), typeof(LayoutElement));
            textObject.transform.SetParent(parent, false);

            TextMeshProUGUI label = textObject.GetComponent<TextMeshProUGUI>();
            label.text = text;
            label.fontSize = fontSize;
            label.fontStyle = style;
            label.color = color;
            label.enableWordWrapping = true;
            label.overflowMode = TextOverflowModes.Truncate;
            label.alignment = TextAlignmentOptions.Left;
            label.margin = Vector4.zero;

            LayoutElement layoutElement = textObject.GetComponent<LayoutElement>();
            layoutElement.preferredHeight = preferredHeight;
            layoutElement.flexibleWidth = 1f;

            return label;
        }

        private static void CreateBackground(RectTransform canvasRect)
        {
            GameObject backgroundObject = new GameObject("ContentBackground", typeof(RectTransform), typeof(Image));
            backgroundObject.transform.SetParent(canvasRect, false);
            backgroundObject.transform.SetAsFirstSibling();

            RectTransform rect = backgroundObject.GetComponent<RectTransform>();
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;

            Image image = backgroundObject.GetComponent<Image>();
            image.sprite = CreateBackgroundSprite();
            image.color = Color.white;
            image.raycastTarget = false;
        }

        private static Sprite CreateBackgroundSprite()
        {
            const int width = 64;
            const int height = 128;

            Texture2D texture = new Texture2D(width, height, TextureFormat.RGBA32, false)
            {
                name = "StoryMintContentBackground",
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear
            };

            Color top = new Color(0.08f, 0.09f, 0.12f, 1f);
            Color middle = new Color(0.08f, 0.15f, 0.14f, 1f);
            Color bottom = new Color(0.18f, 0.14f, 0.12f, 1f);
            Color accent = new Color(0.2f, 0.46f, 0.44f, 0.18f);

            for (int y = 0; y < height; y++)
            {
                float vertical = y / (float)(height - 1);
                Color baseColor = vertical < 0.58f
                    ? Color.Lerp(bottom, middle, vertical / 0.58f)
                    : Color.Lerp(middle, top, (vertical - 0.58f) / 0.42f);

                for (int x = 0; x < width; x++)
                {
                    float diagonal = Mathf.Abs(((x + y * 0.42f) % 38f) - 19f) / 19f;
                    float line = Mathf.SmoothStep(0.18f, 0f, diagonal);
                    Color pixel = Color.Lerp(baseColor, accent, line * 0.35f);
                    texture.SetPixel(x, y, pixel);
                }
            }

            texture.Apply();
            return Sprite.Create(texture, new Rect(0f, 0f, width, height), new Vector2(0.5f, 0.5f), 100f);
        }

        private TMP_InputField CreateInputField(
            RectTransform parent,
            string name,
            string label,
            string initialValue,
            string placeholder,
            float preferredHeight)
        {
            GameObject rowObject = new GameObject(name + "Row", typeof(RectTransform), typeof(HorizontalLayoutGroup), typeof(LayoutElement));
            rowObject.transform.SetParent(parent, false);

            HorizontalLayoutGroup rowLayout = rowObject.GetComponent<HorizontalLayoutGroup>();
            rowLayout.spacing = 12f;
            rowLayout.childControlWidth = true;
            rowLayout.childControlHeight = true;
            rowLayout.childForceExpandWidth = false;
            rowLayout.childForceExpandHeight = false;

            LayoutElement rowElement = rowObject.GetComponent<LayoutElement>();
            rowElement.preferredHeight = preferredHeight;
            rowElement.flexibleWidth = 1f;

            GameObject labelObject = new GameObject("Label", typeof(RectTransform), typeof(TextMeshProUGUI), typeof(LayoutElement));
            labelObject.transform.SetParent(rowObject.transform, false);

            TextMeshProUGUI labelText = labelObject.GetComponent<TextMeshProUGUI>();
            labelText.text = label;
            labelText.fontSize = 18f;
            labelText.fontStyle = FontStyles.Bold;
            labelText.color = new Color(0.78f, 0.88f, 1f, 1f);
            labelText.enableWordWrapping = false;
            labelText.overflowMode = TextOverflowModes.Ellipsis;
            labelText.alignment = TextAlignmentOptions.Left | TextAlignmentOptions.Midline;

            LayoutElement labelElement = labelObject.GetComponent<LayoutElement>();
            labelElement.preferredWidth = 240f;
            labelElement.preferredHeight = preferredHeight;

            GameObject inputObject = new GameObject("Input", typeof(RectTransform), typeof(Image), typeof(TMP_InputField), typeof(LayoutElement));
            inputObject.transform.SetParent(rowObject.transform, false);

            Image inputImage = inputObject.GetComponent<Image>();
            inputImage.color = new Color(0.08f, 0.12f, 0.16f, 0.95f);

            LayoutElement inputElement = inputObject.GetComponent<LayoutElement>();
            inputElement.flexibleWidth = 1f;
            inputElement.preferredHeight = preferredHeight;

            TextMeshProUGUI inputText = CreateInputFieldText(inputObject.transform, "Text", initialValue, Color.white);
            TextMeshProUGUI placeholderText = CreateInputFieldText(inputObject.transform, "Placeholder", placeholder, new Color(0.58f, 0.66f, 0.74f, 1f));
            placeholderText.fontStyle = FontStyles.Italic;

            TMP_InputField inputField = inputObject.GetComponent<TMP_InputField>();
            inputField.textComponent = inputText;
            inputField.placeholder = placeholderText;
            inputField.text = initialValue;
            inputField.characterLimit = 80;
            inputField.onValueChanged.AddListener(_ => RefreshContentAssistant());

            return inputField;
        }

        private static TextMeshProUGUI CreateInputFieldText(Transform parent, string name, string text, Color color)
        {
            GameObject textObject = new GameObject(name, typeof(RectTransform), typeof(TextMeshProUGUI));
            textObject.transform.SetParent(parent, false);

            RectTransform rect = textObject.GetComponent<RectTransform>();
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = new Vector2(14f, 6f);
            rect.offsetMax = new Vector2(-14f, -6f);

            TextMeshProUGUI label = textObject.GetComponent<TextMeshProUGUI>();
            label.text = text;
            label.fontSize = 21f;
            label.color = color;
            label.enableWordWrapping = false;
            label.overflowMode = TextOverflowModes.Ellipsis;
            label.alignment = TextAlignmentOptions.Left | TextAlignmentOptions.Midline;
            label.margin = Vector4.zero;

            return label;
        }

        private static RectTransform CreateButtonRow(RectTransform parent)
        {
            GameObject rowObject = new GameObject("ButtonRow", typeof(RectTransform), typeof(HorizontalLayoutGroup), typeof(LayoutElement));
            rowObject.transform.SetParent(parent, false);

            HorizontalLayoutGroup layout = rowObject.GetComponent<HorizontalLayoutGroup>();
            layout.spacing = 10f;
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = false;

            LayoutElement layoutElement = rowObject.GetComponent<LayoutElement>();
            layoutElement.preferredHeight = 58f;

            return rowObject.GetComponent<RectTransform>();
        }

        private static void CreateButton(RectTransform parent, string label, UnityEngine.Events.UnityAction onClick)
        {
            GameObject buttonObject = new GameObject(label, typeof(RectTransform), typeof(Image), typeof(Button), typeof(LayoutElement));
            buttonObject.transform.SetParent(parent, false);

            Image image = buttonObject.GetComponent<Image>();
            image.color = new Color(0.12f, 0.38f, 0.55f, 0.95f);

            Button button = buttonObject.GetComponent<Button>();
            button.onClick.AddListener(onClick);
            ColorBlock colors = button.colors;
            colors.normalColor = new Color(0.12f, 0.38f, 0.55f, 0.95f);
            colors.highlightedColor = new Color(0.18f, 0.52f, 0.72f, 1f);
            colors.pressedColor = new Color(0.07f, 0.24f, 0.36f, 1f);
            colors.selectedColor = colors.highlightedColor;
            colors.disabledColor = new Color(0.18f, 0.18f, 0.18f, 0.55f);
            button.colors = colors;

            LayoutElement buttonLayout = buttonObject.GetComponent<LayoutElement>();
            buttonLayout.minHeight = 54f;
            buttonLayout.flexibleWidth = 1f;

            GameObject textObject = new GameObject("Label", typeof(RectTransform), typeof(TextMeshProUGUI));
            textObject.transform.SetParent(buttonObject.transform, false);

            RectTransform textRect = textObject.GetComponent<RectTransform>();
            textRect.anchorMin = Vector2.zero;
            textRect.anchorMax = Vector2.one;
            textRect.offsetMin = new Vector2(8f, 0f);
            textRect.offsetMax = new Vector2(-8f, 0f);

            TextMeshProUGUI buttonText = textObject.GetComponent<TextMeshProUGUI>();
            buttonText.text = label;
            buttonText.fontSize = 19f;
            buttonText.fontStyle = FontStyles.Bold;
            buttonText.color = Color.white;
            buttonText.enableWordWrapping = false;
            buttonText.overflowMode = TextOverflowModes.Ellipsis;
            buttonText.alignment = TextAlignmentOptions.Center;
        }

        private static void EnsureEventSystem()
        {
            if (FindObjectOfType<EventSystem>() != null)
                return;

            GameObject eventSystemObject = new GameObject("EventSystem", typeof(EventSystem), typeof(StandaloneInputModule));
            DontDestroyOnLoad(eventSystemObject);
        }
    }
}
