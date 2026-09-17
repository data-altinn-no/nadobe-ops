using System.Runtime.CompilerServices;
using Nadobe.Ops.Functions.KeyVault;

namespace Nadobe.Ops.Functions.Tests;

internal sealed class FakeKeyVaultInventory : IKeyVaultInventory
{
    private readonly Dictionary<string, IReadOnlyList<KeyVaultItem>> items = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, Exception> failures = new(StringComparer.OrdinalIgnoreCase);

    public List<string> EnumeratedVaults { get; } = [];

    public FakeKeyVaultInventory WithVault(string vaultName, params KeyVaultItem[] vaultItems)
    {
        items[vaultName] = vaultItems;
        return this;
    }

    public FakeKeyVaultInventory WithFailingVault(string vaultName, Exception exception)
    {
        failures[vaultName] = exception;
        return this;
    }

    public async IAsyncEnumerable<KeyVaultItem> EnumerateAsync(
        string vaultName,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        EnumeratedVaults.Add(vaultName);

        if (failures.TryGetValue(vaultName, out var exception))
        {
            throw exception;
        }

        foreach (var item in items.GetValueOrDefault(vaultName, []))
        {
            cancellationToken.ThrowIfCancellationRequested();
            await Task.Yield();
            yield return item;
        }
    }
}
