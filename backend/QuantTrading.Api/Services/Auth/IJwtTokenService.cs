using QuantTrading.Api.Models;

namespace QuantTrading.Api.Services.Auth;

public interface IJwtTokenService
{
    string CreateToken(AppUser user);
}
