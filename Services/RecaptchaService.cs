using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Options;
using UKK.Models;

namespace UKK.Services;

public sealed class RecaptchaService
{
    private readonly HttpClient httpClient;
    private readonly RecaptchaOptions options;

    public RecaptchaService(HttpClient client, IOptions<RecaptchaOptions> recaptchaOptions)
    {
        httpClient = client;
        options = recaptchaOptions.Value;
    }

    public CaptchaConfigResponse GetPublicConfig()
    {
        return new CaptchaConfigResponse { SiteKey = options.SiteKey };
    }

    public async Task<bool> VerifyAsync(
        string? token,
        string? remoteIp,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(token) || token.Length > 4096)
        {
            return false;
        }

        var form = new Dictionary<string, string>
        {
            ["secret"] = options.SecretKey,
            ["response"] = token
        };

        if (!string.IsNullOrWhiteSpace(remoteIp))
        {
            form["remoteip"] = remoteIp;
        }

        try
        {
            using var content = new FormUrlEncodedContent(form);
            using var response = await httpClient.PostAsync(options.VerificationEndpoint, content, cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                throw new AppException(503, "Captcha verification service is unavailable");
            }

            var result = await response.Content.ReadFromJsonAsync<RecaptchaVerificationResponse>(cancellationToken);

            if (result is null)
            {
                throw new AppException(503, "Captcha verification service returned an invalid response");
            }

            if (!result.Success)
            {
                return false;
            }

            return options.AllowedHostnames.Length == 0
                || result.Hostname is not null
                && options.AllowedHostnames.Contains(result.Hostname, StringComparer.OrdinalIgnoreCase);
        }
        catch (AppException)
        {
            throw;
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new AppException(503, "Captcha verification service timed out");
        }
        catch (HttpRequestException)
        {
            throw new AppException(503, "Captcha verification service is unavailable");
        }
        catch (JsonException)
        {
            throw new AppException(503, "Captcha verification service returned an invalid response");
        }
    }

    private sealed class RecaptchaVerificationResponse
    {
        public bool Success { get; init; }
        public string? Hostname { get; init; }

        [JsonPropertyName("error-codes")]
        public string[] ErrorCodes { get; init; } = [];
    }
}
