# Instrucciones de Codex para HistoMedic

## Reglas del proyecto

Antes de trabajar, lee y aplica estos archivos, migrados de las reglas `always_on` de Antigravity:

- [Controles de formularios](.agents/rules/controlesforms.md).
- [Acceso a la base de datos local](.agents/rules/accesobasedatos.md).

Ambas reglas se aplican a todo el repositorio, cada una a las operaciones que describe. Los datos de conexión permanecen en su archivo original: no copies contraseñas a código nuevo, respuestas ni registros.

## Skills y perfiles especializados

Selecciona las skills según el trabajo y lee su `SKILL.md` antes de aplicarlas:

| Skill | Cuándo usarla | Entrada |
| --- | --- | --- |
| `architect` | Arquitectura, análisis de dependencias y cambios que afectan varios módulos. | [.agents/skills/architect/SKILL.md](.agents/skills/architect/SKILL.md) |
| `vbnet` | Implementación, correcciones y rendimiento en VB.NET, WinForms y DotNetBar. | [.agents/skills/vbnet/SKILL.md](.agents/skills/vbnet/SKILL.md) |
| `mysql` | Consultas, índices, esquema, migraciones y rendimiento de MySQL. | [.agents/skills/mysql/SKILL.md](.agents/skills/mysql/SKILL.md) |
| `ui-ux` | Diseño y ajustes visuales, navegación y usabilidad de formularios. | [.agents/skills/ui-ux/SKILL.md](.agents/skills/ui-ux/SKILL.md) |
| `qa` | Revisión de calidad, pruebas funcionales, integración y regresiones. | [.agents/skills/qa/SKILL.md](.agents/skills/qa/SKILL.md) |

Los perfiles conservan sus instrucciones originales en `instructions.md`. La carpeta `security` se conserva, pero su `instructions.md` está vacío y no representa una skill implementada. Los perfiles mencionan PHP/API, pero no existe un perfil independiente para esa especialidad en la estructura original.

## Adaptación de las instrucciones heredadas

- Las menciones a «LFM Control» describen el contexto heredado de los perfiles; aplícalas a este proyecto HistoMedic sin renombrar productos, clases o interfaces por ese motivo.
- Conserva la compatibilidad requerida por los perfiles con Visual Studio 2013 y .NET Framework 4.5. Verifica los proyectos y dependencias antes de introducir cambios.
- Las referencias a `controlesforms.md` corresponden a `.agents/rules/controlesforms.md` desde la raíz del repositorio.
- Los agentes especializados describen responsabilidades. Aplica las skills correspondientes; si no hay delegación disponible o autorizada, realiza sus fases secuencialmente. No presupongas que existen herramientas o agentes PHP/API por estar mencionados en el texto.
- Las instrucciones del usuario y las autorizaciones de la sesión tienen prioridad sobre las convenciones heredadas. Ajusta el detalle de planes e informes al alcance de la tarea.

## Mantenimiento de la migración

Mantén las reglas en `.agents/rules` y el contenido especializado en cada `instructions.md`; los `SKILL.md` son las entradas de descubrimiento de Codex. No es necesario duplicarlos en `.codex`.
