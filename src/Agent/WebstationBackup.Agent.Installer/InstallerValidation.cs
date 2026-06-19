using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace WebstationBackup.Agent.Installer;

internal static class InstallerValidation
{
    private static readonly Regex IdentifierRegex = new("^[a-zA-Z0-9][a-zA-Z0-9._-]{1,127}$", RegexOptions.Compiled);
    private static readonly Regex BucketRegex = new("^[a-z0-9][a-z0-9.-]{1,61}[a-z0-9]$", RegexOptions.Compiled);
    private static readonly Regex RegionRegex = new("^[a-z]{2}-[a-z]+-[0-9]$", RegexOptions.Compiled);

    public static IReadOnlyList<string> Validate(InstallerSettingsModel model, PackageLayout layout, string settingsOutputPath)
    {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(layout);

        var errors = new List<string>();

        if (!Directory.Exists(layout.PackageRoot))
        {
            errors.Add("A pasta do pacote nao existe.");
        }

        if (!File.Exists(layout.InstallScriptPath))
        {
            errors.Add("O pacote selecionado nao contem Install-Agent.ps1.");
        }

        if (!File.Exists(layout.ServiceExecutablePath))
        {
            errors.Add("O pacote selecionado nao contem o executavel do servico.");
        }

        if (!File.Exists(layout.RulesPath))
        {
            errors.Add("O pacote selecionado nao contem project.rules.json.");
        }

        if (string.IsNullOrWhiteSpace(model.CustomerId) || !IdentifierRegex.IsMatch(model.CustomerId.Trim()))
        {
            errors.Add("CustomerId invalido. Use letras, numeros, ponto, underline ou hifen.");
        }

        if (string.IsNullOrWhiteSpace(model.HostId) || !IdentifierRegex.IsMatch(model.HostId.Trim()))
        {
            errors.Add("HostId invalido. Use letras, numeros, ponto, underline ou hifen.");
        }

        if ((string.IsNullOrWhiteSpace(model.AgentToken) || model.AgentToken.Trim().Length < 8) &&
            string.IsNullOrWhiteSpace(model.AgentTokenDpapiProtected) &&
            string.IsNullOrWhiteSpace(model.AgentTokenCredentialTargetName))
        {
            errors.Add("Token do cliente invalido. Informe um token valido do painel.");
        }

        if (string.IsNullOrWhiteSpace(model.ControlPlaneBaseUrl) ||
            !Uri.TryCreate(model.ControlPlaneBaseUrl.Trim(), UriKind.Absolute, out var controlPlaneUri) ||
            (controlPlaneUri.Scheme != Uri.UriSchemeHttp && controlPlaneUri.Scheme != Uri.UriSchemeHttps))
        {
            errors.Add("ControlPlaneBaseUrl invalido. Informe uma URL absoluta HTTP ou HTTPS.");
        }

        var anyAwsDestinationValue =
            !string.IsNullOrWhiteSpace(model.AwsRegion) ||
            !string.IsNullOrWhiteSpace(model.S3BucketName) ||
            !string.IsNullOrWhiteSpace(model.S3KeyPrefix);

        var anyAwsCredentialValue =
            !string.IsNullOrWhiteSpace(model.AwsCredentialDpapiProtected) ||
            !string.IsNullOrWhiteSpace(model.AwsCredentialTargetName);

        if (anyAwsDestinationValue)
        {
            if (string.IsNullOrWhiteSpace(model.AwsRegion) || !RegionRegex.IsMatch(model.AwsRegion.Trim()))
            {
                errors.Add("AwsRegion invalida. Exemplo aceito: us-east-1.");
            }

            if (string.IsNullOrWhiteSpace(model.S3BucketName) || !BucketRegex.IsMatch(model.S3BucketName.Trim()))
            {
                errors.Add("S3BucketName invalido. Use o formato padrao de buckets AWS.");
            }

            if (string.IsNullOrWhiteSpace(model.S3KeyPrefix))
            {
                errors.Add("S3KeyPrefix nao pode ficar vazio.");
            }
        }

        if (anyAwsCredentialValue &&
            string.IsNullOrWhiteSpace(model.AwsCredentialDpapiProtected) &&
            string.IsNullOrWhiteSpace(model.AwsCredentialTargetName))
        {
            errors.Add("Informe uma credencial AWS local protegida ou um AwsCredentialTargetName.");
        }

        if (string.IsNullOrWhiteSpace(settingsOutputPath))
        {
            errors.Add("Defina o caminho de saida do agent.settings.json.");
        }
        else
        {
            try
            {
                var outputDirectory = Path.GetDirectoryName(Path.GetFullPath(settingsOutputPath));
                if (string.IsNullOrWhiteSpace(outputDirectory))
                {
                    errors.Add("O caminho de saida do agent.settings.json e invalido.");
                }
            }
            catch (Exception ex)
            {
                errors.Add($"Falha ao validar o caminho de saida do settings: {ex.Message}");
            }
        }

        return errors;
    }

    public static string[] ParsePathList(string value)
    {
        return value
            .Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries)
            .Select(item => item.Trim())
            .Where(item => !string.IsNullOrWhiteSpace(item))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }
}
