namespace AiRouter.Telemetry;

public interface IRouterTelemetry
{
    void Record(RouterTelemetryRecord record);
    IReadOnlyList<RouterTelemetryRecord> Recent();
    RouterTelemetrySummary Summary();
}
