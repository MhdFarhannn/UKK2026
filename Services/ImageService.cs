using UKK.Models;

namespace UKK.Services
{
    public class ImageService
    {
        private const long MaxSizeBytes = 2 * 1024 * 1024;

        private static readonly string[] AllowedExtensions = { ".jpg", ".jpeg", ".png", ".webp" };

        private static readonly string[] AllowedContentTypes =
        {
            "image/jpeg",
            "image/png",
            "image/webp"
        };

        private readonly string _folder;
        private readonly string _urlPrefix;

        public ImageService(IWebHostEnvironment environment, IConfiguration configuration)
        {
            var basePath = environment.WebRootPath ?? Path.Combine(environment.ContentRootPath, "wwwroot");

            _folder = Path.Combine(basePath, "uploads", "alat");
            _urlPrefix = configuration["Uploads:AlatUrl"] ?? "/uploads/alat";
        }

        public async Task<string?> SaveAlatImage(IFormFile? file)
        {
            if (file == null || file.Length == 0)
            {
                return null;
            }

            if (file.Length > MaxSizeBytes)
            {
                throw new AppException(400, "gambar must not exceed 2 MB");
            }

            var extension = Path.GetExtension(file.FileName).ToLowerInvariant();

            if (!AllowedExtensions.Contains(extension) || !AllowedContentTypes.Contains(file.ContentType.ToLowerInvariant()))
            {
                throw new AppException(400, "gambar must be a jpg, jpeg, png or webp file");
            }

            Directory.CreateDirectory(_folder);

            var fileName = $"{Guid.NewGuid():N}{extension}";
            var target = Path.Combine(_folder, fileName);

            using (var stream = File.Create(target))
            {
                await file.CopyToAsync(stream);
            }

            var imagePath = $"{_urlPrefix.TrimEnd('/')}/{fileName}";

            if (imagePath.Length > 255)
            {
                File.Delete(target);
                throw new InvalidOperationException("The configured upload path exceeds alat.image_path capacity.");
            }

            return imagePath;
        }

        public void Delete(string? imagePath)
        {
            if (string.IsNullOrWhiteSpace(imagePath) || !imagePath.StartsWith(_urlPrefix))
            {
                return;
            }

            var fileName = Path.GetFileName(imagePath);
            var target = Path.Combine(_folder, fileName);

            if (File.Exists(target))
            {
                File.Delete(target);
            }
        }
    }
}
