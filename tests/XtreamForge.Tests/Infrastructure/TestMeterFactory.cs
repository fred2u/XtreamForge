using System.Diagnostics.Metrics;

namespace XtreamForge.Tests.Infrastructure;

/// <summary>Creates real meters, disposed with the factory; nothing listens to them unless a test adds a listener.</summary>
public sealed class TestMeterFactory : IMeterFactory
{
    private readonly List<Meter> _meters = [];

    public Meter Create(MeterOptions options)
    {
        var meter = new Meter(options);
        _meters.Add(meter);

        return meter;
    }

    public void Dispose()
    {
        foreach (var meter in _meters)
        {
            meter.Dispose();
        }

        _meters.Clear();
    }
}
