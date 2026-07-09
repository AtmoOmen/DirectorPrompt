using System.Windows;
using System.Windows.Controls;

namespace DirectorPrompt.Views.Components;

public sealed partial class MultiSelectComboBox : UserControl
{
    public static readonly DependencyProperty ItemsSourceProperty =
        DependencyProperty.Register
        (
            nameof(ItemsSource),
            typeof(object),
            typeof(MultiSelectComboBox),
            new PropertyMetadata(null)
        );

    public static readonly DependencyProperty DisplayMemberPathProperty =
        DependencyProperty.Register
        (
            nameof(DisplayMemberPath),
            typeof(string),
            typeof(MultiSelectComboBox),
            new PropertyMetadata(string.Empty)
        );

    public static readonly DependencyProperty SelectedMemberPathProperty =
        DependencyProperty.Register
        (
            nameof(SelectedMemberPath),
            typeof(string),
            typeof(MultiSelectComboBox),
            new PropertyMetadata(string.Empty)
        );

    public static readonly DependencyProperty DelimiterProperty =
        DependencyProperty.Register
        (
            nameof(Delimiter),
            typeof(string),
            typeof(MultiSelectComboBox),
            new PropertyMetadata(", ")
        );

    public static readonly DependencyProperty WatermarkProperty =
        DependencyProperty.Register
        (
            nameof(Watermark),
            typeof(object),
            typeof(MultiSelectComboBox),
            new PropertyMetadata(null)
        );

    public object? ItemsSource
    {
        get => GetValue(ItemsSourceProperty);
        set => SetValue(ItemsSourceProperty, value);
    }

    public string DisplayMemberPath
    {
        get => (string)GetValue(DisplayMemberPathProperty);
        set => SetValue(DisplayMemberPathProperty, value);
    }

    public string SelectedMemberPath
    {
        get => (string)GetValue(SelectedMemberPathProperty);
        set => SetValue(SelectedMemberPathProperty, value);
    }

    public string Delimiter
    {
        get => (string)GetValue(DelimiterProperty);
        set => SetValue(DelimiterProperty, value);
    }

    public object? Watermark
    {
        get => GetValue(WatermarkProperty);
        set => SetValue(WatermarkProperty, value);
    }

    public MultiSelectComboBox() =>
        InitializeComponent();
}
