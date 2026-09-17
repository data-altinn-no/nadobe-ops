using System.Runtime.CompilerServices;
using Azure.Core;
using Azure.Security.KeyVault.Certificates;
using Azure.Security.KeyVault.Secrets;
using Microsoft.Extensions.Options;

namespace Nadobe.Ops.Functions.KeyVault;

public sealed class AzureKeyVaultInventory(
    TokenCredential credential,
    IOptions<KeyVaultExpiryOptions> options) : IKeyVaultInventory
{
    public async IAsyncEnumerable<KeyVaultItem> EnumerateAsync(
        string vaultName,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var vaultUri = options.Value.BuildVaultUri(vaultName);

        var secretClient = new SecretClient(vaultUri, credential);
        await foreach (var secret in secretClient.GetPropertiesOfSecretsAsync(cancellationToken))
        {
            // Managed secrets are the private-key material behind a certificate; the certificate
            // itself is enumerated below, so reporting both would duplicate every finding.
            if (secret.Managed)
            {
                continue;
            }

            yield return new KeyVaultItem(
                vaultName,
                secret.Name,
                KeyVaultItemKind.Secret,
                secret.ExpiresOn,
                secret.Enabled ?? true);
        }

        var certificateClient = new CertificateClient(vaultUri, credential);
        await foreach (var certificate in certificateClient.GetPropertiesOfCertificatesAsync(cancellationToken: cancellationToken))
        {
            yield return new KeyVaultItem(
                vaultName,
                certificate.Name,
                KeyVaultItemKind.Certificate,
                certificate.ExpiresOn,
                certificate.Enabled ?? true);
        }
    }
}
