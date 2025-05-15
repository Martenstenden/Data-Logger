using System;
using Data_Logger.Core;

namespace Data_Logger.Models;

public class PlotDataPoint : ObservableObject
{
    private DateTime _timestamp;
    public DateTime Timestamp
    {
        get => _timestamp;
        set => SetProperty(ref _timestamp, value);
    }

    private double _value;
    public double Value
    {
        get => _value;
        set => SetProperty(ref _value, value);
    }
}