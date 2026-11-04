using Vintagestory.API.Client;

namespace VintageE
{
    /// <summary>
    /// GuiElementRichtext.SetNewText replaces its components without disposing the old ones, and a link component
    /// owns GL textures; a list refilled on every keystroke leaked two textures per link until the window closed.
    /// </summary>
    static class GuiTextures
    {
        public static void Release(GuiElementRichtext list)
        {
            if (list?.Components == null) return;
            foreach (RichTextComponentBase c in list.Components)
            {
                try { c?.Dispose(); } catch { }
            }
        }
    }
}
