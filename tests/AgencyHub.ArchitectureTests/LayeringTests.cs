using System.Reflection;
using AgencyHub.Infrastructure.Persistence;
using AgencyHub.SharedKernel.Domain;
using Microsoft.EntityFrameworkCore;
using NetArchTest.Rules;

namespace AgencyHub.ArchitectureTests;

/// <summary>
/// Layering and module-boundary rules. Modules are discovered by assembly name
/// (AgencyHub.Modules.X and AgencyHub.Modules.X.Contracts), so new modules are covered automatically.
/// </summary>
public sealed class LayeringTests
{
    private static readonly string[] InternalLayers = ["Domain", "Application", "Infrastructure", "Endpoints"];
    private static readonly string[] FrameworkNamespaces = ["Microsoft.EntityFrameworkCore", "Microsoft.AspNetCore", "AgencyHub.Infrastructure"];

    private static readonly Assembly SharedKernel = typeof(Entity).Assembly;
    private static readonly Assembly Infrastructure = typeof(TenantAwareDbContext).Assembly;

    private static readonly Assembly[] ProductionAssemblies = Directory
        .GetFiles(AppContext.BaseDirectory, "AgencyHub.*.dll")
        .Where(path => !Path.GetFileName(path).Contains("Tests", StringComparison.Ordinal))
        .Select(Assembly.LoadFrom)
        .ToArray();

    private static readonly Assembly[] ModuleAssemblies = ProductionAssemblies
        .Where(a => Name(a).StartsWith("AgencyHub.Modules.", StringComparison.Ordinal) && !Name(a).EndsWith(".Contracts", StringComparison.Ordinal))
        .ToArray();

    private static readonly Assembly[] ContractAssemblies = ProductionAssemblies
        .Where(a => Name(a).StartsWith("AgencyHub.Modules.", StringComparison.Ordinal) && Name(a).EndsWith(".Contracts", StringComparison.Ordinal))
        .ToArray();

    public static TheoryData<string> Modules => [.. ModuleAssemblies.Select(Name)];

    [Fact]
    public void All_expected_modules_are_discovered()
    {
        Assert.Equal(
            ["AgencyHub.Modules.Agency", "AgencyHub.Modules.Client", "AgencyHub.Modules.Identity", "AgencyHub.Modules.Project", "AgencyHub.Modules.Website"],
            ModuleAssemblies.Select(Name).Order(StringComparer.Ordinal));
    }

    [Fact]
    public void SharedKernel_has_no_framework_or_outer_layer_dependencies()
    {
        var result = Types.InAssembly(SharedKernel)
            .ShouldNot()
            .HaveDependencyOnAny("Microsoft.EntityFrameworkCore", "Microsoft.AspNetCore", "AgencyHub.Infrastructure", "AgencyHub.Api", "AgencyHub.Modules")
            .GetResult();

        Assert.True(result.IsSuccessful, Describe(result));
    }

    [Fact]
    public void Infrastructure_does_not_depend_on_AspNetCore_hosts_or_modules()
    {
        var result = Types.InAssembly(Infrastructure)
            .ShouldNot()
            .HaveDependencyOnAny("Microsoft.AspNetCore", "AgencyHub.Api", "AgencyHub.Worker", "AgencyHub.Modules")
            .GetResult();

        Assert.True(result.IsSuccessful, Describe(result));
    }

    [Fact]
    public void Every_DbContext_inherits_TenantAwareDbContext()
    {
        // A plain DbContext would silently skip tenant query filters and write guards.
        var result = Types.InAssemblies(ProductionAssemblies)
            .That().Inherit(typeof(DbContext))
            .And().AreNotAbstract()
            .Should().Inherit(typeof(TenantAwareDbContext))
            .GetResult();

        Assert.True(result.IsSuccessful, Describe(result));
    }

    [Fact]
    public void Contracts_have_no_framework_or_implementation_dependencies()
    {
        var forbidden = FrameworkNamespaces
            .Concat(ModuleAssemblies.SelectMany(ImplementationNamespaces))
            .ToArray();

        var result = Types.InAssemblies(ContractAssemblies).ShouldNot().HaveDependencyOnAny(forbidden).GetResult();

        Assert.True(result.IsSuccessful, Describe(result));
    }

    [Theory]
    [MemberData(nameof(Modules))]
    public void Other_assemblies_use_only_the_contracts_of_a_module(string module)
    {
        // Hosts compose modules and may reference the module assembly; other modules may not.
        var others = ProductionAssemblies
            .Where(a => Name(a) != module && !Name(a).StartsWith("AgencyHub.Api", StringComparison.Ordinal))
            .ToArray();

        var result = Types.InAssemblies(others)
            .ShouldNot()
            .HaveDependencyOnAny([.. ImplementationNamespaces(module)])
            .GetResult();

        Assert.True(result.IsSuccessful, Describe(result));
    }

    [Theory]
    [MemberData(nameof(Modules))]
    public void Module_domain_has_no_framework_dependencies(string module)
    {
        var result = Types.InAssembly(Assembly.Load(module))
            .That().ResideInNamespace($"{module}.Domain")
            .ShouldNot()
            .HaveDependencyOnAny("Microsoft.EntityFrameworkCore", "Microsoft.AspNetCore", "AgencyHub.Infrastructure")
            .GetResult();

        Assert.True(result.IsSuccessful, Describe(result));
    }

    [Theory]
    [MemberData(nameof(Modules))]
    public void Module_implementation_types_are_internal(string module)
    {
        // Only the composition root (XModule) and generated EF migrations may be public.
        var result = Types.InAssembly(Assembly.Load(module))
            .That().AreNotNested()
            .And().DoNotResideInNamespace($"{module}.Infrastructure.Migrations")
            .And().DoNotHaveNameEndingWith("Module")
            .Should().NotBePublic()
            .GetResult();

        Assert.True(result.IsSuccessful, Describe(result));
    }

    private static IEnumerable<string> ImplementationNamespaces(Assembly module) => ImplementationNamespaces(Name(module));

    private static IEnumerable<string> ImplementationNamespaces(string module) => InternalLayers.Select(layer => $"{module}.{layer}");

    private static string Name(Assembly assembly) => assembly.GetName().Name!;

    private static string Describe(NetArchTest.Rules.TestResult result) =>
        "Violating types: " + string.Join(", ", result.FailingTypeNames ?? []);
}
