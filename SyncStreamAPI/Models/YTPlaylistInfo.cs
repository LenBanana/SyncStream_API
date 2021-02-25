using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace SyncStreamAPI.Models
{
    public static class PlaylistInfoExtension
    {
        public static PlaylistInfo FromJson(this PlaylistInfo api, string json) => JsonConvert.DeserializeObject<PlaylistInfo>(json);
    }

    public partial class PlaylistInfo
    {
        public ResponseContext ResponseContext { get; set; }
        public Contents Contents { get; set; }
        public Metadata Metadata { get; set; }
        public string TrackingParams { get; set; }
        public Topbar Topbar { get; set; }
        public Microformat Microformat { get; set; }
        public Sidebar Sidebar { get; set; }
    }

    public partial class Contents
    {
        public TwoColumnBrowseResultsRenderer TwoColumnBrowseResultsRenderer { get; set; }
    }

    public partial class TwoColumnBrowseResultsRenderer
    {
        public List<Tab> Tabs { get; set; }
    }

    public partial class Tab
    {
        public TabRenderer TabRenderer { get; set; }
    }

    public partial class TabRenderer
    {
        public bool Selected { get; set; }
        public TabRendererContent Content { get; set; }
        public string TrackingParams { get; set; }
    }

    public partial class TabRendererContent
    {
        public SectionListRenderer SectionListRenderer { get; set; }
    }

    public partial class SectionListRenderer
    {
        public List<SectionListRendererContent> Contents { get; set; }
        public string TrackingParams { get; set; }
    }

    public partial class SectionListRendererContent
    {
        public ItemSectionRenderer ItemSectionRenderer { get; set; }
    }

    public partial class ItemSectionRenderer
    {
        public List<ItemSectionRendererContent> Contents { get; set; }
        public string TrackingParams { get; set; }
    }

    public partial class ItemSectionRendererContent
    {
        public PlaylistVideoListRenderer PlaylistVideoListRenderer { get; set; }
    }

    public partial class PlaylistVideoListRenderer
    {
        public List<PlaylistVideoListRendererContent> Contents { get; set; }
        public string PlaylistId { get; set; }
        public bool IsEditable { get; set; }
        public List<Continuation> Continuations { get; set; }
        public bool CanReorder { get; set; }
        public string TrackingParams { get; set; }
        public string TargetId { get; set; }
    }

    public partial class PlaylistVideoListRendererContent
    {
        public PlaylistVideoRenderer PlaylistVideoRenderer { get; set; }
    }

    public partial class PlaylistVideoRenderer
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

    public partial class Badge
    {
        public MetadataBadgeRenderer MetadataBadgeRenderer { get; set; }
    }

    public partial class MetadataBadgeRenderer
    {
        public string Style { get; set; }
        public string Label { get; set; }
        public string TrackingParams { get; set; }
    }

    public partial class ContentClass
    {
        public string SimpleText { get; set; }
    }

    public partial class LengthTextClass
    {
        public AccessibilityData Accessibility { get; set; }
        public string SimpleText { get; set; }
    }

    public partial class AccessibilityData
    {
        public Accessibility AccessibilityDataAccessibilityData { get; set; }
    }

    public partial class Accessibility
    {
        public string Label { get; set; }
    }

    public partial class PlaylistVideoRendererMenu
    {
        public PurpleMenuRenderer MenuRenderer { get; set; }
    }

    public partial class PurpleMenuRenderer
    {
        public List<PurpleItem> Items { get; set; }
        public string TrackingParams { get; set; }
        public AccessibilityData Accessibility { get; set; }
    }

    public partial class PurpleItem
    {
        public PurpleMenuServiceItemRenderer MenuServiceItemRenderer { get; set; }
    }

    public partial class PurpleMenuServiceItemRenderer
    {
        public TextElement Text { get; set; }
        public IconImage Icon { get; set; }
        public PurpleServiceEndpoint ServiceEndpoint { get; set; }
        public string TrackingParams { get; set; }
    }

    public partial class IconImage
    {
        public string IconType { get; set; }
    }

    public partial class PurpleServiceEndpoint
    {
        public string ClickTrackingParams { get; set; }
        public CommandCommandMetadata CommandMetadata { get; set; }
        public PurpleSignalServiceEndpoint SignalServiceEndpoint { get; set; }
    }

    public partial class CommandCommandMetadata
    {
        public PurpleWebCommandMetadata WebCommandMetadata { get; set; }
    }

    public partial class PurpleWebCommandMetadata
    {
        public string Url { get; set; }
        public bool SendPost { get; set; }
    }

    public partial class PurpleSignalServiceEndpoint
    {
        public string Signal { get; set; }
        public List<PurpleAction> Actions { get; set; }
    }

    public partial class PurpleAction
    {
        public AddToPlaylistCommand AddToPlaylistCommand { get; set; }
    }

    public partial class AddToPlaylistCommand
    {
        public bool OpenMiniplayer { get; set; }
        public string VideoId { get; set; }
        public string ListType { get; set; }
        public OnCreateListCommand OnCreateListCommand { get; set; }
        public List<string> VideoIds { get; set; }
    }

    public partial class OnCreateListCommand
    {
        public string ClickTrackingParams { get; set; }
        public OnCreateListCommandCommandMetadata CommandMetadata { get; set; }
        public CreatePlaylistServiceEndpoint CreatePlaylistServiceEndpoint { get; set; }
    }

    public partial class OnCreateListCommandCommandMetadata
    {
        public FluffyWebCommandMetadata WebCommandMetadata { get; set; }
    }

    public partial class FluffyWebCommandMetadata
    {
        public string Url { get; set; }
        public bool SendPost { get; set; }
        public string ApiUrl { get; set; }
    }

    public partial class CreatePlaylistServiceEndpoint
    {
        public List<string> VideoIds { get; set; }
        public string Params { get; set; }
    }

    public partial class TextElement
    {
        public List<TextRun> Runs { get; set; }
    }

    public partial class TextRun
    {
        public string Text { get; set; }
    }

    public partial class PlaylistVideoRendererNavigationEndpoint
    {
        public string ClickTrackingParams { get; set; }
        public EndpointCommandMetadata CommandMetadata { get; set; }
        public PurpleWatchEndpoint WatchEndpoint { get; set; }
    }

    public partial class EndpointCommandMetadata
    {
        public TentacledWebCommandMetadata WebCommandMetadata { get; set; }
    }

    public partial class TentacledWebCommandMetadata
    {
        public string Url { get; set; }
        public string WebPageType { get; set; }
        public long RootVe { get; set; }
    }

    public partial class PurpleWatchEndpoint
    {
        public string VideoId { get; set; }
        public string PlaylistId { get; set; }
        public long Index { get; set; }
    }

    public partial class ShortBylineTextClass
    {
        public List<ShortBylineTextRun> Runs { get; set; }
    }

    public partial class ShortBylineTextRun
    {
        public string Text { get; set; }
        public VideoOwnerRendererNavigationEndpoint NavigationEndpoint { get; set; }
    }

    public partial class VideoOwnerRendererNavigationEndpoint
    {
        public string ClickTrackingParams { get; set; }
        public EndpointCommandMetadata CommandMetadata { get; set; }
        public NavigationEndpointBrowseEndpoint BrowseEndpoint { get; set; }
    }

    public partial class NavigationEndpointBrowseEndpoint
    {
        public string BrowseId { get; set; }
        public string CanonicalBaseUrl { get; set; }
    }

    public partial class PlaylistVideoRendererThumbnail
    {
        public List<ThumbnailElement> Thumbnails { get; set; }
    }

    public partial class ThumbnailElement
    {
        public Uri Url { get; set; }
        public long Width { get; set; }
        public long Height { get; set; }
    }

    public partial class PlaylistVideoRendererThumbnailOverlay
    {
        public ThumbnailOverlayTimeStatusRenderer ThumbnailOverlayTimeStatusRenderer { get; set; }
        public ThumbnailOverlayNowPlayingRenderer ThumbnailOverlayNowPlayingRenderer { get; set; }
    }

    public partial class ThumbnailOverlayNowPlayingRenderer
    {
        public TextElement Text { get; set; }
    }

    public partial class ThumbnailOverlayTimeStatusRenderer
    {
        public LengthTextClass Text { get; set; }
        public string Style { get; set; }
    }

    public partial class PurpleTitle
    {
        public List<TextRun> Runs { get; set; }
        public AccessibilityData Accessibility { get; set; }
    }

    public partial class Continuation
    {
        public NextContinuationData NextContinuationData { get; set; }
    }

    public partial class NextContinuationData
    {
        public string Continuation { get; set; }
        public string ClickTrackingParams { get; set; }
    }

    public partial class Metadata
    {
        public PlaylistMetadataRenderer PlaylistMetadataRenderer { get; set; }
    }

    public partial class PlaylistMetadataRenderer
    {
        public string Title { get; set; }
        public string Description { get; set; }
        public string AndroidAppindexingLink { get; set; }
        public string IosAppindexingLink { get; set; }
    }

    public partial class Microformat
    {
        public MicroformatDataRenderer MicroformatDataRenderer { get; set; }
    }

    public partial class MicroformatDataRenderer
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

    public partial class LinkAlternate
    {
        public string HrefUrl { get; set; }
    }

    public partial class ResponseContext
    {
        public List<ServiceTrackingParam> ServiceTrackingParams { get; set; }
        public WebResponseContextExtensionData WebResponseContextExtensionData { get; set; }
    }

    public partial class ServiceTrackingParam
    {
        public string Service { get; set; }
        public List<Param> Params { get; set; }
    }

    public partial class Param
    {
        public string Key { get; set; }
        public string Value { get; set; }
    }

    public partial class WebResponseContextExtensionData
    {
        public YtConfigData YtConfigData { get; set; }
        public bool HasDecorated { get; set; }
    }

    public partial class YtConfigData
    {
        public string Csn { get; set; }
        public string VisitorData { get; set; }
        public long RootVisualElementType { get; set; }
    }

    public partial class Sidebar
    {
        public PlaylistSidebarRenderer PlaylistSidebarRenderer { get; set; }
    }

    public partial class PlaylistSidebarRenderer
    {
        public List<PlaylistSidebarRendererItem> Items { get; set; }
        public string TrackingParams { get; set; }
    }

    public partial class PlaylistSidebarRendererItem
    {
        public PlaylistSidebarPrimaryInfoRenderer PlaylistSidebarPrimaryInfoRenderer { get; set; }
        public PlaylistSidebarSecondaryInfoRenderer PlaylistSidebarSecondaryInfoRenderer { get; set; }
    }

    public partial class PlaylistSidebarPrimaryInfoRenderer
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

    public partial class Description
    {
        public List<DescriptionRun> Runs { get; set; }
    }

    public partial class DescriptionRun
    {
        public string Text { get; set; }
        public PurpleNavigationEndpoint NavigationEndpoint { get; set; }
    }

    public partial class PurpleNavigationEndpoint
    {
        public string ClickTrackingParams { get; set; }
        public EndpointCommandMetadata CommandMetadata { get; set; }
        public PurpleUrlEndpoint UrlEndpoint { get; set; }
    }

    public partial class PurpleUrlEndpoint
    {
        public Uri Url { get; set; }
        public string Target { get; set; }
        public bool Nofollow { get; set; }
    }

    public partial class PlaylistSidebarPrimaryInfoRendererMenu
    {
        public FluffyMenuRenderer MenuRenderer { get; set; }
    }

    public partial class FluffyMenuRenderer
    {
        public List<FluffyItem> Items { get; set; }
        public string TrackingParams { get; set; }
        public List<TopLevelButton> TopLevelButtons { get; set; }
        public AccessibilityData Accessibility { get; set; }
    }

    public partial class FluffyItem
    {
        public FluffyMenuServiceItemRenderer MenuServiceItemRenderer { get; set; }
    }

    public partial class FluffyMenuServiceItemRenderer
    {
        public TextElement Text { get; set; }
        public IconImage Icon { get; set; }
        public FluffyServiceEndpoint ServiceEndpoint { get; set; }
        public string TrackingParams { get; set; }
    }

    public partial class FluffyServiceEndpoint
    {
        public string ClickTrackingParams { get; set; }
        public CommandCommandMetadata CommandMetadata { get; set; }
        public FluffySignalServiceEndpoint SignalServiceEndpoint { get; set; }
    }

    public partial class FluffySignalServiceEndpoint
    {
        public string Signal { get; set; }
        public List<FluffyAction> Actions { get; set; }
    }

    public partial class FluffyAction
    {
        public PurpleOpenPopupAction OpenPopupAction { get; set; }
    }

    public partial class PurpleOpenPopupAction
    {
        public PurplePopup Popup { get; set; }
        public string PopupType { get; set; }
    }

    public partial class PurplePopup
    {
        public ConfirmDialogRenderer ConfirmDialogRenderer { get; set; }
    }

    public partial class ConfirmDialogRenderer
    {
        public TextElement Title { get; set; }
        public string TrackingParams { get; set; }
        public List<TextElement> DialogMessages { get; set; }
        public A11YSkipNavigationButtonClass ConfirmButton { get; set; }
        public A11YSkipNavigationButtonClass CancelButton { get; set; }
        public bool PrimaryIsCancel { get; set; }
    }

    public partial class A11YSkipNavigationButtonClass
    {
        public A11YSkipNavigationButtonButtonRenderer ButtonRenderer { get; set; }
    }

    public partial class A11YSkipNavigationButtonButtonRenderer
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

    public partial class ButtonRendererCommand
    {
        public string ClickTrackingParams { get; set; }
        public CommandCommandMetadata CommandMetadata { get; set; }
        public CommandSignalServiceEndpoint SignalServiceEndpoint { get; set; }
    }

    public partial class CommandSignalServiceEndpoint
    {
        public string Signal { get; set; }
        public List<TentacledAction> Actions { get; set; }
    }

    public partial class TentacledAction
    {
        public SignalAction SignalAction { get; set; }
    }

    public partial class SignalAction
    {
        public string Signal { get; set; }
    }

    public partial class FluffyNavigationEndpoint
    {
        public string ClickTrackingParams { get; set; }
        public DefaultNavigationEndpointCommandMetadata CommandMetadata { get; set; }
        public NavigationEndpointModalEndpoint ModalEndpoint { get; set; }
    }

    public partial class DefaultNavigationEndpointCommandMetadata
    {
        public StickyWebCommandMetadata WebCommandMetadata { get; set; }
    }

    public partial class StickyWebCommandMetadata
    {
        public bool IgnoreNavigation { get; set; }
    }

    public partial class NavigationEndpointModalEndpoint
    {
        public PurpleModal Modal { get; set; }
    }

    public partial class PurpleModal
    {
        public PurpleModalWithTitleAndButtonRenderer ModalWithTitleAndButtonRenderer { get; set; }
    }

    public partial class PurpleModalWithTitleAndButtonRenderer
    {
        public ContentClass Title { get; set; }
        public ContentClass Content { get; set; }
        public PurpleButton Button { get; set; }
    }

    public partial class PurpleButton
    {
        public PurpleButtonRenderer ButtonRenderer { get; set; }
    }

    public partial class PurpleButtonRenderer
    {
        public string Style { get; set; }
        public string Size { get; set; }
        public bool IsDisabled { get; set; }
        public ContentClass Text { get; set; }
        public TentacledNavigationEndpoint NavigationEndpoint { get; set; }
        public string TrackingParams { get; set; }
    }

    public partial class TentacledNavigationEndpoint
    {
        public string ClickTrackingParams { get; set; }
        public EndpointCommandMetadata CommandMetadata { get; set; }
        public PurpleSignInEndpoint SignInEndpoint { get; set; }
    }

    public partial class PurpleSignInEndpoint
    {
        public Endpoint NextEndpoint { get; set; }
        public string ContinueAction { get; set; }
        public long IdamTag { get; set; }
    }

    public partial class Endpoint
    {
        public string ClickTrackingParams { get; set; }
        public EndpointCommandMetadata CommandMetadata { get; set; }
        public EndpointBrowseEndpoint BrowseEndpoint { get; set; }
    }

    public partial class EndpointBrowseEndpoint
    {
        public string BrowseId { get; set; }
    }

    public partial class TentacledServiceEndpoint
    {
        public string ClickTrackingParams { get; set; }
        public OnCreateListCommandCommandMetadata CommandMetadata { get; set; }
        public FlagEndpoint FlagEndpoint { get; set; }
    }

    public partial class FlagEndpoint
    {
        public string FlagAction { get; set; }
    }

    public partial class TopLevelButton
    {
        public ToggleButtonRenderer ToggleButtonRenderer { get; set; }
        public TopLevelButtonButtonRenderer ButtonRenderer { get; set; }
    }

    public partial class TopLevelButtonButtonRenderer
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

    public partial class StickyNavigationEndpoint
    {
        public string ClickTrackingParams { get; set; }
        public EndpointCommandMetadata CommandMetadata { get; set; }
        public FluffyWatchEndpoint WatchEndpoint { get; set; }
    }

    public partial class FluffyWatchEndpoint
    {
        public string VideoId { get; set; }
        public string PlaylistId { get; set; }
        public string Params { get; set; }
    }

    public partial class StickyServiceEndpoint
    {
        public string ClickTrackingParams { get; set; }
        public OnCreateListCommandCommandMetadata CommandMetadata { get; set; }
        public ShareEntityServiceEndpoint ShareEntityServiceEndpoint { get; set; }
    }

    public partial class ShareEntityServiceEndpoint
    {
        public string SerializedShareEntity { get; set; }
        public List<CommandElement> Commands { get; set; }
    }

    public partial class CommandElement
    {
        public CommandOpenPopupAction OpenPopupAction { get; set; }
    }

    public partial class CommandOpenPopupAction
    {
        public FluffyPopup Popup { get; set; }
        public string PopupType { get; set; }
        public bool BeReused { get; set; }
    }

    public partial class FluffyPopup
    {
        public UnifiedSharePanelRenderer UnifiedSharePanelRenderer { get; set; }
    }

    public partial class UnifiedSharePanelRenderer
    {
        public string TrackingParams { get; set; }
        public bool ShowLoadingSpinner { get; set; }
    }

    public partial class ToggleButtonRenderer
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

    public partial class DefaultNavigationEndpoint
    {
        public string ClickTrackingParams { get; set; }
        public DefaultNavigationEndpointCommandMetadata CommandMetadata { get; set; }
        public DefaultNavigationEndpointModalEndpoint ModalEndpoint { get; set; }
    }

    public partial class DefaultNavigationEndpointModalEndpoint
    {
        public FluffyModal Modal { get; set; }
    }

    public partial class FluffyModal
    {
        public FluffyModalWithTitleAndButtonRenderer ModalWithTitleAndButtonRenderer { get; set; }
    }

    public partial class FluffyModalWithTitleAndButtonRenderer
    {
        public ContentClass Title { get; set; }
        public ContentClass Content { get; set; }
        public FluffyButton Button { get; set; }
    }

    public partial class FluffyButton
    {
        public FluffyButtonRenderer ButtonRenderer { get; set; }
    }

    public partial class FluffyButtonRenderer
    {
        public string Style { get; set; }
        public string Size { get; set; }
        public bool IsDisabled { get; set; }
        public ContentClass Text { get; set; }
        public IndigoNavigationEndpoint NavigationEndpoint { get; set; }
        public string TrackingParams { get; set; }
    }

    public partial class IndigoNavigationEndpoint
    {
        public string ClickTrackingParams { get; set; }
        public EndpointCommandMetadata CommandMetadata { get; set; }
        public FluffySignInEndpoint SignInEndpoint { get; set; }
    }

    public partial class FluffySignInEndpoint
    {
        public Endpoint NextEndpoint { get; set; }
        public long IdamTag { get; set; }
    }

    public partial class Size
    {
        public string SizeType { get; set; }
    }

    public partial class StyleClass
    {
        public string StyleType { get; set; }
    }

    public partial class PlaylistSidebarPrimaryInfoRendererNavigationEndpoint
    {
        public string ClickTrackingParams { get; set; }
        public EndpointCommandMetadata CommandMetadata { get; set; }
        public TentacledWatchEndpoint WatchEndpoint { get; set; }
    }

    public partial class TentacledWatchEndpoint
    {
        public string VideoId { get; set; }
        public string PlaylistId { get; set; }
    }

    public partial class Stat
    {
        public List<TextRun> Runs { get; set; }
        public string SimpleText { get; set; }
    }

    public partial class PlaylistSidebarPrimaryInfoRendererThumbnailOverlay
    {
        public ThumbnailOverlaySidePanelRenderer ThumbnailOverlaySidePanelRenderer { get; set; }
    }

    public partial class ThumbnailOverlaySidePanelRenderer
    {
        public ContentClass Text { get; set; }
        public IconImage Icon { get; set; }
    }

    public partial class ThumbnailRenderer
    {
        public PlaylistVideoThumbnailRenderer PlaylistVideoThumbnailRenderer { get; set; }
    }

    public partial class PlaylistVideoThumbnailRenderer
    {
        public PlaylistVideoRendererThumbnail Thumbnail { get; set; }
    }

    public partial class PlaylistSidebarPrimaryInfoRendererTitle
    {
        public List<PurpleRun> Runs { get; set; }
    }

    public partial class PurpleRun
    {
        public string Text { get; set; }
        public PlaylistSidebarPrimaryInfoRendererNavigationEndpoint NavigationEndpoint { get; set; }
    }

    public partial class PlaylistSidebarSecondaryInfoRenderer
    {
        public VideoOwner VideoOwner { get; set; }
        public A11YSkipNavigationButtonClass Button { get; set; }
    }

    public partial class VideoOwner
    {
        public VideoOwnerRenderer VideoOwnerRenderer { get; set; }
    }

    public partial class VideoOwnerRenderer
    {
        public PlaylistVideoRendererThumbnail Thumbnail { get; set; }
        public ShortBylineTextClass Title { get; set; }
        public VideoOwnerRendererNavigationEndpoint NavigationEndpoint { get; set; }
        public string TrackingParams { get; set; }
    }

    public partial class Topbar
    {
        public DesktopTopbarRenderer DesktopTopbarRenderer { get; set; }
    }

    public partial class DesktopTopbarRenderer
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

    public partial class Button
    {
        public BackButtonButtonRenderer ButtonRenderer { get; set; }
    }

    public partial class BackButtonButtonRenderer
    {
        public string TrackingParams { get; set; }
        public ButtonRendererCommand Command { get; set; }
    }

    public partial class HotkeyDialog
    {
        public HotkeyDialogRenderer HotkeyDialogRenderer { get; set; }
    }

    public partial class HotkeyDialogRenderer
    {
        public TextElement Title { get; set; }
        public List<HotkeyDialogRendererSection> Sections { get; set; }
        public A11YSkipNavigationButtonClass DismissButton { get; set; }
        public string TrackingParams { get; set; }
    }

    public partial class HotkeyDialogRendererSection
    {
        public HotkeyDialogSectionRenderer HotkeyDialogSectionRenderer { get; set; }
    }

    public partial class HotkeyDialogSectionRenderer
    {
        public TextElement Title { get; set; }
        public List<Option> Options { get; set; }
    }

    public partial class Option
    {
        public HotkeyDialogSectionOptionRenderer HotkeyDialogSectionOptionRenderer { get; set; }
    }

    public partial class HotkeyDialogSectionOptionRenderer
    {
        public TextElement Label { get; set; }
        public string Hotkey { get; set; }
        public AccessibilityData HotkeyAccessibilityLabel { get; set; }
    }

    public partial class Logo
    {
        public TopbarLogoRenderer TopbarLogoRenderer { get; set; }
    }

    public partial class TopbarLogoRenderer
    {
        public IconImage IconImage { get; set; }
        public TextElement TooltipText { get; set; }
        public Endpoint Endpoint { get; set; }
        public string TrackingParams { get; set; }
    }

    public partial class Searchbox
    {
        public FusionSearchboxRenderer FusionSearchboxRenderer { get; set; }
    }

    public partial class FusionSearchboxRenderer
    {
        public IconImage Icon { get; set; }
        public TextElement PlaceholderText { get; set; }
        public Config Config { get; set; }
        public string TrackingParams { get; set; }
        public FusionSearchboxRendererSearchEndpoint SearchEndpoint { get; set; }
    }

    public partial class Config
    {
        public WebSearchboxConfig WebSearchboxConfig { get; set; }
    }

    public partial class WebSearchboxConfig
    {
        public string RequestLanguage { get; set; }
        public string RequestDomain { get; set; }
        public bool HasOnscreenKeyboard { get; set; }
        public bool FocusSearchbox { get; set; }
    }

    public partial class FusionSearchboxRendererSearchEndpoint
    {
        public string ClickTrackingParams { get; set; }
        public EndpointCommandMetadata CommandMetadata { get; set; }
        public SearchEndpointSearchEndpoint SearchEndpoint { get; set; }
    }

    public partial class SearchEndpointSearchEndpoint
    {
        public string Query { get; set; }
    }

    public partial class TopbarButton
    {
        public TopbarButtonButtonRenderer ButtonRenderer { get; set; }
        public TopbarMenuButtonRenderer TopbarMenuButtonRenderer { get; set; }
    }

    public partial class TopbarButtonButtonRenderer
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

    public partial class IndecentNavigationEndpoint
    {
        public string ClickTrackingParams { get; set; }
        public EndpointCommandMetadata CommandMetadata { get; set; }
        public TentacledSignInEndpoint SignInEndpoint { get; set; }
    }

    public partial class TentacledSignInEndpoint
    {
        public NextEndpoint NextEndpoint { get; set; }
        public long? IdamTag { get; set; }
    }

    public partial class NextEndpoint
    {
        public string ClickTrackingParams { get; set; }
        public EndpointCommandMetadata CommandMetadata { get; set; }
        public UploadEndpoint UploadEndpoint { get; set; }
    }

    public partial class UploadEndpoint
    {
        public bool Hack { get; set; }
    }

    public partial class TopbarMenuButtonRenderer
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

    public partial class TopbarMenuButtonRendererMenuRenderer
    {
        public MenuRendererMultiPageMenuRenderer MultiPageMenuRenderer { get; set; }
    }

    public partial class MenuRendererMultiPageMenuRenderer
    {
        public List<MultiPageMenuRendererSection> Sections { get; set; }
        public string TrackingParams { get; set; }
    }

    public partial class MultiPageMenuRendererSection
    {
        public MultiPageMenuSectionRenderer MultiPageMenuSectionRenderer { get; set; }
    }

    public partial class MultiPageMenuSectionRenderer
    {
        public List<MultiPageMenuSectionRendererItem> Items { get; set; }
        public string TrackingParams { get; set; }
    }

    public partial class MultiPageMenuSectionRendererItem
    {
        public CompactLinkRenderer CompactLinkRenderer { get; set; }
    }

    public partial class CompactLinkRenderer
    {
        public IconImage Icon { get; set; }
        public TextElement Title { get; set; }
        public CompactLinkRendererNavigationEndpoint NavigationEndpoint { get; set; }
        public string TrackingParams { get; set; }
    }

    public partial class CompactLinkRendererNavigationEndpoint
    {
        public string ClickTrackingParams { get; set; }
        public EndpointCommandMetadata CommandMetadata { get; set; }
        public FluffyUrlEndpoint UrlEndpoint { get; set; }
    }

    public partial class FluffyUrlEndpoint
    {
        public Uri Url { get; set; }
        public string Target { get; set; }
    }

    public partial class MenuRequest
    {
        public string ClickTrackingParams { get; set; }
        public OnCreateListCommandCommandMetadata CommandMetadata { get; set; }
        public MenuRequestSignalServiceEndpoint SignalServiceEndpoint { get; set; }
    }

    public partial class MenuRequestSignalServiceEndpoint
    {
        public string Signal { get; set; }
        public List<StickyAction> Actions { get; set; }
    }

    public partial class StickyAction
    {
        public FluffyOpenPopupAction OpenPopupAction { get; set; }
    }

    public partial class FluffyOpenPopupAction
    {
        public TentacledPopup Popup { get; set; }
        public string PopupType { get; set; }
        public bool BeReused { get; set; }
    }

    public partial class TentacledPopup
    {
        public PopupMultiPageMenuRenderer MultiPageMenuRenderer { get; set; }
    }

    public partial class PopupMultiPageMenuRenderer
    {
        public string TrackingParams { get; set; }
        public string Style { get; set; }
        public bool ShowLoadingSpinner { get; set; }
    }
}

