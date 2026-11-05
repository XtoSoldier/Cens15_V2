# Calificaciones: Ausente y Sin Calificar

- `A` (Ausente) cuenta como cero y se guarda con condicion `Ausente`.
- `S/C` (Sin Calificar, por falta de docente) no promedia y se guarda con condicion `SinCalificar` y valor nulo.
- Un casillero vacio sigue pendiente: valor nulo y condicion `Numerica`. No equivale a S/C.
- Los ceros numericos historicos conservan su condicion `Numerica`; no se convierten en ausencias.
- Las condiciones corresponden a los dos promedios cuatrimestrales y los recuperatorios de diciembre/marzo. Los seis parciales siguen siendo numericos y se preservan al editar desde la app.

## Calculo

Con dos cuatrimestres computables, el anual es el techo del promedio. Para aprobar se requiere segundo cuatrimestre >= 6 y promedio **sin redondear** >= 6. Por ejemplo, 5/7 aprueba con 6; 7/5 recupera el segundo y A/8 recupera el primero con anual 4.

Si un cuatrimestre es S/C, se usa exclusivamente el otro, sin redondear su nota anual. Si es >= 6, aprueba. Si es menor que 6, la app consulta que recuperar, preseleccionando el cuatrimestre desaprobado y permitiendo primero, segundo o ambos. Cancelar no guarda. La API rechaza este caso si falta una eleccion valida, incluso si ya hay un examen aprobado.

La eleccion se guarda en `recuperacionExcepcional` con `RecuperaPrimerCuatrimestre`, `RecuperaSegundoCuatrimestre` o `RecuperaAmbos`. Se conserva tras aprobar un recuperatorio y se limpia al dejar de ser un caso excepcional. La app vuelve a consultar si cambia el cuatrimestre computable.

Dos S/C o un cuatrimestre pendiente no generan promedio ni aprobacion automatica. Para materias que deben recuperar, diciembre >= 4 aprueba; en su defecto marzo >= 4. La nota final del examen se redondea hacia arriba. Se conserva el flujo manual AP/APE cuando ambos cuatrimestres y examenes estan vacios.

La API calcula anual, final y estado al crear/actualizar; no confia en los valores calculados enviados por la app. Las lecturas y los certificados no recalculan registros historicos. Los certificados distinguen `A (Ausente)` y `S/C (Sin Calificar)`.

## Despliegue

La migracion `20261105012034_AddCalificacionCondiciones` agrega cuatro condiciones integer con valor predeterminado 0 (`Numerica`) y la eleccion nullable. No modifica notas ni recalcula datos.

Esta migracion requiere aprobacion antes de aplicarse en una base real. **La API aplica migraciones pendientes al arrancar**: no reiniciar la nueva API hasta autorizar el cambio y disponer de respaldo. Desplegar primero el esquema/API y luego la app. Los checkpoints anteriores pueden resultar incompatibles por la nueva huella de esquema.

## Verificacion Sin Datos Reales

En la carpeta de API:

```powershell
dotnet test Tests/CENS15_V2.Tests.csproj -c Release --filter FullyQualifiedName~CalificacionTests
```

Estas pruebas usan EF InMemory y no conectan a PostgreSQL. Incluyen matriz de calculo, validacion, rechazo sin efectos sobre entidades seguidas, persistencia de condiciones/parciales, proyeccion docente y certificados.

En la carpeta de app:

```powershell
pnpm run test:calificaciones
pnpm exec tsc --noEmit
```

Las pruebas de pantallas simulan la API y verifican confirmacion/cancelacion, relectura, errores de guardado y guardados concurrentes por fila. No reemplazan una prueba visual en dispositivo ni una integracion PostgreSQL descartable.
