using System.Security.Claims;
using System.Globalization;
using UKK.Services;
using UKK.Models;

namespace UKK.Controllers
{
    public static class AlatController
    {
        public static void MapAlat(this WebApplication app)
        {
            var g = app.MapGroup("api/v1/alat").WithTags("Alat");

            //MENAMBAHKAN ALAT (multipart/form-data: idKategori, nama, deskripsi, hargaSewa, gambar)
            g.MapPost("/", async (AlatService service, ClaimsPrincipal user, HttpRequest http) =>
            {
                try
                {
                    if (!http.HasFormContentType)
                    {
                        throw new AppException(400, "Content-Type must be multipart/form-data");
                    }

                    var form = await http.ReadFormAsync();
                    var request = ReadForm(form);
                    await service.CreateAlat(request, UserAuth.GetId(user), form.Files.GetFile("gambar"));
                    return Results.Ok();
                }
                catch (Exception ex)
                {
                    var problem = Problem.Fault(ex);
                    return Results.Problem(title: problem.Title, statusCode: problem.StatusCode, detail: problem.Detail);
                }
            }).Accepts<AlatFormRequest>("multipart/form-data")
              .RequireAuthorization(policy => policy.RequireRole("Admin"));

            //EDIT ALAT (multipart/form-data, gambar optional: leave empty to keep the old image)
            g.MapPut("/{id}", async (AlatService service, ClaimsPrincipal user, uint id, HttpRequest http) =>
            {
                try
                {
                    if (!http.HasFormContentType)
                    {
                        throw new AppException(400, "Content-Type must be multipart/form-data");
                    }

                    var form = await http.ReadFormAsync();
                    var request = ReadForm(form);
                    await service.UpdateAlat(id, request, UserAuth.GetId(user), form.Files.GetFile("gambar"));
                    return Results.Ok();
                }
                catch (Exception ex)
                {
                    var problem = Problem.Fault(ex);
                    return Results.Problem(title: problem.Title, statusCode: problem.StatusCode, detail: problem.Detail);
                }
            }).Accepts<AlatFormRequest>("multipart/form-data")
              .RequireAuthorization(policy => policy.RequireRole("Admin"));

            //MENGHAPUS ALAT
            g.MapDelete("/{id}", async (AlatService service, ClaimsPrincipal user, uint id) =>
            {
                try
                {
                    await service.DeleteAlat(id, UserAuth.GetId(user));
                    return Results.Ok();
                }
                catch (Exception ex)
                {
                    var problem = Problem.Fault(ex);
                    return Results.Problem(title: problem.Title, statusCode: problem.StatusCode, detail: problem.Detail);
                }
            }).RequireAuthorization(policy => policy.RequireRole("Admin"));

            //GET ALL ALAT (INCLUDES AVAILABLE STOCK)
            g.MapGet("/", async (AlatService service, int? page, int? pageSize, string? search, uint? idKategori) =>
            {
                try
                {
                    var alats = await service.GetAllAlat(page ?? 1, pageSize ?? 20, search, idKategori);
                    return Results.Ok(alats);
                }
                catch (Exception ex)
                {
                    var problem = Problem.Fault(ex);
                    return Results.Problem(title: problem.Title, statusCode: problem.StatusCode, detail: problem.Detail);
                }
            }).RequireAuthorization();

            //GET ALAT BY ID
            g.MapGet("/{id}", async (AlatService service, uint id) =>
            {
                try
                {
                    var alat = await service.GetAlatById(id);
                    return Results.Ok(alat);
                }
                catch (Exception ex)
                {
                    var problem = Problem.Fault(ex);
                    return Results.Problem(title: problem.Title, statusCode: problem.StatusCode, detail: problem.Detail);
                }
            }).RequireAuthorization();
        }

        //READ multipart/form-data FIELDS INTO AlatRequest
        private static AlatRequest ReadForm(IFormCollection form)
        {
            if (!decimal.TryParse(
                    form["hargaSewa"],
                    NumberStyles.Number,
                    CultureInfo.InvariantCulture,
                    out var hargaSewa))
            {
                throw new AppException(400, "hargaSewa must be a valid decimal using '.' as the separator");
            }

            return new AlatRequest
            {
                IdKategori = uint.TryParse(form["idKategori"], out var idKategori) ? idKategori : 0,
                Nama = form["nama"].ToString(),
                Deskripsi = form["deskripsi"].ToString(),
                HargaSewa = hargaSewa,
                ImagePath = form["imagePath"].ToString()
            };
        }
    }
}
