namespace QuantTrading.Api.Services.Auth;

public interface IPasswordService
{
    string HashPassword(string password);
    bool VerifyPassword(string password, string passwordHash);
}
