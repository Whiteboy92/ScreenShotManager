using System;
using System.Drawing;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Forms;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using ScreenShotManager.Shared.Helpers;
using ScreenShotManager.Screenshot.Models;
using ScreenShotManager.Screenshot.Services.Interfaces;
using Color = System.Windows.Media.Color;
using KeyEventArgs = System.Windows.Input.KeyEventArgs;
using MessageBox = System.Windows.MessageBox;
using MouseEventArgs = System.Windows.Input.MouseEventArgs;
using Point = System.Windows.Point;


namespace ScreenShotManager.Screenshot.Views.Overlay;

public partial class OverlayWindow
{
    private readonly IScreenCaptureService captureService;
    private readonly AppSettings settings;
    
    private Point dragStartPoint;
    private Rect selectionBounds;
    private bool isCreatingSelection;
    private bool isResizingSelection;
    private bool hasDragged;
    private ResizeHandle activeResizeHandle = ResizeHandle.None;
    
    private bool isDrawing;
    private Polyline? currentStroke;
    private Color currentColor = Colors.Red;
    private double currentBrushSize = 3;
    
    private bool selectionFinalized;
    
    private Bitmap? fullScreenCapture;
    private BitmapSource? frozenScreenCache; // Cache the converted frozen screen
    
    public Bitmap? CapturedBitmap { get; private set; }
    
    private enum ResizeHandle
    {
        None,
        TopLeft,
        TopRight,
        BottomLeft,
        BottomRight,
    }

    public OverlayWindow(IScreenCaptureService captureService, AppSettings settings)
    {
        try
        {
            InitializeComponent();
            
            this.captureService = captureService ?? throw new ArgumentNullException(nameof(captureService));
            this.settings = settings ?? throw new ArgumentNullException(nameof(settings));
            
            SetupWindow();
            CaptureFrozenScreen();
            SetupEventHandlers();

            Loaded += OnWindowLoaded;
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Failed to initialize overlay window: {ex.Message}", "Error",
                MessageBoxButton.OK, MessageBoxImage.Error);
            throw;
        }
    }

    private void SetupWindow()
    {
        WindowState = WindowState.Normal;
        
        var bounds = GeometryHelper.GetVirtualScreenBounds();

        Left = bounds.Left;
        Top = bounds.Top;
        Width = bounds.Width;
        Height = bounds.Height;

        Topmost = true;
    }

    private void OnWindowLoaded(object sender, RoutedEventArgs e)
    {
        var bounds = GeometryHelper.GetVirtualScreenBounds();
        
        if (Math.Abs(Left - bounds.Left) > 1 || Math.Abs(Top - bounds.Top) > 1 ||
            Math.Abs(ActualWidth - bounds.Width) > 1 || Math.Abs(ActualHeight - bounds.Height) > 1)
        {
            Left = bounds.Left;
            Top = bounds.Top;
            Width = bounds.Width;
            Height = bounds.Height;
        }

        InitializeDimming();

        Focus();
    }

    /// <summary>
    /// Captures the frozen screen synchronously before the window is shown.
    /// Running before display gives a clean capture (no overlay in frame) and
    /// lets the window use an opaque, hardware-accelerated surface for smooth dragging.
    /// </summary>
    private void CaptureFrozenScreen()
    {
        try
        {
            var bounds = GeometryHelper.GetVirtualScreenBounds();

            fullScreenCapture = captureService.CaptureRegion(
                (int)bounds.Left, (int)bounds.Top, (int)bounds.Width, (int)bounds.Height);

            frozenScreenCache = BitmapHelper.BitmapToBitmapSourceOptimized(fullScreenCapture);
            FrozenScreenImage.Source = frozenScreenCache;
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Failed to capture screen: {ex.Message}", "Error",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void SetupEventHandlers()
    {
        MainCanvas.MouseLeftButtonDown += OnMouseLeftButtonDown;
        MainCanvas.MouseMove += OnMouseMove;
        MainCanvas.MouseLeftButtonUp += OnMouseLeftButtonUp;
        KeyDown += OnKeyDown;
        
        SetupAnnotationToolbar();
        SetupResizeHandles();
    }

    private void SetupAnnotationToolbar()
    {
        BrushToggle.Checked += (_, _) =>
        {
            isDrawing = true;
            AnnotationCanvas.IsHitTestVisible = true;
        };
        BrushToggle.Unchecked += (_, _) =>
        {
            isDrawing = false;
            AnnotationCanvas.IsHitTestVisible = false;
        };
        
        ColorRed.Click += (_, _) => currentColor = Colors.Red;
        ColorGreen.Click += (_, _) => currentColor = Colors.LimeGreen;
        ColorBlue.Click += (_, _) => currentColor = Colors.DodgerBlue;
        ColorYellow.Click += (_, _) => currentColor = Colors.Yellow;
        ColorBlack.Click += (_, _) => currentColor = Colors.Black;
        ColorWhite.Click += (_, _) => currentColor = Colors.White;
        ColorCustom.Click += (_, _) => OpenCustomColorPicker();
        
        BrushSizeSlider.ValueChanged += (_, _) => UpdateBrushSize();
        BrushSizeDecrease.Click += (_, _) => DecreaseBrushSize();
        BrushSizeIncrease.Click += (_, _) => IncreaseBrushSize();
        ClearButton.Click += (_, _) => AnnotationCanvas.Children.Clear();
        ConfirmButton.Click += (_, _) => SaveAndClose();
    }

    private void SetupResizeHandles()
    {
        HandleTopLeft.MouseLeftButtonDown += (_, e) => StartResize(ResizeHandle.TopLeft, e);
        HandleTopRight.MouseLeftButtonDown += (_, e) => StartResize(ResizeHandle.TopRight, e);
        HandleBottomLeft.MouseLeftButtonDown += (_, e) => StartResize(ResizeHandle.BottomLeft, e);
        HandleBottomRight.MouseLeftButtonDown += (_, e) => StartResize(ResizeHandle.BottomRight, e);
    }

    private void UpdateBrushSize()
    {
        currentBrushSize = BrushSizeSlider.Value;
        BrushSizeText.Text = ((int)currentBrushSize).ToString();
    }

    private void DecreaseBrushSize()
    {
        if (BrushSizeSlider.Value > BrushSizeSlider.Minimum)
        {
            BrushSizeSlider.Value = Math.Max(BrushSizeSlider.Minimum, BrushSizeSlider.Value - 1);
        }
    }

    private void IncreaseBrushSize()
    {
        if (BrushSizeSlider.Value < BrushSizeSlider.Maximum)
        {
            BrushSizeSlider.Value = Math.Min(BrushSizeSlider.Maximum, BrushSizeSlider.Value + 1);
        }
    }

    private void OpenCustomColorPicker()
    {
        var colorDialog = new ColorDialog
        {
            FullOpen = true,
            Color = System.Drawing.Color.FromArgb(
                currentColor.A,
                currentColor.R,
                currentColor.G,
                currentColor.B
            ),
        };

        if (colorDialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
        {
            currentColor = Color.FromArgb(
                colorDialog.Color.A,
                colorDialog.Color.R,
                colorDialog.Color.G,
                colorDialog.Color.B
            );

            ColorCustom.Background = new SolidColorBrush(currentColor);
        }
    }

    private void OnMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        var position = e.GetPosition(MainCanvas);

        if (isDrawing && BrushToggle.IsChecked == true)
        {
            StartDrawing(e.GetPosition(AnnotationCanvas));
            e.Handled = true;
            return;
        }

        if (isResizingSelection)
            return;

        StartNewSelection(position);
    }

    private void StartDrawing(Point position)
    {
        currentStroke = new Polyline
        {
            Stroke = new SolidColorBrush(currentColor),
            StrokeThickness = currentBrushSize,
            StrokeLineJoin = PenLineJoin.Round,
            StrokeStartLineCap = PenLineCap.Round,
            StrokeEndLineCap = PenLineCap.Round,
        };
        
        currentStroke.Points.Add(position);
        AnnotationCanvas.Children.Add(currentStroke);
    }

    private void StartNewSelection(Point position)
    {
        dragStartPoint = position;
        isCreatingSelection = true;
        selectionFinalized = false;
        hasDragged = false;
        
        SelectionRect.Visibility = Visibility.Visible;
        HideHandles();
        HideAnnotationUi();
        
        selectionBounds = new Rect(position, position);
        
        MainCanvas.CaptureMouse();
    }

    private void HideAnnotationUi()
    {
        ScreenshotPreview.Visibility = Visibility.Collapsed;
        AnnotationToolbar.Visibility = Visibility.Collapsed;
        AnnotationCanvas.Visibility = Visibility.Collapsed;
        AnnotationCanvas.Children.Clear();
    }

    private void OnMouseMove(object sender, MouseEventArgs e)
    {
        var currentPosition = e.GetPosition(MainCanvas);
        
        // Priority 1: Handle drawing
        if (isDrawing && BrushToggle.IsChecked == true && currentStroke != null 
            && e.LeftButton == MouseButtonState.Pressed)
        {
            ContinueDrawing(e.GetPosition(AnnotationCanvas));
            return;
        }

        // Priority 2: Handle resizing via corner handles
        if (isResizingSelection && e.LeftButton == MouseButtonState.Pressed)
        {
            UpdateResizing(currentPosition);
            return;
        }

        // Priority 3: Handle creating new selection
        if (isCreatingSelection && e.LeftButton == MouseButtonState.Pressed)
        {
            UpdateCreatingSelection(currentPosition);
        }
    }

    private void ContinueDrawing(Point position)
    {
        currentStroke?.Points.Add(position);
    }

    private void UpdateCreatingSelection(Point currentPosition)
    {
        var distance = (currentPosition - dragStartPoint).Length;
        if (distance > 2)
        {
            hasDragged = true;
        }

        selectionBounds = GeometryHelper.CreateRectFromPoints(dragStartPoint, currentPosition);

        UpdateSelectionVisuals();
        UpdateDimmingOverlay(selectionBounds);
    }

    private void UpdateResizing(Point currentPosition)
    {
        var newBounds = CalculateResizedBounds(currentPosition);
        
        if (newBounds is { Width: > 1, Height: > 1 })
        {
            selectionBounds = newBounds;
            UpdateSelectionVisuals();
            UpdateDimmingOverlay(selectionBounds);
        }
    }

    private void UpdateSelectionVisuals()
    {
        SelectionRect.Visibility = Visibility.Visible;
        
        Canvas.SetLeft(SelectionRect, selectionBounds.X);
        Canvas.SetTop(SelectionRect, selectionBounds.Y);
        SelectionRect.Width = selectionBounds.Width;
        SelectionRect.Height = selectionBounds.Height;
        
        if (selectionFinalized)
        {
            UpdateHandlePositions();
        }
    }

    private void OnMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (isDrawing && currentStroke != null)
        {
            currentStroke = null;
            return;
        }

        if (isResizingSelection)
        {
            CompleteResizing();
            return;
        }

        if (isCreatingSelection)
        {
            CompleteCreatingSelection();
            return;
        }
    }

    private void CompleteCreatingSelection()
    {
        isCreatingSelection = false;

        MainCanvas.ReleaseMouseCapture();

        if (!hasDragged)
        {
            SelectionRect.Visibility = Visibility.Collapsed;
            return;
        }

        if (GeometryHelper.IsValidSelection(selectionBounds))
        {
            selectionFinalized = true;
            
            SelectionRect.Visibility = Visibility.Visible;
            
            UpdateDimmingOverlay(selectionBounds);
            ShowHandles();
            CaptureSelection();
            ShowAnnotationToolbar();
        }
        else
        {
            SelectionRect.Visibility = Visibility.Collapsed;
            UpdateDimmingOverlay(new Rect(0, 0, ActualWidth, ActualHeight));
        }
    }

    private void CompleteResizing()
    {
        isResizingSelection = false;
        activeResizeHandle = ResizeHandle.None;

        MainCanvas.ReleaseMouseCapture();
        
        if (GeometryHelper.IsValidSelection(selectionBounds))
        {
            SelectionRect.Visibility = Visibility.Visible;
            
            UpdateDimmingOverlay(selectionBounds);
            CaptureSelection();
            ShowAnnotationToolbar();
        }
        else
        {
            SelectionRect.Visibility = Visibility.Collapsed;
            HideHandles();
            HideAnnotationUi();
            UpdateDimmingOverlay(new Rect(0, 0, ActualWidth, ActualHeight));
        }
    }

    private void ShowHandles()
    {
        UpdateHandlePositions();
        
        HandleTopLeft.Visibility = Visibility.Visible;
        HandleTopRight.Visibility = Visibility.Visible;
        HandleBottomLeft.Visibility = Visibility.Visible;
        HandleBottomRight.Visibility = Visibility.Visible;
    }

    private void UpdateHandlePositions()
    {
        Canvas.SetLeft(HandleTopLeft, selectionBounds.X - 6);
        Canvas.SetTop(HandleTopLeft, selectionBounds.Y - 6);
        
        Canvas.SetLeft(HandleTopRight, selectionBounds.Right - 6);
        Canvas.SetTop(HandleTopRight, selectionBounds.Y - 6);
        
        Canvas.SetLeft(HandleBottomLeft, selectionBounds.X - 6);
        Canvas.SetTop(HandleBottomLeft, selectionBounds.Bottom - 6);
        
        Canvas.SetLeft(HandleBottomRight, selectionBounds.Right - 6);
        Canvas.SetTop(HandleBottomRight, selectionBounds.Bottom - 6);
    }

    private void HideHandles()
    {
        HandleTopLeft.Visibility = Visibility.Collapsed;
        HandleTopRight.Visibility = Visibility.Collapsed;
        HandleBottomLeft.Visibility = Visibility.Collapsed;
        HandleBottomRight.Visibility = Visibility.Collapsed;
    }

    private void StartResize(ResizeHandle handle, MouseButtonEventArgs e)
    {
        isResizingSelection = true;
        activeResizeHandle = handle;
        dragStartPoint = e.GetPosition(MainCanvas);

        ScreenshotPreview.Visibility = Visibility.Collapsed;
        AnnotationCanvas.Visibility = Visibility.Collapsed;
        AnnotationToolbar.Visibility = Visibility.Collapsed;

        MainCanvas.CaptureMouse();
        
        e.Handled = true;
    }

    private Rect CalculateResizedBounds(Point currentPosition)
    {
        var x = selectionBounds.X;
        var y = selectionBounds.Y;
        var right = selectionBounds.Right;
        var bottom = selectionBounds.Bottom;

        switch (activeResizeHandle)
        {
            case ResizeHandle.TopLeft:
                return GeometryHelper.CreateRectFromPoints(currentPosition, new Point(right, bottom));
            
            case ResizeHandle.TopRight:
                return GeometryHelper.CreateRectFromPoints(currentPosition with { X = x }, currentPosition with { Y = bottom });
            
            case ResizeHandle.BottomLeft:
                return GeometryHelper.CreateRectFromPoints(currentPosition with { Y = y }, currentPosition with { X = right });
            
            case ResizeHandle.BottomRight:
                return GeometryHelper.CreateRectFromPoints(new Point(x, y), currentPosition);
            
            default:
                return selectionBounds;
        }
    }

    private void InitializeDimming()
    {
        var screenWidth = ActualWidth;
        var screenHeight = ActualHeight;
        
        Canvas.SetLeft(DimTop, 0);
        Canvas.SetTop(DimTop, 0);
        DimTop.Width = screenWidth;
        DimTop.Height = screenHeight;
        DimTop.Visibility = Visibility.Visible;
        
        DimLeft.Visibility = Visibility.Collapsed;
        DimRight.Visibility = Visibility.Collapsed;
        DimBottom.Visibility = Visibility.Collapsed;
    }

    private void UpdateDimmingOverlay(Rect selectionRect)
    {
        if (settings.DisableDimming)
        {
            DimTop.Visibility = Visibility.Collapsed;
            DimLeft.Visibility = Visibility.Collapsed;
            DimRight.Visibility = Visibility.Collapsed;
            DimBottom.Visibility = Visibility.Collapsed;
            return;
        }

        var screenWidth = ActualWidth;
        var screenHeight = ActualHeight;

        if (screenWidth <= 0 || screenHeight <= 0)
        {
            return;
        }
        
        Canvas.SetLeft(DimTop, 0);
        Canvas.SetTop(DimTop, 0);
        DimTop.Width = screenWidth;
        DimTop.Height = Math.Max(0, selectionRect.Y);
        DimTop.Visibility = Visibility.Visible;
        
        Canvas.SetLeft(DimLeft, 0);
        Canvas.SetTop(DimLeft, selectionRect.Y);
        DimLeft.Width = Math.Max(0, selectionRect.X);
        DimLeft.Height = Math.Max(0, selectionRect.Height);
        DimLeft.Visibility = Visibility.Visible;
        
        var rightX = Math.Min(selectionRect.Right, screenWidth);
        Canvas.SetLeft(DimRight, rightX);
        Canvas.SetTop(DimRight, selectionRect.Y);
        DimRight.Width = Math.Max(0, screenWidth - rightX);
        DimRight.Height = Math.Max(0, selectionRect.Height);
        DimRight.Visibility = Visibility.Visible;
        
        var bottomY = Math.Min(selectionRect.Bottom, screenHeight);
        Canvas.SetLeft(DimBottom, 0);
        Canvas.SetTop(DimBottom, bottomY);
        DimBottom.Width = screenWidth;
        DimBottom.Height = Math.Max(0, screenHeight - bottomY);
        DimBottom.Visibility = Visibility.Visible;
    }

    private async void CaptureSelection()
    {
        try
        {
            if (fullScreenCapture == null)
            {
                return;
            }

            var width = (int)selectionBounds.Width;
            var height = (int)selectionBounds.Height;

            if (width > 0 && height > 0)
            {
                var croppedBitmap = await Task.Run(() => CropBitmap(fullScreenCapture, 
                    (int)selectionBounds.X, 
                    (int)selectionBounds.Y, 
                    width, 
                    height));
                
                var bitmapSource = await Task.Run(() => 
                    BitmapHelper.BitmapToBitmapSourceOptimized(croppedBitmap));

                UpdateScreenshotPreview(bitmapSource);
                SetupAnnotationCanvas();
                
                croppedBitmap.Dispose();
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Failed to capture screenshot: {ex.Message}", "Error",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private static Bitmap CropBitmap(Bitmap source, int x, int y, int width, int height)
    {
        try
        {
            x = Math.Max(0, Math.Min(x, source.Width - 1));
            y = Math.Max(0, Math.Min(y, source.Height - 1));
            width = Math.Min(width, source.Width - x);
            height = Math.Min(height, source.Height - y);
            
            var cropArea = new System.Drawing.Rectangle(x, y, width, height);
            
            return source.Clone(cropArea, source.PixelFormat);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Failed to crop bitmap: {ex.Message}", "Error",
                MessageBoxButton.OK, MessageBoxImage.Error);
            throw;
        }
    }

    private void UpdateScreenshotPreview(BitmapSource bitmapSource)
    {
        ScreenshotPreview.Source = bitmapSource;
        Canvas.SetLeft(ScreenshotPreview, selectionBounds.X);
        Canvas.SetTop(ScreenshotPreview, selectionBounds.Y);
        ScreenshotPreview.Width = selectionBounds.Width;
        ScreenshotPreview.Height = selectionBounds.Height;
        ScreenshotPreview.Visibility = Visibility.Visible;
    }

    private void SetupAnnotationCanvas()
    {
        Canvas.SetLeft(AnnotationCanvas, selectionBounds.X);
        Canvas.SetTop(AnnotationCanvas, selectionBounds.Y);
        AnnotationCanvas.Width = selectionBounds.Width;
        AnnotationCanvas.Height = selectionBounds.Height;
        AnnotationCanvas.Visibility = Visibility.Visible;
        
        AnnotationCanvas.IsHitTestVisible = false;
    }

    private void ShowAnnotationToolbar()
    {
        var y = selectionBounds.Bottom + 10;

        if (y + 60 > ActualHeight)
        {
            y = selectionBounds.Y - 60;
        }
        
        var x = selectionBounds.X;
        var toolbarWidth = 450;
        
        if (x + toolbarWidth > ActualWidth)
        {
            x = ActualWidth - toolbarWidth - 10;
        }

        Canvas.SetLeft(AnnotationToolbar, x);
        Canvas.SetTop(AnnotationToolbar, y);
        AnnotationToolbar.Visibility = Visibility.Visible;
    }

    private void OnKeyDown(object sender, KeyEventArgs e)
    {
        try
        {
            if (e.Key == Key.Escape)
            {
                DialogResult = false;
                Close();
            }
            else if (e.Key == Key.Enter || e.Key == Key.Return)
            {
                if (selectionFinalized && GeometryHelper.IsValidSelection(selectionBounds))
                {
                    SaveAndClose();
                }
            }
            else if (e.Key == Key.D)
            {
                BrushToggle.IsChecked = !BrushToggle.IsChecked;
            }
            else if (e.Key == Key.C && Keyboard.Modifiers == ModifierKeys.Control)
            {
                if (selectionFinalized && GeometryHelper.IsValidSelection(selectionBounds))
                {
                    SaveAndClose();
                }
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Keyboard shortcut error: {ex.Message}", "Error",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void SaveAndClose()
    {
        try
        {
            if (ScreenshotPreview.Source == null)
                return;

            CapturedBitmap = RenderFinalImage();
            DialogResult = true;
            Close();
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Failed to save screenshot: {ex.Message}", "Error",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private Bitmap RenderFinalImage()
    {
        try
        {
            if (AnnotationCanvas.Children.Count == 0)
            {
                return CropBitmap(fullScreenCapture!,
                    (int)selectionBounds.X,
                    (int)selectionBounds.Y,
                    (int)selectionBounds.Width,
                    (int)selectionBounds.Height);
            }
            
            var renderBitmap = new RenderTargetBitmap(
                (int)selectionBounds.Width,
                (int)selectionBounds.Height,
                96, 96, PixelFormats.Pbgra32
            );

            var visual = new DrawingVisual();
            using (var context = visual.RenderOpen())
            {
                context.DrawImage(ScreenshotPreview.Source,
                    selectionBounds with { X = 0, Y = 0 });

                var annotationBounds = new Rect(0, 0, AnnotationCanvas.Width, AnnotationCanvas.Height);
                context.DrawRectangle(
                    new VisualBrush(AnnotationCanvas) { Stretch = Stretch.None },
                    null,
                    annotationBounds
                );
            }

            renderBitmap.Render(visual);
            return captureService.ConvertToBitmap(renderBitmap);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Failed to render final image: {ex.Message}", "Error",
                MessageBoxButton.OK, MessageBoxImage.Error);
            throw;
        }
    }
}

