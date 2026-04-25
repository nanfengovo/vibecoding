using QuantTrading.Api.Models;

namespace QuantTrading.Api.Services.Auth;

public interface ICurrentUserService
{
    int? UserId { get; }
    string Role { get; }
    bool IsAuthenticated { get; }
    bool IsAdmin { get; }
    Task<int> GetEffectiveUserIdAsync(CancellationToken cancellationToken = default);
}
