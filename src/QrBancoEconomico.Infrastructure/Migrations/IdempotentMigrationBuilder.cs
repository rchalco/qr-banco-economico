using Microsoft.EntityFrameworkCore.Migrations;

namespace QrBancoEconomico.Infrastructure.Migrations;

/// <summary>
/// Emite DDL protegido por comprobaciones de existencia, para que <c>dotnet ef database update</c>
/// converja al esquema objetivo desde cualquier punto de partida.
/// </summary>
/// <remarks>
/// La base de este proyecto se creó antes de adoptar migraciones, de modo que puede tener tablas sin
/// la fila correspondiente en <c>__EFMigrationsHistory</c>. Con el DDL desnudo que genera EF, la
/// primera migración fallaría con «There is already an object named …» y dejaría la base a medio
/// migrar. Con estas guardas, cada objeto se crea solo si falta: una base vacía se construye entera,
/// una base preexistente recibe únicamente lo que le falta y una ya migrada no cambia.
/// Las guardas consultan el catálogo (<c>OBJECT_ID</c>, <c>COL_LENGTH</c>, <c>sys.indexes</c>), nunca
/// el historial de migraciones, que es justamente el dato que puede faltar.
/// </remarks>
internal static class IdempotentMigrationBuilder
{
    public static void CreateTableIfMissing(this MigrationBuilder migrationBuilder, string table, string columnsAndConstraints) =>
        migrationBuilder.Sql($"""
            IF OBJECT_ID(N'[{table}]', N'U') IS NULL
            BEGIN
                CREATE TABLE [{table}] (
            {columnsAndConstraints}
                );
            END;
            """);

    /// <summary>
    /// Agrega la columna si falta. <c>COL_LENGTH</c> devuelve NULL tanto si falta la columna como si
    /// falta la tabla; se invoca siempre después de haber asegurado la tabla.
    /// </summary>
    public static void AddColumnIfMissing(this MigrationBuilder migrationBuilder, string table, string column, string definition) =>
        migrationBuilder.Sql($"""
            IF COL_LENGTH(N'[{table}]', N'{column}') IS NULL
                ALTER TABLE [{table}] ADD [{column}] {definition};
            """);

    /// <param name="definition">Todo lo que sigue al nombre del índice, por ejemplo <c>ON [T] ([C]) WHERE [C] IS NOT NULL</c>.</param>
    public static void CreateIndexIfMissing(this MigrationBuilder migrationBuilder, string name, string table, string definition) =>
        migrationBuilder.Sql($"""
            IF OBJECT_ID(N'[{table}]', N'U') IS NOT NULL
               AND NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'{name}' AND object_id = OBJECT_ID(N'[{table}]'))
                CREATE {definition};
            """);

    public static void DropIndexIfPresent(this MigrationBuilder migrationBuilder, string name, string table) =>
        migrationBuilder.Sql($"""
            IF OBJECT_ID(N'[{table}]', N'U') IS NOT NULL
               AND EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'{name}' AND object_id = OBJECT_ID(N'[{table}]'))
                DROP INDEX [{name}] ON [{table}];
            """);

    public static void DropColumnIfPresent(this MigrationBuilder migrationBuilder, string table, string column) =>
        migrationBuilder.Sql($"""
            IF COL_LENGTH(N'[{table}]', N'{column}') IS NOT NULL
                ALTER TABLE [{table}] DROP COLUMN [{column}];
            """);

    /// <summary>
    /// Agrega la clave foránea si falta. Se emite además de la que ya declara el <c>CREATE TABLE</c>,
    /// para cubrir el caso de una tabla creada a mano antes de las migraciones y sin sus restricciones.
    /// </summary>
    public static void AddForeignKeyIfMissing(this MigrationBuilder migrationBuilder, string name, string table,
        string column, string principalTable, string principalColumn, string onDelete) =>
        migrationBuilder.Sql($"""
            IF OBJECT_ID(N'[{table}]', N'U') IS NOT NULL AND OBJECT_ID(N'[{name}]', N'F') IS NULL
                ALTER TABLE [{table}] ADD CONSTRAINT [{name}] FOREIGN KEY ([{column}])
                    REFERENCES [{principalTable}] ([{principalColumn}]) ON DELETE {onDelete};
            """);
}
