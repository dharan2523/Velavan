using VelavanDemo.Application.Authentication.Models;

namespace VelavanDemo.Application.Authentication;

public enum AuthenticationFailure
{
    None = 0,
    InvalidCredentials = 1,
    AccountInactive = 2
}

public sealed record AuthenticationResult(
    bool Succeeded,
    AuthenticationFailure Failure,
    AuthResponse? Response = null)
{
    public static AuthenticationResult Success(AuthResponse response)
    {
        return new AuthenticationResult(true, AuthenticationFailure.None, response);
    }

    public static AuthenticationResult Failed(AuthenticationFailure failure)
    {
        return new AuthenticationResult(false, failure);
    }
}
