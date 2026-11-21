using System.Reflection;
using NetArchTest.Rules;

namespace MorningInk.Architecture.Tests;

public class LayerTests
{
    private const string Domain = "MorningInk.Domain";
    private const string Application = "MorningInk.Application";
    private const string Infrastructure = "MorningInk.Infrastructure";
    private const string Web = "MorningInk.Web";

    private static readonly string[] Frameworks =
    [
        "Microsoft.EntityFrameworkCore",
        "Microsoft.AspNetCore",
        "System.Net.Http",
    ];

    public static TheoryData<string, string> ForbiddenDependencies()
    {
        var data = new TheoryData<string, string>();
        foreach (var forbidden in new[] { Application, Infrastructure, Web }.Concat(Frameworks))
        {
            data.Add(Domain, forbidden);
        }

        foreach (var forbidden in new[] { Infrastructure, Web }.Concat(Frameworks))
        {
            data.Add(Application, forbidden);
        }

        data.Add(Infrastructure, Web);
        return data;
    }

    [Theory]
    [MemberData(nameof(ForbiddenDependencies))]
    public void Layer_does_not_depend_on(string layer, string forbidden)
    {
        var result = Types.InAssembly(Assembly.Load(layer))
            .ShouldNot()
            .HaveDependencyOn(forbidden)
            .GetResult();

        Assert.True(result.IsSuccessful, $"{layer} depends on {forbidden}: {string.Join(", ", result.FailingTypeNames ?? [])}");
    }
}
