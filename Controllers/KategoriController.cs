using System.Security.Claims;
using UKK.Services;
using UKK.Models;

namespace UKK.Controllers
{
    public static class KategoriController
    {
        public static void MapKategori(this WebApplication app)
        {
            var g = app.MapGroup("api/v1/kategori").WithTags("Kategori");

            //MENAMBAHKAN KATEGORI
            g.MapPost("/", async (KategoriService service, ClaimsPrincipal user, KategoriRequest request) =>
            {
                try
                {
                    await service.CreateKategori(request, UserAuth.GetId(user));
                    return Results.Ok();
                }
                catch (Exception ex)
                {
                    var problem = Problem.Fault(ex);
                    return Results.Problem(title: problem.Title, statusCode: problem.StatusCode, detail: problem.Detail);
                }
            }).RequireAuthorization(policy => policy.RequireRole("Admin"));

            //EDIT KATEGORI
            g.MapPut("/{id}", async (KategoriService service, ClaimsPrincipal user, uint id, KategoriRequest request) =>
            {
                try
                {
                    await service.UpdateKategori(id, request, UserAuth.GetId(user));
                    return Results.Ok();
                }
                catch (Exception ex)
                {
                    var problem = Problem.Fault(ex);
                    return Results.Problem(title: problem.Title, statusCode: problem.StatusCode, detail: problem.Detail);
                }
            }).RequireAuthorization(policy => policy.RequireRole("Admin"));

            //MENGHAPUS KATEGORI
            g.MapDelete("/{id}", async (KategoriService service, ClaimsPrincipal user, uint id) =>
            {
                try
                {
                    await service.DeleteKategori(id, UserAuth.GetId(user));
                    return Results.Ok();
                }
                catch (Exception ex)
                {
                    var problem = Problem.Fault(ex);
                    return Results.Problem(title: problem.Title, statusCode: problem.StatusCode, detail: problem.Detail);
                }
            }).RequireAuthorization(policy => policy.RequireRole("Admin"));

            //GET ALL KATEGORI
            g.MapGet("/", async (KategoriService service) =>
            {
                try
                {
                    var kategoris = await service.GetAllKategori();
                    return Results.Ok(kategoris);
                }
                catch (Exception ex)
                {
                    var problem = Problem.Fault(ex);
                    return Results.Problem(title: problem.Title, statusCode: problem.StatusCode, detail: problem.Detail);
                }
            }).RequireAuthorization();

            //GET KATEGORI BY ID
            g.MapGet("/{id}", async (KategoriService service, uint id) =>
            {
                try
                {
                    var kategori = await service.GetKategoriById(id);
                    return Results.Ok(kategori);
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
