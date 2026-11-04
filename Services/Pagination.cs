namespace UKK.Services
{
    public static class Pagination
    {
        public const int MaxPageSize = 100;

        public static (int Page, int PageSize) Normalize(int page, int pageSize)
        {
            if (page < 1)
            {
                page = 1;
            }

            if (pageSize < 1)
            {
                pageSize = 20;
            }

            if (pageSize > MaxPageSize)
            {
                pageSize = MaxPageSize;
            }

            return (page, pageSize);
        }

        public static string? Search(string? search)
        {
            return string.IsNullOrWhiteSpace(search) ? null : search.Trim();
        }

        public static long Offset(int page, int pageSize)
        {
            return checked((long)(page - 1) * pageSize);
        }
    }
}
