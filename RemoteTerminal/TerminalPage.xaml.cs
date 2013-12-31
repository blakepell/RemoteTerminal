﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using RemoteTerminal.Model;
using RemoteTerminal.Screens;
using RemoteTerminal.Terminals;
using Windows.ApplicationModel.DataTransfer;
using Windows.Storage.Streams;
using Windows.UI.Text;
using Windows.UI.ViewManagement;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Input;
using Windows.UI.Xaml.Media;
using Windows.UI.Xaml.Navigation;
using RemoteTerminal.Common;

// The Basic Page item template is documented at http://go.microsoft.com/fwlink/?LinkId=234237

namespace RemoteTerminal
{
    /// <summary>
    /// A basic page that provides characteristics common to most applications.
    /// </summary>
    public sealed partial class TerminalPage : Page
    {
        private NavigationHelper navigationHelper;
        private ObservableDictionary defaultViewModel = new ObservableDictionary();

        /// <summary>
        /// This can be changed to a strongly typed view model.
        /// </summary>
        public ObservableDictionary DefaultViewModel
        {
            get { return this.defaultViewModel; }
        }

        /// <summary>
        /// NavigationHelper is used on each page to aid in navigation and 
        /// process lifetime management
        /// </summary>
        public NavigationHelper NavigationHelper
        {
            get { return this.navigationHelper; }
        }

        private ManualResetEventSlim screenDisplayCopyBoxLoaded = new ManualResetEventSlim();
        private InputPaneHelper inputPaneHelper;

        private static Lazy<Regex> hyperlinkRegex = new Lazy<Regex>(CreateHyperlinkRegex);

        public static readonly DependencyProperty TerminalProperty = DependencyProperty.Register("Terminal", typeof(ITerminal), typeof(TerminalPage), null);
        public ITerminal Terminal
        {
            get { return (ITerminal)this.GetValue(TerminalProperty); }
            set
            {
                this.SetValue(TerminalProperty, value);
                if (this.screenDisplay != null)
                {
                    this.screenDisplay.AssignTerminal(value);
                    this.screenDisplay.ColorTheme = ColorThemesDataSource.GetCustomTheme();
                }
            }
        }

        public static Regex CreateHyperlinkRegex()
        {
            // based on http://www.ietf.org/rfc/rfc2396.txt
            string scheme = "[A-Za-z][-+.0-9A-Za-z]{3,}";
            string unreserved = "[-._~0-9A-Za-z]";
            string pctEncoded = "%[0-9A-Fa-f]{2}";
            string subDelims = "[!$&'()*+,;:=]";
            string userinfo = "(?:" + unreserved + "|" + pctEncoded + "|" + subDelims + "|:)*";
            string h16 = "[0-9A-Fa-f]{1,4}";
            string decOctet = "(?:[0-9]|[1-9][0-9]|1[0-9]{2}|2[0-4][0-9]|25[0-5])";
            string ipv4address = decOctet + "\\." + decOctet + "\\." + decOctet + "\\." + decOctet;
            string ls32 = "(?:" + h16 + ":" + h16 + "|" + ipv4address + ")";
            string ipv6address = "(?:(?:" + h16 + "){6}" + ls32 + ")";
            string ipvfuture = "v[0-9A-Fa-f]+.(?:" + unreserved + "|" + subDelims + "|:)+";
            string ipLiteral = "\\[(?:" + ipv6address + "|" + ipvfuture + ")\\]";
            string regName = "(?:" + unreserved + "|" + pctEncoded + "|" + subDelims + ")*";
            string host = "(?:" + ipLiteral + "|" + ipv4address + "|" + regName + ")";
            string port = "[0-9]*";
            string authority = "(?:" + userinfo + "@)?" + host + "(?::" + port + ")?";
            string pchar = "(?:" + unreserved + "|" + pctEncoded + "|" + subDelims + "|@)";
            string segment = pchar + "*";
            string pathAbempty = "(?:/" + segment + ")*";
            string segmentNz = pchar + "+";
            string pathAbsolute = "/(?:" + segmentNz + "(?:/" + segment + ")*)?";
            string pathRootless = segmentNz + "(?:/" + segment + ")*";
            string hierPart = "(?://" + authority + pathAbempty + "|" + pathAbsolute + "|" + pathRootless + ")";
            string query = "(?:" + pchar + "|/|\\?)*";
            string fragment = "(?:" + pchar + "|/|\\?)*";
            string uriRegex = scheme + ":" + hierPart + "(?:" + query + ")?(?:#" + fragment + ")?";
            return new Regex(uriRegex);
        }

        public TerminalPage()
        {
            this.InitializeComponent();
            this.navigationHelper = new NavigationHelper(this);
            this.navigationHelper.LoadState += this.navigationHelper_LoadState;
            this.navigationHelper.SaveState += this.navigationHelper_SaveState;

            // InputPaneHelper is a custom class that allows keyboard event listeners to
            // be attached to individual elements
            this.inputPaneHelper = new InputPaneHelper();
            this.inputPaneHelper.SubscribeToKeyboard(true);
            this.inputPaneHelper.AddShowingHandler(this.screenDisplay, new InputPaneShowingHandler(CustomKeyboardHandler));
            this.inputPaneHelper.SetHidingHandler(new InputPaneHidingHandler(InputPaneHiding));

            this.copyContainer.Visibility = Visibility.Collapsed;
        }

        /// <summary>
        /// Populates the page with content passed during navigation. Any saved state is also
        /// provided when recreating a page from a prior session.
        /// </summary>
        /// <param name="sender">
        /// The source of the event; typically <see cref="NavigationHelper"/>
        /// </param>
        /// <param name="e">Event data that provides both the navigation parameter passed to
        /// <see cref="Frame.Navigate(Type, Object)"/> when this page was initially requested and
        /// a dictionary of state preserved by this page during an earlier
        /// session. The state will be null the first time a page is visited.</param>
        private void navigationHelper_LoadState(object sender, LoadStateEventArgs e)
        {
            if (e.NavigationParameter == null)
            {
                this.Frame.GoBack();
            }

            ConnectionData connectionData;
            if (e.NavigationParameter is string)
            {
                connectionData = FavoritesDataSource.GetFavorite((string)e.NavigationParameter);
                if (connectionData == null)
                {
                    this.Frame.GoBack();
                    return;
                }

                this.Terminal = TerminalManager.Create(connectionData);
            }
            else if (e.NavigationParameter is ConnectionData)
            {
                connectionData = e.NavigationParameter as ConnectionData;
                this.Terminal = TerminalManager.Create(connectionData);
            }
            else if (e.NavigationParameter is ITerminal)
            {
                this.Terminal = e.NavigationParameter as ITerminal;
            }
            else
            {
                this.Frame.GoBack();
                return;
            }

            this.previewGrid.ItemsSource = TerminalManager.Terminals;
        }

        /// <summary>
        /// Preserves state associated with this page in case the application is suspended or the
        /// page is discarded from the navigation cache.  Values must conform to the serialization
        /// requirements of <see cref="SuspensionManager.SessionState"/>.
        /// </summary>
        /// <param name="sender">The source of the event; typically <see cref="NavigationHelper"/></param>
        /// <param name="e">Event data that provides an empty dictionary to be populated with
        /// serializable state.</param>
        private void navigationHelper_SaveState(object sender, SaveStateEventArgs e)
        {
            if (this.inputPaneHelper != null)
            {
                inputPaneHelper.SubscribeToKeyboard(false);
                inputPaneHelper.RemoveShowingHandler(this.screenDisplay);
                inputPaneHelper.SetHidingHandler(null);
            }

            if (this.screenDisplay != null)
            {
                this.screenDisplay.Dispose();
                this.screenDisplay = null;
            }

            if (this.Terminal != null)
            {
                if (!this.Terminal.IsConnected)
                {
                    TerminalManager.Remove(this.Terminal);
                }
            }
        }

        private void backAppBarButton_Click(object sender, RoutedEventArgs e)
        {
            this.navigationHelper.GoBack();
        }

        private void PreviewGrid_ItemClick(object sender, ItemClickEventArgs e)
        {
            ITerminal terminal = e.ClickedItem as ITerminal;
            if (terminal == null)
            {
                return;
            }

            this.Terminal = terminal;

            this.TopAppBar.IsOpen = false;
            this.BottomAppBar.IsOpen = false;
        }

        private void PreviewGrid_ItemCloseButtonClick(object sender, RoutedEventArgs e)
        {
            ITerminal terminal = ((Button)sender).Tag as ITerminal;
            if (this.Terminal == terminal)
            {
                var switchToTerminal = TerminalManager.Terminals.Where(t => t != terminal).FirstOrDefault();
                if (switchToTerminal == null)
                {
                    this.NavigationHelper.GoBack();
                }
                else
                {
                    this.Terminal = switchToTerminal;
                }
            }

            TerminalManager.Remove(terminal);
        }

        private void CustomKeyboardHandler(object sender, InputPaneVisibilityEventArgs e)
        {
            this.ContentGrid.VerticalAlignment = VerticalAlignment.Top;
            this.ContentGrid.Height = e.OccludedRect.Y;

            // Be careful with this property. Once it has been set, the framework will
            // do nothing to help you keep the focused element in view.
            e.EnsuredFocusedElementInView = true;
        }

        private void InputPaneHiding(InputPane sender, InputPaneVisibilityEventArgs e)
        {
            this.ContentGrid.VerticalAlignment = VerticalAlignment.Stretch;
            this.ContentGrid.Height = Double.NaN;

            // Be careful with this property. Once it has been set, the framework will not change
            // any layouts in response to the keyboard coming up
            e.EnsuredFocusedElementInView = true;
        }

        private async void pasteAppBarButton_Click(object sender, RoutedEventArgs e)
        {
            if (!this.Terminal.IsConnected)
            {
                return;
            }

            var clipboardContent = Clipboard.GetContent();
            if (!clipboardContent.Contains(StandardDataFormats.Text))
            {
                return;
            }

            string text = await clipboardContent.GetTextAsync();
            this.Terminal.ProcessPastedText(text);

            this.TopAppBar.IsOpen = false;
            this.BottomAppBar.IsOpen = false;
        }

        private async void copyModeAppBarButton_Click(object sender, RoutedEventArgs e)
        {
            if (this.copyContainer.Visibility == Visibility.Visible)
            {
                this.HideCopyMode();
                return;
            }

            await this.ShowCopyMode();
        }

        private void ChangeCopyModeSize(object sender, SizeChangedEventArgs e)
        {
            if (this.screenDisplayCopyBoxScroll.Width > e.NewSize.Width || this.screenDisplayCopyBoxScroll.Height > e.NewSize.Height)
            {
                this.HideCopyMode();
            }
        }

        private void HideCopyMode()
        {
            this.SizeChanged -= ChangeCopyModeSize;

            if (this.copyContainer.Visibility == Visibility.Visible)
            {
                this.screenDisplayCopyBox.IsReadOnly = false;
                this.screenDisplayCopyBox.Document.SetText(TextSetOptions.None, string.Empty);
                this.copyContainer.Visibility = Visibility.Collapsed;
                this.BottomAppBar.IsOpen = false;
                this.TopAppBar.IsOpen = false;
                this.screenDisplay.Focus(FocusState.Programmatic);
            }
        }

        private async Task ShowCopyMode()
        {
            this.screenDisplayCopyBox.IsReadOnly = false;
            this.screenDisplayCopyBox.Background = new SolidColorBrush(Windows.UI.Colors.White);
            this.screenDisplayCopyBox.Foreground = new SolidColorBrush(Windows.UI.Colors.Black);
            this.screenDisplayCopyBox.BorderBrush = new SolidColorBrush(Windows.UI.Colors.ForestGreen);

            // We have to wait until the RichEditBox is loaded before we try to load any content into it.
            // If we don't do that the fonts and colors get lost.
            this.copyContainer.Visibility = Visibility.Visible;
            this.SizeChanged += ChangeCopyModeSize;
            ManualResetEventSlim loadedEvent = this.screenDisplayCopyBoxLoaded;
            if (loadedEvent != null)
            {
                await Task.Run(() => loadedEvent.Wait());
                loadedEvent.Dispose();
                this.screenDisplayCopyBoxLoaded = null;
            }

            var screenCopy = this.Terminal.RenderableScreen.GetScreenCopy();

            // Activate scrolling for the RichEditBox, then load the generated RTF document.
            this.screenDisplayCopyBoxScroll.HorizontalScrollMode = ScrollMode.Auto;
            this.screenDisplayCopyBoxScroll.VerticalScrollMode = ScrollMode.Auto;

            var rtfData = await this.GenerateRtf(screenCopy);
            float widthRatio = rtfData.Item2;
            using (InMemoryRandomAccessStream rtfStream = rtfData.Item1)
            {
                this.screenDisplayCopyBox.Document.LoadFromStream(TextSetOptions.ApplyRtfDocumentDefaults | TextSetOptions.FormatRtf | TextSetOptions.Unhide, rtfStream);
            }

            // Adjust the size of the RichEditBox, then deactivate scrolling.
            this.screenDisplayCopyBoxScroll.Width = Window.Current.Bounds.Width / 2;
            if (widthRatio >= 0f)
            {
                this.screenDisplayCopyBoxScroll.Width = (this.screenDisplayCopyBoxScroll.ExtentWidth - this.screenDisplayCopyBox.BorderThickness.Left - this.screenDisplayCopyBox.BorderThickness.Right) / widthRatio;
                this.screenDisplayCopyBoxScroll.Width += 1 + this.screenDisplayCopyBox.BorderThickness.Left + this.screenDisplayCopyBox.BorderThickness.Right;
            }
            this.screenDisplayCopyBoxScroll.Height = this.screenDisplayCopyBoxScroll.ExtentHeight;
            this.screenDisplayCopyBoxScroll.HorizontalScrollMode = ScrollMode.Disabled;
            this.screenDisplayCopyBoxScroll.VerticalScrollMode = ScrollMode.Disabled;

            this.screenDisplayCopyBox.IsReadOnly = true;
            this.screenDisplayCopyBox.Focus(FocusState.Programmatic);
            this.BottomAppBar.IsOpen = false;
            this.TopAppBar.IsOpen = false;
        }

        private async Task<Tuple<InMemoryRandomAccessStream, float>> GenerateRtf(IRenderableScreenCopy screenCopy)
        {
            int rightmostNonSpace = -1;
            int columnCount = -1;
            Encoding codepage1252 = Encoding.GetEncoding("Windows-1252");
            var rtfStream = new InMemoryRandomAccessStream();
            using (DataWriter rtf = new DataWriter(rtfStream))
            {
                rtf.WriteString(@"{");
                rtf.WriteString(@"\rtf1");
                rtf.WriteString(@"\ansi");
                rtf.WriteString(@"\ansicpg1252");
                rtf.WriteString(@"{\fonttbl{\f0\fmodern " + this.screenDisplay.ColorTheme.FontFamily + ";}}");
                rtf.WriteString(Environment.NewLine);

                rtf.WriteString(@"{\colortbl ;");
                var colorTable = this.screenDisplay.ColorTheme.ColorTable;
                for (ScreenColor screenColor = ScreenColor.DefaultBackground; screenColor <= ScreenColor.WhiteBright; screenColor++)
                {
                    var color = colorTable[screenColor];
                    rtf.WriteString(@"\red" + color.R + @"\green" + color.G + @"\blue" + color.B + ";");
                }
                rtf.WriteString(@"}");
                rtf.WriteString(Environment.NewLine);

                int fontSize = (int)(ScreenDisplay.BaseLogicalFontMetrics[this.screenDisplay.ColorTheme.FontFamily].FontSize * (1 + (ScreenDisplay.FontSizeScalingFactor * (float)this.screenDisplay.ColorTheme.FontSize)));
                rtf.WriteString(@"\pard\ltrpar\f0\fs" + fontSize);
                rtf.WriteString(Environment.NewLine);

                StringBuilder formatCodes = new StringBuilder();
                for (int y = 0; y < screenCopy.Cells.Length; y++)
                {
                    var line = screenCopy.Cells[y];
                    string lineString = new string(line.Select(c => c.Character).ToArray());
                    var hyperlinkMatches = hyperlinkRegex.Value.Matches(lineString).Cast<Match>();

                    for (int x = 0; x < line.Length; x++)
                    {
                        Match startingMatch = hyperlinkMatches.Where(m => m.Index == x).SingleOrDefault();
                        if (startingMatch != null)
                        {
                            rtf.WriteString(@"{\field{\*\fldinst HYPERLINK """ + RtfEscape(startingMatch.Value) + @"""}{\fldrslt ");
                        }

                        if (x == 0 || line[x - 1].BackgroundColor != line[x].BackgroundColor)
                        {
                            formatCodes.Append(@"\chshdng0\chcbpat" + (line[x].BackgroundColor - ScreenColor.DefaultBackground + 1));
                        }

                        if (x == 0 || line[x - 1].ForegroundColor != line[x].ForegroundColor)
                        {
                            formatCodes.Append(@"\cf" + (line[x].ForegroundColor - ScreenColor.DefaultBackground + 1));
                        }

                        if (x == 0 || line[x - 1].Modifications.HasFlag(ScreenCellModifications.Bold) != line[x].Modifications.HasFlag(ScreenCellModifications.Bold))
                        {
                            formatCodes.Append(@"\b");
                            if (!line[x].Modifications.HasFlag(ScreenCellModifications.Bold))
                            {
                                formatCodes.Append("0");
                            }
                        }

                        if (x == 0 || line[x - 1].Modifications.HasFlag(ScreenCellModifications.Underline) != line[x].Modifications.HasFlag(ScreenCellModifications.Underline))
                        {
                            formatCodes.Append(@"\ul");
                            if (!line[x].Modifications.HasFlag(ScreenCellModifications.Underline))
                            {
                                formatCodes.Append("0");
                            }
                        }

                        if (formatCodes.Length > 0)
                        {
                            rtf.WriteString(formatCodes.ToString());
                            formatCodes.Clear();
                            rtf.WriteString(" ");
                        }

                        if (line[x].Character == CjkWidth.UCSWIDE)
                        {
                            // don't write this
                        }
                        else if (line[x].Character == codepage1252.GetChars(codepage1252.GetBytes(new[] { line[x].Character }))[0])
                        {
                            rtf.WriteBytes(codepage1252.GetBytes(RtfEscape(line[x].Character.ToString())));
                        }
                        else
                        {
                            rtf.WriteString(@"\u" + ((int)line[x].Character).ToString() + "?");
                        }

                        Match endingMatch = hyperlinkMatches.Where(m => m.Index + m.Length == x + 1).SingleOrDefault();
                        if (endingMatch != null)
                        {
                            rtf.WriteString("}}");
                        }

                        if (x + 1 >= line.Length)
                        {
                            if (line[x].Modifications.HasFlag(ScreenCellModifications.Bold))
                            {
                                formatCodes.Append(@"\b0");
                            }
                            if (line[x].Modifications.HasFlag(ScreenCellModifications.Underline))
                            {
                                formatCodes.Append(@"\ul0");
                            }
                        }

                        if (line[x].Character != 0x0020)
                        {
                            rightmostNonSpace = Math.Max(rightmostNonSpace, x);
                        }
                    }

                    if (formatCodes.Length > 0)
                    {
                        rtf.WriteString(formatCodes.ToString());
                        formatCodes.Clear();
                    }

                    if (y + 1 < screenCopy.Cells.Length)
                    {
                        rtf.WriteString(@"\par" + Environment.NewLine);
                    }

                    columnCount = Math.Max(columnCount, line.Length);
                }

                rtf.WriteString(@"}");

                await rtf.StoreAsync();
                await rtf.FlushAsync();
                rtf.DetachStream();
            }

            rtfStream.Seek(0);

            float widthRatio = rightmostNonSpace >= 0 ? (rightmostNonSpace + 1f) / columnCount : -1f;

            return new Tuple<InMemoryRandomAccessStream, float>(rtfStream, widthRatio);
        }

        private static string RtfEscape(string str)
        {
            str = str.Replace(@"\", @"\\");
            str = str.Replace(@"{", @"\{");
            str = str.Replace(@"}", @"\}");
            return str;
        }

        public void ForceRender(bool fontChanged)
        {
            if (this.screenDisplay != null)
            {
                this.screenDisplay.ForceRender(fontChanged);
            }
        }

        private void screenDisplayCopyBox_Loaded(object sender, RoutedEventArgs e)
        {
            this.screenDisplayCopyBoxLoaded.Set();
        }

        private void copyContainer_Tapped(object sender, TappedRoutedEventArgs e)
        {
            this.HideCopyMode();
        }

        #region NavigationHelper registration

        /// The methods provided in this section are simply used to allow
        /// NavigationHelper to respond to the page's navigation methods.
        /// 
        /// Page specific logic should be placed in event handlers for the  
        /// <see cref="GridCS.Common.NavigationHelper.LoadState"/>
        /// and <see cref="GridCS.Common.NavigationHelper.SaveState"/>.
        /// The navigation parameter is available in the LoadState method 
        /// in addition to page state preserved during an earlier session.

        protected override void OnNavigatedTo(NavigationEventArgs e)
        {
            this.navigationHelper.OnNavigatedTo(e);
        }

        protected override void OnNavigatedFrom(NavigationEventArgs e)
        {
            this.navigationHelper.OnNavigatedFrom(e);
        }

        #endregion
    }
}
