using System.Security.Claims;
using UKK.Services;
using UKK.Models;

namespace UKK.Controllers
{
    public static class StatusBarangController
    {
        public static void MapStatusBarang(this WebApplication app)
        {
            var g = app.MapGroup("api/v1/status-barang").WithTags("Status Barang");

            //MENAMBAHKAN STATUS BARANG
            g.MapPost("/", async (StatusBarangServices service, StatusBarangRequest statusBarang) =>
            {
                try
                {
                    await service.CreateStatusBarang(statusBarang);
                    return Results.Ok();
                }
                catch (Exception ex)
                {
                    var problem = Problem.Fault(ex);
                    return Results.Problem(title: problem.Title, statusCode: problem.StatusCode, detail: problem.Detail);
                }
            }).RequireAuthorization(policy => policy.RequireRole("Admin"));

            //MENGHAPUS STATUS BARANG
            g.MapDelete("/{id}", async (StatusBarangServices service, uint id) =>
            {
                try
                {
                    await service.DeleteStatusBarang(id);
                    return Results.Ok();
                }
                catch (Exception ex)
                {
                    var problem = Problem.Fault(ex);
                    return Results.Problem(title: problem.Title, statusCode: problem.StatusCode, detail: problem.Detail);
                }
            }).RequireAuthorization(policy => policy.RequireRole("Admin"));

            //EDIT STATUS BARANG
            g.MapPatch("/{id}", async (StatusBarangServices service, uint id, StatusBarangRequest statusBarang) =>
            {
                try
                {
                    await service.PatchStatusBarang(id, statusBarang);
                    return Results.Ok();
                }
                catch (Exception ex)
                {
                    var problem = Problem.Fault(ex);
                    return Results.Problem(title: problem.Title, statusCode: problem.StatusCode, detail: problem.Detail);
                }
            }).RequireAuthorization(policy => policy.RequireRole("Admin"));

            //GET ALL STATUS BARANG
            g.MapGet("/", async (StatusBarangServices service) =>
            {
                try
                {
                    var statusBarangs = await service.GetAllStatusBarang();
                    return Results.Ok(statusBarangs);
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
