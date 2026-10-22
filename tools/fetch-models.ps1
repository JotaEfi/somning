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
function Test-Model($Path, $Model) {
    return (Test-Path -LiteralPath $Path -PathType Leaf) -and
        (Get-Item -LiteralPath $Path).Length -eq $Model.Bytes -and
        (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash -eq $Model.Hash
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
    foreach ($model in $models) {
        if ($model.Name -notin $selected) { continue }
        $target = Join-Path $ModelsDir $model.Name
        if (Test-Model $target $model) { Write-Host "OK (verified) $($model.Name)"; continue }
        # A previously damaged target is never retained as if it were verified.
        if (Test-Path -LiteralPath $target) { Remove-Item -LiteralPath $target -Force }
        $part = $target + '.part'
        try {
            $cached = $null
            if ($CacheDir) { $cached = Join-Path $CacheDir $model.Name }
            if ($cached -and (Test-Path -LiteralPath $cached -PathType Leaf)) {
                Copy-Item -LiteralPath $cached -Destination $part -Force
            } else {
                Invoke-WebRequest -Uri $model.Url -OutFile $part -UseBasicParsing
            }
            if (-not (Test-Model $part $model)) { throw "hash mismatch: $($model.Name)" }
            Move-Item -LiteralPath $part -Destination $target -Force
            Write-Host "OK (verified) $($model.Name)"
        } finally {
            if (Test-Path -LiteralPath $part) { Remove-Item -LiteralPath $part -Force }
        }
    }
} catch { Write-Error $_ -ErrorAction Continue; exit 1 }
exit 0

