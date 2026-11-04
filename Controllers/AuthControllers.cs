using System.Security.Claims;
using Microsoft.AspNetCore.RateLimiting;
using UKK.Services;
using UKK.Models;

namespace UKK.Controllers
{
    public static class AuthController
    {
        public static void MapAuth(this WebApplication app)
        {
            var g = app.MapGroup("api/v1/auth").WithTags("Auth");

            //LOGIN
            g.MapPost("/login", async (
                AuthService service,
                LoginRequest request,
                HttpContext context,
                CancellationToken cancellationToken) =>
            {
                try
                {
                    var response = await service.LoginAsync(
                        request,
                        context.Connection.RemoteIpAddress?.ToString(),
                        cancellationToken);
                    return Results.Ok(response);
                }
                catch (Exception ex)
                {
                    var problem = Problem.Fault(ex);
                    return Results.Problem(title: problem.Title, statusCode: problem.StatusCode, detail: problem.Detail);
                }
            })
                .RequireRateLimiting("login")
                .Produces<AuthResponse>()
                .ProducesProblem(400)
                .ProducesProblem(401)
                .ProducesProblem(429)
                .ProducesProblem(503);

            g.MapGet("/captcha-config", (RecaptchaService service) =>
                Results.Ok(service.GetPublicConfig()))
                .Produces<CaptchaConfigResponse>();

            g.MapPost("/register", async (AuthService service, RegisterRequest request) =>
            {
                try
                {
                    var response = await service.RegisterAsync(request);
                    return Results.Created($"/api/v1/users/{response.Id}", response);
                }
                catch (Exception ex)
                {
                    var problem = Problem.Fault(ex);
                    return Results.Problem(title: problem.Title, statusCode: problem.StatusCode, detail: problem.Detail);
                }
            });

            //CURRENT USER PROFILE
            g.MapGet("/me", async (UsersService service, ClaimsPrincipal user) =>
            {
                try
                {
                    var profile = await service.GetUserById(UserAuth.GetId(user));
                    return Results.Ok(profile);
                }
                catch (Exception ex)
                {
                    var problem = Problem.Fault(ex);
                    return Results.Problem(title: problem.Title, statusCode: problem.StatusCode, detail: problem.Detail);
                }
            }).RequireAuthorization();

            //LOGOUT (JWT IS STATELESS, ONLY WRITES log_aktivitas, THE CLIENT DISCARDS THE TOKEN)
            g.MapPost("/logout", async (AuthService service, ClaimsPrincipal user) =>
            {
                try
                {
                    await service.LogoutAsync(UserAuth.GetId(user));
                    return Results.Ok();
                }
                catch (Exception ex)
                {
                    var problem = Problem.Fault(ex);
                    return Results.Problem(title: problem.Title, statusCode: problem.StatusCode, detail: problem.Detail);
                }
            }).RequireAuthorization();
        }
    }
}
