using System.Security.Claims;
using UKK.Services;
using UKK.Models;

namespace UKK.Controllers
{
    public static class UsersController
    {
        public static void MapUsers(this WebApplication app)
        {
            var g = app.MapGroup("api/v1/users").WithTags("Users");

            //GET ALL USERS
            g.MapGet("/", async (UsersService service, int? page, int? pageSize, string? search, uint? idRole) =>
            {
                try
                {
                    var users = await service.GetAllUsers(page ?? 1, pageSize ?? 20, search, idRole);
                    return Results.Ok(users);
                }
                catch (Exception ex)
                {
                    var problem = Problem.Fault(ex);
                    return Results.Problem(title: problem.Title, statusCode: problem.StatusCode, detail: problem.Detail);
                }
            }).RequireAuthorization(policy => policy.RequireRole("Admin"));

            //GET USER BY ID
            g.MapGet("/{id}", async (UsersService service, uint id) =>
            {
                try
                {
                    var user = await service.GetUserById(id);
                    return Results.Ok(user);
                }
                catch (Exception ex)
                {
                    var problem = Problem.Fault(ex);
                    return Results.Problem(title: problem.Title, statusCode: problem.StatusCode, detail: problem.Detail);
                }
            }).RequireAuthorization(policy => policy.RequireRole("Admin"));

            //MENAMBAHKAN USER
            g.MapPost("/", async (UsersService service, ClaimsPrincipal user, UserRequest request) =>
            {
                try
                {
                    await service.CreateUser(request, UserAuth.GetId(user));
                    return Results.Ok();
                }
                catch (Exception ex)
                {
                    var problem = Problem.Fault(ex);
                    return Results.Problem(title: problem.Title, statusCode: problem.StatusCode, detail: problem.Detail);
                }
            }).RequireAuthorization(policy => policy.RequireRole("Admin"));

            //EDIT USER
            g.MapPut("/{id}", async (UsersService service, ClaimsPrincipal user, uint id, UserRequest request) =>
            {
                try
                {
                    await service.UpdateUser(id, request, UserAuth.GetId(user));
                    return Results.Ok();
                }
                catch (Exception ex)
                {
                    var problem = Problem.Fault(ex);
                    return Results.Problem(title: problem.Title, statusCode: problem.StatusCode, detail: problem.Detail);
                }
            }).RequireAuthorization(policy => policy.RequireRole("Admin"));

            //MENGHAPUS USER
            g.MapDelete("/{id}", async (UsersService service, ClaimsPrincipal user, uint id) =>
            {
                try
                {
                    await service.DeleteUser(id, UserAuth.GetId(user));
                    return Results.Ok();
                }
                catch (Exception ex)
                {
                    var problem = Problem.Fault(ex);
                    return Results.Problem(title: problem.Title, statusCode: problem.StatusCode, detail: problem.Detail);
                }
            }).RequireAuthorization(policy => policy.RequireRole("Admin"));
        }
    }
}
