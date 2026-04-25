using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using QuantTrading.Api.Data;
using QuantTrading.Api.Models;

namespace QuantTrading.Api.Services.Auth;

public sealed class CurrentUserService : ICurrentUserService
{
    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly QuantTradingDbContext _dbContext;

    public CurrentUserService(IHttpContextAccessor httpContextAccessor, QuantTradingDbContext dbContext)
    {
        _httpContextAccessor = httpContextAccessor;
        _dbContext = dbContext;
    }

    public int? UserId
    {
        get
        {
            var value = _httpContextAccessor.HttpContext?.User.FindFirstValue(ClaimTypes.NameIdentifier)
                ?? _httpContextAccessor.HttpContext?.User.FindFirstValue(ClaimTypes.NameIdentifier)
                ?? _httpContextAccessor.HttpContext?.User.FindFirstValue("sub");
            return int.TryParse(value, out var id) ? id : null;
        }
    }

    public string Role => _httpContextAccessor.HttpContext?.User.FindFirstValue(ClaimTypes.Role) ?? string.Empty;

    public bool IsAuthenticated => _httpContextAccessor.HttpContext?.User.Identity?.IsAuthenticated == true;

    public bool IsAdmin => string.Equals(Role, UserRoles.Admin, StringComparison.OrdinalIgnoreCase);

    public async Task<int> GetEffectiveUserIdAsync(CancellationToken cancellationToken = default)
    {
        if (UserId is { } userId)
        {
            return userId;
        }

        var admin = await _dbContext.AppUsers
            .Where(u => u.Role == UserRoles.Admin && u.IsActive)
            .OrderBy(u => u.Id)
            .FirstOrDefaultAsync(cancellationToken);
        if (admin != null)
        {
            return admin.Id;
        }

        var first = await _dbContext.AppUsers
            .Where(u => u.IsActive)
            .OrderBy(u => u.Id)
            .FirstAsync(cancellationToken);
        return first.Id;
    }
}
