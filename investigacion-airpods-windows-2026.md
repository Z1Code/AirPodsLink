# Ingeniería inversa de AirPods en Windows

## Conclusión ejecutiva

Sí. A 11 de septiembre de 2026 existe ingeniería inversa real, pública y funcional de una parte considerable de los sistemas propietarios de los AirPods. Ya no se limita a imitar la ventana emergente: la comunidad ha identificado el canal Bluetooth, el protocolo de control, el intercambio inicial, numerosos códigos de operación y los formatos de telemetría. En Windows hay implementaciones que leen batería individual, detectan el uso, cambian ANC/Transparencia/Adaptativo, reciben Conciencia de conversación y, experimentalmente, extraen movimiento de la cabeza.

El resultado, sin embargo, no es una reproducción completa del ecosistema Apple. La integración se divide en tres capas y sólo las dos primeras están razonablemente resueltas:

| Capa | Estado en Windows | Evidencia |
|---|---|---|
| Anuncios BLE: modelo, batería, estuche, carga, oído | Resuelta sin driver, con variaciones por firmware | AirPodsDesktop y otros decodifican los anuncios Apple Continuity |
| AAP/AACP: batería precisa, ANC, Transparencia, Adaptativo, gestos y sensores | Resuelta de forma experimental mediante driver de kernel | MagicAAP, LibrePods-WindowsBridge y SoundStage |
| MagicPairing+iCloud: emparejamiento y cambio automático idénticos a iPhone/Mac | Protocolo estudiado, pero no existe una solución Windows madura e integrada con iCloud | Investigación académica y estado declarado por LibrePods |

Por tanto, la respuesta exacta es: **sí han revertido gran parte del protocolo operativo de los AirPods y lo han hecho funcionar en Windows; no han entregado aún un clon completo, seguro y listo para producción del emparejamiento/cambio automático de Apple.**

## Qué fue revertido

### Anuncios Bluetooth Low Energy

Los AirPods transmiten paquetes de fabricante con el identificador de Apple `0x004C`. El mensaje conocido como *Proximity Pairing Message* contiene, entre otros campos, modelo, estados de oído/estuche, niveles de batería y estado de conexión. Parte de la carga puede venir cifrada en firmware reciente, pero varios campos continúan siendo observables y las implementaciones que disponen de `MagicAccIRK` y `MagicAccEncKey` pueden resolver más información.^1

Ésta es la ruta que usan las aplicaciones sin driver. No controlan el auricular: escuchan sus anuncios. Es suficiente para una ventana estilo iPhone, batería aproximada, detección de oído y reproducción/pausa. AirPodsDesktop demuestra esta ruta en Windows mediante el escáner WinRT de anuncios BLE y código abierto.^2

### Apple Accessory/AirPods Protocol (AAP/AACP)

La ingeniería inversa más profunda identificó un servicio Bluetooth propietario, UUID `74ec2172-0bad-4d01-8f77-997b2be0722a`, y el canal L2CAP PSM `0x1001`. Tras un *handshake* fijo y la solicitud de notificaciones, los AirPods intercambian tramas AAP/AACP.^3

LibrePods constituye la referencia pública más completa. Implementa el protocolo propietario y documenta funciones operativas como:

- batería separada de auricular izquierdo, derecho y estuche;
- detección de oído;
- ANC, Transparencia, Desactivado y Adaptativo;
- Conciencia de conversación;
- configuración de gestos y botones;
- cambio de nombre y ajustes de accesibilidad;
- extracción/uso de claves de accesorio para interpretar anuncios cifrados.

Su documentación y código muestran, entre otros, el *opcode* de batería `0x04`, oído `0x06`, controles de ruido `0x09/0x0D`, gestos `0x14–0x16`, cambio de nombre `0x1A/0x1E` y Conciencia de conversación `0x28/0x4B`.^4 El repositorio principal ofrece estas funciones de forma madura principalmente en Android y Linux, no como una aplicación Windows oficial.^5

### Movimiento de cabeza y audio espacial

En 2026 también aparecieron pruebas de concepto Windows que reciben datos de movimiento de los AirPods a través de AACP. `airpods-head-track` abre la interfaz expuesta por MagicAAP, ejecuta el *handshake*, interpreta orientación y la envía a OpenTrack.^6 SoundStage afirma combinar el flujo de IMU con un renderizador binaural propio para audio espacial con seguimiento de cabeza.^7

Esto no equivale al audio espacial personalizado de Apple. SoundStage declara expresamente que no usa el escaneo de orejas ni el HRTF propietario de Apple; su procesamiento espacial es una implementación independiente.

## Por qué Windows necesita un driver

Windows permite A2DP, HFP y AVRCP normales, pero no expone a una aplicación de usuario una API general para abrir este canal L2CAP propietario. Éste es el cuello de botella central: conocer los bytes de AAP no basta si el proceso no puede enviarlos.

Las implementaciones completas instalan un controlador de perfil Bluetooth KMDF. El controlador se enlaza al servicio AAP anunciado por los AirPods, abre PSM `0x1001` mediante la interfaz de perfiles Bluetooth del kernel y entrega tramas acotadas a la aplicación mediante IOCTL. LibrePods-WindowsBridge describe un driver derivado del ejemplo `bthecho` de Microsoft; SoundStage publica una arquitectura equivalente y su propio driver.^8

Esto explica las dos categorías de aplicaciones:

- **Sin driver:** fáciles y relativamente seguras; batería/anuncios BLE, ventana, oído y atajos.
- **Con driver:** controles AAP reales y telemetría precisa; requieren privilegios y, hoy, normalmente Test Mode.

## Proyectos Windows comprobables

### MagicPods + MagicAAP

Es la implementación de usuario final con el historial más largo y el conjunto más amplio. MagicPods afirma ofrecer ANC/Transparencia/Adaptativo, batería precisa, personalización de botones, detección avanzada, Conciencia de conversación y ajustes de accesibilidad mediante MagicAAP.^9 La aplicación es propietaria; por ello demuestra que la técnica funciona, pero no permite auditar toda la implementación.

El bloqueo actual es la firma. La versión oficial de MagicAAP es no firmada y exige Windows Test Mode. La variante firmada por la comunidad dejó de funcionar desde la actualización de seguridad de abril de 2026, según la propia documentación del proyecto.^10 Además, el método comunitario empleaba una solución de firma no estándar y podía activar Defender o antitrampas.

### AirPodsDesktop

Proyecto libre GPL para Windows, actualmente en beta, que muestra batería, detección automática de oído, animación y un modo orientado a reducir la latencia.^11 Su versión de marzo de 2026 añadió AirPods 4, AirPods 4 ANC y AirPods Pro 3. Su valor principal es demostrar que la capa BLE/Continuity puede implementarse sin controlador de kernel.

No ofrece control AAP completo; por diseño, escuchar anuncios no permite ordenar un cambio de ANC ni solicitar todas las configuraciones internas.

### LibrePods-WindowsBridge

Port experimental que combina la investigación LibrePods con un puente L2CAP para Windows. Declara funcionando batería, estado de conexión/oído, ANC/Transparencia/Adaptativo, Conciencia de conversación y pausa automática.^12 Es una beta para usuarios técnicos y requiere Test Mode con Secure Boot desactivado.

El proyecto merece más confianza técnica que una simple maqueta porque publica el mecanismo del driver, paquetes y flujo de instalación. Aun así, tiene una comunidad muy pequeña y su propio README advierte que no se espere una experiencia pulida.

### SoundStage for AirPods

Proyecto GPL reciente que publica aplicación WPF, núcleo AAP, escáner BLE, driver KMDF y procesamiento espacial. Documenta el enlace al UUID AAP/UARP, las llamadas de perfil Bluetooth y el puente IOCTL; declara validación física con AirPods 4 ANC en Windows 10 y pruebas de recuperación en Windows 11 26H1.^13

Es la demostración pública más ambiciosa encontrada, porque agrega controles, batería, IMU y audio espacial. Pero sólo tenía una comunidad mínima al cierre de esta investigación; sus resultados deben tratarse como una prueba experimental del autor, no como compatibilidad ampliamente reproducida.

### Herramientas especializadas

`airpods-head-track` demuestra de manera independiente que otra aplicación puede consumir el flujo AACP expuesto por MagicAAP y convertirlo en orientación para OpenTrack.^6 `RustPods` y otras utilidades se concentran en batería mediante anuncios. Estos proyectos refuerzan que los formatos ya son conocimiento reutilizable, aunque no resuelven por sí solos la distribución segura de un driver.

## Lo que todavía no está resuelto

### Cambio automático idéntico a iPhone/Mac

Apple denomina MagicPairing al protocolo que permite que los AirPods emparejados con un dispositivo se vuelvan utilizables por los demás dispositivos de la misma cuenta. Investigadores publicaron en 2020 una reconstrucción detallada: el primer emparejamiento crea una *Accessory Key*, iCloud la distribuye a los dispositivos de la cuenta y cada conexión deriva una nueva Bluetooth Link Key usando criptografía AES-SIV.^14

Que MagicPairing haya sido revertido académicamente no significa que Windows pueda incorporarse legítima y automáticamente a la cuenta iCloud. Hacen falta distribución autenticada de claves, integración del sistema, arbitraje de conexión/audio y manejo de secretos. A septiembre de 2026 no encontré una implementación Windows pública, madura y reproducible que ofrezca el mismo cambio automático bidireccional entre iPhone, Mac y PC.

MagicPods puede conectar automáticamente o mediante atajo y mejorar mucho la experiencia local, pero eso es automatizar la conexión Bluetooth del PC; no convierte Windows en otro miembro nativo de la red de dispositivos iCloud.

### Micrófono de alta calidad mientras se escucha estéreo

AAP no corrige la limitación tradicional de perfiles Bluetooth de Windows. Al activar el micrófono mediante HFP/HSP, Windows abandona A2DP estéreo de alta calidad. SoundStage recomienda explícitamente usar AirPods Stereo como salida y otro micrófono como entrada.^15

LibrePods también marca el audio bidireccional de alta calidad como no implementado. AirPods modernos parecen contar con rutas propietarias adicionales, pero no hay una solución Windows general comparable a la de Apple.

### Funciones de cuenta y firmware

No están resueltas de forma general Find My, incorporación segura a la red Find My, actualizaciones de firmware, audio espacial personalizado de Apple, conmutación iCloud completa ni todas las funciones nuevas de sensores. LibrePods enumera Find My y seguimiento espacial como desconocidos/pendientes, y algunas funciones recientes como trabajo en curso.^5

## Evaluación de la evidencia

| Afirmación | Confianza | Motivo |
|---|---:|---|
| AAP/AACP fue objeto de ingeniería inversa | Muy alta | Código, documentación, tramas, varios proyectos y artículo académico relacionado |
| Batería izquierda/derecha/estuche funciona en Windows | Alta | Implementaciones sin driver y con driver; limitación conocida del estuche cerrado |
| ANC/Transparencia/Adaptativo funcionan en Windows | Alta | MagicPods comercial y varios drivers/proyectos abiertos describen el mismo canal y comandos |
| Seguimiento de cabeza funciona experimentalmente | Media | Código y arquitectura públicos, pero poca validación independiente |
| Existe una experiencia idéntica a iCloud/Mac | Baja/negativa | No se halló una implementación madura; los proyectos distinguen conexión automática local de MagicPairing |
| Existe un driver completo, firmado por Microsoft y apto para cualquier usuario | Negativa | Los proyectos actuales exigen Test Mode o usan una variante bloqueada desde abril de 2026 |

## Recomendación práctica

Para ver batería y obtener una conexión más cómoda, la opción sensata es empezar por una solución sin driver: AirPodsDesktop si se prefiere código abierto, o MagicPods sin MagicAAP si se prefiere una aplicación más pulida.

Para experimentar con ANC, Adaptativo, ajustes y sensores desde Windows, la ingeniería inversa ya lo permite, pero debe hacerse en un equipo de pruebas. La ruta defendible es un driver abierto compilado por el propio usuario en Test Mode, después de revisar el código y con una copia de seguridad. No es recomendable desactivar Secure Boot en un equipo corporativo, con información sensible o dedicado a juegos competitivos.

No debe instalarse la antigua variante comunitaria de MagicAAP ni scripts que agreguen exclusiones a Defender o aprovechen certificados filtrados. El hecho de que una técnica haga funcionar AACP no garantiza que el paquete distribuido sea seguro.

## Fuentes

1. LibrePods-WindowsBridge. “[Bluetooth Low Energy — Apple Proximity Pairing Message](https://github.com/will-ch-h/librepods-windowsbridge/blob/main/Proximity%20Pairing%20Message.md).” Consultado el 11 de septiembre de 2026.
2. SpriteOvO et al. “[AirPodsDesktop](https://github.com/SpriteOvO/AirPodsDesktop).” Repositorio y documentación del analizador Windows BLE/Apple Continuity.
3. LibrePods contributors. “[LibrePods AAP implementation](https://github.com/librepods-org/librepods).” Código y documentación del protocolo propietario.
4. PodBridge. “[Prior Art: AAP/AACP protocol reverse-engineering](https://github.com/bhemsen/PodBridge/blob/main/docs/prior-art.md).” Auditoría técnica actualizada el 8 de julio de 2026.
5. LibrePods contributors. “[Feature availability](https://github.com/librepods-org/librepods#feature-availability).” Estado de funciones en Android/Linux.
6. StarNumber12046. “[AirPods Head Tracking for Windows](https://github.com/StarNumber12046/airpods-head-track).” Implementación AACP/OpenTrack para Windows.
7. capriqqw. “[SoundStage for AirPods](https://github.com/capriqqw/soundstage-for-airpods).” Implementación experimental para Windows.
8. capriqqw. “[SoundStage Architecture](https://github.com/capriqqw/soundstage-for-airpods/blob/main/docs/ARCHITECTURE.md)”; LibrePods-WindowsBridge, “[README](https://github.com/will-ch-h/librepods-windowsbridge).” Arquitectura de los puentes L2CAP/KMDF.
9. MagicPods. “[MagicAAP Driver](https://magicpods.app/magicaap/).” Funciones declaradas y opciones de instalación.
10. MagicPods. “[MagicAAP Community Driver](https://help.magicpods.app/fun-magicaap-community/).” Bloqueo desde la actualización de abril de 2026 y advertencias.
11. SpriteOvO et al. “[AirPodsDesktop releases](https://github.com/SpriteOvO/AirPodsDesktop/releases).” Versiones y compatibilidad.
12. will-ch-h. “[LibrePods-WindowsBridge](https://github.com/will-ch-h/librepods-windowsbridge).” Funciones, driver e instrucciones beta.
13. capriqqw. “[SoundStage architecture and compatibility](https://github.com/capriqqw/soundstage-for-airpods).” Validación declarada y límites.
14. Heinze, Classen, Rohrbach y Hollick. “[MagicPairing: Apple’s Take on Securing Bluetooth Peripherals](https://wisec2020.ins.jku.at/proceedings/wisec20-28.pdf).” WiSec 2020.
15. capriqqw. “[Discord, games, and microphone quality](https://github.com/capriqqw/soundstage-for-airpods#discord-games-and-microphone-quality).” Limitación A2DP/HFP.
