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
using Rectangle = System.Windows.Shapes.Rectangle;
using TextBox = System.Windows.Controls.TextBox;
using Brushes = System.Windows.Media.Brushes;
using HorizontalAlignment = System.Windows.HorizontalAlignment;


namespace ScreenShotManager.Screenshot.Views.Overlay;

public partial class OverlayWindow
{
    private readonly IScreenCaptureService captureService;
    private readonly AppSettings settings;
    
    private Point dragStartPoint;
    private Rect selectionBounds;
    private bool isCreatingSelection;
    private bool isResizingSelection;
    private bool isMovingSelection;
    private bool hasDragged;
    private ResizeHandle activeResizeHandle = ResizeHandle.None;
    
    private bool isDrawing;
    private Polyline? currentStroke;
    private readonly System.Collections.Generic.Stack<IDrawCommand> undoStack = new();
    private readonly System.Collections.Generic.Stack<IDrawCommand> redoStack = new();
    private Color currentColor = Colors.Red;
    private double currentBrushSize = 3;

    private bool isTextMode;
    private double currentTextSize = 24;
    private TextAnnotation? lastText;
    private readonly System.Collections.Generic.List<TextAnnotation> textAnnotations = new();
    
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
        Top,
        Bottom,
        Left,
        Right,
    }

    private sealed class TextAnnotation
    {
        public required Grid Container { get; init; }
        public required TextBox TextBox { get; init; }
        public required Rectangle Chrome { get; init; }
        public required System.Windows.Controls.Primitives.Thumb ResizeThumb { get; init; }
    }

    private interface IDrawCommand
    {
        void Undo();
        void Redo();
    }

    private sealed class AddStrokeCommand(Canvas canvas, System.Windows.UIElement stroke) : IDrawCommand
    {
        public void Undo() => canvas.Children.Remove(stroke);
        public void Redo()
        {
            if (!canvas.Children.Contains(stroke))
                canvas.Children.Add(stroke);
        }
    }

    private sealed class ClearStrokesCommand(Canvas canvas, System.Collections.Generic.List<System.Windows.UIElement> removed) : IDrawCommand
    {
        public void Undo()
        {
            foreach (var element in removed)
            {
                if (!canvas.Children.Contains(element))
                    canvas.Children.Add(element);
            }
        }

        public void Redo()
        {
            foreach (var element in removed)
                canvas.Children.Remove(element);
        }
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

        // Canvas must be able to hold keyboard focus so we can park focus off any text box
        // (see MoveFocusToOverlaySink) — the window itself is a focus scope and would just
        // bounce focus back to the last-focused text box.
        MainCanvas.Focusable = true;

        SetupAnnotationToolbar();
        SetupResizeHandles();
    }

    private void SetupAnnotationToolbar()
    {
        BrushToggle.Checked += (_, _) =>
        {
            isDrawing = true;
            TextToggle.IsChecked = false;
            AnnotationCanvas.IsHitTestVisible = true;
        };
        BrushToggle.Unchecked += (_, _) =>
        {
            isDrawing = false;
            UpdateAnnotationHitTest();
        };

        TextToggle.Checked += (_, _) =>
        {
            isTextMode = true;
            BrushToggle.IsChecked = false;
            AnnotationCanvas.IsHitTestVisible = true;
        };
        TextToggle.Unchecked += (_, _) =>
        {
            isTextMode = false;
            UpdateAnnotationHitTest();
        };

        ColorRed.Click += (_, _) => SetDrawColor(Colors.Red);
        ColorGreen.Click += (_, _) => SetDrawColor(Colors.LimeGreen);
        ColorBlue.Click += (_, _) => SetDrawColor(Colors.DodgerBlue);
        ColorYellow.Click += (_, _) => SetDrawColor(Colors.Yellow);
        ColorBlack.Click += (_, _) => SetDrawColor(Colors.Black);
        ColorWhite.Click += (_, _) => SetDrawColor(Colors.White);
        ColorCustom.Click += (_, _) => OpenCustomColorPicker();

        BrushSizeSlider.ValueChanged += (_, _) => UpdateBrushSize();
        BrushSizeDecrease.Click += (_, _) => DecreaseBrushSize();
        BrushSizeIncrease.Click += (_, _) => IncreaseBrushSize();
        TextSizeSlider.ValueChanged += (_, _) => UpdateTextSize();
        ClearButton.Click += (_, _) => ClearAnnotations();
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

    private void UpdateAnnotationHitTest()
    {
        AnnotationCanvas.IsHitTestVisible = isDrawing || isTextMode;
    }

    private void SetDrawColor(Color color)
    {
        currentColor = color;
        if (lastText != null)
            lastText.TextBox.Foreground = new SolidColorBrush(color);
    }

    private void UpdateTextSize()
    {
        currentTextSize = TextSizeSlider.Value;
        TextSizeText.Text = ((int)currentTextSize).ToString();
        if (lastText != null)
            lastText.TextBox.FontSize = currentTextSize;
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

            if (lastText != null)
                lastText.TextBox.Foreground = new SolidColorBrush(currentColor);
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

        // Border of a finalized selection (Paint-like): edge center moves the whole
        // area, off-center resizes that one side, corners resize diagonally.
        // Checked before text placement so hovering the border wins over spawning a text box.
        if (selectionFinalized)
        {
            var zone = GetBorderZone(position);
            if (zone == BorderZone.Move)
            {
                StartMoveSelection(position);
                e.Handled = true;
                return;
            }
            if (zone != BorderZone.None)
            {
                BeginResize(ZoneToHandle(zone), position);
                e.Handled = true;
                return;
            }
        }

        // Text mode: click on empty area (not the border) places a new text box.
        // Clicks on an existing text box are handled by that box and never reach here.
        if (isTextMode && selectionFinalized)
        {
            PlaceText(e.GetPosition(AnnotationCanvas));
            e.Handled = true;
            return;
        }

        StartNewSelection(position);
    }

    private enum BorderZone
    {
        None,
        Move,
        Top,
        Bottom,
        Left,
        Right,
        TopLeft,
        TopRight,
        BottomLeft,
        BottomRight,
    }

    private BorderZone GetBorderZone(Point p)
    {
        const double band = 8;
        const double centerFraction = 0.15; // edge center ±15% moves the whole area

        var b = selectionBounds;

        var outer = new Rect(b.X - band, b.Y - band, b.Width + 2 * band, b.Height + 2 * band);
        if (!outer.Contains(p))
            return BorderZone.None;

        var innerWidth = b.Width - 2 * band;
        var innerHeight = b.Height - 2 * band;
        if (innerWidth > 0 && innerHeight > 0)
        {
            var inner = new Rect(b.X + band, b.Y + band, innerWidth, innerHeight);
            if (inner.Contains(p))
                return BorderZone.None; // deep interior — not the border
        }

        var nearLeft = Math.Abs(p.X - b.X) <= band;
        var nearRight = Math.Abs(p.X - b.Right) <= band;
        var nearTop = Math.Abs(p.Y - b.Y) <= band;
        var nearBottom = Math.Abs(p.Y - b.Bottom) <= band;

        // Corners → diagonal resize.
        if (nearTop && nearLeft) return BorderZone.TopLeft;
        if (nearTop && nearRight) return BorderZone.TopRight;
        if (nearBottom && nearLeft) return BorderZone.BottomLeft;
        if (nearBottom && nearRight) return BorderZone.BottomRight;

        var midX = b.X + b.Width / 2;
        var midY = b.Y + b.Height / 2;

        if (nearTop || nearBottom)
        {
            if (Math.Abs(p.X - midX) <= b.Width * centerFraction)
                return BorderZone.Move;
            return nearTop ? BorderZone.Top : BorderZone.Bottom;
        }

        if (nearLeft || nearRight)
        {
            if (Math.Abs(p.Y - midY) <= b.Height * centerFraction)
                return BorderZone.Move;
            return nearLeft ? BorderZone.Left : BorderZone.Right;
        }

        return BorderZone.None;
    }

    private static ResizeHandle ZoneToHandle(BorderZone zone) => zone switch
    {
        BorderZone.Top => ResizeHandle.Top,
        BorderZone.Bottom => ResizeHandle.Bottom,
        BorderZone.Left => ResizeHandle.Left,
        BorderZone.Right => ResizeHandle.Right,
        BorderZone.TopLeft => ResizeHandle.TopLeft,
        BorderZone.TopRight => ResizeHandle.TopRight,
        BorderZone.BottomLeft => ResizeHandle.BottomLeft,
        BorderZone.BottomRight => ResizeHandle.BottomRight,
        _ => ResizeHandle.None,
    };

    private static System.Windows.Input.Cursor CursorForZone(BorderZone zone) => zone switch
    {
        BorderZone.Move => System.Windows.Input.Cursors.SizeAll,
        BorderZone.Top or BorderZone.Bottom => System.Windows.Input.Cursors.SizeNS,
        BorderZone.Left or BorderZone.Right => System.Windows.Input.Cursors.SizeWE,
        BorderZone.TopLeft or BorderZone.BottomRight => System.Windows.Input.Cursors.SizeNWSE,
        BorderZone.TopRight or BorderZone.BottomLeft => System.Windows.Input.Cursors.SizeNESW,
        _ => System.Windows.Input.Cursors.Cross,
    };

    private void StartMoveSelection(Point position)
    {
        isMovingSelection = true;
        dragStartPoint = position;

        // Keep the annotation canvas visible so strokes/text travel with the selection
        // and the user sees what the moved area will contain.
        ScreenshotPreview.Visibility = Visibility.Collapsed;
        AnnotationToolbar.Visibility = Visibility.Collapsed;

        MainCanvas.CaptureMouse();
    }

    private void UpdateMovingSelection(Point currentPosition)
    {
        var dx = currentPosition.X - dragStartPoint.X;
        var dy = currentPosition.Y - dragStartPoint.Y;
        dragStartPoint = currentPosition;

        var x = selectionBounds.X + dx;
        var y = selectionBounds.Y + dy;

        // Keep the whole area inside the screen.
        x = Math.Max(0, Math.Min(x, ActualWidth - selectionBounds.Width));
        y = Math.Max(0, Math.Min(y, ActualHeight - selectionBounds.Height));

        selectionBounds = selectionBounds with { X = x, Y = y };
        UpdateSelectionVisuals();
        UpdateDimmingOverlay(selectionBounds);

        // Move the annotation layer with the selection; child coords are relative to the
        // canvas origin, so repositioning the canvas carries every stroke/text along.
        Canvas.SetLeft(AnnotationCanvas, selectionBounds.X);
        Canvas.SetTop(AnnotationCanvas, selectionBounds.Y);
    }

    private void CompleteMovingSelection()
    {
        isMovingSelection = false;
        MainCanvas.ReleaseMouseCapture();

        SelectionRect.Visibility = Visibility.Visible;
        UpdateDimmingOverlay(selectionBounds);
        CaptureSelection();
        ShowAnnotationToolbar();

        // Pull keyboard focus back so Esc/Enter shortcuts keep working after a move.
        MoveFocusToOverlaySink();
    }

    /// <summary>
    /// Moves keyboard focus onto the canvas, off any annotation text box. The window is a
    /// focus scope, so <c>Window.Focus()</c> would restore focus to the last-focused child
    /// (a text box) — leaving <c>editingText</c> true and swallowing Esc forever.
    /// </summary>
    private void MoveFocusToOverlaySink()
    {
        MainCanvas.Focus();
        Keyboard.Focus(MainCanvas);
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

    private void PlaceText(Point position)
    {
        var container = new Grid
        {
            Width = 180,
            Height = 54,
            MinWidth = 60,
            MinHeight = 30,
        };

        var chrome = new Rectangle
        {
            Stroke = Brushes.DeepSkyBlue,
            StrokeThickness = 1,
            StrokeDashArray = [4, 3],
            Fill = Brushes.Transparent,
            IsHitTestVisible = false,
        };

        var textBox = new TextBox
        {
            Background = Brushes.Transparent,
            Foreground = new SolidColorBrush(currentColor),
            FontSize = currentTextSize,
            FontWeight = FontWeights.Bold,
            BorderThickness = new Thickness(0),
            Padding = new Thickness(3),
            AcceptsReturn = true,
            TextWrapping = TextWrapping.Wrap,
            VerticalContentAlignment = VerticalAlignment.Top,
        };

        var thumb = new System.Windows.Controls.Primitives.Thumb
        {
            Width = 14,
            Height = 14,
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Bottom,
            Cursor = System.Windows.Input.Cursors.SizeNWSE,
            Template = BuildThumbTemplate(),
        };

        var annotation = new TextAnnotation
        {
            Container = container,
            TextBox = textBox,
            Chrome = chrome,
            ResizeThumb = thumb,
        };

        thumb.DragDelta += (_, e) =>
        {
            // Clamp resize so the box never grows past the screenshot (selection) bounds.
            var maxWidth = AnnotationCanvasWidth() - Canvas.GetLeft(container);
            var maxHeight = AnnotationCanvasHeight() - Canvas.GetTop(container);

            var newWidth = Math.Min(container.Width + e.HorizontalChange, maxWidth);
            var newHeight = Math.Min(container.Height + e.VerticalChange, maxHeight);

            container.Width = Math.Max(container.MinWidth, newWidth);
            container.Height = Math.Max(container.MinHeight, newHeight);
        };

        textBox.GotKeyboardFocus += (_, _) => lastText = annotation;

        // Move-grips: center 30% of each edge (center ±15%), cursor = move.
        var moveGrips = new[]
        {
            CreateMoveGrip(container, isHorizontalEdge: true, atStart: true),   // top
            CreateMoveGrip(container, isHorizontalEdge: true, atStart: false),  // bottom
            CreateMoveGrip(container, isHorizontalEdge: false, atStart: true),  // left
            CreateMoveGrip(container, isHorizontalEdge: false, atStart: false), // right
        };

        container.Children.Add(chrome);
        container.Children.Add(textBox);
        foreach (var grip in moveGrips)
            container.Children.Add(grip);
        container.Children.Add(thumb);

        container.SizeChanged += (_, _) => UpdateMoveGripLengths(container, moveGrips);
        UpdateMoveGripLengths(container, moveGrips);

        // Keep the initial box fully inside the screenshot bounds.
        var left = Math.Max(0, Math.Min(position.X, AnnotationCanvasWidth() - container.Width));
        var top = Math.Max(0, Math.Min(position.Y, AnnotationCanvasHeight() - container.Height));
        Canvas.SetLeft(container, left);
        Canvas.SetTop(container, top);
        AnnotationCanvas.Children.Add(container);

        textAnnotations.Add(annotation);
        lastText = annotation;
        PushCommand(new AddStrokeCommand(AnnotationCanvas, container));

        textBox.Focus();
    }

    private const double MoveGripBand = 10;
    private const double MoveGripFraction = 0.30; // center ±15%

    private double AnnotationCanvasWidth() =>
        AnnotationCanvas.ActualWidth > 0 ? AnnotationCanvas.ActualWidth : AnnotationCanvas.Width;

    private double AnnotationCanvasHeight() =>
        AnnotationCanvas.ActualHeight > 0 ? AnnotationCanvas.ActualHeight : AnnotationCanvas.Height;

    private System.Windows.Controls.Primitives.Thumb CreateMoveGrip(Grid container, bool isHorizontalEdge, bool atStart)
    {
        var grip = new System.Windows.Controls.Primitives.Thumb
        {
            Cursor = System.Windows.Input.Cursors.SizeAll,
            Opacity = 0, // invisible but hit-testable
            Background = Brushes.Transparent,
        };

        if (isHorizontalEdge)
        {
            grip.Height = MoveGripBand;
            grip.HorizontalAlignment = HorizontalAlignment.Center;
            grip.VerticalAlignment = atStart ? VerticalAlignment.Top : VerticalAlignment.Bottom;
        }
        else
        {
            grip.Width = MoveGripBand;
            grip.VerticalAlignment = VerticalAlignment.Center;
            grip.HorizontalAlignment = atStart ? HorizontalAlignment.Left : HorizontalAlignment.Right;
        }

        grip.DragDelta += (_, e) => MoveTextContainer(container, e.HorizontalChange, e.VerticalChange);
        return grip;
    }

    private void UpdateMoveGripLengths(Grid container, System.Windows.Controls.Primitives.Thumb[] grips)
    {
        var width = container.ActualWidth > 0 ? container.ActualWidth : container.Width;
        var height = container.ActualHeight > 0 ? container.ActualHeight : container.Height;

        // grips: [0]=top, [1]=bottom, [2]=left, [3]=right
        grips[0].Width = grips[1].Width = width * MoveGripFraction;
        grips[2].Height = grips[3].Height = height * MoveGripFraction;
    }

    private void MoveTextContainer(Grid container, double dx, double dy)
    {
        var left = Canvas.GetLeft(container) + dx;
        var top = Canvas.GetTop(container) + dy;

        var maxLeft = AnnotationCanvasWidth() - container.ActualWidth;
        var maxTop = AnnotationCanvasHeight() - container.ActualHeight;

        left = Math.Max(0, Math.Min(left, Math.Max(0, maxLeft)));
        top = Math.Max(0, Math.Min(top, Math.Max(0, maxTop)));

        Canvas.SetLeft(container, left);
        Canvas.SetTop(container, top);
    }

    private static ControlTemplate BuildThumbTemplate()
    {
        var rectangle = new FrameworkElementFactory(typeof(Rectangle));
        rectangle.SetValue(Rectangle.FillProperty, Brushes.DeepSkyBlue);
        rectangle.SetValue(Rectangle.StrokeProperty, Brushes.White);
        rectangle.SetValue(Rectangle.StrokeThicknessProperty, 2.0);

        return new ControlTemplate(typeof(System.Windows.Controls.Primitives.Thumb))
        {
            VisualTree = rectangle,
        };
    }

    private void StartNewSelection(Point position)
    {
        // Arm only. Don't touch an existing finalized selection until an actual
        // drag begins (see UpdateCreatingSelection) — a bare click must be a no-op.
        dragStartPoint = position;
        isCreatingSelection = true;
        hasDragged = false;

        MainCanvas.CaptureMouse();
    }

    private void BeginNewSelectionVisuals()
    {
        selectionFinalized = false;

        SelectionRect.Visibility = Visibility.Visible;
        HideHandles();
        HideAnnotationUi();
    }

    private void HideAnnotationUi()
    {
        ScreenshotPreview.Visibility = Visibility.Collapsed;
        AnnotationToolbar.Visibility = Visibility.Collapsed;
        AnnotationCanvas.Visibility = Visibility.Collapsed;
        AnnotationCanvas.Children.Clear();
        textAnnotations.Clear();
        lastText = null;
        ResetUndoHistory();
    }

    private void PushCommand(IDrawCommand command)
    {
        undoStack.Push(command);
        redoStack.Clear();
    }

    private void ResetUndoHistory()
    {
        undoStack.Clear();
        redoStack.Clear();
    }

    private void ClearAnnotations()
    {
        if (AnnotationCanvas.Children.Count == 0)
            return;

        var removed = new System.Collections.Generic.List<System.Windows.UIElement>();
        foreach (System.Windows.UIElement child in AnnotationCanvas.Children)
            removed.Add(child);

        AnnotationCanvas.Children.Clear();
        lastText = null;
        PushCommand(new ClearStrokesCommand(AnnotationCanvas, removed));
    }

    private void PerformUndo()
    {
        if (undoStack.Count == 0)
            return;

        var command = undoStack.Pop();
        command.Undo();
        redoStack.Push(command);
    }

    private void PerformRedo()
    {
        if (redoStack.Count == 0)
            return;

        var command = redoStack.Pop();
        command.Redo();
        undoStack.Push(command);
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

        // Priority 2: Handle moving the whole selection
        if (isMovingSelection && e.LeftButton == MouseButtonState.Pressed)
        {
            UpdateMovingSelection(currentPosition);
            return;
        }

        // Priority 3: Handle resizing via handles
        if (isResizingSelection && e.LeftButton == MouseButtonState.Pressed)
        {
            UpdateResizing(currentPosition);
            return;
        }

        // Priority 4: Handle creating new selection
        if (isCreatingSelection && e.LeftButton == MouseButtonState.Pressed)
        {
            UpdateCreatingSelection(currentPosition);
            return;
        }

        // Idle: reflect the border action under the pointer.
        if (e.LeftButton == MouseButtonState.Released)
        {
            Cursor = selectionFinalized
                ? CursorForZone(GetBorderZone(currentPosition))
                : System.Windows.Input.Cursors.Cross;
        }
    }

    private void ContinueDrawing(Point position)
    {
        currentStroke?.Points.Add(position);
    }

    private void UpdateCreatingSelection(Point currentPosition)
    {
        var distance = (currentPosition - dragStartPoint).Length;

        if (!hasDragged)
        {
            if (distance <= 2)
                return; // still a bare click — leave any existing selection intact

            // First real movement: now tear down the old selection.
            hasDragged = true;
            BeginNewSelectionVisuals();
        }

        selectionBounds = GeometryHelper.CreateRectFromPoints(dragStartPoint, currentPosition);

        UpdateSelectionVisuals();
        UpdateDimmingOverlay(selectionBounds);
    }

    private void UpdateResizing(Point currentPosition)
    {
        var newBounds = ClampToScreen(CalculateResizedBounds(currentPosition));

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

        UpdateDimensionLabel();
    }

    private void UpdateDimensionLabel()
    {
        DimensionText.Text = $"{(int)selectionBounds.Width} x {(int)selectionBounds.Height}";

        // Measure to get actual size for space checks
        DimensionLabel.Measure(new System.Windows.Size(double.PositiveInfinity, double.PositiveInfinity));
        var w = DimensionLabel.DesiredSize.Width;
        var h = DimensionLabel.DesiredSize.Height;

        const double gap = 4;
        var left = selectionBounds.X;

        double top;
        if (selectionBounds.Y - h - gap >= 0)
        {
            // Space above top-left corner
            top = selectionBounds.Y - h - gap;
        }
        else if (selectionBounds.Bottom + gap + h <= ActualHeight)
        {
            // No space above -> below bottom-left corner
            top = selectionBounds.Bottom + gap;
        }
        else
        {
            // No space either -> inside top-left corner
            top = selectionBounds.Y + gap;
            left = selectionBounds.X + gap;
        }

        // Keep label on-screen horizontally
        left = Math.Max(0, Math.Min(left, ActualWidth - w));

        Canvas.SetLeft(DimensionLabel, left);
        Canvas.SetTop(DimensionLabel, top);
        DimensionLabel.Visibility = Visibility.Visible;
    }

    private void HideDimensionLabel()
    {
        DimensionLabel.Visibility = Visibility.Collapsed;
    }

    private void OnMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (isDrawing && currentStroke != null)
        {
            PushCommand(new AddStrokeCommand(AnnotationCanvas, currentStroke));
            currentStroke = null;
            return;
        }

        if (isMovingSelection)
        {
            CompleteMovingSelection();
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
            // Bare click: existing selection was never touched — keep it as-is.
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
            HideDimensionLabel();
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

            // The canvas stayed at its pre-resize origin during the drag; rebase children so
            // their on-screen position is preserved once the canvas moves to the new origin.
            RebaseAnnotationsToNewOrigin();

            UpdateDimmingOverlay(selectionBounds);
            CaptureSelection();
            ShowAnnotationToolbar();

            // Pull keyboard focus back so Esc/Enter shortcuts keep working after a resize.
            MoveFocusToOverlaySink();
        }
        else
        {
            SelectionRect.Visibility = Visibility.Collapsed;
            HideHandles();
            HideAnnotationUi();
            UpdateDimmingOverlay(new Rect(0, 0, ActualWidth, ActualHeight));
        }
    }

    /// <summary>
    /// Shifts every annotation child so it keeps its current on-screen position after the
    /// canvas origin moves to <see cref="selectionBounds"/>, then repositions the canvas.
    /// Child coords are relative to the canvas origin, so this preserves pixel alignment.
    /// </summary>
    private void RebaseAnnotationsToNewOrigin()
    {
        var oldX = Canvas.GetLeft(AnnotationCanvas);
        var oldY = Canvas.GetTop(AnnotationCanvas);
        if (double.IsNaN(oldX)) oldX = 0;
        if (double.IsNaN(oldY)) oldY = 0;

        var dx = oldX - selectionBounds.X;
        var dy = oldY - selectionBounds.Y;

        if (dx != 0 || dy != 0)
        {
            foreach (System.Windows.UIElement child in AnnotationCanvas.Children)
            {
                var left = Canvas.GetLeft(child);
                var top = Canvas.GetTop(child);
                if (double.IsNaN(left)) left = 0;
                if (double.IsNaN(top)) top = 0;
                Canvas.SetLeft(child, left + dx);
                Canvas.SetTop(child, top + dy);
            }
        }

        SetupAnnotationCanvas();
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
        BeginResize(handle, e.GetPosition(MainCanvas));
        e.Handled = true;
    }

    private void BeginResize(ResizeHandle handle, Point start)
    {
        isResizingSelection = true;
        activeResizeHandle = handle;
        dragStartPoint = start;

        // Leave the annotation canvas pinned to the screen during the drag so existing
        // strokes/text stay put over the frozen desktop while the box resizes around them.
        ScreenshotPreview.Visibility = Visibility.Collapsed;
        AnnotationToolbar.Visibility = Visibility.Collapsed;

        MainCanvas.CaptureMouse();
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

            case ResizeHandle.Top:
                return GeometryHelper.CreateRectFromPoints(new Point(x, currentPosition.Y), new Point(right, bottom));

            case ResizeHandle.Bottom:
                return GeometryHelper.CreateRectFromPoints(new Point(x, y), new Point(right, currentPosition.Y));

            case ResizeHandle.Left:
                return GeometryHelper.CreateRectFromPoints(new Point(currentPosition.X, y), new Point(right, bottom));

            case ResizeHandle.Right:
                return GeometryHelper.CreateRectFromPoints(new Point(x, y), new Point(currentPosition.X, bottom));

            default:
                return selectionBounds;
        }
    }

    private Rect ClampToScreen(Rect rect)
    {
        var left = Math.Max(0, rect.Left);
        var top = Math.Max(0, rect.Top);
        var right = Math.Min(ActualWidth, rect.Right);
        var bottom = Math.Min(ActualHeight, rect.Bottom);

        return new Rect(left, top, Math.Max(0, right - left), Math.Max(0, bottom - top));
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
        var toolbarWidth = 640;
        
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
            var editingText = Keyboard.FocusedElement is TextBox;

            if (e.Key == Key.Escape)
            {
                if (editingText || BrushToggle.IsChecked == true || TextToggle.IsChecked == true)
                {
                    // Exit draw/text mode; keep the capture session open.
                    BrushToggle.IsChecked = false;
                    TextToggle.IsChecked = false;
                    // Park focus off any text box so the next Esc reaches OnKeyDown and closes.
                    MoveFocusToOverlaySink();
                    e.Handled = true;
                    return;
                }

                DialogResult = false;
                Close();
            }
            else if (e.Key == Key.Enter || e.Key == Key.Return)
            {
                // While typing in a text box, Enter belongs to the text box, not save-and-close.
                if (!editingText && selectionFinalized && GeometryHelper.IsValidSelection(selectionBounds))
                {
                    SaveAndClose();
                }
            }
            else if (e.Key == Key.Z && Keyboard.Modifiers == (ModifierKeys.Control | ModifierKeys.Shift))
            {
                PerformRedo();
            }
            else if (e.Key == Key.Z && Keyboard.Modifiers == ModifierKeys.Control)
            {
                PerformUndo();
            }
            else if (e.Key == Key.Y && Keyboard.Modifiers == ModifierKeys.Control)
            {
                PerformRedo();
            }
            else if (e.Key == Key.D && !editingText)
            {
                BrushToggle.IsChecked = !BrushToggle.IsChecked;
            }
            else if (e.Key == Key.A && !editingText)
            {
                TextToggle.IsChecked = !TextToggle.IsChecked;
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

    private void PrepareTextForRender()
    {
        // Drop focus so no caret/selection is captured in the render.
        Keyboard.ClearFocus();

        foreach (var annotation in textAnnotations)
        {
            if (!AnnotationCanvas.Children.Contains(annotation.Container))
                continue;

            if (string.IsNullOrWhiteSpace(annotation.TextBox.Text))
            {
                AnnotationCanvas.Children.Remove(annotation.Container);
                continue;
            }

            annotation.Chrome.Visibility = Visibility.Collapsed;
            annotation.ResizeThumb.Visibility = Visibility.Collapsed;
            annotation.TextBox.IsReadOnly = true;
        }

        AnnotationCanvas.UpdateLayout();
    }

    private Bitmap RenderFinalImage()
    {
        try
        {
            PrepareTextForRender();

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

