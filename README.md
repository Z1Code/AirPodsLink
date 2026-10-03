# AirPodsLink

<p align="center">
  <img src="social/airpodslink-twitter-launch.png" alt="AirPodsLink — AirPods en Windows, conexión más rápida" width="880">
</p>

**Conecta tus AirPods más rápido en Windows.** Detecta los anuncios Bluetooth LE,
acelera el perfil de audio estéreo (A2DP) y muestra la batería en la barra de tareas.
Sin controladores, sin reemplazar el stack Bluetooth de Windows.

AirPodsLink es una aplicación local para Windows que detecta anuncios Bluetooth LE
de AirPods y acelera la conexión del perfil de audio estéreo (A2DP). Vive en la
bandeja del sistema, muestra el estado y la batería aproximada, y permite solicitar
una conexión inmediata sin instalar controladores.

## Funciones

- Detecta anuncios Apple Proximity BLE (`0x004C`, tipo `0x07`).
- Reconoce modelos conocidos de AirPods.
- Muestra batería aproximada de ambos auriculares y del estuche.
- Dibuja en la barra de tareas el porcentaje del auricular más bajo, con color
  según el nivel y un punto cuando están cargando. Muestra `--` en gris si no se
  reciben anuncios durante 30 segundos.
- Reduce lecturas de AirPods ajenos usando proximidad y continuidad de señal.
  RSSI es una heurística: los anuncios rotatorios de Apple no exponen una
  identidad estable que permita garantizar la propiedad del dispositivo.
- Detecta cambios de oído, estuche y tapa cuando el modelo los anuncia.
- Solicita una reconexión A2DP al controlador Bluetooth de Windows mediante
  `KSPROPERTY_ONESHOT_RECONNECT`.
- Después de activar A2DP, selecciona el ID exacto del endpoint que WASAPI abrió
  como salida predeterminada. Basta con eso para FXSound: adopta el dispositivo
  predeterminado como su salida física y luego se restituye él como
  predeterminado, sin necesidad de controlarlo desde fuera.
- Abre brevemente el endpoint con WASAPI compartido para reducir la demora de
  activación.
- Incluye **Conectar ahora**, un interruptor de aceleración y un registro local.

La batería BLE se publica en pasos de 10 %: `70` significa 70-79 %, y `<10` significa
menos del 10 % —no vacío; un auricular que lo reporta puede seguir sonando—. Un valor en
gris es la última lectura conocida, no la actual (por ejemplo, el estuche cerrado o un
auricular apagado); la ventana de estado indica su antigüedad. El estuche suele anunciarse
sólo cuando está abierto y al menos un auricular está dentro.

## Requisitos

- Windows 10 versión 2004 (build 19041) o Windows 11.
- Adaptador Bluetooth LE y AirPods previamente emparejados desde Windows.
- [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0) para compilar.

## Replicar la instalación en otro PC

Abre PowerShell y ejecuta:

```powershell
git clone https://github.com/Z1Code/AirPodsLink.git
cd AirPodsLink
dotnet restore
dotnet test -c Release
dotnet publish src/AirPodsLink.App -c Release -r win-x64 --self-contained false -o artifacts/win-x64
./artifacts/win-x64/AirPodsLink.exe
```

Si el equipo no tiene Git, descarga el ZIP desde GitHub, extráelo y ejecuta los
comandos desde su carpeta.

El ejecutable publicado requiere el runtime de escritorio de .NET 8. Para generar
una copia que no lo requiera:

```powershell
dotnet publish src/AirPodsLink.App -c Release -r win-x64 --self-contained true -o artifacts/win-x64-self-contained
```

## Uso

Ejecuta `AirPodsLink.exe`; el programa quedará en la bandeja. Para realizar una
prueba única del acelerador desde el código fuente:

```powershell
dotnet run --project src/AirPodsLink.App -- --connect-once
```

El registro se guarda en `%LOCALAPPDATA%\AirPodsLink\connection-events.jsonl`.

## Modo opcional sólo estéreo

Windows puede cambiar al perfil manos libres (HFP) cuando una aplicación usa el
micrófono Bluetooth, reduciendo la calidad del audio. El script incluido permite
deshabilitar únicamente ese perfil para unos AirPods concretos, conservando A2DP y
AVRCP. Requiere PowerShell como administrador.

Primero localiza la dirección del dispositivo en su `InstanceId`:

```powershell
Get-PnpDevice | Where-Object FriendlyName -Like '*AirPods*' |
  Select-Object Status, FriendlyName, InstanceId
```

Copia los 12 caracteres hexadecimales de la dirección, sin `:` ni `-`:

```powershell
# Ejemplo: reemplaza la dirección por la de tus AirPods
./tools/Set-AirPodsHandsFree.ps1 -Mode Disable -BluetoothAddress A1B2C3D4E5F6

# Restaurar micrófono/HFP
./tools/Set-AirPodsHandsFree.ps1 -Mode Enable -BluetoothAddress A1B2C3D4E5F6
```

## Privacidad y seguridad

AirPodsLink funciona localmente: no requiere cuenta, servidor ni conexión a
Internet. No instala drivers, no desactiva Secure Boot y no activa el modo de prueba
de Windows. Excluye de la aceleración los endpoints `Hands-Free`, `Headset` y los
identificados como micrófono.

## Limitaciones

- No replica el cambio automático de dispositivo de iCloud/MagicPairing.
- El formato BLE de Apple no es una API pública y podría cambiar.
- El comportamiento depende del adaptador y del controlador Bluetooth del equipo.
- ANC, modo Adaptativo, gestos e IMU no forman parte de esta versión.

## Desarrollo

```powershell
dotnet build AirPodsLink.sln -c Release
dotnet test AirPodsLink.sln -c Release
```

La lógica BLE está separada en `AirPodsLink.Core` y cuenta con pruebas unitarias.
Los detalles técnicos y fuentes están en los documentos de investigación incluidos.
Las atribuciones están en [THIRD_PARTY.md](THIRD_PARTY.md).

## Licencia

Distribuido bajo la licencia MIT. Consulta [LICENSE](LICENSE).
