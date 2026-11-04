using System.Security.Claims;
using UKK.Services;
using UKK.Models;

namespace UKK.Controllers
{
    public static class PeminjamanController
    {
        public static void MapPeminjaman(this WebApplication app)
        {
            var g = app.MapGroup("api/v1/peminjaman").WithTags("Peminjaman");

            //MENGAMBIL SEMUA PEMINJAMAN
            g.MapGet("/", async (PeminjamanService service, int? page, int? pageSize, string? status, uint? idPeminjam, DateOnly? from, DateOnly? to) =>
            {
                try
                {
                    var loans = await service.GetAllPeminjaman(page ?? 1, pageSize ?? 20, status, idPeminjam, from, to);
                    return Results.Ok(loans);
                }
                catch (Exception ex)
                {
                    var problem = Problem.Fault(ex);
                    return Results.Problem(title: problem.Title, statusCode: problem.StatusCode, detail: problem.Detail);
                }
            }).RequireAuthorization(policy => policy.RequireRole("Admin", "Petugas"));

            //PEMINJAMAN MILIK PEMINJAM YANG LOGIN
            g.MapGet("/mine", async (PeminjamanService service, ClaimsPrincipal user, int? page, int? pageSize, string? status) =>
            {
                try
                {
                    var loans = await service.GetMyPeminjaman(UserAuth.GetId(user), page ?? 1, pageSize ?? 20, status);
                    return Results.Ok(loans);
                }
                catch (Exception ex)
                {
                    var problem = Problem.Fault(ex);
                    return Results.Problem(title: problem.Title, statusCode: problem.StatusCode, detail: problem.Detail);
                }
            }).RequireAuthorization(policy => policy.RequireRole("Peminjam"));

            //MONITOR PENGEMBALIAN (JATUH TEMPO / TERLAMBAT)
            g.MapGet("/monitor", async (PeminjamanService service, int? page, int? pageSize, string? status) =>
            {
                try
                {
                    var loans = await service.GetMonitorPeminjaman(page ?? 1, pageSize ?? 20, status);
                    return Results.Ok(loans);
                }
                catch (Exception ex)
                {
                    var problem = Problem.Fault(ex);
                    return Results.Problem(title: problem.Title, statusCode: problem.StatusCode, detail: problem.Detail);
                }
            }).RequireAuthorization(policy => policy.RequireRole("Admin", "Petugas"));

            //GET PEMINJAMAN BY ID
            g.MapGet("/{id}", async (PeminjamanService service, ClaimsPrincipal user, uint id) =>
            {
                try
                {
                    var loan = await service.GetPeminjamanById(id, UserAuth.GetId(user), UserAuth.GetRole(user));
                    return Results.Ok(loan);
                }
                catch (Exception ex)
                {
                    var problem = Problem.Fault(ex);
                    return Results.Problem(title: problem.Title, statusCode: problem.StatusCode, detail: problem.Detail);
                }
            }).RequireAuthorization();

            //MENGAJUKAN PEMINJAMAN
            g.MapPost("/", async (PeminjamanService service, ClaimsPrincipal user, PeminjamanRequest request) =>
            {
                try
                {
                    await service.CreatePeminjaman(request, UserAuth.GetId(user));
                    return Results.Ok();
                }
                catch (Exception ex)
                {
                    var problem = Problem.Fault(ex);
                    return Results.Problem(title: problem.Title, statusCode: problem.StatusCode, detail: problem.Detail);
                }
            }).RequireAuthorization(policy => policy.RequireRole("Peminjam"));

            //EDIT PEMINJAMAN (HANYA STATUS diajukan)
            g.MapPut("/{id}", async (PeminjamanService service, ClaimsPrincipal user, uint id, PeminjamanRequest request) =>
            {
                try
                {
                    await service.UpdatePeminjaman(id, request, UserAuth.GetId(user));
                    return Results.Ok();
                }
                catch (Exception ex)
                {
                    var problem = Problem.Fault(ex);
                    return Results.Problem(title: problem.Title, statusCode: problem.StatusCode, detail: problem.Detail);
                }
            }).RequireAuthorization(policy => policy.RequireRole("Admin"));

            //MENGHAPUS PEMINJAMAN (HANYA STATUS diajukan / ditolak)
            g.MapDelete("/{id}", async (PeminjamanService service, ClaimsPrincipal user, uint id) =>
            {
                try
                {
                    await service.DeletePeminjaman(id, UserAuth.GetId(user));
                    return Results.Ok();
                }
                catch (Exception ex)
                {
                    var problem = Problem.Fault(ex);
                    return Results.Problem(title: problem.Title, statusCode: problem.StatusCode, detail: problem.Detail);
                }
            }).RequireAuthorization(policy => policy.RequireRole("Admin"));

            //MENYETUJUI PEMINJAMAN (diajukan -> disetujui)
            g.MapPatch("/{id}/approve", async (PeminjamanService service, ClaimsPrincipal user, uint id) =>
            {
                try
                {
                    await service.ApprovePeminjaman(id, UserAuth.GetId(user));
                    return Results.Ok();
                }
                catch (Exception ex)
                {
                    var problem = Problem.Fault(ex);
                    return Results.Problem(title: problem.Title, statusCode: problem.StatusCode, detail: problem.Detail);
                }
            }).RequireAuthorization(policy => policy.RequireRole("Admin", "Petugas"));

            //MENOLAK PEMINJAMAN (diajukan -> ditolak)
            g.MapPatch("/{id}/reject", async (PeminjamanService service, ClaimsPrincipal user, uint id) =>
            {
                try
                {
                    await service.RejectPeminjaman(id, UserAuth.GetId(user));
                    return Results.Ok();
                }
                catch (Exception ex)
                {
                    var problem = Problem.Fault(ex);
                    return Results.Problem(title: problem.Title, statusCode: problem.StatusCode, detail: problem.Detail);
                }
            }).RequireAuthorization(policy => policy.RequireRole("Admin", "Petugas"));

            //MENYERAHKAN ALAT (disetujui -> dipinjam)
            g.MapPatch("/{id}/borrow", async (PeminjamanService service, ClaimsPrincipal user, uint id) =>
            {
                try
                {
                    await service.BorrowPeminjaman(id, UserAuth.GetId(user));
                    return Results.Ok();
                }
                catch (Exception ex)
                {
                    var problem = Problem.Fault(ex);
                    return Results.Problem(title: problem.Title, statusCode: problem.StatusCode, detail: problem.Detail);
                }
            }).RequireAuthorization(policy => policy.RequireRole("Admin", "Petugas"));
        }
    }
}
