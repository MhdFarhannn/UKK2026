using UKK.Models;
using MySqlConnector;

namespace UKK.Services
{
    public static class Problem
    {
        public static (string Title, int StatusCode, string Detail) Fault(Exception exception)
        {
            if (exception is AppException appException)
            {
                return (Title(appException.StatusCode), appException.StatusCode, appException.Message);
            }

            if (exception is BadHttpRequestException or InvalidDataException)
            {
                return (Title(400), 400, "The request body is invalid.");
            }

            if (exception is OverflowException)
            {
                return (Title(400), 400, "A numeric value exceeds the supported range.");
            }

            if (exception is MySqlException mySqlException)
            {
                var (status, detail) = mySqlException.Number switch
                {
                    1062 => (409, "Data already exists."),
                    1048 or 1364 or 1406 or 1264 or 3819 => (400, "The submitted data violates the database contract."),
                    1452 => (400, "A referenced record does not exist."),
                    1451 => (409, "The record is still referenced by other data."),
                    _ => (500, "A database error occurred.")
                };

                return (Title(status), status, detail);
            }

            return ("Internal Server Error", 500, "An unexpected error occurred.");
        }

        public static string Title(int statusCode)
        {
            return statusCode switch
            {
                400 => "Bad Request",
                401 => "Unauthorized",
                403 => "Forbidden",
                404 => "Not Found",
                409 => "Conflict",
                429 => "Too Many Requests",
                503 => "Service Unavailable",
                _ => "Internal Server Error"
            };
        }
    }
}
