START TRANSACTION;

ALTER TABLE "Calificaciones" ADD "DesdePase" boolean NOT NULL DEFAULT FALSE;

ALTER TABLE "Calificaciones" ADD "DisposicionEquivalencia" text;

INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
VALUES ('20261111201830_AddCalificacionOrigen', '8.0.0');

COMMIT;

