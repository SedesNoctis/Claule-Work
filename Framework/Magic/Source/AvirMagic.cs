namespace Avir.Magic
{
    /// <summary>The magic system's one switch. Everything of magic asks here first: off, it does not exist in the game
    /// (no traits, implants, spells or runes of its own); the rest of the mod works as before. Read once at start, so a
    /// change takes effect after a restart and never leaves a game half with magic.</summary>
    public static class AvirMagic
    {
        private static bool? on;
        public static bool On => on ?? (on = AvirMod.Settings != null && AvirMod.Settings.magicOn).Value;
    }
}
