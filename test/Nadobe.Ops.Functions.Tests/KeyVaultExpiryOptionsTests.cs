using Nadobe.Ops.Functions.KeyVault;

namespace Nadobe.Ops.Functions.Tests;

public class KeyVaultExpiryOptionsTests
{
    [Theory]
    [InlineData("", 0)]
    [InlineData("one", 1)]
    [InlineData("one,two", 2)]
    [InlineData(" one , two ", 2)]
    [InlineData("one,,two,", 2)]
    [InlineData("one,ONE", 1)]
    public void GetVaultNames_SplitsTrimsAndDeduplicates(string setting, int expectedCount)
    {
        var options = new KeyVaultExpiryOptions { KeyVaultNames = setting };

        var names = options.GetVaultNames();

        Assert.Equal(expectedCount, names.Count);
        Assert.DoesNotContain(names, name => name != name.Trim());
    }

    [Fact]
    public void BuildVaultUri_ExpandsBareName()
    {
        var options = new KeyVaultExpiryOptions();

        Assert.Equal(new Uri("https://my-vault.vault.azure.net/"), options.BuildVaultUri("my-vault"));
    }

    [Fact]
    public void BuildVaultUri_PassesThroughFullUri()
    {
        var options = new KeyVaultExpiryOptions();

        Assert.Equal(
            new Uri("https://my-vault.vault.usgovcloudapi.net/"),
            options.BuildVaultUri("https://my-vault.vault.usgovcloudapi.net/"));
    }
}
