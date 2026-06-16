namespace MSSQLPlanViewer.Web.Showplans;

public interface IEstimatedShowplanProvider
{
    Task<IReadOnlyList<EstimatedShowplanXml>> GetEstimatedShowplansAsync(
        EstimatedShowplanRequest request,
        CancellationToken cancellationToken = default);
}

