using System.Security.Claims;
using UKK.Services;
using UKK.Models;

namespace UKK.Controllers
{
    public static class PengembalianController
    {
        public static void MapPengembalian(this WebApplication app)
        {
            var g = app.MapGroup("api/v1/pengembalian").WithTags("Pengembalian");

            //MEMPROSES PENGEMBALIAN
            g.MapPost("/", async (PengembalianService service, ClaimsPrincipal user, PengembalianRequest request) =>
            {
                try
                {
                    await service.CreatePengembalian(request, UserAuth.GetId(user), UserAuth.GetRole(user));
                    return Results.Ok();
                }
                catch (Exception ex)
                {
                    var problem = Problem.Fault(ex);
                    return Results.Problem(title: problem.Title, statusCode: problem.StatusCode, detail: problem.Detail);
                }
            }).RequireAuthorization(policy => policy.RequireRole("Admin", "Petugas"));

            //GET ALL PENGEMBALIAN
            g.MapGet("/", async (PengembalianService service, int? page, int? pageSize, DateOnly? from, DateOnly? to) =>
            {
                try
                {
                    var returns = await service.GetAllPengembalian(page ?? 1, pageSize ?? 20, from, to);
                    return Results.Ok(returns);
                }
                catch (Exception ex)
                {
                    var problem = Problem.Fault(ex);
                    return Results.Problem(title: problem.Title, statusCode: problem.StatusCode, detail: problem.Detail);
                }
            }).RequireAuthorization(policy => policy.RequireRole("Admin", "Petugas"));

            //GET PENGEMBALIAN BY ID
            g.MapGet("/{id}", async (PengembalianService service, ClaimsPrincipal user, uint id) =>
            {
                try
                {
                    var pengembalian = await service.GetPengembalianById(id, UserAuth.GetId(user), UserAuth.GetRole(user));
                    return Results.Ok(pengembalian);
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
