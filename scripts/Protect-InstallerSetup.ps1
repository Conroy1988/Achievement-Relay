[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [string] $OutputFile
)

$ErrorActionPreference = 'Stop'
$apiKey = $env:ACHIEVEMENT_RELAY_OPENXBL_KEY
$webhookUrl = $env:ACHIEVEMENT_RELAY_DISCORD_WEBHOOK

try {
    if ([string]::IsNullOrWhiteSpace($webhookUrl)) {
        throw 'The Discord setting was not available to the secure handoff process.'
    }

    Add-Type -AssemblyName System.Security

    function Protect-CurrentUserValue {
        param(
            [Parameter(Mandatory)]
            [string] $Value,

            [Parameter(Mandatory)]
            [string] $Entropy
        )

        $plainBytes = [Text.Encoding]::UTF8.GetBytes($Value.Trim())
        try {
            $entropyBytes = [Text.Encoding]::UTF8.GetBytes($Entropy)
            $protectedBytes = [Security.Cryptography.ProtectedData]::Protect(
                $plainBytes,
                $entropyBytes,
                [Security.Cryptography.DataProtectionScope]::CurrentUser)
            return [Convert]::ToBase64String($protectedBytes)
        }
        finally {
            [Array]::Clear($plainBytes, 0, $plainBytes.Length)
        }
    }

    $protectedApiKey = if ([string]::IsNullOrWhiteSpace($apiKey)) {
        ''
    }
    else {
        Protect-CurrentUserValue `
            -Value $apiKey `
            -Entropy 'AchievementRelay.OpenXBL.v1'
    }

    $payload = [ordered]@{
        schemaVersion = 1
        protectedOpenXblApiKey = $protectedApiKey
        protectedWebhookUrl = Protect-CurrentUserValue `
            -Value $webhookUrl `
            -Entropy 'AchievementRelay.Webhook.v1'
    }

    $resolvedOutput = [IO.Path]::GetFullPath($OutputFile)
    $outputDirectory = Split-Path -Parent $resolvedOutput
    New-Item -ItemType Directory -Path $outputDirectory -Force | Out-Null
    [IO.File]::WriteAllText(
        $resolvedOutput,
        ($payload | ConvertTo-Json -Compress),
        [Text.UTF8Encoding]::new($false))
}
finally {
    Remove-Item Env:ACHIEVEMENT_RELAY_OPENXBL_KEY -ErrorAction SilentlyContinue
    Remove-Item Env:ACHIEVEMENT_RELAY_DISCORD_WEBHOOK -ErrorAction SilentlyContinue
    $apiKey = $null
    $webhookUrl = $null
}
