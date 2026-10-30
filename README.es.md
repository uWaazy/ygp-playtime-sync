<div align="center">

# YGP Playtime Sync

**Envía tu tiempo de juego de Playnite a tu cuenta de [Your Gamer Profile](https://yourgamerprofile.com/), automáticamente.**

![Estado](https://img.shields.io/badge/estado-beta-orange)
![Versión](https://img.shields.io/badge/versi%C3%B3n-0.5.0--beta-blue)
![Playnite](https://img.shields.io/badge/Playnite-10.x-5b8def)
![Idiomas](https://img.shields.io/badge/UI-English%20%7C%20Portugu%C3%AAs%20%7C%20Espa%C3%B1ol-green)

[English](README.md) · [Português (Brasil)](README.pt-BR.md) · Español

</div>

> **Beta.** Esta es la primera versión pública. Funciona en el día a día, pero todavía puede tener asperezas. Por favor, reporta los problemas (consulta [Cómo reportar problemas](#cómo-reportar-problemas)).
>
> **Proyecto no oficial.** No tiene relación con Your Gamer Profile (YGP) ni cuenta con su respaldo.

## Qué hace

- **Sincroniza automáticamente al cerrar un juego** (sesiones de 1 minuto o más) y revisa pendientes periódicamente en segundo plano.
- **Siempre envía el tiempo total del juego** y nunca reduce las horas que el sitio ya tiene.
- **Añade juegos nuevos y actualiza los que ya están** en tu perfil. La plataforma, el estado y la nota que definiste en el sitio se conservan.
- **Juegos recientes arriba.** La sincronización va del juego que jugaste hace más tiempo al que jugaste por último, así que los más recientes quedan arriba en los recientes de tu perfil, el mismo efecto que editar las horas a mano en el sitio.
- **Finalización automática (opcional).** Elige de **7 a 30 días**. Si pasas ese tiempo sin abrir un juego, el plugin define en YGP la fecha de finalización como el **último día en que realmente jugaste**. Consulta [Finalización automática](#finalización-automática).
- **Fechas de inicio y de finalización** a partir de tu historial (usa el plugin *GameActivity*, si lo tienes, y el registro del propio plugin). Se pueden activar/desactivar por juego.
- **Detecta ediciones manuales de horas** en Playnite (por ejemplo, con FriendlySetTime) y actualiza el sitio.
- **Sincronizar toda la biblioteca** con un clic y barra de progreso. Los juegos antiguos entran.
- **Filtros:** ignora aplicaciones/software, juegos con menos de 1 minuto, copias duplicadas del mismo juego y una lista de nombres que tú elijas.
- **Cola segura sin conexión:** las sesiones se guardan en tu computadora, sobreviven a los reinicios y se reenvían solas.
- **Tarjeta del Perfil del Creador** en la esquina superior derecha de la configuración: avatar, usuario y enlace al perfil del autor del plugin en YGP, y el último juego jugado en su perfil.
- **Interfaz en Español, English y Português (Brasil).** Sigue el idioma de Playnite y se puede cambiar en la configuración.

## Requisitos

- Windows con **Playnite 10.x**
- Una cuenta en **Your Gamer Profile**

## Instalación

1. Descarga el `.pext` más reciente desde la página de [**Releases**](https://github.com/uwaazy/ygp-playtime-sync/releases).
2. Cierra Playnite y haz doble clic en el archivo `.pext` (o arrastra el archivo `.pext` a la ventana abierta de Playnite y confirma la instalación).
3. Ve a **Extensiones → Configuración de extensiones → Genérico → Your Gamer Profile Sync**.
4. Haz clic en **Vincular cuenta (iniciar sesión)** e inicia sesión en YGP en la ventana que se abre. Si Google bloquea el inicio de sesión en el navegador integrado, usa **Vincular pegando una cookie (alternativa)** y sigue las instrucciones de la pantalla.
5. Haz clic una vez en **Sincronizar toda la biblioteca (juegos antiguos)**. Tarda unos minutos (aproximadamente de 1 a 2 segundos por juego).

Después de eso, todo ocurre en segundo plano.

## Configuración

| Opción | Qué hace |
| --- | --- |
| Sincronizar automáticamente en segundo plano | Envía tus horas sin que tengas que hacer nada |
| Sincronizar al cerrar el juego | Sincroniza justo después de cada sesión |
| Detectar ediciones manuales de horas | Recoge las horas que cambias a mano en Playnite |
| Enviar la fecha de inicio por defecto | Envía el primer día jugado (se puede cambiar por juego) |
| Mostrar aviso de Playnite | Muestra el juego, las horas que subieron, el total del juego y el total general |
| Sincronizar siempre el tiempo total acumulado | Mantiene el total del sitio igual al de Playnite |
| Intervalo de sincronización periódica | Cada cuánto tiempo se revisa la cola (1 a 60 min) |
| Finalización automática + días | Consulta más abajo |
| Ignorar aplicaciones/software · Nombres ignorados | Mantiene fuera de tu perfil lo que no es un juego |
| Idioma | Automático, English, Português (Brasil) o Español |

**Por juego:** haz clic derecho en el juego → **YGP: Configuración** (un único submenú) para sincronizar el tiempo de ese juego al instante o activar/desactivar su fecha de inicio y su fecha de finalización.

## Finalización automática

Activa **Marcar la fecha de finalización cuando pase un tiempo sin jugar** y elige cuántos días (de 7 a 30).

Cuando un juego pasa ese número de días sin abrirse, el plugin envía a YGP la **fecha de la última vez que jugaste** como fecha de finalización. La comprobación se hace al abrir Playnite, cada hora mientras esté abierto y cuando guardas la configuración.

> Ejemplo con 7 días: jugaste por última vez el día 22. El día 29 el plugin define la fecha de finalización como el **22** (y no el 29).

- Cada "último día jugado" se envía una sola vez por juego. Si vuelves a jugar y paras de nuevo, se envía la nueva fecha.
- Solo se define la **fecha** de finalización. En los juegos que ya están en tu perfil, el estado y la nota del sitio no se modifican.
- Si dejas la opción desactivada, aún puedes definir las fechas a mano en el sitio, o por juego con clic derecho → **YGP: Configuración**.

## Cómo funciona la conexión

- Inicias sesión en el sitio oficial de YGP dentro del navegador integrado de Playnite. El plugin **nunca ve tu contraseña**.
- Para actuar en tu nombre, guarda la **cookie de sesión** del sitio en la configuración del plugin, en tu computadora. Consulta [Privacidad y seguridad](#privacidad-y-seguridad).
- Los datos van solo a `https://yourgamerprofile.com`, por HTTPS. **No hay telemetría, analíticas ni servicios de terceros.**
- Los botones **Diagnosticar sitio** y **Aprender rutas del sitio** (Avanzado) sirven para resolver problemas. El diagnóstico hace muchas solicitudes al sitio, así que úsalo solo cuando algo deje de funcionar.

## Privacidad y seguridad

Lo que se envía a YGP: nombre del juego, plataforma, tiempo total en minutos y, si está activado, las fechas de inicio y de finalización.

Lo que se queda en tu PC (en la carpeta `ExtensionsData` de Playnite, dentro del directorio de este plugin): la cola de sesiones, el registro de lo que ya se envió, las asignaciones de juegos, las opciones por juego y las fechas jugadas.

**Tu cookie de sesión se guarda en texto plano** en la configuración del plugin, como la mayoría de las configuraciones de plugins de Playnite. Quien consiga ese archivo puede actuar como tú en YGP hasta que la sesión expire. Por eso:

- No compartas la carpeta `ExtensionsData` de Playnite ni los archivos de configuración.
- Revisa el `ygp-diagnostico.txt` antes de adjuntarlo a un reporte de error; contiene respuestas en bruto del sitio.
- **Desconectar** (en la configuración) borra la cookie guardada.

## Limitaciones conocidas (beta)

- Las rutas del sitio no están documentadas, así que una actualización de YGP puede romper la sincronización hasta que se actualice el plugin.
- La identificación de los juegos es estricta a propósito (las secuelas y las versiones Mobile/Demo no se mezclan). Algunos juegos pueden tener que añadirse a mano en el sitio.
- El orden "recientes arriba" depende de cómo YGP registra las ediciones; hoy equivale a lo que ocurre cuando editas las horas en el sitio, pero YGP puede cambiarlo.
- El sitio puede limitar la velocidad (HTTP 429). Espera unos minutos y sincroniza de nuevo.

## Cómo reportar problemas

Abre un [issue](https://github.com/SEU-USUARIO/ygp-playtime-sync/issues) e incluye:

1. Las versiones de Playnite y del plugin.
2. Qué hiciste y qué esperabas que pasara.
3. El `extensions.log` de Playnite (*Menú → Configuración → General → Abrir carpeta de logs*). Busca las líneas que empiezan con `[YGP]`.
4. Si es un problema de sincronización, el `ygp-diagnostico.txt` del botón **Abrir carpeta de diagnóstico**. Revísalo antes para quitar datos personales.

## Próximos pasos

- Más pruebas durante la beta
- Correcciones para lo que YGP cambie en el sitio
- Tus comentarios: las sugerencias son bienvenidas en los issues

## Licencia y código fuente

El plugin es gratuito. El código fuente no está publicado por ahora; este repositorio reúne la documentación y las versiones (Releases).

## Créditos

Hecho para [Playnite](https://playnite.link/). Gracias a todos los que están probando la beta.

