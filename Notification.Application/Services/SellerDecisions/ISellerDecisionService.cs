namespace Notification.Application.Services.SellerDecisions;

public interface ISellerDecisionService
{
    Task<SellerDecisionResponse> CreateAsync(CreateSellerDecisionRequest request,CancellationToken cancellationToken = default);
}
