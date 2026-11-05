using System;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.Sound;

namespace Avir.Artifactorics
{
    /// <summary>The artifactorics' own confirmation: a parchment slip with a gold edge and the sheet
    /// buttons, the same as the one on the amulet page - instead of the game's black message box.</summary>
    public class Dialog_ArtConfirm : Window
    {
        private static readonly Color Ink = new Color(0.03f, 0.018f, 0.008f);
        private static readonly Color InkSoft = new Color(0.10f, 0.06f, 0.025f);
        private static readonly Color Gold = new Color(0.70f, 0.50f, 0.12f);
        private static readonly Color Warn = new Color(0.50f, 0.05f, 0.02f);

        private readonly string title;
        private readonly string text;
        private readonly Action onConfirm;
        private readonly bool destructive;

        public Dialog_ArtConfirm(string text, Action onConfirm, bool destructive = false, string title = "For good")
        {
            this.text = text;
            this.onConfirm = onConfirm;
            this.destructive = destructive;
            this.title = title;
            doWindowBackground = false;
            drawShadow = true;
            doCloseX = false;
            forcePause = true;
            absorbInputAroundWindow = true;
            closeOnClickedOutside = false;
            closeOnAccept = false;
            closeOnCancel = true;
        }

        protected override float Margin => 0f;

        public override Vector2 InitialSize
        {
            get
            {
                Text.Font = GameFont.Small;
                float h = Text.CalcHeight(text, 520f - 48f);
                return new Vector2(520f, Mathf.Clamp(h + 150f, 200f, 560f));
            }
        }

        public override void DoWindowContents(Rect box)
        {
            GUI.color = Color.white;
            ArtifactoricsTex.TileParchment(box);
            GUI.color = new Color(Gold.r, Gold.g, Gold.b, 0.95f);
            Widgets.DrawBox(box, 2);
            Widgets.DrawBox(box.ContractedBy(5f), 1);

            GUI.color = Ink; Text.Font = GameFont.Medium; Text.Anchor = TextAnchor.UpperCenter;
            Widgets.Label(new Rect(box.x + 16f, box.y + 14f, box.width - 32f, 32f), title);
            Text.Font = GameFont.Small; GUI.color = InkSoft;
            Widgets.Label(new Rect(box.x + 24f, box.y + 54f, box.width - 48f, box.height - 116f), text);
            Text.Anchor = TextAnchor.UpperLeft;

            float bw = Mathf.Min(180f, (box.width - 60f) / 2f);
            Rect back = new Rect(box.center.x - bw - 10f, box.yMax - 56f, bw, 40f);
            Rect ok = new Rect(box.center.x + 10f, box.yMax - 56f, bw, 40f);
            if (Button(back, "Go back", Ink)) Close();
            if (Button(ok, "Confirm", destructive ? Warn : Ink))
            {
                Close();
                onConfirm?.Invoke();
            }
            GUI.color = Color.white;
        }

        public override void OnAcceptKeyPressed()
        {
            Close();
            onConfirm?.Invoke();
        }

        private static bool Button(Rect r, string label, Color ink)
        {
            bool over = Mouse.IsOver(r);
            Texture2D tex = !over ? ArtifactoricsTex.ButtonNormal : Input.GetMouseButton(0) ? ArtifactoricsTex.ButtonPressed : ArtifactoricsTex.ButtonHover;
            GUI.color = Color.white;
            GUI.DrawTexture(r, tex, ScaleMode.StretchToFill);
            GUI.color = ink;
            Text.Anchor = TextAnchor.MiddleCenter;
            Widgets.Label(r, label);
            Text.Anchor = TextAnchor.UpperLeft;
            GUI.color = Color.white;
            if (Widgets.ButtonInvisible(r)) { SoundDefOf.Click.PlayOneShotOnCamera(); return true; }
            return false;
        }
    }
}
