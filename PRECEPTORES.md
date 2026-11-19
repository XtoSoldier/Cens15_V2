# Preceptores

## Panel Admin

La seccion **Preceptores** aparece junto a Docentes en el menu y en `/preceptores`.
Permite crear, editar y eliminar preceptores con nombres, apellidos y email opcional.
La vinculacion a un usuario es opcional; el selector muestra solamente cuentas
con rol Preceptor que no esten vinculadas a otro preceptor.
El primer campo del formulario es **Usuario vinculado**. Elegir una cuenta copia
sus nombres, apellidos y email al formulario, manteniendolos editables. Cambiar
de usuario actualiza esos datos; elegir Sin usuario vinculado conserva los datos
actuales para poder guardar el preceptor sin cuenta. Abrir una edicion no
sobrescribe los datos ya guardados con los de su usuario asociado.

En **Cursos a cargo** se pueden seleccionar varios cursos o ninguno. Cada opcion
identifica curso/division, anexo, orientacion y modalidad semipresencial cuando
corresponde. Los cursos ocupados muestran su preceptor y estan deshabilitados.
Para reasignarlos, primero se quitan del preceptor actual y luego se seleccionan
en el nuevo. Editar permite quitar todos los cursos y elegir Sin usuario vinculado.

La baja solicita confirmacion y libera las asignaciones sin eliminar cursos ni
usuario. Los errores de guardado conservan el formulario y sus datos; los de
eliminacion mantienen abierta la confirmacion. Actualizar permite recargar las
opciones y asignaciones actuales. La API vuelve a validar todas las escrituras.

## Contrato HTTP

Todos los endpoints requieren autenticacion. El modulo `Preceptores` habilita sus
cuatro responsabilidades granulares, siguiendo el esquema existente.

| Endpoint | Responsabilidad | Respuesta correcta |
| --- | --- | --- |
| `GET /api/Preceptores` | `Preceptores.Consultar` | `200`, lista de preceptores |
| `GET /api/Preceptores/{id:int}` | `Preceptores.Consultar` | `200`, preceptor |
| `GET /api/Preceptores/opciones` | `Preceptores.Consultar` | `200`, `{usuarios: UserDTO[], cursos: CursoDto[]}` |
| `POST /api/Preceptores` | `Preceptores.Crear` | `201`, preceptor y Location |
| `PUT /api/Preceptores/{id:int}` | `Preceptores.Editar` | `204` |
| `DELETE /api/Preceptores/{id:int}` | `Preceptores.Eliminar` | `204` |

POST y PUT reciben el mismo objeto:

```json
{
  "nombres": "Ana",
  "apellidos": "Perez",
  "email": null,
  "userId": null,
  "cursoIds": [1, 2]
}
```

PUT es reemplazo completo: `userId: null` desvincula el usuario y `cursoIds: []`
libera todos los cursos. Omitir `userId` equivale a null; omitir `cursoIds`
equivale a lista vacia. Una lista explicitamente null es invalida.

GET y POST devuelven:

```json
{
  "id": 1,
  "nombres": "Ana",
  "apellidos": "Perez",
  "email": null,
  "userId": null,
  "cursos": [
    {
      "id": 1,
      "curso": "1",
      "division": "A",
      "id_anexo": 1,
      "id_orientacion": 1,
      "anexoNombre": "Centro",
      "orientacionNombreCorto": "SOC",
      "semipresencial": false
    }
  ]
}
```

`CursoDto` es el DTO existente, sin campos nuevos. `UserDTO` mantiene
`id, firstName, lastName, image, status, roleId, role, email`.
Opciones incluye usuarios ya asociados, incluso inactivos, y todos los cursos,
incluidos los ocupados. El cliente filtra usando el listado de preceptores;
el servidor vuelve a validar cada escritura. No exige permisos de consulta de
Usuarios ni Cursos y no expone credenciales.

## Reglas e Integridad

- Nombres y apellidos obligatorios; se eliminan espacios exteriores.
- Email opcional, vacio se normaliza a null; si se proporciona se valida su formato.
  No se exige email unico.
- Usuario opcional, existente y con nombre de rol `Preceptor`, comparado con Trim
  y sin distinguir mayusculas. Un usuario puede pertenecer como maximo a un preceptor.
- Cursos existentes y sin ids duplicados. Un curso no puede ser tomado de otro
  preceptor. Un preceptor puede tener cero, uno o varios cursos.
- Las validaciones se ejecutan antes de modificar entidades rastreadas.
- La baja elimina solo al preceptor y libera cursos; nunca elimina cursos ni usuario.
  Eliminar un usuario conserva el preceptor con UserId null.
- UserService no permite cambiar a otro rol un usuario asociado; RoleService no
  permite renombrar su rol a uno distinto de Preceptor. Desvincular primero.
- El seed agrega el rol si falta, sin asignarle responsabilidades. Admin recibe
  las nuevas responsabilidades mediante el recorrido existente de `All`.

Errores de reglas: `400`. Registro inexistente en GET/PUT/DELETE: `404`.
Conflictos de persistencia/concurrencia en escrituras de preceptores: `409` con
mensaje generico, sin detalles SQL. Se descarta la unidad de trabajo rechazada.

La FK nullable `Cursos.PreceptorId` garantiza un solo preceptor por curso; ambas
FK nuevas usan ON DELETE SET NULL. El indice UNIQUE de `Preceptores.UserId`
permite varios NULL en PostgreSQL. `Curso.PreceptorId` es token de concurrencia,
incluyendo su valor original NULL, y `Preceptor.Version` protege PUT simultaneos
del mismo preceptor. SaveChanges usa la transaccion atomica del proveedor relacional.
La version es interna y no cambia el contrato HTTP. Las escrituras de preceptores
y las ediciones de usuarios/roles comparten aislamiento serializable para impedir
cambios cruzados de rol y asociacion que pasen validaciones simultaneas. Los abortos
por serializacion o deadlock se convierten en rechazos controlados; el cliente debe
recargar y reintentar. Usuarios/roles mantienen su respuesta de rechazo `400`.

## Checkpoints y Transferencias

CheckpointModel recorre las entidades y propiedades de EF: captura, valida y
restaura automaticamente Preceptores, Version y Cursos.PreceptorId. El cambio de
modelo/huella impide restaurar checkpoints antiguos incompatibles.

No se agrega una seccion de transferencia de preceptores. Las transferencias de
usuarios conservan asociaciones y rechazan cambios a roles incompatibles. Las de
roles se identifican por nombre, no renombran roles existentes y no eliminan roles
con usuarios asociados. Se incluyen preceptores en el bloqueo de importacion y en
la huella de las secciones de seguridad.

## Migracion y Verificacion

No se aplico la migracion a la base de la aplicacion ni se reinicio la API. Antes
de desplegar, respaldar la base y autorizar el cambio de esquema: el inicio de la
API existente aplica automaticamente las migraciones pendientes. Desplegar
esquema/API antes que el panel y volver a iniciar sesion para obtener los nuevos
permisos en el token. El seed habitual agrega las responsabilidades al Admin y
crea el rol Preceptor, sin conceder permisos a este ultimo. Si el seed esta
deshabilitado por una restauracion previa, configurar explicitamente ese rol y
las responsabilidades necesarias desde Roles/Responsabilidades.

`20261105235342_AddPreceptores` crea la tabla, indices y FK; no contiene seed de
datos ni modifica tablas ajenas a la funcionalidad. Se excluyo un drift preexistente
de `AlumnoContactos.Email` (modelo requerido versus snapshot nullable); ese drift
queda pendiente y no se corrige dentro de esta migracion.

Comandos seguros, sin iniciar API ni actualizar la base:

```powershell
dotnet build CENS15_V2.csproj -c Release --no-restore
dotnet test Tests\CENS15_V2.Tests.csproj -c Release --no-restore
dotnet ef migrations script 20261105012034_AddCalificacionCondiciones 20261105235342_AddPreceptores --configuration Release --no-build
```

PreceptorTests usa InMemory para reglas, relaciones y conflictos optimistas;
consulta metadata Npgsql sin abrir conexiones. InMemory no prueba FK/UNIQUE ni
rollback atomico de PostgreSQL. No ejecutar `dotnet run` solo para verificar este
cambio: el inicio existente aplica migraciones y seed.

Prueba relacional opt-in ejecutada correctamente en PostgreSQL local (2026-11-05):

```powershell
dotnet test Tests/CENS15_V2.Tests.csproj -c Release --no-restore --filter FullyQualifiedName~PreceptorAssignmentsEnforceRelationalIntegrityAndRollback --environment CENS15_RUN_SNAPSHOT_POSTGRES_TESTS=true
```

Resultado: 1 aprobada, 0 fallidas, 0 omitidas. Reutiliza el fixture privado
DisposableDatabase existente, sin modificarlo: solo crea una base local
`cens15_checkpoint_it_<guid>` y usa sus contextos de destino validados.
Verifica asignacion multiple, rechazo de curso ocupado, UNIQUE de UserId
(SQLSTATE 23505), fallo de FK (23503) con rollback de un SaveChanges que inserta
un preceptor y modifica cursos, y ambos ON DELETE SET NULL sin cargar dependientes.
La comprobacion final confirma ausencia de la base en pg_database y del directorio
temporal tras DisposeAsync. No se inicio la API ni se accedio a la base de aplicacion.

El fixture usa EnsureCreated en la base descartable: esta prueba no ejecuta la
migracion ni prueba carreras entre transacciones concurrentes. La migracion y esos
escenarios siguen pendientes de verificacion relacional antes del despliegue.
