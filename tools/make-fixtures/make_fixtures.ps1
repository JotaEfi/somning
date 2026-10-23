param([string]$EdgeTts, [switch]$UseSapi)

$ErrorActionPreference = 'Stop'
$toolsRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$repoRoot = (Resolve-Path (Join-Path $toolsRoot '../..')).Path
$fixturesRoot = Join-Path $repoRoot 'tests/fixtures'
$speechRoot = Join-Path $fixturesRoot 'speech'
$nonspeechRoot = Join-Path $fixturesRoot 'nonspeech'
$workingRoot = Join-Path $toolsRoot ('.working-' + [guid]::NewGuid().ToString('N'))
$ffmpeg = (Get-Command ffmpeg -ErrorAction Stop).Source
$ffprobe = Join-Path (Split-Path $ffmpeg) 'ffprobe.exe'

function Invoke-Checked {
    param([string]$Executable, [string[]]$Arguments)
    & $Executable @Arguments
    $exitCode = $LASTEXITCODE
    if ($exitCode -ne 0) {
        throw "External command failed with exit code ${exitCode}: $Executable"
    }
}

function Invoke-CheckedOutput {
    param([string]$Executable, [string[]]$Arguments)
    $output = & $Executable @Arguments
    $exitCode = $LASTEXITCODE
    if ($exitCode -ne 0) {
        throw "External command failed with exit code ${exitCode}: $Executable"
    }
    return ($output -join '').Trim()
}

function Convert-ToPcmWav {
    param([string]$InputPath, [string]$OutputPath, [string[]]$ExtraArguments = @())
    $arguments = @('-hide_banner', '-loglevel', 'error', '-y', '-i', $InputPath) + $ExtraArguments +
        @('-ar', '16000', '-ac', '1', '-c:a', 'pcm_s16le', $OutputPath)
    Invoke-Checked $ffmpeg $arguments
}

function Measure-WavSeconds {
    param([string]$Path)
    $value = Invoke-CheckedOutput $ffprobe @('-v', 'error', '-show_entries', 'format=duration',
        '-of', 'default=noprint_wrappers=1:nokey=1', $Path)
    return [double]::Parse($value, [Globalization.CultureInfo]::InvariantCulture)
}

function Invoke-SpeechSynthesis {
    param([string]$Text, [string]$Voice, [string]$OutputPath)
    if ($script:edgeExecutable) {
        Invoke-Checked $script:edgeExecutable @('--no-progress', '--rate=-10%', '--voice', $Voice, '--text', $Text,
            '--write-media', $OutputPath)
        return
    }

    Add-Type -AssemblyName System.Speech
    $synthesizer = New-Object System.Speech.Synthesis.SpeechSynthesizer
    try {
        $available = @($synthesizer.GetInstalledVoices() | ForEach-Object { $_.VoiceInfo.Name })
        if ($available -notcontains 'Microsoft Maria Desktop') {
            throw "Required SAPI fallback voice 'Microsoft Maria Desktop' is unavailable."
        }
        $synthesizer.SelectVoice('Microsoft Maria Desktop')
        $synthesizer.Rate = -2
        $synthesizer.SetOutputToWaveFile($OutputPath)
        $synthesizer.Speak($Text)
    }
    finally { $synthesizer.Dispose() }
}

function Add-SpeechFixture {
    param([string]$Name, [string]$Text, [string]$Voice, [bool]$Clean, [string]$Outcome = 'text',
        [string]$Reason, [string]$Note = '', [bool]$KeepSpeechSamples = $false, [string]$SynthesisText = '')
    $synthesisPath = Join-Path $workingRoot ($Name + $(if ($script:edgeExecutable) { '.mp3' } else { '.wav' }))
    $outputPath = Join-Path $speechRoot ($Name + '.wav')
    $spokenText = if ([string]::IsNullOrWhiteSpace($SynthesisText)) { $Text } else { $SynthesisText }
    Invoke-SpeechSynthesis $spokenText $Voice $synthesisPath
    if ($script:edgeExecutable) { Convert-ToPcmWav $synthesisPath $outputPath }
    else { Convert-ToPcmWav $synthesisPath $outputPath }

    if ($Name -eq 'longo-numeros') {
        $duration = Measure-WavSeconds $outputPath
        if ($duration -lt 14 -or $duration -gt 16) {
            $speed = $duration / 15.0
            $adjusted = Join-Path $workingRoot 'longo-numeros-adjusted.wav'
            Convert-ToPcmWav $outputPath $adjusted @('-filter:a', ('atempo=' + [string]::Format(
                [Globalization.CultureInfo]::InvariantCulture, '{0:0.000}', $speed)))
            Move-Item -LiteralPath $adjusted -Destination $outputPath -Force
        }
    }

    $script:manifest.Add([ordered]@{
        file = "speech/$Name.wav"; kind = 'speech'; expected = $Text; clean = $Clean
        expect_outcome = $Outcome; expect_reason = $Reason
        voice = $(if ($script:edgeExecutable) { $Voice } else { 'Microsoft Maria Desktop' })
        generator = $script:generatorName; note = $Note
    })

    if ($KeepSpeechSamples) { $script:speechSamples[$Name] = $outputPath }
}

try {
    $null = New-Item -ItemType Directory -Path $workingRoot
    $null = New-Item -ItemType Directory -Path $speechRoot -Force
    $null = New-Item -ItemType Directory -Path $nonspeechRoot -Force

    if ($EdgeTts) {
        if (-not (Test-Path -LiteralPath $EdgeTts -PathType Leaf)) { throw "Edge TTS executable not found: $EdgeTts" }
        $script:edgeExecutable = (Resolve-Path -LiteralPath $EdgeTts).Path
    }
    else {
        foreach ($candidate in @((Join-Path $toolsRoot '.venv/Scripts/edge-tts.exe'),
            'C:/somning-spike/.venv/Scripts/edge-tts.exe')) {
            if (Test-Path -LiteralPath $candidate -PathType Leaf) {
                $script:edgeExecutable = (Resolve-Path -LiteralPath $candidate).Path
                break
            }
        }
    }
    if ($UseSapi) { $script:edgeExecutable = $null }
    $script:generatorName = if ($script:edgeExecutable) { 'edge-tts' } else { 'sapi' }
    $script:cmdVoice = if ($script:edgeExecutable) { 'pt-BR-FranciscaNeural' } else { 'Microsoft Maria Desktop' }
    $script:technicalVoice = if ($script:edgeExecutable) { 'pt-BR-FranciscaNeural' } else { 'Microsoft Maria Desktop' }
    $script:manifest = [System.Collections.Generic.List[object]]::new()
    $script:speechSamples = @{}

    $cmdText = 'Abra o arquivo de configuração e salve as alterações.'
    $longText = 'Na reunião de terça-feira, a Mariana e o Rafael apresentaram o relatório do terceiro trimestre. As vendas cresceram 12% em relação ao ano passado, e o novo escritório em Curitiba abre no dia 15 de março, com 40 pessoas na equipe.'
    $technicalText = 'Preciso fazer o deploy da API no servidor de homologação e depois rodar os testes de integração.'
    $simText = 'Sim.'
    $greetingText = 'E aí, tudo bem?'
    $thanksText = 'Obrigado.'

    Add-SpeechFixture 'cmd-curto' $cmdText 'pt-BR-FranciscaNeural' $true -KeepSpeechSamples $true
    Add-SpeechFixture 'longo-numeros' $longText 'pt-BR-AntonioNeural' $true -KeepSpeechSamples $true
    Add-SpeechFixture 'tecnico' $technicalText 'pt-BR-FranciscaNeural' $true -KeepSpeechSamples $true
    Add-SpeechFixture 'sim' $simText 'pt-BR-FranciscaNeural' $true
    Add-SpeechFixture 'e-ai' $greetingText 'pt-BR-AntonioNeural' $true
    Add-SpeechFixture 'obrigado' $thanksText 'pt-BR-FranciscaNeural' $false 'empty' 'blocklist' `
        "D-12 whole-output blocklist tradeoff: a dictation consisting only of 'Obrigado.' is discarded."

    $quietOutput = Join-Path $speechRoot 'quiet20.wav'
    Convert-ToPcmWav $script:speechSamples['cmd-curto'] $quietOutput @('-filter:a', 'volume=-20dB')
    $script:manifest.Add([ordered]@{
        file = 'speech/quiet20.wav'; kind = 'speech'; expected = $cmdText; clean = $false
        expect_outcome = 'text'; expect_reason = $null; voice = $script:cmdVoice
        generator = "$($script:generatorName)+ffmpeg"; note = 'cmd-curto reduced by 20 dB.'
    })

    $technicalSeconds = Measure-WavSeconds $script:speechSamples['tecnico']
    $noiseSource = "anoisesrc=color=pink:amplitude=0.05:sample_rate=16000:duration=$technicalSeconds"
    $noisyOutput = Join-Path $speechRoot 'noisy.wav'
    Invoke-Checked $ffmpeg @('-hide_banner', '-loglevel', 'error', '-y', '-i', $script:speechSamples['tecnico'],
        '-f', 'lavfi', '-i', $noiseSource, '-filter_complex',
        '[0:a]aresample=16000,pan=mono|c0=c0[speech];[1:a]pan=mono|c0=c0[noise];[speech][noise]amix=inputs=2:duration=first:normalize=0[mix]',
        '-map', '[mix]', '-ar', '16000', '-ac', '1', '-c:a', 'pcm_s16le', $noisyOutput)
    $script:manifest.Add([ordered]@{
        file = 'speech/noisy.wav'; kind = 'speech'; expected = $technicalText; clean = $false
        expect_outcome = 'text'; expect_reason = $null; voice = $script:technicalVoice
        generator = "$($script:generatorName)+ffmpeg"; note = 'Technical speech mixed with pink noise at amplitude 0.05.'
    })

    $tapOutput = Join-Path $nonspeechRoot 'tap.wav'
    Convert-ToPcmWav $script:speechSamples['cmd-curto'] $tapOutput @('-t', '0.2')
    $script:manifest.Add([ordered]@{
        file = 'nonspeech/tap.wav'; kind = 'tap'; expected = $null; clean = $false
        expect_outcome = 'empty'; expect_reason = 'too_short'; voice = $script:cmdVoice
        generator = 'ffmpeg'; note = 'First 0.2 seconds of cmd-curto.'
    })

    foreach ($fixture in @(
        @{ Name = 'silence'; Kind = 'silence'; Source = 'anullsrc=r=16000:cl=mono'; Duration = '3'; Reason = 'vad_no_speech' },
        @{ Name = 'noise'; Kind = 'noise'; Source = 'anoisesrc=color=pink:amplitude=0.05:sample_rate=16000:duration=3'; Duration = '3'; Reason = 'vad_no_speech' },
        @{ Name = 'tone'; Kind = 'tone'; Source = 'sine=frequency=440:sample_rate=16000:duration=2'; Duration = '2'; Reason = 'vad_no_speech' }
    )) {
        $outputPath = Join-Path $nonspeechRoot ($fixture.Name + '.wav')
        $arguments = @('-hide_banner', '-loglevel', 'error', '-y', '-f', 'lavfi', '-i', $fixture.Source,
            '-t', $fixture.Duration)
        if ($fixture.Kind -eq 'tone') { $arguments += @('-af', 'volume=0.1') }
        $arguments += @('-ar', '16000', '-ac', '1', '-c:a', 'pcm_s16le', $outputPath)
        Invoke-Checked $ffmpeg $arguments
        $script:manifest.Add([ordered]@{
            file = "nonspeech/$($fixture.Name).wav"; kind = $fixture.Kind; expected = $null; clean = $false
            expect_outcome = 'empty'; expect_reason = $fixture.Reason; voice = $null
            generator = 'ffmpeg'; note = "Generated $($fixture.Duration)-second $($fixture.Kind) audio."
        })
    }

    $tracerText = [IO.File]::ReadAllText((Join-Path $fixturesRoot 'tracer/hoje.txt'), [Text.Encoding]::UTF8).Trim()
    $script:manifest.Add([ordered]@{
        file = 'tracer/hoje.wav'; kind = 'speech'; expected = $tracerText; clean = $true
        expect_outcome = 'text'; expect_reason = $null; voice = 'pt-BR-FranciscaNeural'
        generator = 'existing-fixture'; note = 'Plan 01 CPU/Vulkan tracer sample.'
    })

    $manifestPath = Join-Path $fixturesRoot 'manifest.json'
    $json = ConvertTo-Json -InputObject @($script:manifest) -Depth 4
    foreach ($property in @('expected', 'expect_reason', 'voice')) {
        $json = [regex]::Replace($json, ('("' + $property + '"\s*:\s*)""'), '$1null')
    }
    Set-Content -LiteralPath $manifestPath -Value $json -Encoding UTF8

    $totalBytes = (Get-ChildItem -LiteralPath $fixturesRoot -Filter '*.wav' -Recurse -File |
        Measure-Object -Property Length -Sum).Sum
    if ($totalBytes -ge 5242880) { throw "Fixture set is too large: $totalBytes bytes (maximum 5 MiB)." }
    Write-Output "Generated $($script:manifest.Count) manifest entries with $totalBytes WAV bytes using $($script:generatorName)."
}
finally {
    if (Test-Path -LiteralPath $workingRoot) { Remove-Item -LiteralPath $workingRoot -Recurse -Force }
}
