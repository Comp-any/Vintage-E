using System;
using Cairo;
using Vintagestory.API.Client;

namespace VintageE
{
    /// <summary>
    /// The Emc Craft button on a handbook page: a rich-text component drawn as a button (a rounded box behind the text,
    /// lighter under the mouse), red when the Arcane Tablet isn't carried. The game's rich text has only links, so
    /// this follows <see cref="LinkTextComponent"/>: two textures composed once, the hovered one drawn under the mouse.
    /// </summary>
    public class EmcCraftButtonComponent : RichTextComponent
    {
        const double PadX = 8, PadY = 3, Radius = 3;

        readonly Action onClick;
        readonly bool red;
        LoadedTexture normalTex, hoverTex;
        double left, top, width, height;
        bool isHover, wasMouseDown;

        public EmcCraftButtonComponent(ICoreClientAPI api, string text, CairoFont font, bool red, Action onClick) : base(api, text, font)
        {
            this.onClick = onClick;
            this.red = red;
            MouseOverCursor = "linkselect";
            Font = Font.Clone().WithColor(red ? new double[] { 1, 0.82, 0.8, 1 } : (double[])GuiStyle.ButtonTextColor.Clone());
            // Room for the box on either side, so it doesn't run into what's next to it.
            PaddingLeft += PadX;
            PaddingRight += PadX;
            normalTex = new LoadedTexture(api);
            hoverTex = new LoadedTexture(api);
        }

        public override void ComposeElements(Context ctxStatic, ImageSurface surfaceStatic)
        {
            if (Lines == null || Lines.Length == 0) return;
            double padX = GuiElement.scaled(PadX), padY = GuiElement.scaled(PadY);
            double minX = double.MaxValue, minY = double.MaxValue, maxX = 0, maxY = 0;
            foreach (TextLine line in Lines)
            {
                minX = Math.Min(minX, line.Bounds.X);
                minY = Math.Min(minY, line.Bounds.Y);
                maxX = Math.Max(maxX, line.Bounds.X + line.Bounds.Width);
                maxY = Math.Max(maxY, line.Bounds.Y + line.Bounds.Height);
            }
            // The last line's width already has PaddingRight in it (the game adds it in CalcBounds).
            left = minX - padX;
            top = minY - padY;
            width = Math.Max(1, maxX - left);
            height = Math.Max(1, maxY + padY - top);

            Compose(ref normalTex, false);
            Compose(ref hoverTex, true);
        }

        void Compose(ref LoadedTexture tex, bool hover)
        {
            using var surface = new ImageSurface(Format.Argb32, (int)Math.Ceiling(width), (int)Math.Ceiling(height));
            using var ctx = new Context(surface);

            double[] fill = red
                ? (hover ? new double[] { 0.62, 0.16, 0.13, 0.95 } : new double[] { 0.5, 0.12, 0.1, 0.9 })
                : (hover ? new double[] { 0.36, 0.3, 0.24, 0.95 } : new double[] { 0.26, 0.21, 0.17, 0.9 });
            double[] border = red ? new double[] { 0.9, 0.35, 0.3, 1 } : new double[] { 0.55, 0.47, 0.38, 1 };

            GuiElement.RoundRectangle(ctx, 0.5, 0.5, width - 1, height - 1, GuiElement.scaled(Radius));
            ctx.SetSourceRGBA(fill[0], fill[1], fill[2], fill[3]);
            ctx.FillPreserve();
            ctx.LineWidth = 1;
            ctx.SetSourceRGBA(border[0], border[1], border[2], border[3]);
            ctx.Stroke();

            ctx.Save();
            Matrix m = ctx.Matrix;
            m.Translate((int)-left, (int)-top);
            ctx.Matrix = m;
            textUtil.DrawMultilineText(ctx, Font, Lines, EnumTextOrientation.Left);
            ctx.Restore();

            api.Gui.LoadOrUpdateCairoTexture(surface, false, ref tex);
        }

        bool Inside(double x, double y) => x >= left && x <= left + width && y >= top && y <= top + height;

        public override void RenderInteractiveElements(float deltaTime, double renderX, double renderY, double renderZ)
        {
            if (normalTex == null || normalTex.TextureId == 0) return;
            isHover = Inside(api.Input.MouseX - renderX, api.Input.MouseY - renderY);
            LoadedTexture tex = isHover ? hoverTex : normalTex;
            api.Render.Render2DTexturePremultipliedAlpha(tex.TextureId, (int)(renderX + left), (int)(renderY + top), tex.Width, tex.Height, (float)renderZ + 50);
        }

        public override bool UseMouseOverCursor(ElementBounds richtextBounds) => isHover;

        public override void OnMouseDown(MouseEvent args)
        {
            wasMouseDown = Inside(args.X, args.Y);
        }

        public override void OnMouseUp(MouseEvent args)
        {
            if (!wasMouseDown) return;
            wasMouseDown = false;
            if (!Inside(args.X, args.Y)) return;
            args.Handled = true;
            onClick?.Invoke();
        }

        public override void Dispose()
        {
            base.Dispose();
            normalTex?.Dispose();
            hoverTex?.Dispose();
        }
    }
}
