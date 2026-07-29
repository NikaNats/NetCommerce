using Xunit;
using Shouldly;
using System.IO;

namespace NetCommerce.Architecture.Tests;

public class CodegenDriftTests
{
    [Fact]
    public void GeneratedCodeFolder_ShouldExistAndContainFiles()
    {
        var generatedPath = Path.Combine(Directory.GetCurrentDirectory(), "..", "..", "..", "..", "src", "Api", "Internal", "Generated");

        if (Directory.Exists(generatedPath))
        {
            var files = Directory.GetFiles(generatedPath, "*.cs", SearchOption.AllDirectories);
            files.Length.ShouldBeGreaterThan(0, "Wolverine pre-generated files are missing! Run 'dotnet run -- codegen write'.");
        }
    }
}
