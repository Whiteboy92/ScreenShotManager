using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace ScreenShotManager.Shared.Components;

public partial class CustomDropdown
{
    public static readonly DependencyProperty SelectedValueProperty =
        DependencyProperty.Register(
            nameof(SelectedValue), 
            typeof(string), 
            typeof(CustomDropdown), 
            new FrameworkPropertyMetadata(
                "PNG", 
                FrameworkPropertyMetadataOptions.BindsTwoWayByDefault,
                OnSelectedValueChanged));

    public string SelectedValue
    {
        get => (string)GetValue(SelectedValueProperty);
        set => SetValue(SelectedValueProperty, value);
    }

    public event EventHandler<string>? SelectionChanged;

    public CustomDropdown()
    {
        InitializeComponent();
        UpdateSelectedText();
        
        // Close popup when parent scrolls
        Loaded += (_, _) =>
        {
            var scrollViewer = FindParentScrollViewer(this);
            if (scrollViewer != null)
            {
                scrollViewer.ScrollChanged += (_, _) =>
                {
                    if (DropdownPopup.IsOpen)
                    {
                        DropdownPopup.IsOpen = false;
                    }
                };
            }
        };
    }

    private ScrollViewer? FindParentScrollViewer(DependencyObject child)
    {
        var parent = System.Windows.Media.VisualTreeHelper.GetParent(child);
        
        if (parent == null)
            return null;
            
        if (parent is ScrollViewer scrollViewer)
            return scrollViewer;
            
        return FindParentScrollViewer(parent);
    }

    private static void OnSelectedValueChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is CustomDropdown dropdown)
        {
            dropdown.UpdateSelectedText();
            dropdown.SelectionChanged?.Invoke(dropdown, dropdown.SelectedValue);
        }
    }

    private void UpdateSelectedText()
    {
        SelectedText.Text = SelectedValue?.ToUpper() switch
        {
            "PNG" => "PNG (Recommended)",
            "JPEG" or "JPG" => "JPEG",
            "BMP" => "BMP",
            _ => "PNG (Recommended)",
        };
    }

    private void DropdownButton_Click(object sender, RoutedEventArgs e)
    {
        DropdownPopup.IsOpen = !DropdownPopup.IsOpen;
    }

    private void Item_Click(object sender, MouseButtonEventArgs e)
    {
        if (sender is FrameworkElement element && element.Tag is string value)
        {
            SelectedValue = value;
            DropdownPopup.IsOpen = false;
        }
    }
}

