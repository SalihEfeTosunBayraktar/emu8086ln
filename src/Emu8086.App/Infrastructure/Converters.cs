using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;
using Emu8086.App.Services;
using Emu8086.App.ViewModels;

namespace Emu8086.App.Infrastructure;

public sealed class BoolToVisibilityConverter : IValueConverter
{
    public bool Invert { get; set; }

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        (value is true) ^ Invert ? Visibility.Visible : Visibility.Collapsed;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        (value is Visibility.Visible) ^ Invert;
}

public sealed class NullToVisibilityConverter : IValueConverter
{
    public bool Invert { get; set; }

    public object Convert(object? value, Type targetType, object parameter, CultureInfo culture) =>
        (value != null) ^ Invert ? Visibility.Visible : Visibility.Collapsed;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>Visible when a count is above zero (ConverterParameter "invert" flips it).</summary>
public sealed class CountToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object? parameter, CultureInfo culture) =>
        (value is int n && n > 0) ^ (parameter as string == "invert") ? Visibility.Visible : Visibility.Collapsed;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>Looks up a localization key held in the bound value.</summary>
public sealed class LocKeyConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is string key ? Loc.Instance[key] : "";

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>Maps output, severity and session state values to theme brushes.</summary>
public sealed class StatusBrushConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        string key = value switch
        {
            OutputKind.Success => "Success",
            OutputKind.Warning => "Warning",
            OutputKind.Error => "Error",
            true => "Error",
            false => "Warning",
            SessionState.Running => "Success",
            SessionState.WaitingInput => "Warning",
            SessionState.Paused or SessionState.Ready => "Accent",
            SessionState.Stopped => "Error",
            _ => "Fg.Secondary",
        };
        return Application.Current.FindResource(key) as Brush ?? Brushes.Gray;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>Chooses the icon geometry for a diagnostic (true = error).</summary>
public sealed class SeverityIconConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        Application.Current.FindResource(value is true ? "Icon.Error" : "Icon.Warning");

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
