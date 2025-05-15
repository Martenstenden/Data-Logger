using Data_Logger.Core;

namespace Data_Logger.ViewModels;

public class PlottedSeriesDisplayInfo : ObservableObject
{
    private string _seriesKey; 
    public string SeriesKey
    {
        get => _seriesKey;
        private set => SetProperty(ref _seriesKey, value); 
    }

    private bool _showMeanLine;
    public bool ShowMeanLine
    {
        get => _showMeanLine;
        set
        {
            if (SetProperty(ref _showMeanLine, value))
            {
                OnStatLineVisibilityChanged?.Invoke(this, "mean", value);
            }
        }
    }

    private bool _showMaxLine;
    public bool ShowMaxLine
    {
        get => _showMaxLine;
        set
        {
            if (SetProperty(ref _showMaxLine, value))
            {
                OnStatLineVisibilityChanged?.Invoke(this, "max", value);
            }
        }
    }

    private bool _showMinLine;
    public bool ShowMinLine
    {
        get => _showMinLine;
        set
        {
            if (SetProperty(ref _showMinLine, value))
            {
                OnStatLineVisibilityChanged?.Invoke(this, "min", value);
            }
        }
    }

    
    public delegate void StatLineVisibilityChangedHandler(PlottedSeriesDisplayInfo seriesInfo, string lineType, bool visibility);
    public event StatLineVisibilityChangedHandler OnStatLineVisibilityChanged;

    public PlottedSeriesDisplayInfo(string seriesKey)
    {
        SeriesKey = seriesKey;
    }
}