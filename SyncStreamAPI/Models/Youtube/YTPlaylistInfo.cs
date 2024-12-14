using System;
using System.Collections.Generic;
using Newtonsoft.Json;

namespace SyncStreamAPI.Models.Youtube;

public static class PlaylistInfoExtension
{
    public static PlaylistInfo FromJson(this PlaylistInfo api, string json)
    {
        return JsonConvert.DeserializeObject<PlaylistInfo>(json);
    }
}

public class PlaylistInfo
{
    public ResponseContext ResponseContext { get; set; }
    public Contents Contents { get; set; }
    public Metadata Metadata { get; set; }
    public string TrackingParams { get; set; }
    public Topbar Topbar { get; set; }
    public Microformat Microformat { get; set; }
    public Sidebar Sidebar { get; set; }
}

public class Contents
{
    public TwoColumnBrowseResultsRenderer TwoColumnBrowseResultsRenderer { get; set; }
}

public class TwoColumnBrowseResultsRenderer
{
    public List<Tab> Tabs { get; set; }
}

public class Tab
{
    public TabRenderer TabRenderer { get; set; }
}

public class TabRenderer
{
    public bool Selected { get; set; }
    public TabRendererContent Content { get; set; }
    public string TrackingParams { get; set; }
}

public class TabRendererContent
{
    public SectionListRenderer SectionListRenderer { get; set; }
}

public class SectionListRenderer
{
    public List<SectionListRendererContent> Contents { get; set; }
    public string TrackingParams { get; set; }
}

public class SectionListRendererContent
{
    public ItemSectionRenderer ItemSectionRenderer { get; set; }
}

public class ItemSectionRenderer
{
    public List<ItemSectionRendererContent> Contents { get; set; }
    public string TrackingParams { get; set; }
}

public class ItemSectionRendererContent
{
    public PlaylistVideoListRenderer PlaylistVideoListRenderer { get; set; }
}

public class PlaylistVideoListRenderer
{
    public List<PlaylistVideoListRendererContent> Contents { get; set; }
    public string PlaylistId { get; set; }
    public bool IsEditable { get; set; }
    public List<Continuation> Continuations { get; set; }
    public bool CanReorder { get; set; }
    public string TrackingParams { get; set; }
    public string TargetId { get; set; }
}

public class PlaylistVideoListRendererContent
{
    public PlaylistVideoRenderer PlaylistVideoRenderer { get; set; }
}

public class PlaylistVideoRenderer
{
    public string VideoId { get; set; }
    public PlaylistVideoRendererThumbnail Thumbnail { get; set; }
    public PurpleTitle Title { get; set; }
    public ContentClass Index { get; set; }
    public ShortBylineTextClass ShortBylineText { get; set; }
    public LengthTextClass LengthText { get; set; }
    public PlaylistVideoRendererNavigationEndpoint NavigationEndpoint { get; set; }
    public long LengthSeconds { get; set; }
    public string TrackingParams { get; set; }
    public bool IsPlayable { get; set; }
    public PlaylistVideoRendererMenu Menu { get; set; }
    public List<PlaylistVideoRendererThumbnailOverlay> ThumbnailOverlays { get; set; }
    public List<Badge> Badges { get; set; }
}

public class Badge
{
    public MetadataBadgeRenderer MetadataBadgeRenderer { get; set; }
}

public class MetadataBadgeRenderer
{
    public string Style { get; set; }
    public string Label { get; set; }
    public string TrackingParams { get; set; }
}

public class ContentClass
{
    public string SimpleText { get; set; }
}

public class LengthTextClass
{
    public AccessibilityData Accessibility { get; set; }
    public string SimpleText { get; set; }
}

public class AccessibilityData
{
    public Accessibility AccessibilityDataAccessibilityData { get; set; }
}

public class Accessibility
{
    public string Label { get; set; }
}

public class PlaylistVideoRendererMenu
{
    public PurpleMenuRenderer MenuRenderer { get; set; }
}

public class PurpleMenuRenderer
{
    public List<PurpleItem> Items { get; set; }
    public string TrackingParams { get; set; }
    public AccessibilityData Accessibility { get; set; }
}

public class PurpleItem
{
    public PurpleMenuServiceItemRenderer MenuServiceItemRenderer { get; set; }
}

public class PurpleMenuServiceItemRenderer
{
    public TextElement Text { get; set; }
    public IconImage Icon { get; set; }
    public PurpleServiceEndpoint ServiceEndpoint { get; set; }
    public string TrackingParams { get; set; }
}

public class IconImage
{
    public string IconType { get; set; }
}

public class PurpleServiceEndpoint
{
    public string ClickTrackingParams { get; set; }
    public CommandCommandMetadata CommandMetadata { get; set; }
    public PurpleSignalServiceEndpoint SignalServiceEndpoint { get; set; }
}

public class CommandCommandMetadata
{
    public PurpleWebCommandMetadata WebCommandMetadata { get; set; }
}

public class PurpleWebCommandMetadata
{
    public string Url { get; set; }
    public bool SendPost { get; set; }
}

public class PurpleSignalServiceEndpoint
{
    public string Signal { get; set; }
    public List<PurpleAction> Actions { get; set; }
}

public class PurpleAction
{
    public AddToPlaylistCommand AddToPlaylistCommand { get; set; }
}

public class AddToPlaylistCommand
{
    public bool OpenMiniplayer { get; set; }
    public string VideoId { get; set; }
    public string ListType { get; set; }
    public OnCreateListCommand OnCreateListCommand { get; set; }
    public List<string> VideoIds { get; set; }
}

public class OnCreateListCommand
{
    public string ClickTrackingParams { get; set; }
    public OnCreateListCommandCommandMetadata CommandMetadata { get; set; }
    public CreatePlaylistServiceEndpoint CreatePlaylistServiceEndpoint { get; set; }
}

public class OnCreateListCommandCommandMetadata
{
    public FluffyWebCommandMetadata WebCommandMetadata { get; set; }
}

public class FluffyWebCommandMetadata
{
    public string Url { get; set; }
    public bool SendPost { get; set; }
    public string ApiUrl { get; set; }
}

public class CreatePlaylistServiceEndpoint
{
    public List<string> VideoIds { get; set; }
    public string Params { get; set; }
}

public class TextElement
{
    public List<TextRun> Runs { get; set; }
}

public class TextRun
{
    public string Text { get; set; }
}

public class PlaylistVideoRendererNavigationEndpoint
{
    public string ClickTrackingParams { get; set; }
    public EndpointCommandMetadata CommandMetadata { get; set; }
    public PurpleWatchEndpoint WatchEndpoint { get; set; }
}

public class EndpointCommandMetadata
{
    public TentacledWebCommandMetadata WebCommandMetadata { get; set; }
}

public class TentacledWebCommandMetadata
{
    public string Url { get; set; }
    public string WebPageType { get; set; }
    public long RootVe { get; set; }
}

public class PurpleWatchEndpoint
{
    public string VideoId { get; set; }
    public string PlaylistId { get; set; }
    public long Index { get; set; }
}

public class ShortBylineTextClass
{
    public List<ShortBylineTextRun> Runs { get; set; }
}

public class ShortBylineTextRun
{
    public string Text { get; set; }
    public VideoOwnerRendererNavigationEndpoint NavigationEndpoint { get; set; }
}

public class VideoOwnerRendererNavigationEndpoint
{
    public string ClickTrackingParams { get; set; }
    public EndpointCommandMetadata CommandMetadata { get; set; }
    public NavigationEndpointBrowseEndpoint BrowseEndpoint { get; set; }
}

public class NavigationEndpointBrowseEndpoint
{
    public string BrowseId { get; set; }
    public string CanonicalBaseUrl { get; set; }
}

public class PlaylistVideoRendererThumbnail
{
    public List<ThumbnailElement> Thumbnails { get; set; }
}

public class ThumbnailElement
{
    public Uri Url { get; set; }
    public long Width { get; set; }
    public long Height { get; set; }
}

public class PlaylistVideoRendererThumbnailOverlay
{
    public ThumbnailOverlayTimeStatusRenderer ThumbnailOverlayTimeStatusRenderer { get; set; }
    public ThumbnailOverlayNowPlayingRenderer ThumbnailOverlayNowPlayingRenderer { get; set; }
}

public class ThumbnailOverlayNowPlayingRenderer
{
    public TextElement Text { get; set; }
}

public class ThumbnailOverlayTimeStatusRenderer
{
    public LengthTextClass Text { get; set; }
    public string Style { get; set; }
}

public class PurpleTitle
{
    public List<TextRun> Runs { get; set; }
    public AccessibilityData Accessibility { get; set; }
}

public class Continuation
{
    public NextContinuationData NextContinuationData { get; set; }
}

public class NextContinuationData
{
    public string Continuation { get; set; }
    public string ClickTrackingParams { get; set; }
}

public class Metadata
{
    public PlaylistMetadataRenderer PlaylistMetadataRenderer { get; set; }
}

public class PlaylistMetadataRenderer
{
    public string Title { get; set; }
    public string Description { get; set; }
    public string AndroidAppindexingLink { get; set; }
    public string IosAppindexingLink { get; set; }
}

public class Microformat
{
    public MicroformatDataRenderer MicroformatDataRenderer { get; set; }
}

public class MicroformatDataRenderer
{
    public Uri UrlCanonical { get; set; }
    public string Title { get; set; }
    public string Description { get; set; }
    public PlaylistVideoRendererThumbnail Thumbnail { get; set; }
    public string SiteName { get; set; }
    public string AppName { get; set; }
    public string AndroidPackage { get; set; }
    public long IosAppStoreId { get; set; }
    public Uri IosAppArguments { get; set; }
    public string OgType { get; set; }
    public Uri UrlApplinksWeb { get; set; }
    public Uri UrlApplinksIos { get; set; }
    public Uri UrlApplinksAndroid { get; set; }
    public Uri UrlTwitterIos { get; set; }
    public Uri UrlTwitterAndroid { get; set; }
    public string TwitterCardType { get; set; }
    public string TwitterSiteHandle { get; set; }
    public Uri SchemaDotOrgType { get; set; }
    public bool Noindex { get; set; }
    public bool Unlisted { get; set; }
    public List<LinkAlternate> LinkAlternates { get; set; }
}

public class LinkAlternate
{
    public string HrefUrl { get; set; }
}

public class ResponseContext
{
    public List<ServiceTrackingParam> ServiceTrackingParams { get; set; }
    public WebResponseContextExtensionData WebResponseContextExtensionData { get; set; }
}

public class ServiceTrackingParam
{
    public string Service { get; set; }
    public List<Param> Params { get; set; }
}

public class Param
{
    public string Key { get; set; }
    public string Value { get; set; }
}

public class WebResponseContextExtensionData
{
    public YtConfigData YtConfigData { get; set; }
    public bool HasDecorated { get; set; }
}

public class YtConfigData
{
    public string Csn { get; set; }
    public string VisitorData { get; set; }
    public long RootVisualElementType { get; set; }
}

public class Sidebar
{
    public PlaylistSidebarRenderer PlaylistSidebarRenderer { get; set; }
}

public class PlaylistSidebarRenderer
{
    public List<PlaylistSidebarRendererItem> Items { get; set; }
    public string TrackingParams { get; set; }
}

public class PlaylistSidebarRendererItem
{
    public PlaylistSidebarPrimaryInfoRenderer PlaylistSidebarPrimaryInfoRenderer { get; set; }
    public PlaylistSidebarSecondaryInfoRenderer PlaylistSidebarSecondaryInfoRenderer { get; set; }
}

public class PlaylistSidebarPrimaryInfoRenderer
{
    public ThumbnailRenderer ThumbnailRenderer { get; set; }
    public PlaylistSidebarPrimaryInfoRendererTitle Title { get; set; }
    public List<Stat> Stats { get; set; }
    public PlaylistSidebarPrimaryInfoRendererMenu Menu { get; set; }
    public List<PlaylistSidebarPrimaryInfoRendererThumbnailOverlay> ThumbnailOverlays { get; set; }
    public PlaylistSidebarPrimaryInfoRendererNavigationEndpoint NavigationEndpoint { get; set; }
    public Description Description { get; set; }
    public TextElement ShowMoreText { get; set; }
}

public class Description
{
    public List<DescriptionRun> Runs { get; set; }
}

public class DescriptionRun
{
    public string Text { get; set; }
    public PurpleNavigationEndpoint NavigationEndpoint { get; set; }
}

public class PurpleNavigationEndpoint
{
    public string ClickTrackingParams { get; set; }
    public EndpointCommandMetadata CommandMetadata { get; set; }
    public PurpleUrlEndpoint UrlEndpoint { get; set; }
}

public class PurpleUrlEndpoint
{
    public Uri Url { get; set; }
    public string Target { get; set; }
    public bool Nofollow { get; set; }
}

public class PlaylistSidebarPrimaryInfoRendererMenu
{
    public FluffyMenuRenderer MenuRenderer { get; set; }
}

public class FluffyMenuRenderer
{
    public List<FluffyItem> Items { get; set; }
    public string TrackingParams { get; set; }
    public List<TopLevelButton> TopLevelButtons { get; set; }
    public AccessibilityData Accessibility { get; set; }
}

public class FluffyItem
{
    public FluffyMenuServiceItemRenderer MenuServiceItemRenderer { get; set; }
}

public class FluffyMenuServiceItemRenderer
{
    public TextElement Text { get; set; }
    public IconImage Icon { get; set; }
    public FluffyServiceEndpoint ServiceEndpoint { get; set; }
    public string TrackingParams { get; set; }
}

public class FluffyServiceEndpoint
{
    public string ClickTrackingParams { get; set; }
    public CommandCommandMetadata CommandMetadata { get; set; }
    public FluffySignalServiceEndpoint SignalServiceEndpoint { get; set; }
}

public class FluffySignalServiceEndpoint
{
    public string Signal { get; set; }
    public List<FluffyAction> Actions { get; set; }
}

public class FluffyAction
{
    public PurpleOpenPopupAction OpenPopupAction { get; set; }
}

public class PurpleOpenPopupAction
{
    public PurplePopup Popup { get; set; }
    public string PopupType { get; set; }
}

public class PurplePopup
{
    public ConfirmDialogRenderer ConfirmDialogRenderer { get; set; }
}

public class ConfirmDialogRenderer
{
    public TextElement Title { get; set; }
    public string TrackingParams { get; set; }
    public List<TextElement> DialogMessages { get; set; }
    public A11YSkipNavigationButtonClass ConfirmButton { get; set; }
    public A11YSkipNavigationButtonClass CancelButton { get; set; }
    public bool PrimaryIsCancel { get; set; }
}

public class A11YSkipNavigationButtonClass
{
    public A11YSkipNavigationButtonButtonRenderer ButtonRenderer { get; set; }
}

public class A11YSkipNavigationButtonButtonRenderer
{
    public string Style { get; set; }
    public string Size { get; set; }
    public bool IsDisabled { get; set; }
    public TextElement Text { get; set; }
    public string TrackingParams { get; set; }
    public ButtonRendererCommand Command { get; set; }
    public TentacledServiceEndpoint ServiceEndpoint { get; set; }
    public FluffyNavigationEndpoint NavigationEndpoint { get; set; }
}

public class ButtonRendererCommand
{
    public string ClickTrackingParams { get; set; }
    public CommandCommandMetadata CommandMetadata { get; set; }
    public CommandSignalServiceEndpoint SignalServiceEndpoint { get; set; }
}

public class CommandSignalServiceEndpoint
{
    public string Signal { get; set; }
    public List<TentacledAction> Actions { get; set; }
}

public class TentacledAction
{
    public SignalAction SignalAction { get; set; }
}

public class SignalAction
{
    public string Signal { get; set; }
}

public class FluffyNavigationEndpoint
{
    public string ClickTrackingParams { get; set; }
    public DefaultNavigationEndpointCommandMetadata CommandMetadata { get; set; }
    public NavigationEndpointModalEndpoint ModalEndpoint { get; set; }
}

public class DefaultNavigationEndpointCommandMetadata
{
    public StickyWebCommandMetadata WebCommandMetadata { get; set; }
}

public class StickyWebCommandMetadata
{
    public bool IgnoreNavigation { get; set; }
}

public class NavigationEndpointModalEndpoint
{
    public PurpleModal Modal { get; set; }
}

public class PurpleModal
{
    public PurpleModalWithTitleAndButtonRenderer ModalWithTitleAndButtonRenderer { get; set; }
}

public class PurpleModalWithTitleAndButtonRenderer
{
    public ContentClass Title { get; set; }
    public ContentClass Content { get; set; }
    public PurpleButton Button { get; set; }
}

public class PurpleButton
{
    public PurpleButtonRenderer ButtonRenderer { get; set; }
}

public class PurpleButtonRenderer
{
    public string Style { get; set; }
    public string Size { get; set; }
    public bool IsDisabled { get; set; }
    public ContentClass Text { get; set; }
    public TentacledNavigationEndpoint NavigationEndpoint { get; set; }
    public string TrackingParams { get; set; }
}

public class TentacledNavigationEndpoint
{
    public string ClickTrackingParams { get; set; }
    public EndpointCommandMetadata CommandMetadata { get; set; }
    public PurpleSignInEndpoint SignInEndpoint { get; set; }
}

public class PurpleSignInEndpoint
{
    public Endpoint NextEndpoint { get; set; }
    public string ContinueAction { get; set; }
    public long IdamTag { get; set; }
}

public class Endpoint
{
    public string ClickTrackingParams { get; set; }
    public EndpointCommandMetadata CommandMetadata { get; set; }
    public EndpointBrowseEndpoint BrowseEndpoint { get; set; }
}

public class EndpointBrowseEndpoint
{
    public string BrowseId { get; set; }
}

public class TentacledServiceEndpoint
{
    public string ClickTrackingParams { get; set; }
    public OnCreateListCommandCommandMetadata CommandMetadata { get; set; }
    public FlagEndpoint FlagEndpoint { get; set; }
}

public class FlagEndpoint
{
    public string FlagAction { get; set; }
}

public class TopLevelButton
{
    public ToggleButtonRenderer ToggleButtonRenderer { get; set; }
    public TopLevelButtonButtonRenderer ButtonRenderer { get; set; }
}

public class TopLevelButtonButtonRenderer
{
    public string Style { get; set; }
    public string Size { get; set; }
    public bool IsDisabled { get; set; }
    public IconImage Icon { get; set; }
    public StickyNavigationEndpoint NavigationEndpoint { get; set; }
    public Accessibility Accessibility { get; set; }
    public string Tooltip { get; set; }
    public string TrackingParams { get; set; }
    public StickyServiceEndpoint ServiceEndpoint { get; set; }
}

public class StickyNavigationEndpoint
{
    public string ClickTrackingParams { get; set; }
    public EndpointCommandMetadata CommandMetadata { get; set; }
    public FluffyWatchEndpoint WatchEndpoint { get; set; }
}

public class FluffyWatchEndpoint
{
    public string VideoId { get; set; }
    public string PlaylistId { get; set; }
    public string Params { get; set; }
}

public class StickyServiceEndpoint
{
    public string ClickTrackingParams { get; set; }
    public OnCreateListCommandCommandMetadata CommandMetadata { get; set; }
    public ShareEntityServiceEndpoint ShareEntityServiceEndpoint { get; set; }
}

public class ShareEntityServiceEndpoint
{
    public string SerializedShareEntity { get; set; }
    public List<CommandElement> Commands { get; set; }
}

public class CommandElement
{
    public CommandOpenPopupAction OpenPopupAction { get; set; }
}

public class CommandOpenPopupAction
{
    public FluffyPopup Popup { get; set; }
    public string PopupType { get; set; }
    public bool BeReused { get; set; }
}

public class FluffyPopup
{
    public UnifiedSharePanelRenderer UnifiedSharePanelRenderer { get; set; }
}

public class UnifiedSharePanelRenderer
{
    public string TrackingParams { get; set; }
    public bool ShowLoadingSpinner { get; set; }
}

public class ToggleButtonRenderer
{
    public StyleClass Style { get; set; }
    public Size Size { get; set; }
    public bool IsToggled { get; set; }
    public bool IsDisabled { get; set; }
    public IconImage DefaultIcon { get; set; }
    public IconImage ToggledIcon { get; set; }
    public string TrackingParams { get; set; }
    public string DefaultTooltip { get; set; }
    public string ToggledTooltip { get; set; }
    public DefaultNavigationEndpoint DefaultNavigationEndpoint { get; set; }
    public AccessibilityData AccessibilityData { get; set; }
    public AccessibilityData ToggledAccessibilityData { get; set; }
}

public class DefaultNavigationEndpoint
{
    public string ClickTrackingParams { get; set; }
    public DefaultNavigationEndpointCommandMetadata CommandMetadata { get; set; }
    public DefaultNavigationEndpointModalEndpoint ModalEndpoint { get; set; }
}

public class DefaultNavigationEndpointModalEndpoint
{
    public FluffyModal Modal { get; set; }
}

public class FluffyModal
{
    public FluffyModalWithTitleAndButtonRenderer ModalWithTitleAndButtonRenderer { get; set; }
}

public class FluffyModalWithTitleAndButtonRenderer
{
    public ContentClass Title { get; set; }
    public ContentClass Content { get; set; }
    public FluffyButton Button { get; set; }
}

public class FluffyButton
{
    public FluffyButtonRenderer ButtonRenderer { get; set; }
}

public class FluffyButtonRenderer
{
    public string Style { get; set; }
    public string Size { get; set; }
    public bool IsDisabled { get; set; }
    public ContentClass Text { get; set; }
    public IndigoNavigationEndpoint NavigationEndpoint { get; set; }
    public string TrackingParams { get; set; }
}

public class IndigoNavigationEndpoint
{
    public string ClickTrackingParams { get; set; }
    public EndpointCommandMetadata CommandMetadata { get; set; }
    public FluffySignInEndpoint SignInEndpoint { get; set; }
}

public class FluffySignInEndpoint
{
    public Endpoint NextEndpoint { get; set; }
    public long IdamTag { get; set; }
}

public class Size
{
    public string SizeType { get; set; }
}

public class StyleClass
{
    public string StyleType { get; set; }
}

public class PlaylistSidebarPrimaryInfoRendererNavigationEndpoint
{
    public string ClickTrackingParams { get; set; }
    public EndpointCommandMetadata CommandMetadata { get; set; }
    public TentacledWatchEndpoint WatchEndpoint { get; set; }
}

public class TentacledWatchEndpoint
{
    public string VideoId { get; set; }
    public string PlaylistId { get; set; }
}

public class Stat
{
    public List<TextRun> Runs { get; set; }
    public string SimpleText { get; set; }
}

public class PlaylistSidebarPrimaryInfoRendererThumbnailOverlay
{
    public ThumbnailOverlaySidePanelRenderer ThumbnailOverlaySidePanelRenderer { get; set; }
}

public class ThumbnailOverlaySidePanelRenderer
{
    public ContentClass Text { get; set; }
    public IconImage Icon { get; set; }
}

public class ThumbnailRenderer
{
    public PlaylistVideoThumbnailRenderer PlaylistVideoThumbnailRenderer { get; set; }
}

public class PlaylistVideoThumbnailRenderer
{
    public PlaylistVideoRendererThumbnail Thumbnail { get; set; }
}

public class PlaylistSidebarPrimaryInfoRendererTitle
{
    public List<PurpleRun> Runs { get; set; }
}

public class PurpleRun
{
    public string Text { get; set; }
    public PlaylistSidebarPrimaryInfoRendererNavigationEndpoint NavigationEndpoint { get; set; }
}

public class PlaylistSidebarSecondaryInfoRenderer
{
    public VideoOwner VideoOwner { get; set; }
    public A11YSkipNavigationButtonClass Button { get; set; }
}

public class VideoOwner
{
    public VideoOwnerRenderer VideoOwnerRenderer { get; set; }
}

public class VideoOwnerRenderer
{
    public PlaylistVideoRendererThumbnail Thumbnail { get; set; }
    public ShortBylineTextClass Title { get; set; }
    public VideoOwnerRendererNavigationEndpoint NavigationEndpoint { get; set; }
    public string TrackingParams { get; set; }
}

public class Topbar
{
    public DesktopTopbarRenderer DesktopTopbarRenderer { get; set; }
}

public class DesktopTopbarRenderer
{
    public Logo Logo { get; set; }
    public Searchbox Searchbox { get; set; }
    public string TrackingParams { get; set; }
    public string CountryCode { get; set; }
    public List<TopbarButton> TopbarButtons { get; set; }
    public HotkeyDialog HotkeyDialog { get; set; }
    public Button BackButton { get; set; }
    public Button ForwardButton { get; set; }
    public A11YSkipNavigationButtonClass A11YSkipNavigationButton { get; set; }
}

public class Button
{
    public BackButtonButtonRenderer ButtonRenderer { get; set; }
}

public class BackButtonButtonRenderer
{
    public string TrackingParams { get; set; }
    public ButtonRendererCommand Command { get; set; }
}

public class HotkeyDialog
{
    public HotkeyDialogRenderer HotkeyDialogRenderer { get; set; }
}

public class HotkeyDialogRenderer
{
    public TextElement Title { get; set; }
    public List<HotkeyDialogRendererSection> Sections { get; set; }
    public A11YSkipNavigationButtonClass DismissButton { get; set; }
    public string TrackingParams { get; set; }
}

public class HotkeyDialogRendererSection
{
    public HotkeyDialogSectionRenderer HotkeyDialogSectionRenderer { get; set; }
}

public class HotkeyDialogSectionRenderer
{
    public TextElement Title { get; set; }
    public List<Option> Options { get; set; }
}

public class Option
{
    public HotkeyDialogSectionOptionRenderer HotkeyDialogSectionOptionRenderer { get; set; }
}

public class HotkeyDialogSectionOptionRenderer
{
    public TextElement Label { get; set; }
    public string Hotkey { get; set; }
    public AccessibilityData HotkeyAccessibilityLabel { get; set; }
}

public class Logo
{
    public TopbarLogoRenderer TopbarLogoRenderer { get; set; }
}

public class TopbarLogoRenderer
{
    public IconImage IconImage { get; set; }
    public TextElement TooltipText { get; set; }
    public Endpoint Endpoint { get; set; }
    public string TrackingParams { get; set; }
}

public class Searchbox
{
    public FusionSearchboxRenderer FusionSearchboxRenderer { get; set; }
}

public class FusionSearchboxRenderer
{
    public IconImage Icon { get; set; }
    public TextElement PlaceholderText { get; set; }
    public Config Config { get; set; }
    public string TrackingParams { get; set; }
    public FusionSearchboxRendererSearchEndpoint SearchEndpoint { get; set; }
}

public class Config
{
    public WebSearchboxConfig WebSearchboxConfig { get; set; }
}

public class WebSearchboxConfig
{
    public string RequestLanguage { get; set; }
    public string RequestDomain { get; set; }
    public bool HasOnscreenKeyboard { get; set; }
    public bool FocusSearchbox { get; set; }
}

public class FusionSearchboxRendererSearchEndpoint
{
    public string ClickTrackingParams { get; set; }
    public EndpointCommandMetadata CommandMetadata { get; set; }
    public SearchEndpointSearchEndpoint SearchEndpoint { get; set; }
}

public class SearchEndpointSearchEndpoint
{
    public string Query { get; set; }
}

public class TopbarButton
{
    public TopbarButtonButtonRenderer ButtonRenderer { get; set; }
    public TopbarMenuButtonRenderer TopbarMenuButtonRenderer { get; set; }
}

public class TopbarButtonButtonRenderer
{
    public string Style { get; set; }
    public string Size { get; set; }
    public bool? IsDisabled { get; set; }
    public IconImage Icon { get; set; }
    public IndecentNavigationEndpoint NavigationEndpoint { get; set; }
    public Accessibility Accessibility { get; set; }
    public string Tooltip { get; set; }
    public string TrackingParams { get; set; }
    public AccessibilityData AccessibilityData { get; set; }
    public TextElement Text { get; set; }
    public string TargetId { get; set; }
}

public class IndecentNavigationEndpoint
{
    public string ClickTrackingParams { get; set; }
    public EndpointCommandMetadata CommandMetadata { get; set; }
    public TentacledSignInEndpoint SignInEndpoint { get; set; }
}

public class TentacledSignInEndpoint
{
    public NextEndpoint NextEndpoint { get; set; }
    public long? IdamTag { get; set; }
}

public class NextEndpoint
{
    public string ClickTrackingParams { get; set; }
    public EndpointCommandMetadata CommandMetadata { get; set; }
    public UploadEndpoint UploadEndpoint { get; set; }
}

public class UploadEndpoint
{
    public bool Hack { get; set; }
}

public class TopbarMenuButtonRenderer
{
    public IconImage Icon { get; set; }
    public TopbarMenuButtonRendererMenuRenderer MenuRenderer { get; set; }
    public string TrackingParams { get; set; }
    public AccessibilityData Accessibility { get; set; }
    public string Tooltip { get; set; }
    public string Style { get; set; }
    public string TargetId { get; set; }
    public MenuRequest MenuRequest { get; set; }
}

public class TopbarMenuButtonRendererMenuRenderer
{
    public MenuRendererMultiPageMenuRenderer MultiPageMenuRenderer { get; set; }
}

public class MenuRendererMultiPageMenuRenderer
{
    public List<MultiPageMenuRendererSection> Sections { get; set; }
    public string TrackingParams { get; set; }
}

public class MultiPageMenuRendererSection
{
    public MultiPageMenuSectionRenderer MultiPageMenuSectionRenderer { get; set; }
}

public class MultiPageMenuSectionRenderer
{
    public List<MultiPageMenuSectionRendererItem> Items { get; set; }
    public string TrackingParams { get; set; }
}

public class MultiPageMenuSectionRendererItem
{
    public CompactLinkRenderer CompactLinkRenderer { get; set; }
}

public class CompactLinkRenderer
{
    public IconImage Icon { get; set; }
    public TextElement Title { get; set; }
    public CompactLinkRendererNavigationEndpoint NavigationEndpoint { get; set; }
    public string TrackingParams { get; set; }
}

public class CompactLinkRendererNavigationEndpoint
{
    public string ClickTrackingParams { get; set; }
    public EndpointCommandMetadata CommandMetadata { get; set; }
    public FluffyUrlEndpoint UrlEndpoint { get; set; }
}

public class FluffyUrlEndpoint
{
    public Uri Url { get; set; }
    public string Target { get; set; }
}

public class MenuRequest
{
    public string ClickTrackingParams { get; set; }
    public OnCreateListCommandCommandMetadata CommandMetadata { get; set; }
    public MenuRequestSignalServiceEndpoint SignalServiceEndpoint { get; set; }
}

public class MenuRequestSignalServiceEndpoint
{
    public string Signal { get; set; }
    public List<StickyAction> Actions { get; set; }
}

public class StickyAction
{
    public FluffyOpenPopupAction OpenPopupAction { get; set; }
}

public class FluffyOpenPopupAction
{
    public TentacledPopup Popup { get; set; }
    public string PopupType { get; set; }
    public bool BeReused { get; set; }
}

public class TentacledPopup
{
    public PopupMultiPageMenuRenderer MultiPageMenuRenderer { get; set; }
}

public class PopupMultiPageMenuRenderer
{
    public string TrackingParams { get; set; }
    public string Style { get; set; }
    public bool ShowLoadingSpinner { get; set; }
}