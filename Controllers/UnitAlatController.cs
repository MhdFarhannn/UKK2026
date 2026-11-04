using System.Security.Claims;
using UKK.Services;
using UKK.Models;

namespace UKK.Controllers
{
    public static class UnitAlatController
    {
        public static void MapUnitAlat(this WebApplication app)
        {
            var g = app.MapGroup("api/v1/unit-alat").WithTags("Unit Alat");

            //MENAMBAHKAN UNIT ALAT
            g.MapPost("/", async (UnitAlatService service, ClaimsPrincipal user, UnitAlatRequest request) =>
            {
                try
                {
                    await service.CreateUnitAlat(request, UserAuth.GetId(user));
                    return Results.Ok();
                }
                catch (Exception ex)
                {
                    var problem = Problem.Fault(ex);
                    return Results.Problem(title: problem.Title, statusCode: problem.StatusCode, detail: problem.Detail);
                }
            }).RequireAuthorization(policy => policy.RequireRole("Admin"));

            //EDIT UNIT ALAT
            g.MapPut("/{id}", async (UnitAlatService service, ClaimsPrincipal user, uint id, UnitAlatUpdateRequest request) =>
            {
                try
                {
                    await service.UpdateUnitAlat(id, request, UserAuth.GetId(user));
                    return Results.Ok();
                }
                catch (Exception ex)
                {
                    var problem = Problem.Fault(ex);
                    return Results.Problem(title: problem.Title, statusCode: problem.StatusCode, detail: problem.Detail);
                }
            }).RequireAuthorization(policy => policy.RequireRole("Admin"));

            //MENGHAPUS UNIT ALAT
            g.MapDelete("/{id}", async (UnitAlatService service, ClaimsPrincipal user, uint id) =>
            {
                try
                {
                    await service.DeleteUnitAlat(id, UserAuth.GetId(user));
                    return Results.Ok();
                }
                catch (Exception ex)
                {
                    var problem = Problem.Fault(ex);
                    return Results.Problem(title: problem.Title, statusCode: problem.StatusCode, detail: problem.Detail);
                }
            }).RequireAuthorization(policy => policy.RequireRole("Admin"));

            //GET ALL UNIT ALAT
            g.MapGet("/", async (UnitAlatService service, int? page, int? pageSize, uint? idAlat, uint? idStatusKetersediaan) =>
            {
                try
                {
                    var units = await service.GetAllUnitAlat(page ?? 1, pageSize ?? 20, idAlat, idStatusKetersediaan);
                    return Results.Ok(units);
                }
                catch (Exception ex)
                {
                    var problem = Problem.Fault(ex);
                    return Results.Problem(title: problem.Title, statusCode: problem.StatusCode, detail: problem.Detail);
                }
            }).RequireAuthorization();

            //GET UNIT ALAT BY ID
            g.MapGet("/{id}", async (UnitAlatService service, uint id) =>
            {
                try
                {
                    var unit = await service.GetUnitAlatById(id);
                    return Results.Ok(unit);
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
