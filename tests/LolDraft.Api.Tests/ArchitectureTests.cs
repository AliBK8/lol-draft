using System.Reflection;

namespace LolDraft.Api.Tests;

// Application sadece Domain ve Engine'i bilir; altyapı portların arkasında kalır (DESIGN.md §8.2)
public sealed class ArchitectureTests
{
    private static readonly string[] Forbidden = ["LolDraft.Infrastructure", "LolDraft.Api", "Microsoft.AspNetCore", "Microsoft.EntityFrameworkCore"];

    [Fact]
    public void Application_yasak_bagimlilik_kullanmiyor()
    {
        var references = Assembly.Load("LolDraft.Application").GetReferencedAssemblies().Select(a => a.Name!);

        Assert.DoesNotContain(references, name => Forbidden.Any(name.StartsWith));
    }
}
