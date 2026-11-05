# Transferencias Administrativas V1

Backend solamente. No requiere migraciones. Todos los endpoints requieren JWT con
`ClaimTypes.Role = "Admin"`, igual al rol emitido por `TokenService` y al rol del
administrador sembrado. No basta una responsabilidad de lectura/escritura.

## API

| Metodo | Ruta | Resultado |
| --- | --- | --- |
| GET | `/api/transferencias/{seccion}/exportar` | Archivo JSON descargable, `Content-Type: application/json` |
| POST | `/api/transferencias/preview` | Reporte; no realiza escrituras |
| POST | `/api/transferencias/importar` | Revalida, verifica huella y aplica atomicamente |

Secciones exactas: `docentes`, `materias`, `horarios`, `alumnos`, `usuarios`,
`roles`, `responsabilidades`, `cursos`. Todas permiten exportar e importar.
Modos exactos: `actualizar`, `reemplazar`.

Preview:

```json
{
  "paquete": { "...": "el objeto JSON completo descargado" },
  "modo": "actualizar"
}
```

Importar:

```json
{
  "paquete": { "...": "el mismo objeto JSON completo" },
  "modo": "reemplazar",
  "huella": "huella devuelta por preview",
  "confirmacion": "REEMPLAZAR"
}
```

En `actualizar`, enviar `confirmacion: ""` o no enviar el campo. La huella siempre
es obligatoria para importar. No modificar ni reconstruir el paquete entre pasos.

Reporte de ejemplo (las entidades y sus conteos dependen del plan):

```json
{
  "seccion": "docentes",
  "modo": "actualizar",
  "puedeImportar": true,
  "resumen": [
    { "entidad": "docentes", "crear": 2, "actualizar": 1, "eliminar": 0 }
  ],
  "advertencias": [
    "No se transfieren credenciales, tokens ni sesiones. Se conservan los vinculos de usuarios del destino."
  ],
  "errores": [],
  "huella": "digest SHA-256 de 64 caracteres hexadecimales",
  "importado": false
}
```

Preview semanticamente invalido: HTTP 200, `puedeImportar: false`, errores en
espanol. En ese caso los conteos pueden ser parciales y nada se aplica.
Importacion bloqueada, obsoleta o con conflicto: HTTP 409 con el mismo reporte.
Importacion correcta: HTTP 200 con `importado: true`.
Solicitud/archivo mal formado, version/seccion desconocidas o confirmacion
incompleta: HTTP 400 con `message` o Problem Details del model binding.
Sin autenticacion: 401; rol incorrecto: 403; cuerpo demasiado grande: 413.

Los conteos `actualizar` incluyen registros coincidentes aunque ya tengan los
mismos valores. Los prerrequisitos existentes no se cuentan como actualizaciones,
porque no se sobrescriben. Los nuevos aparecen como filas de prerrequisito.

## Formato Exacto

Ejemplo completo valido de docentes:

```json
{
  "formato": "cens15-transferencia",
  "version": 1,
  "seccion": "docentes",
  "exportadoEn": "2026-10-26T12:00:00Z",
  "datos": {
    "docentes": [
      { "nombres": "Ana", "apellidos": "Perez", "email": "ana@example.com" }
    ],
    "anexos": [],
    "orientaciones": [],
    "cursos": [],
    "materias": [],
    "bloques": [],
    "asignaciones": [],
    "alumnos": []
  },
  "alcance": ["Nombres, apellidos y email de docentes"],
  "exclusiones": [
    "Credenciales, secretos de acceso, tokens, sesiones y permisos legacy",
    "Documentos, archivos, imagenes y certificados",
    "Inscripciones, cursadas, calificaciones e historial academico"
  ]
}
```

`alcance` y `exclusiones` siempre se exportan, pero son opcionales al importar.
No controlan el alcance real ni suprimen advertencias del servidor. Todos los
otros campos originales son obligatorios. **Las ocho listas originales de `datos`
son obligatorias**, tambien las vacias. Las nuevas listas `usuarios`, `roles` y
`responsabilidades` son opcionales y por defecto vacias para leer archivos V1
persistidos. No se aceptan listas/registros nulos ni campos desconocidos
(incluidos IDs, `userId` o credenciales). Tampoco propiedades JSON duplicadas.

Esquema de los elementos de las listas, usando notacion TypeScript solo como
documentacion (no es codigo del frontend):

```ts
type Docente = { nombres: string; apellidos: string; email: string | null };
type CursoClave = {
  cursoNombre: string;
  division: string;
  anexo: string;
  orientacion: string;
};
type MateriaClave = { curso: CursoClave; nombre: string };
type Dia = "Monday" | "Tuesday" | "Wednesday" | "Thursday" | "Friday";
type BloqueClave = { anexo: string; diaSemana: Dia; orden: number };
type Docencia = { docente: Docente; rol: "Titular" | "Suplente"; activo: boolean };
type Datos = {
  usuarios?: { email: string; nombres: string; apellidos: string; activo: boolean; rol: string }[];
  roles?: { nombre: string; responsabilidades: string[] }[];
  responsabilidades?: { nombre: string; descripcion: string | null }[];
  docentes: Docente[];
  anexos: { nombre: string }[];
  orientaciones: { nombre: string; nombreCorto: string }[];
  cursos: { clave: CursoClave; semipresencial: boolean }[];
  materias: { clave: MateriaClave; cargaHoraria: number; docentes: Docencia[] }[];
  bloques: {
    clave: BloqueClave;
    tipo: "Clase" | "Receso";
    horaInicio: string; // HH:mm:ss
    horaFin: string; // HH:mm:ss
  }[];
  asignaciones: { materia: MateriaClave; bloque: BloqueClave }[];
  alumnos: {
    numeroDocumento: string;
    nombres: string;
    apellidos: string;
    fechaNacimiento: string; // yyyy-MM-dd
    genero: "Varon" | "Mujer";
    domicilio: string;
    datosNacimiento: { localidad: string; provincia: string; pais: string } | null;
    contacto: {
      telefonoAlumno: string;
      email: string;
      nombreEmergencia: string;
      telefonoEmergencia: string;
    } | null;
  }[];
};
```

Las claves son por valor, no IDs ni indices de las listas. Los nombres se
comparan con normalizacion Unicode NFC, espacios colapsados y sin distinguir
mayusculas. No se eliminan acentos. Un docente usa email cuando existe; sin email
usa nombres + apellidos. Un homonimo sin email ambiguo se bloquea, incluso si
otro homonimo tiene email. Cambiar la identidad natural no equivale a renombrar:
se trata como alta/ausencia, con todas las protecciones de reemplazo.

## Alcance Por Seccion

| Seccion | Datos propietarios | Prerrequisitos incluidos |
| --- | --- | --- |
| docentes | Nombres, apellidos, email | Ninguno |
| materias | Nombre, curso por clave, carga, docentes con rol/activo | Anexo, orientacion, curso, docentes |
| horarios | Grillas base y asignaciones de materias | Anexo, orientacion, curso, docentes y materias con carga/docentes |
| alumnos | Ficha de alumnos activos, contacto, nacimiento | Ninguno |
| usuarios | Email, nombres, apellidos, activo, rol por nombre | Roles usados y sus responsabilidades |
| roles | Nombre y responsabilidades por nombre | Responsabilidades usadas |
| responsabilidades | Nombre y descripcion nullable | Ninguno |
| cursos | Clave natural y modalidad semipresencial | Anexos y orientaciones usados |

Prerrequisitos: se crean solo si faltan. Si un prerrequisito existente difiere
en modalidad, nombre corto, nombre docente o carga/docencias de materia, preview
se bloquea. Transferir primero la seccion propietaria o corregir el conflicto.
No se crean cursos/orientaciones/docentes de contexto que no sean usados por
las materias del archivo. Nunca se eliminan prerrequisitos por estar ausentes.

**Alumnos no incluye historial academico**: no hay inscripciones, cursadas,
calificaciones, certificados ni adjuntos, tampoco referencias a archivos locales.
Se conservan los existentes del destino. Los alumnos dados de baja no se
exportan; una coincidencia con una baja del destino se bloquea hasta restaurarla
explicitamente. Nacimiento/contacto nulos conservan los detalles existentes.

## Modos Y Protecciones

- `actualizar`: agrega o actualiza coincidencias; no elimina ausentes. En horarios
  combina asignaciones con las existentes. No vacia una materia por recibir una
  lista sin asignaciones. Si la combinacion produce ocupacion/carga invalida,
  bloquea y sugiere usar reemplazar.
- `reemplazar`: reemplazo global de la seccion, no solamente del anexo/curso del
  archivo. Exige preview y texto exacto `REEMPLAZAR`. Un archivo vacio representa
  una seccion vacia, sujeto a todas las protecciones siguientes.
- Docentes ausentes: no se eliminan si tienen materias o usuario vinculado.
- Materias ausentes: no se eliminan si tienen cursadas (incluidas las de alumnos
  dados de baja) o horarios. Carga/docentes de coincidencias se actualizan sin
  borrar horarios del destino. Carga debe ser compatible con esos horarios.
- Alumnos ausentes: solo baja logica, nunca borrado fisico. Se bloquea si tienen
  inscripciones/historial o documentos que la baja ocultaria.
- Horarios: preserva IDs de bloques y asignaciones coincidentes. Un bloque
  asignado no puede editarse ni borrarse, aunque el paquete quite la asignacion;
  quitar primero las asignaciones en una operacion explicita independiente.
- Grillas: lunes a viernes, orden unico positivo, duracion positiva, orden
  temporal sin superposicion. Asignaciones solo a bloques de clase del anexo del
  curso. Cero asignaciones permite carga pendiente; de existir asignaciones, su
  cantidad debe ser exactamente la carga. No hay dos materias del mismo curso
  en el mismo bloque. Se mantiene el indice unico de ocupacion existente.
- Docencias: maximo titular + suplente, sin docente/rol repetidos y exactamente
  uno activo cuando hay docentes.
- Se rechazan claves naturales duplicadas/ambiguas del archivo y del destino.
   No se usan IDs del origen, ni se transfieren vinculos de login.

### Usuarios Y Seguridad

Los elementos nuevos tienen exactamente estos campos serializados:

```json
{
  "usuarios": [{ "email": "ana@example.com", "nombres": "Ana", "apellidos": "Perez", "activo": true, "rol": "Docente" }],
  "roles": [{ "nombre": "Docente", "responsabilidades": ["Alumnos.Leer"] }],
  "responsabilidades": [{ "nombre": "Alumnos.Leer", "descripcion": null }]
}
```

Estos campos van dentro de `datos`, junto a las ocho listas originales. La
descripcion es obligatoria en cada responsabilidad, aunque admita null. Cada rol
incluye su lista de responsabilidades, incluso vacia. No se aceptan IDs, fotos,
archivos, passwords, hashes, secretos, tokens, sesiones ni campos arbitrarios.
Los paquetes nuevos exportan las once listas; los archivos V1 anteriores siguen
leyendose sin agregar campos. Las claves son email normalizado o nombre natural.

Los prerrequisitos de seguridad existentes deben coincidir: descripcion de
responsabilidad y conjunto de responsabilidades del rol. Una diferencia bloquea
y pide importar primero `responsabilidades` o `roles`; el contexto nunca cambia
privilegios existentes. Los prerrequisitos nuevos se crean con GUID del destino.
Los permisos legacy no se transfieren; se conservan en roles coincidentes.

Usuarios coincidentes conservan Id, Auth, email almacenado, PasswordHash, foto,
tokens y todos los vinculos del destino. Se actualizan nombres, apellidos,
estado permitido y rol. Usuarios nuevos reciben GUID nuevo y Auth con bcrypt de
48 bytes criptograficamente aleatorios, secreto no exportado ni comunicado,
`MustChangePassword = true` y sin envio de correo dentro de la transaccion.
El reporte indica solicitar acceso inicial mediante el flujo existente
`RequestInitialAccess` despues de importar.

Las altas inactivas, desactivaciones explicitas y reactivaciones estan habilitadas.
`reemplazar` desactiva los usuarios activos ausentes mediante `Status = false`,
con conteo `usuarios (baja logica)`. No hay borrado fisico ni cascadas sobre Auth,
docentes o historial. El reemplazo conserva las bajas previas. El calculo del
ultimo Admin tiene en cuenta las bajas por ausencia. Login rechaza usuarios
inactivos antes de verificar password o emitir tokens; el acceso inicial existente
tambien rechaza inactivos.

El controlador extrae `ClaimTypes.NameIdentifier`; preview/importacion revalidan
en DB que el actor siga activo y tenga el rol exacto `Admin`. La exportacion
tambien lo verifica. No basta el JWT Admin anterior. El actor no puede darse de
baja ni cambiar su propio rol; no se puede quitar el ultimo Admin activo.
No se elimina ni renombra el rol Admin ni se quita ninguna de sus
responsabilidades existentes, preservando administracion y recuperacion. Roles
ausentes con usuarios vinculados y responsabilidades ausentes usadas por roles
bloquean reemplazo global. Los grants de roles propietarios no Admin si pueden
sustituirse por los del archivo.

`SessionPrincipalValidator` valida cada JWT contra la base con consultas sin
tracking: epoch actual, GUID de usuario, usuario activo con Auth, rol exacto,
conjunto exacto de responsabilidades y email coincidente sin distinguir
mayusculas. Datos ausentes o cambios de estado, rol, grants o email invalidan el
principal. `Program.OnTokenValidated` invoca esa validacion asincrona y falla sin
registrar PII.

Las importaciones de `usuarios`, `roles` y `responsabilidades` rotan el
`SessionEpoch` persistente despues de validar la huella y antes del primer
`SaveChanges`. Si falla la rotacion, no se escribe la base. Si falla el guardado o
commit posterior, las sesiones anteriores siguen invalidadas, aunque la
transaccion revierta los datos: se prioriza cerrar el acceso de forma segura.
Preview, importaciones rechazadas antes de aplicar y otras secciones no rotan.
Una importacion de seguridad confirmada devuelve `requiereReingreso: true`;
el frontend debe cerrar la sesion y solicitar login. El campo es false en preview,
fallos y otras secciones. El constructor opcional sin epoch se mantiene para
pruebas aisladas y advierte que no hay revocacion; DI productiva inyecta el
singleton real. Todos los JWT anteriores se invalidan, incluido el del actor.

`CheckpointGateMiddleware`, antes de autenticacion, toma un lease exclusivo para
el endpoint exacto `/api/transferencias/importar` (sin distinguir mayusculas y
aceptando barra final equivalente). Espera las solicitudes lectoras en curso y
bloquea nuevas hasta terminar la importacion; no se toma otro lease en el
servicio. Los otros endpoints mantienen lease lector. `/api/checkpoints` conserva
su bypass y sus bloqueos internos. Esto evita solicitudes autenticadas con grants
anteriores en vuelo durante el cambio de seguridad.

### Cursos

La seccion usa el elemento existente
`{ "clave": { "cursoNombre": "1\u00b0", "division": "A", "anexo": "Centro", "orientacion": "Sociales" }, "semipresencial": false }`.
Valida anios 1, 2 o 3 con signo de grado y divisiones A a K. Actualiza modalidad
de coincidencias sin tocar materias, horarios ni historial. Anio y division son
globalmente unicos, igual que CursoService: otro anexo u orientacion para esa
combinacion es conflicto, no un alta duplicada. Reemplazo bloquea ausentes con
materias, horarios o inscripciones, incluso de alumnos dados de baja. No elimina
anexos ni orientaciones de contexto.

## Consistencia Y Limites

Preview lee un snapshot sin `SaveChanges`, altas temporales persistidas ni
rollback de escrituras. La huella SHA-256 vincula `datos`, formato/version,
seccion, modo y el estado escalar actual del dominio, incluyendo referencias e
historial oculto. `exportadoEn` y los textos de metadata no afectan la huella.
Para las secciones de seguridad tambien incluye Users, Auths, Roles,
Responsibilities y sus relaciones, incluidos permisos legacy. Los escalares de
Auth solo entran en el digest interno SHA-256; nunca se devuelven ni exportan.
Un cambio de dominio no relacionado tambien puede invalidarla: es deliberadamente
conservadora. Siempre repetir preview despues de un rechazo por cambio de estado.

Importar inicia una transaccion serializable antes de releer y revalidar. En
PostgreSQL toma bloqueos `SHARE ROW EXCLUSIVE` sobre las tablas del dominio para
evitar carreras con endpoints ordinarios, altas de referencias que pudieran
desaparecer por cascada y claves naturales nuevas durante la aplicacion. Esto
puede demorar brevemente otras escrituras del dominio durante una importacion.
Las secciones de seguridad agregan bloqueos de Users, Auths, Roles,
Responsibilities, RoleResponsibility y RolePermission en la misma transaccion.
Las otras secciones no cargan el dominio de seguridad completo.
Las remociones puntuales de docencias/ocupaciones se guardan antes de sus
reemplazos, en la misma transaccion, para permitir intercambios sin colisiones
de indices. Cualquier fallo revierte toda la transaccion; no hay borrados masivos
ni `ExecuteDelete` de tablas relacionadas.

Limite HTTP: 20 MiB. El paquete exportado/importado reserva 4096 bytes para el
envoltorio de la solicitud; maximo 100000 elementos por lista, profundidad JSON
32, texto general de 2000 caracteres y claves/nombres de 200 caracteres. No se
aceptan enums numericos en los paquetes. Carga horaria: entre 0 y 200.

## Verificacion

```powershell
dotnet build "CENS15_V2.csproj" -c Release --no-restore
dotnet test "Tests\CENS15_V2.Tests.csproj" -c Release --no-restore
```

Las pruebas de transferencia y sesiones usan EF InMemory y archivos temporales;
no arrancan `Program`, no ejecutan el seeder/migraciones y no conectan con la base
real. Las cinco pruebas PostgreSQL de snapshots siguen siendo opt-in y se omiten
en la ejecucion normal. Cubren portabilidad,
validacion, preview sin escrituras, huella obsoleta, IDs/vinculos preservados,
reemplazos protegidos, intercambios de roles/ocupacion y alcance de alumnos.
La suite incluye compatibilidad V1,
secciones de seguridad, claves de cursos, Auth/vinculos preservados, contexto sin
sobrescritura de grants, ultimo Admin/actor protegido y rechazo de JWT Admin
obsoleto en los endpoints de transferencia, validacion de principal contra estado
actual, login inactivo, rotacion antes de guardar y fallos seguros, bandera de
reingreso y serializacion del middleware antes de autenticacion.
El proveedor InMemory no verifica bloqueos SQL ni rollback/indices relacionales:
esa verificacion de integracion queda pendiente en una base descartable.
