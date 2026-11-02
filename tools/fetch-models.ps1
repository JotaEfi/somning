param(
    [string[]]$Only = @(),
    [string]$ModelsDir = (Join-Path $env:LOCALAPPDATA 'Somning\models'),
    [string]$CacheDir
)
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$base = 'https://huggingface.co/ggerganov/whisper.cpp/resolve/5359861c739e955e79d9a303bcbc70fb988958b1/'
$models = @(
    @{Name='ggml-large-v3-turbo.bin';Bytes=1624555275;Hash='1fc70f774d38eb169993ac391eea357ef47c88757ef72ee5943879b7e8e2bc69';Url=($base+'ggml-large-v3-turbo.bin')},
    @{Name='ggml-large-v3-turbo-q8_0.bin';Bytes=874188075;Hash='317eb69c11673c9de1e1f0d459b253999804ec71ac4c23c17ecf5fbe24e259a1';Url=($base+'ggml-large-v3-turbo-q8_0.bin')},
    @{Name='ggml-large-v3-turbo-q5_0.bin';Bytes=574041195;Hash='394221709cd5ad1f40c46e6031ca61bce88931e6e088c188294c6d5a55ffa7e2';Url=($base+'ggml-large-v3-turbo-q5_0.bin')},
    @{Name='ggml-silero-v6.2.0.bin';Bytes=885098;Hash='2aa269b785eeb53a82983a20501ddf7c1d9c48e33ab63a41391ac6c9f7fb6987';Url='https://huggingface.co/ggml-org/whisper-vad/resolve/9ffd54a1e1ee413ddf265af9913beaf518d1639b/ggml-silero-v6.2.0.bin'}
)
# Extra room on the drive for the .part file and the final move.
$DiskReserveBytes = 512MB

function Test-Model($Path, $Model) {
    return (Test-Path -LiteralPath $Path -PathType Leaf) -and
        (Get-Item -LiteralPath $Path).Length -eq $Model.Bytes -and
        (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash -eq $Model.Hash
}

function Test-PartialComplete($Path, $Model) {
    return (Test-Path -LiteralPath $Path -PathType Leaf) -and (Get-Item -LiteralPath $Path).Length -eq $Model.Bytes
}

function Get-ModelFile($Url, $Destination) {
    # curl.exe ships with Windows 10 1803 and later. -C - resumes a partial file after a dropped connection,
    # and --retry-all-errors keeps trying through transient network errors. The progress bar stays visible.
    $curl = Get-Command curl.exe -ErrorAction SilentlyContinue
    if ($curl) {
        & $curl.Source --fail --location --progress-bar --connect-timeout 30 --retry 8 --retry-delay 5 --retry-all-errors --continue-at - --output $Destination $Url
        if ($LASTEXITCODE -ne 0) { throw "download failed (curl exit $LASTEXITCODE)" }
        return
    }
    Invoke-WebRequest -Uri $Url -OutFile $Destination -UseBasicParsing
}

try {
    $selected = @($Only | ForEach-Object { $_.Split(',') } | ForEach-Object { $_.Trim() } | Where-Object { $_ })
    if ($selected.Count -eq 0) { $selected = @($models | ForEach-Object { $_.Name }) }
    foreach ($name in $selected) {
        if ($name -notin @($models | ForEach-Object { $_.Name })) {
            throw "Unknown model '$name'. Valid names: $($models.Name -join ', ')"
        }
    }
    New-Item -ItemType Directory -Force -Path $ModelsDir | Out-Null
    [Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12

    $pending = @($models | Where-Object { $_.Name -in $selected -and -not (Test-Model (Join-Path $ModelsDir $_.Name) $_) })
    $needed = ($pending | ForEach-Object { $_.Bytes } | Measure-Object -Sum).Sum
    if ($needed) {
        $drive = [IO.DriveInfo]::new([IO.Path]::GetPathRoot((Resolve-Path -LiteralPath $ModelsDir).Path))
        $required = [long]$needed + $DiskReserveBytes
        if ($drive.AvailableFreeSpace -lt $required) {
            throw ("Not enough free space on {0}: {1:N0} MB needed, {2:N0} MB available." -f $drive.Name, ($required / 1MB), ($drive.AvailableFreeSpace / 1MB))
        }
    }

    foreach ($model in $models) {
        if ($model.Name -notin $selected) { continue }
        $target = Join-Path $ModelsDir $model.Name
        if (Test-Model $target $model) { Write-Host "OK (verified) $($model.Name)"; continue }
        # A previously damaged target is never retained as if it were verified.
        if (Test-Path -LiteralPath $target) { Remove-Item -LiteralPath $target -Force }
        $part = $target + '.part'
        # A partial file larger than the model cannot be resumed; start that one again.
        if ((Test-Path -LiteralPath $part -PathType Leaf) -and (Get-Item -LiteralPath $part).Length -gt $model.Bytes) {
            Remove-Item -LiteralPath $part -Force
        }
        $cached = $null
        if ($CacheDir) { $cached = Join-Path $CacheDir $model.Name }
        if ($cached -and (Test-Path -LiteralPath $cached -PathType Leaf)) {
            Copy-Item -LiteralPath $cached -Destination $part -Force
        } elseif (-not (Test-PartialComplete $part $model)) {
            Write-Host "Downloading $($model.Name)..."
            # The .part file is kept on failure, so the next attempt resumes instead of starting over.
            Get-ModelFile $model.Url $part
        }
        if (-not (Test-Model $part $model)) {
            Remove-Item -LiteralPath $part -Force -ErrorAction SilentlyContinue
            throw "hash mismatch: $($model.Name)"
        }
        Move-Item -LiteralPath $part -Destination $target -Force
        Write-Host "OK (verified) $($model.Name)"
    }
} catch { Write-Error $_ -ErrorAction Continue; exit 1 }
exit 0
