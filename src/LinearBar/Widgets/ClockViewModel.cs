using System.Globalization;
using System.Windows.Threading;
using LinearBar.Core.Config;
using LinearBar.Infrastructure;

namespace LinearBar.Widgets;

/// <summary>The clock at the end of the bar.</summary>
internal sealed class ClockViewModel : ObservableObject, IDisposable
{
    private readonly DispatcherTimer _timer;
    private string _timeFormat = "HH:mm";
    private string _dateFormat = "ddd, d MMM";
    private string _timeText = string.Empty;
    private string _dateText = string.Empty;
    private string _fullDate = string.Empty;
    private bool _isVisible = true;
    private bool _showDate = true;

    public ClockViewModel(ClockSettings settings)
    {
        // Ticking every second keeps the minute change on time; text only changes when needed.
        _timer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromSeconds(1) };
        _timer.Tick += (_, _) => Refresh();
        ApplySettings(settings);
        _timer.Start();
    }

    public string TimeText
    {
        get => _timeText;
        private set => Set(ref _timeText, value);
    }

    public string DateText
    {
        get => _dateText;
        private set => Set(ref _dateText, value);
    }

    /// <summary>Long date for the tooltip.</summary>
    public string FullDate
    {
        get => _fullDate;
        private set => Set(ref _fullDate, value);
    }

    public bool IsVisible
    {
        get => _isVisible;
        private set => Set(ref _isVisible, value);
    }

    public bool ShowDate
    {
        get => _showDate;
        private set => Set(ref _showDate, value);
    }

    public void ApplySettings(ClockSettings settings)
    {
        _timeFormat = settings.TimeFormat;
        _dateFormat = settings.DateFormat;
        IsVisible = settings.Show;
        ShowDate = settings.ShowDate;
        Refresh();
    }

    public void Dispose() => _timer.Stop();

    private void Refresh()
    {
        DateTime now = DateTime.Now;
        CultureInfo culture = CultureInfo.CurrentCulture;

        TimeText = Format(now, _timeFormat, "HH:mm", culture);
        DateText = Format(now, _dateFormat, "ddd, d MMM", culture);
        FullDate = now.ToString("D", culture);
    }

    /// <summary>A format string typed into the settings file may be invalid; never throw for it.</summary>
    private static string Format(DateTime value, string format, string fallback, CultureInfo culture)
    {
        try
        {
            return value.ToString(format, culture);
        }
        catch (FormatException)
        {
            return value.ToString(fallback, culture);
        }
    }
}
