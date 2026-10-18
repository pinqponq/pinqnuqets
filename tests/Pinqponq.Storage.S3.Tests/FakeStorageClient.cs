using System.Net;
using System.Reflection;
using Amazon.Runtime;
using Amazon.S3;
using Amazon.S3.Model;
using Xunit;

namespace Pinqponq.Storage.S3.Tests;

// IAmazonS3 has hundreds of members; a dispatch proxy answers only the calls the package makes.
public class FakeStorageClient : DispatchProxy
{
    public const string PRESIGNED_URL = "https://storage.example/presigned";

    public List<AmazonWebServiceRequest> Requests { get; } = [];

    public GetObjectMetadataResponse MetadataResponse { get; set; } = new();

    public byte[] ObjectContent { get; set; } = [];

    public Exception? Failure { get; set; }

    public T SingleRequest<T>() where T : AmazonWebServiceRequest => Assert.IsType<T>(Assert.Single(Requests));

    public static (IAmazonS3 Client, FakeStorageClient Fake) Create()
    {
        var client = Create<IAmazonS3, FakeStorageClient>();
        return (client, (FakeStorageClient)(object)client);
    }

    public static AmazonS3Exception StatusError(HttpStatusCode statusCode) =>
        new($"Storage answered {statusCode}.") { StatusCode = statusCode };

    protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
    {
        ArgumentNullException.ThrowIfNull(targetMethod);

        if (args is { Length: > 0 } && args[0] is AmazonWebServiceRequest request)
        {
            Requests.Add(request);
        }

        if (Failure is not null)
        {
            return FailedTask(targetMethod.ReturnType, Failure);
        }

        return targetMethod.Name switch
        {
            nameof(IAmazonS3.PutObjectAsync) => Task.FromResult(new PutObjectResponse()),
            nameof(IAmazonS3.GetPreSignedURLAsync) => Task.FromResult(PRESIGNED_URL),
            nameof(IAmazonS3.GetObjectMetadataAsync) => Task.FromResult(MetadataResponse),
            nameof(IAmazonS3.GetObjectAsync) => Task.FromResult(new GetObjectResponse { ResponseStream = new MemoryStream(ObjectContent) }),
            nameof(IAmazonS3.CopyObjectAsync) => Task.FromResult(new CopyObjectResponse()),
            nameof(IAmazonS3.DeleteObjectAsync) => Task.FromResult(new DeleteObjectResponse()),
            nameof(IAmazonS3.HeadBucketAsync) => Task.FromResult(new HeadBucketResponse()),
            _ => throw new NotSupportedException($"{targetMethod.Name} is not faked.")
        };
    }

    private static object FailedTask(Type taskType, Exception failure)
    {
        var resultType = taskType.GetGenericArguments().Single();
        var fromException = typeof(Task).GetMethods()
            .Single(method => method.Name == nameof(Task.FromException) && method.IsGenericMethod)
            .MakeGenericMethod(resultType);
        return fromException.Invoke(null, [failure])!;
    }
}
