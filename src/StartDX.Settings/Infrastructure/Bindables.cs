using System.ComponentModel;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Windows.Data;
using System.Windows.Input;
using StartDX.Shared;

namespace StartDX.Settings.Infrastructure;

public abstract class ObservableObject : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    protected bool Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;
        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        return true;
    }

    protected void Raise([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

public sealed class RelayCommand(Action<object?> execute) : ICommand
{
    public event EventHandler? CanExecuteChanged { add { } remove { } }
    public bool CanExecute(object? parameter) => true;
    public void Execute(object? parameter) => execute(parameter);
}

/// <summary>Two-way maps a <see cref="DockEdge"/> to a RadioButton's IsChecked (ConverterParameter = edge name).</summary>
public sealed class EdgeToBoolConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is DockEdge edge && Enum.TryParse<DockEdge>(parameter as string, out var target) && edge == target;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is true && Enum.TryParse<DockEdge>(parameter as string, out var target) ? target : Binding.DoNothing;
}
