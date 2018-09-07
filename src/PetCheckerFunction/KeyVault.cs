using Microsoft.Azure.KeyVault;
using Microsoft.Azure.KeyVault.Models;
using Microsoft.Azure.Services.AppAuthentication;
using Microsoft.Azure.WebJobs.Host;
using System;
using System.Threading.Tasks;

namespace PetCheckerFunction
{
    public class KeyVault
    {
        private KeyVaultClient _keyVaultClient;
        private TraceWriter _log;
        private readonly string _keyVaultUri;

        public KeyVault(TraceWriter log)
        {
            var azureServiceTokenProvider = new AzureServiceTokenProvider();
            _keyVaultClient = new KeyVaultClient(new KeyVaultClient.AuthenticationCallback(azureServiceTokenProvider.KeyVaultTokenCallback));
            _log = log;
            _keyVaultUri = Environment.GetEnvironmentVariable("KeyVaultUri");
            if (string.IsNullOrEmpty(_keyVaultUri))
            {
                log.Info(">>> Key Vault not configured and won't be used");
            }
        }

        public async Task<string> GetSecretValue(string secretName)
        {
            if (string.IsNullOrEmpty(_keyVaultUri))
            {
                return string.Empty;
            }
            try
            {
                _log.Info($">>> Retrieving secret: {secretName} from Azure KeyVault");

                var secretUri = $"{Environment.GetEnvironmentVariable("KeyVaultUri")}/Secrets/{secretName.Replace("_", "")}";
                return (await _keyVaultClient.GetSecretAsync(secretUri)).Value;
            }
            catch (KeyVaultErrorException kex)
            {
                _log.Error($"--- Error retrieving the secret { secretName }", kex);
                return string.Empty;
            }
        }
    }
}
