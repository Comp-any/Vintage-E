using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;

namespace Vintagestory.GameContent;

public class GuiDialogHandbook : GuiDialog
{
	protected Dictionary<string, int> pageNumberByPageCode = new Dictionary<string, int>();

	internal List<GuiHandbookPage> allHandbookPages = new List<GuiHandbookPage>();

	protected List<IFlatListItem> shownHandbookPages = new List<IFlatListItem>();

	protected List<string> categoryCodes = new List<string>();

	protected Stack<BrowseHistoryElement> browseHistory = new Stack<BrowseHistoryElement>();

	protected string currentSearchText;

	protected GuiComposer overviewGui;

	protected GuiComposer detailViewGui;

	protected bool loadingPagesAsync;

	protected double listHeight = 500.0;

	protected GuiTab[] tabs;

	public string currentCatgoryCode;

	private OnCreatePagesDelegate createPageHandlerAsync;

	private OnComposePageDelegate composePageHandler;

	public override double DrawOrder => 0.2;

	public override string ToggleKeyCombinationCode => "handbook";

	public virtual string DialogTitle => "";

	public override bool PrefersUngrabbedMouse => true;

	public GuiDialogHandbook(ICoreClientAPI capi, OnCreatePagesDelegate createPageHandlerAsync, OnComposePageDelegate composePageHandler)
		: base(capi)
	{
		this.createPageHandlerAsync = createPageHandlerAsync;
		this.composePageHandler = composePageHandler;
		capi.Settings.AddWatcher("guiScale", (float val) =>
		{
			initOverviewGui();
			FilterItems();
			foreach (GuiHandbookPage shownHandbookPage in shownHandbookPages)
			{
				shownHandbookPage.Dispose();
			}
		});
		loadEntries();
	}

	protected virtual void loadEntries()
	{
		capi.Logger.VerboseDebug("Starting initialising handbook");
		pageNumberByPageCode.Clear();
		shownHandbookPages.Clear();
		allHandbookPages.Clear();
		HashSet<string> hashSet = initCustomPages();
		hashSet.Add("stack");
		categoryCodes = hashSet.ToList();
		loadingPagesAsync = true;
		TyronThreadPool.QueueTask((Action)LoadPages_Async, "Handbook.LoadPages_Async");
		initOverviewGui();
		capi.Logger.VerboseDebug("Done creating handbook index GUI");
	}

	public void initOverviewGui()
	{
		ElementBounds elementBounds = ElementBounds.Fixed(GuiStyle.ElementToDialogPadding - 2.0, 45.0, 300.0, 30.0);
		ElementBounds elementBounds2 = ElementBounds.Fixed(0.0, 0.0, 500.0, listHeight).FixedUnder(elementBounds, 5.0);
		ElementBounds elementBounds3 = elementBounds2.ForkBoundingParent();
		ElementBounds elementBounds4 = elementBounds2.FlatCopy().FixedGrow(6.0).WithFixedOffset(-3.0, -3.0);
		ElementBounds elementBounds5 = elementBounds4.CopyOffsetedSibling(3.0 + elementBounds2.fixedWidth + 7.0).WithFixedWidth(20.0);
		ElementBounds elementBounds6 = ElementBounds.FixedSize(0.0, 0.0).FixedUnder(elementBounds3, 18.0).WithAlignment(EnumDialogArea.RightFixed)
			.WithFixedPadding(20.0, 4.0)
			.WithFixedAlignmentOffset(2.0, 0.0);
		ElementBounds elementBounds7 = ElementBounds.Fill.WithFixedPadding(GuiStyle.ElementToDialogPadding);
		elementBounds7.BothSizing = ElementSizing.FitToChildren;
		elementBounds7.WithChildren(elementBounds4, elementBounds2, elementBounds5, elementBounds6);
		ElementBounds bounds = ElementStdBounds.AutosizedMainDialog.WithAlignment(EnumDialogArea.None).WithAlignment(EnumDialogArea.CenterFixed).WithFixedPosition(0.0, 70.0);
		ElementBounds bounds2 = ElementBounds.Fixed(-200.0, 35.0, 200.0, 545.0);
		ElementBounds bounds3 = ElementBounds.FixedSize(0.0, 0.0).FixedUnder(elementBounds3, 15.0).WithAlignment(EnumDialogArea.LeftFixed)
			.WithFixedPadding(20.0, 4.0)
			.WithFixedAlignmentOffset(-6.0, 3.0);
		tabs = genTabs(out var curTab);
		overviewGui = capi.Gui.CreateCompo("handbook-overview", bounds).AddShadedDialogBG(elementBounds7).AddDialogTitleBar(DialogTitle, OnTitleBarClose)
			.AddIf(tabs.Length != 0)
			.AddVerticalTabs(tabs, bounds2, OnTabClicked, "verticalTabs")
			.EndIf()
			.AddTextInput(elementBounds, FilterItemsBySearchText, CairoFont.WhiteSmallishText(), "searchField")
			.BeginChildElements(elementBounds7)
			.BeginClip(elementBounds3)
			.AddInset(elementBounds4, 3)
			.AddFlatList(elementBounds2, onLeftClickListElement, shownHandbookPages, "stacklist")
			.EndClip()
			.AddVerticalScrollbar(OnNewScrollbarvalueOverviewPage, elementBounds5, "scrollbar")
			.AddIf(capi.IsSinglePlayer && !capi.OpenedToLan)
			.AddToggleButton(Lang.Get("Pause game"), CairoFont.WhiteDetailText(), onTogglePause, ElementBounds.Fixed(360.0, -15.0, 100.0, 22.0), "pausegame")
			.EndIf()
			.AddSmallButton(Lang.Get("general-back"), OnButtonBack, bounds3, EnumButtonStyle.Normal, "backButton")
			.AddSmallButton(Lang.Get("Close Handbook"), OnButtonClose, elementBounds6)
			.EndChildElements()
			.Compose();
		overviewGui.GetScrollbar("scrollbar").SetHeights((float)listHeight, (float)overviewGui.GetFlatList("stacklist").insideBounds.fixedHeight);
		overviewGui.GetTextInput("searchField").SetPlaceHolderText(Lang.Get("Search..."));
		if (tabs.Length != 0)
		{
			overviewGui.GetVerticalTab("verticalTabs").SetValue(curTab, triggerHandler: false);
			currentCatgoryCode = (tabs[curTab] as HandbookTab).CategoryCode;
		}
		overviewGui.GetToggleButton("pausegame")?.SetValue(!capi.Settings.Bool["noHandbookPause"]);
		overviewGui.FocusElement(overviewGui.GetTextInput("searchField").TabIndex);
	}

	protected virtual void onTogglePause(bool on)
	{
		capi.PauseGame(on);
		capi.Settings.Bool["noHandbookPause"] = !on;
	}

	protected virtual GuiTab[] genTabs(out int curTab)
	{
		curTab = 0;
		return Array.Empty<GuiTab>();
	}

	protected virtual void OnTabClicked(int index, GuiTab tab)
	{
		selectTab((tab as HandbookTab).CategoryCode);
	}

	public void selectTab(string code)
	{
		currentCatgoryCode = code;
		FilterItems();
		capi.Settings.String["currentHandbookCategoryCode"] = currentCatgoryCode;
	}

	public void ReloadPage()
	{
		if (browseHistory.Count > 0)
		{
			initDetailGui();
		}
		else
		{
			initOverviewGui();
		}
	}

	protected virtual void initDetailGui()
	{
		ElementBounds elementBounds = ElementBounds.Fixed(9.0, 45.0, 500.0, 30.0 + listHeight + 17.0);
		ElementBounds elementBounds2 = elementBounds.ForkBoundingParent();
		ElementBounds elementBounds3 = elementBounds.FlatCopy().FixedGrow(6.0).WithFixedOffset(-3.0, -3.0);
		ElementBounds elementBounds4 = elementBounds2.CopyOffsetedSibling(elementBounds.fixedWidth + 7.0, -6.0, 0.0, 6.0).WithFixedWidth(20.0);
		ElementBounds elementBounds5 = ElementBounds.FixedSize(0.0, 0.0).FixedUnder(elementBounds2, 15.0).WithAlignment(EnumDialogArea.RightFixed)
			.WithFixedPadding(20.0, 4.0)
			.WithFixedAlignmentOffset(-11.0, 1.0);
		ElementBounds elementBounds6 = ElementBounds.FixedSize(0.0, 0.0).FixedUnder(elementBounds2, 15.0).WithAlignment(EnumDialogArea.LeftFixed)
			.WithFixedPadding(20.0, 4.0)
			.WithFixedAlignmentOffset(4.0, 1.0);
		ElementBounds bounds = ElementBounds.FixedSize(0.0, 0.0).FixedUnder(elementBounds2, 15.0).WithAlignment(EnumDialogArea.CenterFixed)
			.WithFixedPadding(20.0, 4.0)
			.WithFixedAlignmentOffset(0.0, 1.0);
		ElementBounds elementBounds7 = elementBounds3.ForkBoundingParent(5.0, 40.0, 36.0, 52.0).WithFixedPadding(GuiStyle.ElementToDialogPadding / 2.0);
		elementBounds7.WithChildren(elementBounds3, elementBounds, elementBounds4, elementBounds6, elementBounds5);
		ElementBounds bounds2 = elementBounds7.ForkBoundingParent().WithAlignment(EnumDialogArea.None).WithAlignment(EnumDialogArea.CenterFixed)
			.WithFixedPosition(0.0, 70.0);
		ElementBounds bounds3 = ElementBounds.Fixed(-200.0, 35.0, 200.0, 545.0);
		BrowseHistoryElement browseHistoryElement = browseHistory.Peek();
		capi.World.FrameProfiler.Mark("handbook-opendetailpage-1");
		detailViewGui?.Dispose();
		detailViewGui = capi.Gui.CreateCompo("handbook-detail", bounds2).AddShadedDialogBG(elementBounds7).AddDialogTitleBar(DialogTitle, OnTitleBarClose)
			.AddVerticalTabs(genTabs(out var curTab), bounds3, OnDetailViewTabClicked, "verticalTabs")
			.BeginChildElements(elementBounds7)
			.BeginClip(elementBounds2)
			.AddInset(elementBounds3, 3);
		capi.World.FrameProfiler.Mark("handbook-opendetailpage-2");
		composePageHandler(browseHistoryElement.Page, detailViewGui, elementBounds, OpenDetailPageFor);
		GuiElement lastAddedElement = detailViewGui.LastAddedElement;
		capi.World.FrameProfiler.Mark("handbook-opendetailpage-3");
		detailViewGui.EndClip().AddVerticalScrollbar(OnNewScrollbarvalueDetailPage, elementBounds4, "scrollbar").AddIf(capi.IsSinglePlayer && !capi.OpenedToLan)
			.AddToggleButton(Lang.Get("Pause game"), CairoFont.WhiteDetailText(), onTogglePause, ElementBounds.Fixed(370.0, -5.0, 100.0, 22.0), "pausegame")
			.EndIf()
			.AddSmallButton(Lang.Get("general-back"), OnButtonBack, elementBounds6)
			.AddSmallButton(Lang.Get("handbook-overview"), OnButtonOverview, bounds)
			.AddSmallButton(Lang.Get("general-close"), OnButtonClose, elementBounds5)
			.EndChildElements();
		detailViewGui.Compose();
		capi.World.FrameProfiler.Mark("handbook-opendetailpage-compose");
		float num = browseHistoryElement.PosY;
		if (num == 0f)
		{
			float num2 = (float)lastAddedElement.Bounds.fixedHeight;
			if (num2 > (float)listHeight)
			{
				float searchTextPosRel = browseHistoryElement.Page.GetSearchTextPosRel(currentSearchText);
				num = Math.Max(0f, searchTextPosRel * 0.98f * num2);
			}
		}
		detailViewGui.GetScrollbar("scrollbar").SetHeights((float)listHeight, (float)lastAddedElement.Bounds.fixedHeight);
		detailViewGui.GetScrollbar("scrollbar").CurrentYPosition = num;
		OnNewScrollbarvalueDetailPage(num);
		detailViewGui.GetVerticalTab("verticalTabs").SetValue(curTab, triggerHandler: false);
		detailViewGui.GetToggleButton("pausegame")?.SetValue(!capi.Settings.Bool["noHandbookPause"]);
		capi.World.FrameProfiler.Mark("handbook-opendetailpage-scrollbars");
	}

	protected virtual void OnDetailViewTabClicked(int index, GuiTab tab)
	{
		browseHistory.Clear();
		OnTabClicked(index, tab);
	}

	protected bool OnButtonOverview()
	{
		browseHistory.Clear();
		return true;
	}

	public virtual bool OpenDetailPageFor(string pageCode)
	{
		capi.Gui.PlaySound("menubutton_press");
		if (pageNumberByPageCode.TryGetValue(pageCode, out var value))
		{
			GuiHandbookPage guiHandbookPage = allHandbookPages[value];
			if (browseHistory.Count > 0 && guiHandbookPage == browseHistory.Peek().Page)
			{
				return true;
			}
			browseHistory.Push(new BrowseHistoryElement
			{
				Page = guiHandbookPage,
				PosY = 0f
			});
			initDetailGui();
			return true;
		}
		return false;
	}

	protected bool OnButtonBack()
	{
		if (browseHistory.Count == 0)
		{
			return true;
		}
		browseHistory.Pop();
		if (browseHistory.Count > 0)
		{
			if (browseHistory.Peek().SearchText != null)
			{
				Search(browseHistory.Peek().SearchText);
			}
			else
			{
				initDetailGui();
			}
		}
		return true;
	}

	protected void onLeftClickListElement(int index)
	{
		browseHistory.Push(new BrowseHistoryElement
		{
			Page = (shownHandbookPages[index] as GuiHandbookPage)
		});
		initDetailGui();
	}

	protected void OnNewScrollbarvalueOverviewPage(float value)
	{
		GuiElementFlatList flatList = overviewGui.GetFlatList("stacklist");
		flatList.insideBounds.fixedY = 3f - value;
		flatList.insideBounds.CalcWorldBounds();
	}

	protected void OnNewScrollbarvalueDetailPage(float value)
	{
		GuiElementRichtext richtext = detailViewGui.GetRichtext("richtext");
		richtext.Bounds.fixedY = 3f - value;
		richtext.Bounds.CalcWorldBounds();
		browseHistory.Peek().PosY = detailViewGui.GetScrollbar("scrollbar").CurrentYPosition;
	}

	protected void OnTitleBarClose()
	{
		TryClose();
	}

	protected bool OnButtonClose()
	{
		TryClose();
		return true;
	}

	public override void OnGuiOpened()
	{
		initOverviewGui();
		FilterItems();
		base.OnGuiOpened();
		if (capi.IsSinglePlayer && !capi.OpenedToLan && !capi.Settings.Bool["noHandbookPause"])
		{
			capi.PauseGame(paused: true);
		}
	}

	public override void OnGuiClosed()
	{
		browseHistory.Clear();
		overviewGui.GetTextInput("searchField").SetValue("");
		if (capi.IsSinglePlayer && !capi.OpenedToLan && !capi.Settings.Bool["noHandbookPause"] && capi.OpenedGuis.FirstOrDefault((object dlg) => dlg is GuiDialogCreateCharacter) == null)
		{
			capi.PauseGame(paused: false);
		}
		base.OnGuiClosed();
	}

	protected virtual HashSet<string> initCustomPages()
	{
		return new HashSet<string>();
	}

	protected void LoadPages_Async()
	{
		allHandbookPages.AddRange(createPageHandlerAsync());
		for (int i = 0; i < allHandbookPages.Count; i++)
		{
			GuiHandbookPage guiHandbookPage = allHandbookPages[i];
			pageNumberByPageCode[guiHandbookPage.PageCode] = (guiHandbookPage.PageNumber = i);
		}
		loadingPagesAsync = false;
	}

	public void Search(string text)
	{
		currentCatgoryCode = null;
		SingleComposer = overviewGui;
		overviewGui.GetTextInput("searchField").SetValue(text);
		if (browseHistory.Count <= 0 || !(browseHistory.Peek().SearchText == text))
		{
			capi.Gui.PlaySound("menubutton_press");
			browseHistory.Push(new BrowseHistoryElement
			{
				Page = null,
				SearchText = text,
				PosY = 0f
			});
		}
	}

	protected void FilterItemsBySearchText(string text)
	{
		if (!(currentSearchText == text))
		{
			currentSearchText = text;
			FilterItems();
		}
	}

	public void FilterItems()
	{
		shownHandbookPages.Clear();
		if (!loadingPagesAsync)
		{
			string text = currentSearchText ?? "";
			Regex regex = GuiDialog.RegexFromSearchText(text);
			Regex strictRegex = GuiDialog.RegexFromSearchText(text, strict: true);
			List<WeightedHandbookPage> weightedPages = new List<WeightedHandbookPage>();
			allHandbookPages.ForEach((GuiHandbookPage page) =>
			{
				if ((currentCatgoryCode == null || !(page.CategoryCode != currentCatgoryCode)) && !page.IsDuplicate)
				{
					PageText pageText = page.GetPageText();
					int num = GuiDialog.CountMatches(pageText.Title ?? "", regex);
					int strictTitleMatches = GuiDialog.CountMatches(pageText.Title ?? "", strictRegex);
					int num2 = GuiDialog.CountMatches(pageText.Text ?? "", regex);
					if (num > 0 || num2 > 0)
					{
						weightedPages.Add(new WeightedHandbookPage
						{
							Page = page,
							TitleMatches = num,
							StrictTitleMatches = strictTitleMatches,
							TitleLength = (pageText.Title?.Length ?? 0),
							TextMatches = num2,
							SearchWeight = 1f + page.SearchWeightOffset
						});
					}
				}
			});
			if (text.Length > 0)
			{
				weightedPages.Sort((WeightedHandbookPage a, WeightedHandbookPage b) =>
				{
					int num = b.StrictTitleMatches - a.StrictTitleMatches;
					if (num != 0)
					{
						return num;
					}
					int num2 = b.TitleMatches - a.TitleMatches;
					if (num2 != 0)
					{
						return num2;
					}
					int num3 = b.SearchWeight.CompareTo(a.SearchWeight);
					if (num3 != 0)
					{
						return num3;
					}
					int num4 = a.TitleLength - b.TitleLength;
					return (num4 != 0) ? num4 : (b.TextMatches - a.TextMatches);
				});
			}
			weightedPages.ForEach((WeightedHandbookPage page) =>
			{
				shownHandbookPages.Add(page.Page);
			});
		}
		GuiElementFlatList flatList = overviewGui.GetFlatList("stacklist");
		flatList.CalcTotalHeight();
		overviewGui.GetScrollbar("scrollbar").SetHeights((float)listHeight, (float)flatList.insideBounds.fixedHeight);
	}

	public override void OnRenderGUI(float deltaTime)
	{
		if (browseHistory.Count == 0 || browseHistory.Peek().SearchText != null)
		{
			SingleComposer = overviewGui;
		}
		else
		{
			SingleComposer = detailViewGui;
		}
		if (SingleComposer == overviewGui)
		{
			overviewGui.GetButton("backButton").Enabled = browseHistory.Count > 0;
		}
		base.OnRenderGUI(deltaTime);
	}

	public override bool CaptureAllInputs()
	{
		return false;
	}

	public override void Dispose()
	{
		if (allHandbookPages != null)
		{
			foreach (GuiHandbookPage allHandbookPage in allHandbookPages)
			{
				allHandbookPage?.Dispose();
			}
		}
		overviewGui?.Dispose();
		detailViewGui?.Dispose();
	}
}
