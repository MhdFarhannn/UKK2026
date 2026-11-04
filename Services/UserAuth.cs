using System.Security.Claims;
using UKK.Models;

namespace UKK.Services
{
    public static class UserAuth
    {
        public static uint GetId(ClaimsPrincipal user)
        {
            var claim = user.FindFirstValue(ClaimTypes.NameIdentifier);

            if (!uint.TryParse(claim, out var id))
            {
                throw new AppException(401, "Invalid token");
            }

            return id;
        }

        public static string GetRole(ClaimsPrincipal user)
        {
            var role = user.FindFirstValue(ClaimTypes.Role);

            if (role is not ("Admin" or "Petugas" or "Peminjam"))
            {
                throw new AppException(401, "Invalid token");
            }

            return role;
        }
    }
}
