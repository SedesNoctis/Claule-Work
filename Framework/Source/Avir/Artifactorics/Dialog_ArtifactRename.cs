using System;
using RimWorld;
using UnityEngine;
using Verse;

namespace Avir.Artifactorics
{
    /// <summary>A name of the player's own for an artifact or an amulet - shown before its usual label everywhere.
    /// An empty name gives the usual label back.</summary>
    public class Dialog_ArtifactRename : Window
    {
        private string name;
        private readonly Action<string> set;
        private bool focused;

        public override Vector2 InitialSize => new Vector2(420f, 170f);

        public Dialog_ArtifactRename(string current, Action<string> set)
        {
            name = current ?? "";
            this.set = set;
            forcePause = true;
            doCloseX = true;
            absorbInputAroundWindow = true;
            closeOnClickedOutside = true;
        }

        public static Command_Action Gizmo(Func<string> get, Action<string> set) => new Command_Action
        {
            defaultLabel = "Rename",
            defaultDesc = "Give it a name of your own, to know it at a glance: in lists, trade, the inspect pane. Empty - the usual label.",
            icon = TexButton.Rename,
            action = () => Find.WindowStack.Add(new Dialog_ArtifactRename(get(), set))
        };

        public override void DoWindowContents(Rect inRect)
        {
            Text.Font = GameFont.Small;
            Widgets.Label(new Rect(inRect.x, inRect.y, inRect.width, 24f), "Name (empty - the usual label):");
            GUI.SetNextControlName("AvirArtifactName");
            name = Widgets.TextField(new Rect(inRect.x, inRect.y + 30f, inRect.width, 32f), name);
            if (!focused) { Verse.UI.FocusControl("AvirArtifactName", this); focused = true; }
            bool enter = Event.current.type == EventType.KeyDown && (Event.current.keyCode == KeyCode.Return || Event.current.keyCode == KeyCode.KeypadEnter);
            if (Widgets.ButtonText(new Rect(inRect.width / 2f - 60f, inRect.yMax - 36f, 120f, 34f), "OK") || enter)
            {
                set(name.Trim().NullOrEmpty() ? null : name.Trim());
                if (enter) Event.current.Use();
                Close();
            }
        }
    }
}
