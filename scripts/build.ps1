param(
    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Release'
)

$ErrorActionPreference = 'Stop'
$workspaceRoot = Split-Path -Parent $PSScriptRoot
Push-Location -LiteralPath $workspaceRoot
try {
    dotnet restore Voiceroid2XIV.sln --locked-mode
    if ($LASTEXITCODE -ne 0) { throw 'パッケージの復元に失敗しました。' }

    dotnet build Voiceroid2XIV.sln -c $Configuration --no-restore --nologo
    if ($LASTEXITCODE -ne 0) { throw 'ビルドに失敗しました。' }

    dotnet test tests/Voiceroid2XIV.Tests/Voiceroid2XIV.Tests.csproj -c $Configuration --no-build --no-restore --nologo
    if ($LASTEXITCODE -ne 0) { throw 'テストに失敗しました。' }

    if ($Configuration -eq 'Release') {
        $outputRoot = Join-Path $workspaceRoot 'src/Voiceroid2XIV/bin/Release'
        $manifest = Get-Content -LiteralPath (Join-Path $outputRoot 'Voiceroid2XIV.json') -Raw | ConvertFrom-Json
        $artifactRoot = Join-Path $workspaceRoot 'artifacts'
        New-Item -ItemType Directory -Path $artifactRoot -Force | Out-Null
        $archivePath = Join-Path $artifactRoot "Voiceroid2XIV-$($manifest.AssemblyVersion).zip"
        $packagePath = Join-Path $outputRoot 'Voiceroid2XIV/latest.zip'
        # 標準Packagerは画像をZIPの外へ出すため、開発用配置でも使えるよう同梱する。
        $package = [IO.Compression.ZipFile]::Open($packagePath, [IO.Compression.ZipArchiveMode]::Update)
        try {
            $existingIcon = $package.GetEntry('images/icon.png')
            if ($null -ne $existingIcon) { $existingIcon.Delete() }
            [IO.Compression.ZipFileExtensions]::CreateEntryFromFile(
                $package, (Join-Path $outputRoot 'images/icon.png'), 'images/icon.png',
                [IO.Compression.CompressionLevel]::Optimal) | Out-Null
        }
        finally {
            $package.Dispose()
        }
        Copy-Item -LiteralPath $packagePath -Destination $archivePath -Force
        Write-Output "配布用ZIP: $archivePath"

        # 公開済みZIPとバージョンを対応させる。リポジトリ直下への反映はRelease公開後に行う。
        $repositoryEntry = Get-Content -LiteralPath (Join-Path $outputRoot 'Voiceroid2XIV.json') -Raw | ConvertFrom-Json -AsHashtable
        $releaseTag = "v$($manifest.AssemblyVersion)"
        $downloadUrl = "$($manifest.RepoUrl)/releases/download/$releaseTag/$(Split-Path -Leaf $archivePath)"
        $repositoryEntry['DownloadLinkInstall'] = $downloadUrl
        $repositoryEntry['DownloadLinkUpdate'] = $downloadUrl
        $repositoryEntry['DownloadLinkTesting'] = $downloadUrl
        $repositoryEntry['IsHide'] = $false
        $repositoryEntry['IsTestingExclusive'] = $false
        $repositoryEntry['LastUpdate'] = [DateTimeOffset]::UtcNow.ToUnixTimeSeconds()
        $repositoryPath = Join-Path $artifactRoot 'pluginmaster.json'
        $repositoryJson = ConvertTo-Json -InputObject @($repositoryEntry) -Depth 20
        [IO.File]::WriteAllText($repositoryPath, $repositoryJson + [Environment]::NewLine,
            [Text.UTF8Encoding]::new($false))
        Write-Output "Dalamud用リポジトリJSON: $repositoryPath"
    }
}
finally {
    Pop-Location
}
