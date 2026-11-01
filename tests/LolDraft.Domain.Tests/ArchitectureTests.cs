using System.Reflection;

namespace LolDraft.Domain.Tests;

// Domain başka katmanı, ASP.NET'i ya da EF'i bilmez (DESIGN.md §8.2)
public sealed class ArchitectureTests
{
    private static readonly string[] Forbidden = ["LolDraft.", "Microsoft.AspNetCore", "Microsoft.EntityFrameworkCore"];

    [Fact]
    public void Domain_yasak_bagimlilik_kullanmiyor()
    {
        var references = Assembly.Load("LolDraft.Domain").GetReferencedAssemblies().Select(a => a.Name!);

        Assert.DoesNotContain(references, name => Forbidden.Any(name.StartsWith));
    }
}
