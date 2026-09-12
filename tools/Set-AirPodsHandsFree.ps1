param(
    [ValidateSet('Disable', 'Enable')]
    [string]$Mode = 'Disable',
    [Parameter(Mandatory = $true)]
    [ValidatePattern('^[0-9A-Fa-f]{12}$')]
    [string]$BluetoothAddress
)

$ErrorActionPreference = 'Stop'
$BluetoothAddress = $BluetoothAddress.ToUpperInvariant()
$hfpUuid = '0000111E-0000-1000-8000-00805F9B34FB'
$targets = Get-PnpDevice | Where-Object {
    $_.InstanceId -like "BTHENUM\{$hfpUuid}*" -and
    $_.InstanceId -like "*$BluetoothAddress*"
}

if (-not $targets) {
    throw "No se encontró el perfil Hands-Free de los AirPods $BluetoothAddress."
}

foreach ($target in $targets) {
    if ($Mode -eq 'Disable' -and $target.Status -ne 'Unknown') {
        Disable-PnpDevice -InstanceId $target.InstanceId -Confirm:$false
    }
    elseif ($Mode -eq 'Enable' -and $target.Status -ne 'OK') {
        Enable-PnpDevice -InstanceId $target.InstanceId -Confirm:$false
    }
}

Get-PnpDevice | Where-Object {
    $_.InstanceId -like "BTHENUM\{$hfpUuid}*" -and
    $_.InstanceId -like "*$BluetoothAddress*"
} | Select-Object Status, FriendlyName, InstanceId
