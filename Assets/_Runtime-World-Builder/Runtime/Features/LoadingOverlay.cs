using UnityEngine;
using UnityEngine.UIElements;

namespace RuntimeWorldBuilder.Runtime.Import
{
    /// Full-screen "please wait" overlay created in code, so no UXML changes are needed.
    /// Usage: Show() -> wait two frames so it can paint -> heavy work -> Set(...) as phases change -> Hide().
    /// While visible it blocks all pointer input to the UI and to scripts that check the UI panel.
    public sealed class LoadingOverlay
    {
        static readonly Color Accent = new Color(0.30f, 0.65f, 1f);

        readonly VisualElement overlay, spinner, barFill;
        readonly Label titleLabel, detailLabel, percentLabel;
        readonly IVisualElementScheduledItem anim;
        float angle, phase, progress = -1f;

        public bool IsVisible { get; private set; }

        public LoadingOverlay(VisualElement root)
        {
            overlay = new VisualElement { name = "loading-overlay", pickingMode = PickingMode.Position };
            var o = overlay.style;
            o.position = Position.Absolute;
            o.left = 0; o.top = 0; o.right = 0; o.bottom = 0;
            o.alignItems = Align.Center;
            o.justifyContent = Justify.Center;
            o.backgroundColor = new Color(0.04f, 0.05f, 0.07f, 0.85f);
            o.display = DisplayStyle.None;

            var card = new VisualElement();
            var c = card.style;
            c.width = 400;
            c.paddingTop = 28; c.paddingBottom = 28; c.paddingLeft = 24; c.paddingRight = 24;
            c.alignItems = Align.Center;
            c.backgroundColor = new Color(0.10f, 0.12f, 0.16f, 1f);
            Radius(card, 12);

            spinner = new VisualElement();
            spinner.style.width = 44;
            spinner.style.height = 44;
            Radius(spinner, 22);
            Border(spinner, 4, new Color(0.25f, 0.30f, 0.38f));
            spinner.style.borderTopColor = Accent;

            titleLabel = new Label("Loading");
            titleLabel.style.fontSize = 20;
            titleLabel.style.unityFontStyleAndWeight = FontStyle.Bold;
            titleLabel.style.color = Color.white;
            titleLabel.style.marginTop = 16;
            titleLabel.style.unityTextAlign = TextAnchor.MiddleCenter;

            detailLabel = new Label("");
            detailLabel.style.fontSize = 13;
            detailLabel.style.color = new Color(0.70f, 0.75f, 0.82f);
            detailLabel.style.marginTop = 6;
            detailLabel.style.whiteSpace = WhiteSpace.Normal;
            detailLabel.style.unityTextAlign = TextAnchor.MiddleCenter;

            var track = new VisualElement();
            track.style.width = 320;
            track.style.height = 8;
            track.style.marginTop = 18;
            track.style.backgroundColor = new Color(0.20f, 0.23f, 0.29f);
            track.style.overflow = Overflow.Hidden;
            Radius(track, 4);

            barFill = new VisualElement();
            barFill.style.position = Position.Absolute;
            barFill.style.top = 0;
            barFill.style.bottom = 0;
            barFill.style.left = 0;
            barFill.style.width = 0;
            barFill.style.backgroundColor = Accent;
            track.Add(barFill);

            percentLabel = new Label("");
            percentLabel.style.fontSize = 12;
            percentLabel.style.color = new Color(0.70f, 0.75f, 0.82f);
            percentLabel.style.marginTop = 8;

            card.Add(spinner);
            card.Add(titleLabel);
            card.Add(detailLabel);
            card.Add(track);
            card.Add(percentLabel);
            overlay.Add(card);
            root.Add(overlay);

            anim = overlay.schedule.Execute(Tick).Every(16);
            anim.Pause();
        }

        public void Show(string title, string detail = "")
        {
            titleLabel.text = title;
            detailLabel.text = detail;
            SetProgress(-1f);
            overlay.style.display = DisplayStyle.Flex;
            overlay.BringToFront();
            IsVisible = true;
            anim.Resume();
        }

        /// progress 0..1 shows a filled bar with a percentage; a negative value shows a moving bar.
        public void Set(string detail, float progress = -1f)
        {
            detailLabel.text = detail;
            SetProgress(progress);
        }

        public void Hide()
        {
            overlay.style.display = DisplayStyle.None;
            IsVisible = false;
            anim.Pause();
        }

        void SetProgress(float p)
        {
            progress = p;
            if (p < 0f) { percentLabel.text = ""; return; }

            p = Mathf.Clamp01(p);
            barFill.style.left = 0;
            barFill.style.width = Length.Percent(p * 100f);
            percentLabel.text = Mathf.RoundToInt(p * 100f) + "%";
        }

        void Tick()
        {
            angle = (angle + 9f) % 360f;
            spinner.transform.rotation = Quaternion.Euler(0f, 0f, angle);

            if (progress < 0f)
            {
                phase += 0.06f;
                float t = (Mathf.Sin(phase) + 1f) * 0.5f;
                barFill.style.width = Length.Percent(35f);
                barFill.style.left = Length.Percent(t * 65f);
            }
        }

        static void Radius(VisualElement e, float r)
        {
            e.style.borderTopLeftRadius = r;
            e.style.borderTopRightRadius = r;
            e.style.borderBottomLeftRadius = r;
            e.style.borderBottomRightRadius = r;
        }

        static void Border(VisualElement e, float w, Color col)
        {
            e.style.borderTopWidth = w; e.style.borderBottomWidth = w;
            e.style.borderLeftWidth = w; e.style.borderRightWidth = w;
            e.style.borderTopColor = col; e.style.borderBottomColor = col;
            e.style.borderLeftColor = col; e.style.borderRightColor = col;
        }
    }
}