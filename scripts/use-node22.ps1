$nodeHome = Join-Path $env:LOCALAPPDATA 'Programs\node-v22.23.4'
$nodeExe = Join-Path $nodeHome 'node.exe'

if (-not (Test-Path -LiteralPath $nodeExe)) {
    throw "Node.js 22.23.4 is not installed at $nodeHome"
}

$env:PATH = "$nodeHome;$env:PATH"
