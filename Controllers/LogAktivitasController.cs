using UKK.Services;

namespace UKK.Controllers
{
    public static class LogAktivitasController
    {
        public static void MapLogAktivitas(this WebApplication app)
        {
            var g = app.MapGroup("api/v1/log-aktivitas").WithTags("Log Aktivitas");

            //GET ALL LOG AKTIVITAS
            g.MapGet("/", async (LogAktivitasService service, int? page, int? pageSize, uint? idUser, DateOnly? from, DateOnly? to) =>
            {
                try
                {
                    var logs = await service.GetAllLogAktivitas(page ?? 1, pageSize ?? 20, idUser, from, to);
                    return Results.Ok(logs);
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
