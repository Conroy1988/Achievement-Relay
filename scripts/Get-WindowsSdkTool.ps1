[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [ValidateSet('makeappx.exe', 'signtool.exe')]
    [string] $Name
)

$kitsRoot = Join-Path ${env:ProgramFiles(x86)} 'Windows Kits\10\bin'
if (-not (Test-Path -LiteralPath $kitsRoot)) {
    throw 'Windows SDK tools were not found. Install the Windows 10/11 SDK, then try again.'
}

$tool = Get-ChildItem -LiteralPath $kitsRoot -Directory |
    Sort-Object Name -Descending |
    ForEach-Object { Join-Path $_.FullName "x64\$Name" } |
    Where-Object { Test-Path -LiteralPath $_ } |
    Select-Object -First 1

if (-not $tool) {
    throw "$Name was not found beneath $kitsRoot. Install the Windows SDK Desktop C++ tools."
}

$tool
