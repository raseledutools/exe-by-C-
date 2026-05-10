// =============================================================================
// EnterprisePdfEditor — UI/MainWindow.xaml.cs + UI/ViewModels/MainViewModel.cs
// =============================================================================

// ─────────────────────────────────────────────────────────────────────────────
// MainWindow.xaml.cs
// ─────────────────────────────────────────────────────────────────────────────
using System.Windows;
using System.Windows.Input;
using EnterprisePdfEditor.UI.ViewModels;
using EnterprisePdfEditor.UI.Controls;

namespace EnterprisePdfEditor.UI
{
    public partial class MainWindow : Window
    {
        private readonly MainViewModel _vm;
        private PdfPageView? _pageView;

        public MainWindow()
        {
            InitializeComponent();
            _vm = new MainViewModel(this);
            DataContext = _vm;

            // Wire up zoom with Ctrl+scroll on the PDF area.
            PdfScrollViewer.PreviewMouseWheel += OnPdfScrollViewerWheel;
        }

        private void OnPdfScrollViewerWheel(object sender,
            System.Windows.Input.MouseWheelEventArgs e)
        {
            if (Keyboard.Modifiers == ModifierKeys.Control)
            {
                e.Handled = true;
                if (e.Delta > 0) _vm.ZoomInCommand.Execute(null);
                else             _vm.ZoomOutCommand.Execute(null);
            }
        }

        // Called by ViewModel to set the PdfPageView in the host.
        public void SetPageView(PdfPageView view)
        {
            _pageView = view;
            PageViewHost.Content = view;
        }
    }
}

// =============================================================================
// UI/ViewModels/MainViewModel.cs
// =============================================================================
namespace EnterprisePdfEditor.UI.ViewModels
{
    using System;
    using System.Collections.ObjectModel;
    using System.ComponentModel;
    using System.IO;
    using System.Runtime.CompilerServices;
    using System.Threading.Tasks;
    using System.Windows;
    using System.Windows.Input;
    using System.Windows.Media.Imaging;
    using CommunityToolkit.Mvvm.Input;
    using EnterprisePdfEditor.Core.Models;
    using EnterprisePdfEditor.Core.Services;
    using EnterprisePdfEditor.UI.Controls;
    using Microsoft.Win32;

    public sealed class MainViewModel : INotifyPropertyChanged
    {
        // ------------------------------------------------------------------
        // Private state
        // ------------------------------------------------------------------
        private readonly MainWindow   _window;
        private readonly PdfDocumentService _docService = new();
        private PdfPageView?          _pageView;

        private int    _currentPageIndex = 0;
        private double _zoom             = 1.0;
        private bool   _showThumbnails   = true;

        private string _statusMessage   = "Ready. Open a PDF to begin.";
        private string _documentState   = "No document";
        private string _cursorPosition  = "";
        private string _selectedZoomLabel = "100%";

        private ParagraphBlock? _selectedParagraph;

        // ------------------------------------------------------------------
        // Observable collections
        // ------------------------------------------------------------------
        public ObservableCollection<string> ZoomLevels { get; } = new(
            new[] { "50%", "75%", "100%", "125%", "150%", "200%", "300%" });

        public ObservableCollection<PageThumbnailViewModel> PageThumbnails { get; } = new();

        // ------------------------------------------------------------------
        // Constructor
        // ------------------------------------------------------------------
        public MainViewModel(MainWindow window)
        {
            _window = window;

            // Commands.
            OpenCommand        = new AsyncRelayCommand(OpenAsync);
            SaveCommand        = new AsyncRelayCommand(SaveAsync,       () => _docService.IsLoaded);
            SaveAsCommand      = new AsyncRelayCommand(SaveAsAsync,     () => _docService.IsLoaded);
            ExitCommand        = new RelayCommand(() => Application.Current.Shutdown());
            ZoomInCommand      = new AsyncRelayCommand(ZoomInAsync,     () => _docService.IsLoaded);
            ZoomOutCommand     = new AsyncRelayCommand(ZoomOutAsync,    () => _docService.IsLoaded);
            FitWidthCommand    = new AsyncRelayCommand(FitWidthAsync,   () => _docService.IsLoaded);
            NextPageCommand    = new AsyncRelayCommand(NextPageAsync,   () => _docService.IsLoaded && _currentPageIndex < _docService.PageCount - 1);
            PreviousPageCommand= new AsyncRelayCommand(PreviousPageAsync,() => _docService.IsLoaded && _currentPageIndex > 0);
            GoToPageCommand    = new AsyncRelayCommand<int>(GoToPageAsync);
            AboutCommand       = new RelayCommand(ShowAbout);
        }

        // ------------------------------------------------------------------
        // Commands
        // ------------------------------------------------------------------
        public IAsyncRelayCommand     OpenCommand          { get; }
        public IAsyncRelayCommand     SaveCommand          { get; }
        public IAsyncRelayCommand     SaveAsCommand        { get; }
        public IRelayCommand          ExitCommand          { get; }
        public IAsyncRelayCommand     ZoomInCommand        { get; }
        public IAsyncRelayCommand     ZoomOutCommand       { get; }
        public IAsyncRelayCommand     FitWidthCommand      { get; }
        public IAsyncRelayCommand     NextPageCommand      { get; }
        public IAsyncRelayCommand     PreviousPageCommand  { get; }
        public IAsyncRelayCommand<int> GoToPageCommand     { get; }
        public IRelayCommand          AboutCommand         { get; }

        // ------------------------------------------------------------------
        // Bound properties
        // ------------------------------------------------------------------
        public string StatusMessage
        {
            get => _statusMessage;
            set { _statusMessage = value; OnPropertyChanged(); }
        }

        public string DocumentState
        {
            get => _documentState;
            set { _documentState = value; OnPropertyChanged(); }
        }

        public string CursorPosition
        {
            get => _cursorPosition;
            set { _cursorPosition = value; OnPropertyChanged(); }
        }

        public string SelectedZoomLabel
        {
            get => _selectedZoomLabel;
            set
            {
                _selectedZoomLabel = value;
                OnPropertyChanged();
                if (double.TryParse(value.TrimEnd('%'), out double pct))
                    _ = SetZoomAsync(pct / 100.0);
            }
        }

        public bool ShowThumbnails
        {
            get => _showThumbnails;
            set { _showThumbnails = value; OnPropertyChanged(); OnPropertyChanged(nameof(ThumbnailPanelVisibility)); OnPropertyChanged(nameof(ThumbnailPanelWidth)); }
        }

        public Visibility ThumbnailPanelVisibility
            => _showThumbnails ? Visibility.Visible : Visibility.Collapsed;

        public GridLength ThumbnailPanelWidth
            => _showThumbnails ? new GridLength(148) : new GridLength(0);

        public int TotalPages     => _docService.IsLoaded ? _docService.PageCount : 0;
        public string FileName    => _docService.IsLoaded ? Path.GetFileName(_docService.FilePath) : "—";

        public string CurrentPageDisplay
        {
            get => (_currentPageIndex + 1).ToString();
            set { if (int.TryParse(value, out int pg)) _ = GoToPageAsync(pg - 1); }
        }

        // Para info panel.
        public Visibility ParagraphInfoVisibility
            => _selectedParagraph is not null ? Visibility.Visible : Visibility.Collapsed;
        public Visibility NoParagraphSelectedVisibility
            => _selectedParagraph is null ? Visibility.Visible : Visibility.Collapsed;

        public string SelectedParaFont       => _selectedParagraph?.DominantFont.BaseFont ?? "—";
        public string SelectedParaFontSize   => _selectedParagraph is not null
            ? $"{_selectedParagraph.DominantFontSize:F1} pt" : "—";
        public string SelectedParaBold       => _selectedParagraph?.DominantFont.IsBold.ToString() ?? "—";
        public string SelectedParaLineCount  => _selectedParagraph?.Lines.Count.ToString() ?? "—";
        public string SelectedParaBBox       => _selectedParagraph is not null
            ? $"({_selectedParagraph.BoundingBox.X:F1}, {_selectedParagraph.BoundingBox.Y:F1}) " +
              $"{_selectedParagraph.BoundingBox.Width:F1}×{_selectedParagraph.BoundingBox.Height:F1}" : "—";

        // ------------------------------------------------------------------
        // Command implementations
        // ------------------------------------------------------------------
        private async Task OpenAsync()
        {
            var dlg = new OpenFileDialog
            {
                Filter = "PDF Files (*.pdf)|*.pdf|All Files (*.*)|*.*",
                Title  = "Open PDF Document"
            };
            if (dlg.ShowDialog() != true) return;

            StatusMessage  = $"Loading {Path.GetFileName(dlg.FileName)}…";
            DocumentState  = "Loading";

            try
            {
                _docService.Load(dlg.FileName);

                // Create the page view.
                _pageView = new PdfPageView();
                _window.SetPageView(_pageView);

                await _pageView.InitializeAsync(_docService, _currentPageIndex, _zoom);

                // Build thumbnails.
                await BuildThumbnailsAsync();

                OnPropertyChanged(nameof(TotalPages));
                OnPropertyChanged(nameof(FileName));
                OnPropertyChanged(nameof(CurrentPageDisplay));

                StatusMessage = $"Opened: {Path.GetFileName(dlg.FileName)} — " +
                                $"{_docService.PageCount} page(s). Click any paragraph to edit.";
                DocumentState = "Ready";
            }
            catch (Exception ex)
            {
                StatusMessage = $"Error loading PDF: {ex.Message}";
                DocumentState = "Error";
                MessageBox.Show($"Could not open the PDF:\n\n{ex.Message}",
                    "Open Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private async Task SaveAsync()
        {
            if (!_docService.IsLoaded) return;
            StatusMessage = "Saving…";
            try
            {
                await Task.Run(() => _docService.Save());
                StatusMessage = "Saved successfully.";
                DocumentState = "Saved";
            }
            catch (Exception ex)
            {
                StatusMessage = $"Save error: {ex.Message}";
                MessageBox.Show($"Save failed:\n\n{ex.Message}",
                    "Save Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private async Task SaveAsAsync()
        {
            var dlg = new SaveFileDialog
            {
                Filter = "PDF Files (*.pdf)|*.pdf",
                Title  = "Save As"
            };
            if (dlg.ShowDialog() != true) return;
            StatusMessage = "Saving…";
            try
            {
                await Task.Run(() => _docService.SaveAs(dlg.FileName));
                StatusMessage = $"Saved as: {Path.GetFileName(dlg.FileName)}";
                OnPropertyChanged(nameof(FileName));
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Save As failed:\n\n{ex.Message}",
                    "Save As Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private async Task ZoomInAsync()
        {
            double newZoom = Math.Min(_zoom * 1.25, 4.0);
            await SetZoomAsync(newZoom);
        }

        private async Task ZoomOutAsync()
        {
            double newZoom = Math.Max(_zoom * 0.8, 0.25);
            await SetZoomAsync(newZoom);
        }

        private async Task FitWidthAsync()
        {
            // TODO: compute zoom from window width / page width.
            await SetZoomAsync(1.0);
        }

        private async Task SetZoomAsync(double zoom)
        {
            _zoom = zoom;
            _selectedZoomLabel = $"{(int)(zoom * 100)}%";
            OnPropertyChanged(nameof(SelectedZoomLabel));
            if (_pageView is not null)
                await _pageView.SetZoomAsync(zoom);
        }

        private async Task NextPageAsync()
        {
            if (_currentPageIndex >= _docService.PageCount - 1) return;
            await GoToPageAsync(_currentPageIndex + 1);
        }

        private async Task PreviousPageAsync()
        {
            if (_currentPageIndex <= 0) return;
            await GoToPageAsync(_currentPageIndex - 1);
        }

        private async Task GoToPageAsync(int pageIndex)
        {
            if (!_docService.IsLoaded) return;
            pageIndex = Math.Clamp(pageIndex, 0, _docService.PageCount - 1);
            _currentPageIndex = pageIndex;
            OnPropertyChanged(nameof(CurrentPageDisplay));
            if (_pageView is not null)
                await _pageView.InitializeAsync(_docService, _currentPageIndex, _zoom);
            StatusMessage = $"Page {pageIndex + 1} of {_docService.PageCount}";
        }

        private async Task BuildThumbnailsAsync()
        {
            PageThumbnails.Clear();
            int count = _docService.PageCount;
            for (int i = 0; i < count; i++)
            {
                int idx = i;
                var thumb = await Task.Run(() =>
                    _docService.RenderPage(idx, zoom: 0.15));
                PageThumbnails.Add(new PageThumbnailViewModel
                {
                    PageIndex  = idx,
                    PageNumber = $"{idx + 1}",
                    Thumbnail  = thumb
                });
            }
        }

        private void ShowAbout()
        {
            MessageBox.Show(
                "Enterprise PDF Editor\n" +
                "Version 1.0.0\n\n" +
                "Acrobat-level inline text editing with:\n" +
                "  • ISO 32000 content stream manipulation (iText7)\n" +
                "  • Font metrics & glyph-width-based reflow\n" +
                "  • True paragraph reflow word-wrap algorithm\n" +
                "  • PDFium high-fidelity page rendering\n" +
                "  • Seamless WPF RTB overlay illusion\n\n" +
                "Built with iText7, PdfiumViewer, and WPF / .NET 8",
                "About", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        // ------------------------------------------------------------------
        // INotifyPropertyChanged
        // ------------------------------------------------------------------
        public event PropertyChangedEventHandler? PropertyChanged;
        private void OnPropertyChanged([CallerMemberName] string? name = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }

    // =========================================================================
    // PageThumbnailViewModel
    // =========================================================================
    public sealed class PageThumbnailViewModel
    {
        public int          PageIndex  { get; set; }
        public string       PageNumber { get; set; } = string.Empty;
        public BitmapSource? Thumbnail { get; set; }
    }
}
