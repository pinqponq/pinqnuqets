using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Pinqponq.Identity.EntityFrameworkCore.Tests;

public sealed class EntityFrameworkCoreVersionTests
{
#if NET10_0
    private const int ExpectedMajorVersion = 10;
#elif NET9_0
    private const int ExpectedMajorVersion = 9;
#else
    private const int ExpectedMajorVersion = 8;
#endif

    [Fact]
    public void Store_tests_run_on_the_entity_framework_core_major_of_their_target_framework()
    {
        var loadedVersion = typeof(DbContext).Assembly.GetName().Version;

        Assert.Equal(ExpectedMajorVersion, loadedVersion?.Major);
    }

    [Fact]
    public void Store_is_compiled_against_the_entity_framework_core_major_of_its_target_framework()
    {
        var entityFrameworkCoreReference = typeof(EntityFrameworkRefreshTokenStoreOptions<>).Assembly
            .GetReferencedAssemblies()
            .SingleOrDefault(assembly => assembly.Name == "Microsoft.EntityFrameworkCore");

        Assert.NotNull(entityFrameworkCoreReference);
        Assert.Equal(ExpectedMajorVersion, entityFrameworkCoreReference.Version?.Major);
    }
}
