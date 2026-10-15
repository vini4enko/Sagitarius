using Amazon.S3;
using Amazon.S3.Model;
using Microsoft.Extensions.Options;

namespace PhotoOrganizer.Storage;

public class S3StorageService : IDisposable
{
    private readonly IAmazonS3 _s3Client;
    private readonly S3Options _options;

    public S3StorageService(IOptions<S3Options> options)
    {
        _options = options.Value;

        var config = new AmazonS3Config
        {
            ServiceURL = _options.Endpoint,
            ForcePathStyle = true,   // TimeWeb требует path-style
            AuthenticationRegion = _options.Region
        };

        _s3Client = new AmazonS3Client(
            _options.AccessKey,
            _options.SecretKey,
            config);
    }

    /// <summary>
    /// Загружает поток в S3. Возвращает ключ объекта.
    /// </summary>
    public async Task<string> UploadAsync(
        Stream content,
        string objectKey,
        string contentType,
        CancellationToken ct = default)
    {
        var request = new PutObjectRequest
        {
            BucketName = _options.BucketName,
            Key = objectKey,
            InputStream = content,
            ContentType = contentType,
            AutoCloseStream = false
        };

        await _s3Client.PutObjectAsync(request, ct);
        return objectKey;
    }

    /// <summary>
    /// Скачивает объект в поток.
    /// </summary>
    public async Task<Stream> DownloadAsync(string objectKey, CancellationToken ct = default)
    {
        var response = await _s3Client.GetObjectAsync(_options.BucketName, objectKey, ct);

        var ms = new MemoryStream();
        await response.ResponseStream.CopyToAsync(ms, ct);
        ms.Position = 0;
        return ms;
    }    /// <summary>
         /// Удаляет объект. Идемпотентен: удаление несуществующего объекта не ошибка.
         /// </summary>
    public async Task DeleteAsync(
        string objectKey,
        CancellationToken ct = default)
    {
        await _s3Client.DeleteObjectAsync(
            _options.BucketName, objectKey, ct);
    }

    public void Dispose()
    {
        _s3Client?.Dispose();
    }
}