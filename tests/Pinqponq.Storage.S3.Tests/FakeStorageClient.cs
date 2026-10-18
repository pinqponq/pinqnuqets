using System.Net;
using Amazon;
using Amazon.Runtime;
using Amazon.S3;
using Amazon.S3.Model;
using Xunit;

namespace Pinqponq.Storage.S3.Tests;

// Overrides only the calls the package makes. A DispatchProxy over IAmazonS3 cannot be built on
// the .NET 8 runtime (TypeLoadException), so the fake derives from the real client instead.
// GetPreSignedURLAsync is not virtual, so it is re-implemented on the interface the package calls.
public sealed class FakeStorageClient : AmazonS3Client, IAmazonS3
{
    public const string PRESIGNED_URL = "https://storage.example/presigned";

    public FakeStorageClient()
        : base(new BasicAWSCredentials(StorageOptionsFactory.ACCESS_KEY, StorageOptionsFactory.SECRET_KEY), RegionEndpoint.GetBySystemName(StorageOptionsFactory.REGION))
    {
    }

    public List<AmazonWebServiceRequest> Requests { get; } = [];

    public GetObjectMetadataResponse MetadataResponse { get; set; } = new();

    public byte[] ObjectContent { get; set; } = [];

    public Exception? Failure { get; set; }

    public T GetSingleRequest<T>() where T : AmazonWebServiceRequest => Assert.IsType<T>(Assert.Single(Requests));

    public static AmazonS3Exception CreateStatusError(HttpStatusCode statusCode) =>
        new($"Storage answered {statusCode}.") { StatusCode = statusCode };

    public override Task<PutObjectResponse> PutObjectAsync(PutObjectRequest request, CancellationToken cancellationToken = default) =>
        Respond(request, () => new PutObjectResponse());

    Task<string> IAmazonS3.GetPreSignedURLAsync(GetPreSignedUrlRequest request) =>
        Respond(request, () => PRESIGNED_URL);

    public override Task<GetObjectMetadataResponse> GetObjectMetadataAsync(GetObjectMetadataRequest request, CancellationToken cancellationToken = default) =>
        Respond(request, () => MetadataResponse);

    public override Task<GetObjectResponse> GetObjectAsync(GetObjectRequest request, CancellationToken cancellationToken = default) =>
        Respond(request, () => new GetObjectResponse { ResponseStream = new MemoryStream(ObjectContent) });

    public override Task<CopyObjectResponse> CopyObjectAsync(CopyObjectRequest request, CancellationToken cancellationToken = default) =>
        Respond(request, () => new CopyObjectResponse());

    public override Task<DeleteObjectResponse> DeleteObjectAsync(DeleteObjectRequest request, CancellationToken cancellationToken = default) =>
        Respond(request, () => new DeleteObjectResponse());

    public override Task<HeadBucketResponse> HeadBucketAsync(HeadBucketRequest request, CancellationToken cancellationToken = default) =>
        Respond(request, () => new HeadBucketResponse());

    private Task<T> Respond<T>(AmazonWebServiceRequest request, Func<T> response)
    {
        Requests.Add(request);
        return Failure is null ? Task.FromResult(response()) : Task.FromException<T>(Failure);
    }
}
