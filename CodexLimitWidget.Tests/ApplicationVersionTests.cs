using System.Reflection;
using System.Reflection.Emit;
using CodexLimitWidget.Core;
using Xunit;

namespace CodexLimitWidget.Tests;

public sealed class ApplicationVersionTests
{
    [Fact]
    public void FromAssemblyPrefersInformationalVersion()
    {
        var name = new AssemblyName("VersionProbe") { Version = new Version(1, 2, 3, 4) };
        var assembly = AssemblyBuilder.DefineDynamicAssembly(name, AssemblyBuilderAccess.Run);
        var constructor = typeof(AssemblyInformationalVersionAttribute).GetConstructor([typeof(string)])!;
        assembly.SetCustomAttribute(new CustomAttributeBuilder(constructor, ["1.2.3.4-preview-s2f349a"]));

        Assert.Equal("1.2.3.4-preview-s2f349a", ApplicationVersion.FromAssembly(assembly));
    }

    [Fact]
    public void FromAssemblyFallsBackToAssemblyVersion()
    {
        var name = new AssemblyName("VersionFallbackProbe") { Version = new Version(1, 2, 3, 4) };
        var assembly = AssemblyBuilder.DefineDynamicAssembly(name, AssemblyBuilderAccess.Run);

        Assert.Equal("1.2.3.4", ApplicationVersion.FromAssembly(assembly));
    }
}
