using System;
using System.Collections.Generic;
using Cairo;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.API.Util;

namespace Vintagestory.API.Client;

public class GuiElementRichtext : GuiElement
{
	public static bool DebugLogging;

	protected TextFlowPath[] flowPath;

	public RichTextComponentBase[] Components;

	public float zPos = 50f;

	public LoadedTexture richtTextTexture;

	public bool Debug;

	public Vec4f RenderColor;

	private ImageSurface surface;

	private Context ctx;

	public bool HalfComposed;

	public int MaxHeight { get; set; } = int.MaxValue;

	public double MaxLineWidth
	{
		get
		{
			if (flowPath == null)
			{
				return 0.0;
			}
			double num = 0.0;
			for (int i = 0; i < Components.Length; i++)
			{
				RichTextComponentBase richTextComponentBase = Components[i];
				for (int j = 0; j < richTextComponentBase.BoundsPerLine.Length; j++)
				{
					num = Math.Max(num, richTextComponentBase.BoundsPerLine[j].X + richTextComponentBase.BoundsPerLine[j].Width);
				}
			}
			return num;
		}
	}

	public double TotalHeight
	{
		get
		{
			if (flowPath == null)
			{
				return 0.0;
			}
			double num = 0.0;
			for (int i = 0; i < Components.Length; i++)
			{
				RichTextComponentBase richTextComponentBase = Components[i];
				for (int j = 0; j < richTextComponentBase.BoundsPerLine.Length; j++)
				{
					num = Math.Max(num, richTextComponentBase.BoundsPerLine[j].Y + richTextComponentBase.BoundsPerLine[j].Height);
				}
			}
			return num;
		}
	}

	public GuiElementRichtext(ICoreClientAPI capi, RichTextComponentBase[] components, ElementBounds bounds)
		: base(capi, bounds)
	{
		Components = components;
		richtTextTexture = new LoadedTexture(capi);
	}

	public override void BeforeCalcBounds()
	{
		CalcHeightAndPositions();
	}

	public override void ComposeElements(Context ctxStatic, ImageSurface surfaceStatic)
	{
		Compose();
	}

	public void Compose(bool genTextureLater = false)
	{
		ElementBounds elementBounds = Bounds.CopyOnlySize();
		elementBounds.fixedPaddingX = 0.0;
		elementBounds.fixedPaddingY = 0.0;
		Bounds.CalcWorldBounds();
		int num = (int)Bounds.InnerWidth;
		int num2 = (int)Bounds.InnerHeight;
		if (richtTextTexture.TextureId != 0)
		{
			num = Math.Max(1, Math.Max(num, richtTextTexture.Width));
			num2 = Math.Max(1, Math.Max(num2, richtTextTexture.Height));
		}
		surface = new ImageSurface(Format.Argb32, num, Math.Min(MaxHeight, num2));
		ctx = new Context(surface);
		ctx.SetSourceRGBA(0.0, 0.0, 0.0, 0.0);
		ctx.Paint();
		if (!genTextureLater)
		{
			ComposeFor(elementBounds, ctx, surface);
			generateTexture(surface, ref richtTextTexture);
			ctx.Dispose();
			surface.Dispose();
			ctx = null;
			surface = null;
		}
		else
		{
			HalfComposed = true;
		}
	}

	public void genTexture()
	{
		generateTexture(surface, ref richtTextTexture);
		ctx.Dispose();
		surface.Dispose();
		ctx = null;
		surface = null;
		HalfComposed = false;
	}

	public void CalcHeightAndPositions()
	{
		Bounds.CalcWorldBounds();
		if (DebugLogging)
		{
			api.Logger.VerboseDebug("GuiElementRichtext: before bounds: {0}/{1}  w/h = {2},{3}", Bounds.absX, Bounds.absY, Bounds.OuterWidth, Bounds.OuterHeight);
		}
		double num = 0.0;
		double num2 = 0.0;
		List<int> list = new List<int>();
		List<TextFlowPath> list2 = new List<TextFlowPath>();
		list2.Add(new TextFlowPath(Bounds.InnerWidth));
		double num3 = 0.0;
		double num4 = 0.0;
		RichTextComponentBase richTextComponentBase = null;
		for (int i = 0; i < Components.Length; i++)
		{
			richTextComponentBase = Components[i];
			EnumCalcBoundsResult enumCalcBoundsResult = richTextComponentBase.CalcBounds(list2.ToArray(), num3, num, num2, out var nextOffsetX);
			if (richTextComponentBase.Float == EnumFloat.Inline)
			{
				num = nextOffsetX;
			}
			if (DebugLogging)
			{
				api.Logger.VerboseDebug("GuiElementRichtext, add comp {0}, posY={1}, lineHeight={2}", i, num2, num3);
				if (richTextComponentBase.BoundsPerLine.Length != 0)
				{
					api.Logger.VerboseDebug("GuiElementRichtext, Comp bounds 0 w/h: {0}/{1}", richTextComponentBase.BoundsPerLine[0].Width, richTextComponentBase.BoundsPerLine[0].Height);
				}
			}
			if (richTextComponentBase.Float == EnumFloat.None)
			{
				num = 0.0;
				num2 += Math.Max(num3, richTextComponentBase.BoundsPerLine[0].Height) + ((enumCalcBoundsResult != EnumCalcBoundsResult.Continue) ? GuiElement.scaled(richTextComponentBase.UnscaledMarginTop) : 0.0);
				num2 = Math.Ceiling(num2);
				handleVerticalAlignment(list, num4);
				num3 = 0.0;
				num4 = 0.0;
				list.Clear();
				continue;
			}
			if (enumCalcBoundsResult != EnumCalcBoundsResult.Continue)
			{
				adjustLineTextAlignment(list);
				num3 = Math.Max(num3, richTextComponentBase.BoundsPerLine[0].Height);
				num4 = Math.Max(num4, richTextComponentBase.BoundsPerLine[0].AscentOrHeight);
				handleVerticalAlignment(list, num4);
				if (enumCalcBoundsResult == EnumCalcBoundsResult.Multiline)
				{
					if (richTextComponentBase.VerticalAlign == EnumVerticalAlign.Bottom)
					{
						LineRectangled[] boundsPerLine = richTextComponentBase.BoundsPerLine;
						foreach (LineRectangled obj in boundsPerLine)
						{
							obj.Y = Math.Ceiling(obj.Y + num4 - richTextComponentBase.BoundsPerLine[0].AscentOrHeight);
						}
					}
					if (richTextComponentBase.VerticalAlign == EnumVerticalAlign.Middle)
					{
						LineRectangled[] boundsPerLine = richTextComponentBase.BoundsPerLine;
						foreach (LineRectangled obj2 in boundsPerLine)
						{
							obj2.Y = Math.Ceiling(obj2.Y + num4 / 2.0 - richTextComponentBase.BoundsPerLine[0].AscentOrHeight / 2.0);
						}
					}
					if (richTextComponentBase.VerticalAlign == EnumVerticalAlign.FixedOffset)
					{
						LineRectangled[] boundsPerLine = richTextComponentBase.BoundsPerLine;
						foreach (LineRectangled obj3 in boundsPerLine)
						{
							obj3.Y = Math.Ceiling(obj3.Y + richTextComponentBase.UnscaledMarginTop);
						}
					}
				}
				list.Clear();
				list.Add(i);
				LineRectangled lineRectangled = richTextComponentBase.BoundsPerLine[richTextComponentBase.BoundsPerLine.Length - 1];
				num2 = lineRectangled.Y;
				if (lineRectangled.Width > 0.0)
				{
					num3 = lineRectangled.Height;
					num4 = lineRectangled.AscentOrHeight;
				}
				else
				{
					num3 = 0.0;
					num4 = 0.0;
				}
			}
			else if (richTextComponentBase.Float == EnumFloat.Inline && richTextComponentBase.BoundsPerLine.Length != 0)
			{
				num3 = Math.Max(richTextComponentBase.BoundsPerLine[0].Height, num3);
				num4 = Math.Max(richTextComponentBase.BoundsPerLine[0].AscentOrHeight, num4);
				list.Add(i);
			}
			if (richTextComponentBase.Float != EnumFloat.Inline)
			{
				ConstrainTextFlowPath(list2, num2, richTextComponentBase);
			}
		}
		if (DebugLogging)
		{
			api.Logger.VerboseDebug("GuiElementRichtext: after loop. posY = {0}", num2);
		}
		if (richTextComponentBase != null && num > 0.0 && richTextComponentBase.BoundsPerLine.Length != 0)
		{
			num2 += num3;
		}
		Bounds.fixedHeight = (num2 + 1.0) / (double)RuntimeEnv.GUIScale;
		adjustLineTextAlignment(list);
		double num5 = 0.0;
		foreach (int item in list)
		{
			RichTextComponentBase richTextComponentBase2 = Components[item];
			Rectangled rectangled = richTextComponentBase2.BoundsPerLine[richTextComponentBase2.BoundsPerLine.Length - 1];
			num5 = Math.Max(num5, rectangled.Height);
		}
		foreach (int item2 in list)
		{
			RichTextComponentBase richTextComponentBase3 = Components[item2];
			Rectangled rectangled2 = richTextComponentBase3.BoundsPerLine[richTextComponentBase3.BoundsPerLine.Length - 1];
			if (richTextComponentBase3.VerticalAlign == EnumVerticalAlign.Bottom)
			{
				rectangled2.Y = Math.Ceiling(rectangled2.Y + num4 - richTextComponentBase3.BoundsPerLine[richTextComponentBase3.BoundsPerLine.Length - 1].AscentOrHeight);
			}
			else if (richTextComponentBase3.VerticalAlign == EnumVerticalAlign.Middle)
			{
				rectangled2.Y += (num5 - rectangled2.Height) / 2.0;
			}
			else if (richTextComponentBase3.VerticalAlign == EnumVerticalAlign.FixedOffset)
			{
				rectangled2.Y += richTextComponentBase3.UnscaledMarginTop;
			}
		}
		flowPath = list2.ToArray();
		if (DebugLogging)
		{
			api.Logger.VerboseDebug("GuiElementRichtext: after bounds: {0}/{1}  w/h = {2},{3}", Bounds.absX, Bounds.absY, Bounds.OuterWidth, Bounds.OuterHeight);
			api.Logger.VerboseDebug("GuiElementRichtext: posY = {0}", num2);
			api.Logger.VerboseDebug("GuiElementRichtext: framewidth/height: {0}/{1}", api.Render.FrameWidth, api.Render.FrameHeight);
		}
	}

	private void handleVerticalAlignment(List<int> currentLine, double ascentHeight)
	{
		foreach (int item in currentLine)
		{
			RichTextComponentBase richTextComponentBase = Components[item];
			LineRectangled lineRectangled = richTextComponentBase.BoundsPerLine[richTextComponentBase.BoundsPerLine.Length - 1];
			if (richTextComponentBase.VerticalAlign == EnumVerticalAlign.Bottom)
			{
				lineRectangled.Y = Math.Ceiling(lineRectangled.Y + ascentHeight - lineRectangled.AscentOrHeight);
			}
			else if (richTextComponentBase.VerticalAlign == EnumVerticalAlign.Middle)
			{
				lineRectangled.Y = Math.Ceiling(lineRectangled.Y + ascentHeight / 2.0 - lineRectangled.AscentOrHeight / 2.0);
			}
			else if (richTextComponentBase.VerticalAlign == EnumVerticalAlign.FixedOffset)
			{
				lineRectangled.Y += richTextComponentBase.UnscaledMarginTop;
			}
		}
	}

	private void adjustLineTextAlignment(List<int> currentLine)
	{
		if (currentLine.Count == 0)
		{
			return;
		}
		int num = currentLine[currentLine.Count - 1];
		RichTextComponent obj = Components[num] as RichTextComponent;
		double valueOrDefault = ((obj == null) ? ((double?)null) : obj.Lines[0]?.RightSpace).GetValueOrDefault();
		EnumTextOrientation valueOrDefault2 = ((Components[num] as RichTextComponent)?.Font?.Orientation).GetValueOrDefault();
		foreach (int item in currentLine)
		{
			RichTextComponentBase richTextComponentBase = Components[item];
			if (valueOrDefault2 == EnumTextOrientation.Center && richTextComponentBase is RichTextComponent { Lines: var lines } richTextComponent)
			{
				lines[(num != item) ? (richTextComponent.Lines.Length - 1) : 0].RightSpace = valueOrDefault;
			}
		}
	}

	private void ConstrainTextFlowPath(List<TextFlowPath> flowPath, double posY, RichTextComponentBase comp)
	{
		Rectangled rectangled = comp.BoundsPerLine[0];
		EnumFloat enumFloat = comp.Float;
		double num = ((enumFloat == EnumFloat.Left) ? rectangled.Width : 0.0);
		double num2 = ((enumFloat == EnumFloat.Right) ? (Bounds.InnerWidth - rectangled.Width) : Bounds.InnerWidth);
		double num3 = rectangled.Height;
		for (int i = 0; i < flowPath.Count; i++)
		{
			TextFlowPath textFlowPath = flowPath[i];
			if (textFlowPath.Y2 <= posY)
			{
				continue;
			}
			double x = Math.Max(num, textFlowPath.X1);
			double x2 = Math.Min(num2, textFlowPath.X2);
			if (textFlowPath.Y2 > posY + rectangled.Height)
			{
				if (!(num <= textFlowPath.X1) || !(num2 >= textFlowPath.X2))
				{
					if (i == 0)
					{
						flowPath[i] = new TextFlowPath(x, posY, x2, posY + rectangled.Height);
						flowPath.Insert(i + 1, new TextFlowPath(textFlowPath.X1, posY + rectangled.Height, textFlowPath.X2, textFlowPath.Y2));
					}
					else
					{
						flowPath[i] = new TextFlowPath(textFlowPath.X1, textFlowPath.Y1, textFlowPath.X2, posY);
						flowPath.Insert(i + 1, new TextFlowPath(textFlowPath.X1, posY + rectangled.Height, textFlowPath.X2, textFlowPath.Y2));
						flowPath.Insert(i, new TextFlowPath(x, posY, x2, posY + rectangled.Height));
					}
					num3 = 0.0;
					break;
				}
			}
			else
			{
				flowPath[i].X1 = x;
				flowPath[i].X2 = x2;
				num3 -= textFlowPath.Y2 - posY;
			}
		}
		if (num3 > 0.0)
		{
			flowPath.Add(new TextFlowPath(num, posY, num2, posY + num3));
		}
	}

	public virtual void ComposeFor(ElementBounds bounds, Context ctx, ImageSurface surface)
	{
		bounds.CalcWorldBounds();
		ctx.Save();
		Matrix matrix = ctx.Matrix;
		matrix.Translate(bounds.drawX, bounds.drawY);
		ctx.Matrix = matrix;
		for (int i = 0; i < Components.Length; i++)
		{
			Components[i].ComposeElements(ctx, surface);
			if (Debug)
			{
				ctx.LineWidth = 1.0;
				if (Components[i] is ClearFloatTextComponent)
				{
					ctx.SetSourceRGBA(0.0, 0.0, 1.0, 0.5);
				}
				else
				{
					ctx.SetSourceRGBA(0.0, 0.0, 0.0, 0.5);
				}
				LineRectangled[] boundsPerLine = Components[i].BoundsPerLine;
				foreach (LineRectangled lineRectangled in boundsPerLine)
				{
					ctx.Rectangle(lineRectangled.X, lineRectangled.Y, lineRectangled.Width, lineRectangled.Height);
					ctx.Stroke();
				}
			}
		}
		ctx.Restore();
	}

	/// <summary>
	/// Recomposes the element for lines.
	/// </summary>
	public void RecomposeText()
	{
		CalcHeightAndPositions();
		Bounds.CalcWorldBounds();
		ImageSurface imageSurface = new ImageSurface(Format.Argb32, (int)Bounds.OuterWidth, (int)Math.Min(MaxHeight, Bounds.OuterHeight));
		Context context = genContext(imageSurface);
		ComposeFor(Bounds.CopyOnlySize(), context, imageSurface);
		generateTexture(imageSurface, ref richtTextTexture);
		context.Dispose();
		imageSurface.Dispose();
	}

	public override void RenderInteractiveElements(float deltaTime)
	{
		Render2DTexture(richtTextTexture.TextureId, (int)Bounds.renderX, (int)Bounds.renderY, richtTextTexture.Width, richtTextTexture.Height, zPos, RenderColor);
		MouseOverCursor = null;
		for (int i = 0; i < Components.Length; i++)
		{
			RichTextComponentBase richTextComponentBase = Components[i];
			richTextComponentBase.RenderColor = RenderColor;
			richTextComponentBase.RenderInteractiveElements(deltaTime, Bounds.renderX, Bounds.renderY, zPos);
			if (richTextComponentBase.UseMouseOverCursor(Bounds))
			{
				MouseOverCursor = richTextComponentBase.MouseOverCursor;
			}
		}
	}

	public override void OnMouseMove(ICoreClientAPI api, MouseEvent args)
	{
		MouseEvent mouseEvent = new MouseEvent((int)((double)args.X - Bounds.absX), (int)((double)args.Y - Bounds.absY), args.Button, 0);
		for (int i = 0; i < Components.Length; i++)
		{
			Components[i].OnMouseMove(mouseEvent);
			if (mouseEvent.Handled)
			{
				break;
			}
		}
		args.Handled = mouseEvent.Handled;
	}

	public override void OnMouseDownOnElement(ICoreClientAPI api, MouseEvent args)
	{
		MouseEvent mouseEvent = new MouseEvent((int)((double)args.X - Bounds.absX), (int)((double)args.Y - Bounds.absY), args.Button, 0);
		for (int i = 0; i < Components.Length; i++)
		{
			Components[i].OnMouseDown(mouseEvent);
			if (mouseEvent.Handled)
			{
				break;
			}
		}
		args.Handled = mouseEvent.Handled;
	}

	public override void OnMouseUp(ICoreClientAPI api, MouseEvent args)
	{
		MouseEvent mouseEvent = new MouseEvent((int)((double)args.X - Bounds.absX), (int)((double)args.Y - Bounds.absY), args.Button, 0);
		for (int i = 0; i < Components.Length; i++)
		{
			Components[i].OnMouseUp(mouseEvent);
			if (mouseEvent.Handled)
			{
				break;
			}
		}
		args.Handled |= mouseEvent.Handled;
		base.OnMouseUp(api, args);
	}

	public void SetNewText(string vtmlCode, CairoFont baseFont, Action<LinkTextComponent> didClickLink = null)
	{
		SetNewTextWithoutRecompose(vtmlCode, baseFont, didClickLink, recalcBounds: true);
		RecomposeText();
	}

	public void SetNewText(RichTextComponentBase[] comps)
	{
		Components = comps;
		RecomposeText();
	}

	[Obsolete("Use AppendText(RichTextComponentBase[] comps) instead")]
	public void AppendText(RichTextComponent[] comps)
	{
		Components = Components.Append(comps);
		RecomposeText();
	}

	public void AppendText(RichTextComponentBase[] comps)
	{
		Components = Components.Append(comps);
		RecomposeText();
	}

	public void SetNewTextWithoutRecompose(string vtmlCode, CairoFont baseFont, Action<LinkTextComponent> didClickLink = null, bool recalcBounds = false)
	{
		if (Components != null)
		{
			RichTextComponentBase[] components = Components;
			for (int i = 0; i < components.Length; i++)
			{
				components[i]?.Dispose();
			}
		}
		Components = VtmlUtil.Richtextify(api, vtmlCode, baseFont, didClickLink);
		if (recalcBounds)
		{
			CalcHeightAndPositions();
			Bounds.CalcWorldBounds();
		}
	}

	public void RecomposeInto(ImageSurface surface, Context ctx)
	{
		ComposeFor(Bounds.CopyOnlySize(), ctx, surface);
		generateTexture(surface, ref richtTextTexture);
	}

	public override void Dispose()
	{
		base.Dispose();
		richtTextTexture?.Dispose();
		RichTextComponentBase[] components = Components;
		for (int i = 0; i < components.Length; i++)
		{
			components[i].Dispose();
		}
	}
}
