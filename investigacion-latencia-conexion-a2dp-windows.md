# Reducción del tiempo de conexión A2DP de AirPods en Windows

## Resultado principal

La demora de aproximadamente 30 segundos no corresponde al tiempo normal de despertar del adaptador ni al descubrimiento BLE. En este equipo los anuncios de los AirPods se reciben entre uno y dos segundos después de iniciar el escáner, pero Windows puede mantener el dispositivo Bluetooth base como “conectado” mientras el perfil A2DP y su endpoint de audio todavía no están disponibles o no son la salida predeterminada.

La mejora de software con mayor fundamento es enviar al controlador de audio Bluetooth de Windows la propiedad oficial de Kernel Streaming `KSPROPERTY_ONESHOT_RECONNECT`. Microsoft define específicamente esa propiedad para pedir al controlador de audio que intente conectarse al dispositivo Bluetooth.^1 Es una operación sobre el controlador A2DP normal de Windows; no instala un driver propio ni utiliza AAP.

AirPodsLink actualmente abre el objeto Bluetooth y espera/activa el endpoint mediante WASAPI. Eso funciona cuando el endpoint ya está activo, pero no es la orden correcta para crear la conexión A2DP cuando todavía figura como `UNPLUGGED`. El cambio prioritario es sustituir esa ruta por:

```text
Anuncio BLE de AirPods
        ↓
Localizar endpoint A2DP recordado (ACTIVE o UNPLUGGED)
        ↓
Recorrer Device Topology hasta el filtro KS de Bluetooth
        ↓
KSPROPERTY_ONESHOT_RECONNECT
        ↓
Esperar notificación IMMNotificationClient: endpoint ACTIVE
        ↓
Seleccionar AirPods como salida y confirmar audio
```

## Diagnóstico específico del equipo

| Elemento | Estado observado | Relevancia |
|---|---|---|
| Placa madre | ASUS ROG STRIX B650-A GAMING WIFI | Integra MediaTek MT7922/RZ616 |
| Adaptador | `USB\VID_0489&PID_E0E2` | Bluetooth conectado internamente por USB |
| Driver Bluetooth instalado | MediaTek `1.1043.0.554`, 31 julio 2025 | Está dos versiones importantes por detrás del publicado por ASUS |
| Driver Bluetooth disponible | MediaTek MT7922 `1.1147.0.610`, 4 agosto 2026 | ASUS lo publica para esta placa y Windows 10/11^2 |
| Driver Wi-Fi instalado | `3.4.0.1329`, 5 agosto 2025 | También está atrasado |
| Driver Wi-Fi disponible | `3.6.0.1425`, 4 agosto 2026 | ASUS recomienda actualizar Bluetooth y Wi-Fi conjuntamente^3 |
| Windows | build `26200` | Pila moderna de Windows 11, con controladores A2DP/HFP de Microsoft |
| Señal BLE observada | −70 y −76 dBm | Los paquetes llegan, pero la señal no es especialmente fuerte |
| Tiempo BLE → intento de app | aproximadamente 1,1 s | No explica una espera de 30 s |
| Tiempo registrado como endpoint preparado | aproximadamente 1,24 s | Puede reflejar un endpoint previamente activo; falta medir audio realmente audible |

El desfase entre “endpoint activo” y los 30 segundos percibidos demuestra que la instrumentación actual debe separar al menos cinco hitos: primer anuncio BLE, solicitud KS, enlace Bluetooth Classic, endpoint A2DP `ACTIVE`, cambio de salida predeterminada y primera muestra de audio realmente procesada.

## Opciones evaluadas

### 1. `KSPROPERTY_ONESHOT_RECONNECT`: prioridad máxima

Microsoft documenta `KSPROPSETID_BtAudio` como el conjunto de propiedades enviado al filtro Kernel Streaming de audio Bluetooth. Su enumeración contiene `KSPROPERTY_ONESHOT_RECONNECT` y `KSPROPERTY_ONESHOT_DISCONNECT`; la primera existe precisamente para solicitar una reconexión.^1

El proyecto abierto ToothTray reconstruyó el mecanismo utilizado por la interfaz de Windows: enumera endpoints de render incluso en estado `UNPLUGGED`, navega desde `IMMDevice` por `IDeviceTopology`, obtiene `IKsControl` del filtro conectado y envía la propiedad. ToothTray explica además por qué la ruta de Bluetooth clásica es más lenta: el descubrimiento dura al menos 1,28 segundos y habilitar/deshabilitar servicios equivale parcialmente a instalar o retirar controladores.^4

Una implementación más reciente, BT Audio Tray, usa el mismo mecanismo y declara que los intentos con WinRT/RFCOMM y `BluetoothSetServiceState` no lograban enganchar A2DP de forma fiable, mientras la solicitud KS llevaba el endpoint a `ACTIVE` en alrededor de dos segundos.^5 La cifra es una medición del proyecto, no una garantía para este hardware, pero coincide con el diseño oficial de Microsoft.

**Juicio:** es el cambio correcto para AirPodsLink y puede conservarse completamente en modo usuario.

### 2. Actualizar los controladores MediaTek: prioridad máxima

El controlador instalado es `1.1043.0.554`; ASUS ofrece `1.1147.0.610` para este modelo exacto de placa. También existe una versión intermedia `1.1044.0.556`. El paquete más reciente está firmado y publicado por el fabricante el 4 de agosto de 2026.^2

ASUS recomienda actualizar conjuntamente los controladores Wi-Fi y Bluetooth cuando sus versiones difieren, debido a posibles incompatibilidades.^3 Esto importa porque Wi-Fi y Bluetooth comparten radio/coexistencia en el módulo MT7922 y el espectro de 2,4 GHz. Microsoft confirma que la coordinación Wi-Fi/Bluetooth y Adaptive Frequency Hopping dependen del reporte correcto de canales entre controladores.^6

No existe una nota pública que prometa específicamente reducir la conexión de AirPods. Por ello debe tratarse como una actualización de base muy recomendable y una prueba A/B, no como solución garantizada.

### 3. Antena y señal: prioridad máxima, comprobación física

La ROG STRIX B650-A es un PC de escritorio con antena externa. ASUS indica expresamente que los escritorios con antenas móviles deben tenerlas instaladas correctamente para obtener el rendimiento Bluetooth óptimo.^7 También recomienda mantener el dispositivo cerca y sin obstáculos.

Los RSSI −70/−76 dBm recibidos no impiden detectar los AirPods, pero justifican revisar que ambas conexiones de la antena estén firmes, colocar la base sobre el escritorio —no detrás del gabinete metálico— y alejarla de USB 3.x, discos externos y transmisores de 2,4 GHz. Esta intervención puede mejorar la fase de establecimiento Classic/A2DP si existen retransmisiones, aunque el RSSI BLE no equivale exactamente al RSSI de la conexión Bluetooth Classic.

### 4. Evitar competencia con HFP: prioridad media-alta

Windows administra A2DP estéreo y HFP manos libres como perfiles distintos. A2DP ofrece salida estéreo; HFP se usa para micrófono y audio de menor ancho de banda.^8 En este equipo existen simultáneamente nodos A2DP, AVRCP y Hands-Free para los AirPods.

MagicPods informa que deshabilitar Hands-Free puede acelerar considerablemente la conexión.^9 El fundamento plausible es evitar que Windows negocie y enumere HFP/AG además de A2DP. No es una afirmación oficial de Microsoft y debe medirse en este equipo.

La opción adecuada para AirPodsLink sería “Modo estéreo rápido”: deshabilitar únicamente el nodo HFP de estos AirPods mediante SetupAPI y conservar A2DP/AVRCP. Debe ser reversible y advertir que el micrófono de los AirPods dejará de estar disponible. No conviene deshabilitar servicios de forma global ni tocar otros auriculares.

### 5. Cambiar inmediatamente la salida predeterminada: prioridad media-alta

Que el endpoint exista no garantiza que el programa que ya está reproduciendo cambie a él. Windows y algunas aplicaciones mantienen la salida anterior hasta recibir una notificación o reiniciar su sesión de audio.

AirPodsLink debe observar `IMMNotificationClient.OnDeviceStateChanged`; cuando A2DP llegue a `ACTIVE`, puede seleccionarlo para los roles Console y Multimedia y abrir una sesión silenciosa breve. El cambio de dispositivo predeterminado usado por utilidades comunitarias se apoya normalmente en la interfaz COM no documentada `IPolicyConfig`; por eso debe ser una opción separada y tolerante a cambios de Windows.^5

El diagnóstico debe registrar el ID del endpoint y confirmar que éste sea el A2DP del contenedor físico correcto, no una instancia antigua como `AirPods`, `2-AirPods` o un endpoint Hands-Free.

### 6. Reducir la competencia de dispositivos Apple: prioridad media

Los AirPods pueden conectarse o cambiar automáticamente entre dispositivos Apple asociados a la misma cuenta. Apple permite cambiar `Conectar a este iPhone`/Mac a `Cuando fue el último dispositivo conectado`, en vez de conexión automática.^10

Si el iPhone reclama los AirPods inmediatamente después de abrir el estuche, Windows puede intentar A2DP, perder la conexión y reintentar hasta agotar uno de sus intervalos. Esta hipótesis debe probarse apagando temporalmente Bluetooth en el iPhone durante una serie A/B de cinco conexiones. Si la diferencia es grande, conviene modificar el ajuste oficial de Apple; si no cambia, se descarta.

### 7. `BluetoothSetServiceState`: no usar como ruta principal

Aunque su nombre parece apropiado, Microsoft documenta que esta función habilita/deshabilita un servicio y que habilitarlo instala el controlador asociado; no es una orden general de conexión.^11 Algunos ejemplos comunitarios fuerzan la reconexión apagando y encendiendo servicios, pero ToothTray y BT Audio Tray lo consideran lento o poco fiable para A2DP.^4,5

Esta técnica también puede provocar reenumeración, endpoints duplicados y esperas cercanas a las que se pretende eliminar. Sólo tendría sentido como reparación manual excepcional, nunca por cada apertura del estuche.

### 8. Mantener la radio activa o desactivar ahorro de energía: prioridad baja

Microsoft especifica menos de 100 ms para salir del estado Bluetooth D2 y menos de dos segundos desde D3.^12 Una demora repetible de 30 segundos no encaja por sí sola con el ahorro de energía normal.

Microsoft recomienda no deshabilitar globalmente USB Selective Suspend.^13 Mantener una referencia BLE y escuchar anuncios ya proporciona actividad suficiente para evaluar la radio. Sólo si una traza ETW prueba una transición problemática tendría sentido cambiar temporalmente la administración de energía del adaptador MediaTek o su hub USB, y siempre como experimento reversible.

### 9. Reiniciar radio/servicios: únicamente recuperación

Windows incluye mecanismos de restablecimiento para radios bloqueadas.^14 Alternar Bluetooth puede reconstruir nodos fantasma, pero desconecta todos los dispositivos y no es apropiado para cada uso. AirPodsLink podría ofrecerlo únicamente cuando la solicitud KS fracase varias veces y el endpoint permanezca ausente, con confirmación del usuario.

## Plan recomendado para AirPodsLink

### Fase inmediata

1. Implementar el puente `IMMDevice → IDeviceTopology → IKsControl`.
2. Enviar `KSPROPERTY_ONESHOT_RECONNECT` en el primer anuncio BLE válido.
3. Eliminar la apertura WASAPI como mecanismo primario; conservarla sólo para confirmar el endpoint o mantenerlo despierto.
4. Suscribirse a cambios de estado Core Audio en lugar de sondear cada 200 ms.
5. Medir los seis hitos reales y guardar HRESULT/Win32/estado PnP.
6. Correlacionar endpoints por `PKEY_Device_ContainerId` para evitar instancias duplicadas.

### Pruebas del sistema

1. Confirmar físicamente la antena y repetir cinco ciclos.
2. Medir cinco ciclos con Bluetooth del iPhone apagado y cinco encendido.
3. Actualizar MediaTek Bluetooth a `1.1147.0.610` y Wi-Fi a `3.6.0.1425`; repetir la misma batería de pruebas.
4. Comparar HFP habilitado contra “Modo estéreo rápido”.
5. Objetivo: mediana inferior a tres segundos desde el primer anuncio BLE hasta A2DP `ACTIVE`; percentil 95 inferior a seis segundos.

## Conclusión

La investigación cambia el diagnóstico: el límite no es que “Windows necesite 30 segundos”. Windows dispone de una orden concreta para que su propio controlador de audio intente reconectar, pero AirPodsLink todavía no la está enviando. La aplicación actual observa el dispositivo y abre el endpoint cuando ya aparece; debe actuar un nivel antes, sobre el filtro KS.

La combinación con mayor probabilidad de mejora es:

1. `KSPROPERTY_ONESHOT_RECONNECT` al detectar el primer anuncio;
2. driver MediaTek actualizado;
3. antena externa correctamente colocada;
4. cambio inmediato al endpoint A2DP cuando llegue a `ACTIVE`;
5. modo opcional sin HFP cuando no se necesite el micrófono;
6. evitar que el iPhone reclame los AirPods durante la conexión al PC.

No hay fundamento para aceptar 30 segundos como una limitación inevitable. Con el camino KS correcto y buen enlace de radio, un objetivo de dos a cinco segundos es técnicamente razonable, aunque debe comprobarse con series de mediciones en este equipo.

## Fuentes

1. Microsoft. “[KSPROPSETID_BtAudio](https://github.com/MicrosoftDocs/windows-driver-docs/blob/staging/windows-driver-docs-pr/audio/kspropsetid-btaudio.md)” y “[KSPROPERTY_BTAUDIO](https://learn.microsoft.com/en-us/windows-hardware/drivers/ddi/ksmedia/ne-ksmedia-ksproperty_btaudio).”
2. ASUS. “[ROG STRIX B650-A GAMING WIFI — Drivers & Tools](https://www.asus.com/supportonly/rog%20strix%20b650-a%20gaming%20wifi/helpdesk_download/).”
3. ASUS. “[Motherboard: troubleshooting Wi-Fi/Bluetooth and matching drivers](https://www.asus.com/support/faq/1045135/).”
4. m2jean. “[ToothTray](https://github.com/m2jean/ToothTray).” Implementación BSD-2-Clause de conexión de audio Bluetooth mediante Kernel Streaming.
5. Jeremy Leff. “[BT Audio Tray — BluetoothManager.cs](https://github.com/jeremyleff/BTAudioSysTrayTool/blob/master/BluetoothManager.cs).” Implementación MIT de `KSPROPERTY_ONESHOT_RECONNECT`.
6. Microsoft. “[Bluetooth FAQ — Wi-Fi/Bluetooth coexistence](https://learn.microsoft.com/en-us/windows-hardware/drivers/bluetooth/bluetooth-faq).”
7. ASUS. “[Bluetooth Functionality Anomaly Issue](https://www.asus.com/global/support/faq/1042394/).” Recomendación de antena para equipos de escritorio.
8. Microsoft. “[Bluetooth Classic Audio](https://learn.microsoft.com/en-us/windows-hardware/drivers/bluetooth/bluetooth-classic-audio).”
9. MagicPods. “[Auto disable Handsfree](https://help.magicpods.app/headphones/other-headphones/#auto-disable-handsfree).”
10. Apple. “[Switch your AirPods to another device](https://support.apple.com/en-us/104988).”
11. Microsoft. “[BluetoothSetServiceState](https://learn.microsoft.com/en-us/windows/win32/api/bluetoothapis/nf-bluetoothapis-bluetoothsetservicestate).”
12. Microsoft. “[Bluetooth power management for modern standby platforms](https://learn.microsoft.com/en-us/windows-hardware/design/device-experiences/bluetooth-power-management-for-modern-standby-platforms).”
13. Microsoft. “[USB Selective Suspend](https://learn.microsoft.com/en-us/windows-hardware/drivers/usbcon/usb-selective-suspend).”
14. Microsoft. “[Bluetooth Radio Reset and Recovery](https://learn.microsoft.com/en-us/windows-hardware/drivers/bluetooth/bluetooth-radio-error-recovery).”
