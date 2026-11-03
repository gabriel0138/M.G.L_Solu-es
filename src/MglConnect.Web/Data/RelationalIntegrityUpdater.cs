using System.Data.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;

namespace MglConnect.Web.Data;

/// <summary>Additive legacy upgrade. Never deletes or repairs business records.</summary>
internal static class RelationalIntegrityUpdater
{
    public static async Task EnsureAsync(MglDbContext context, DbTransaction transaction)
    {
        var connection = context.Database.GetDbConnection();
        var sqlServer = context.Database.IsSqlServer();
        var sqlite = context.Database.IsSqlite();
        var keys = context.Model.GetRelationalModel().Tables
            .SelectMany(t => t.ForeignKeyConstraints).OrderBy(k => k.Name).ToList();

        // Preflight ALL relationships before installing any constraint. The outer
        // serializable transaction also rolls back tables/columns/backfill on failure.
        foreach (var key in keys)
        {
            if (key.Columns.Count != 1 || key.PrincipalColumns.Count != 1)
                throw new InvalidOperationException($"Atualização requer planejamento para FK composta: {key.Name}.");
            var child = Table(key.Table);
            var parent = Table(key.PrincipalTable);
            var column = Quote(key.Columns[0].Name);
            var principalColumn = Quote(key.PrincipalColumns[0].Name);
            var orphans = await ScalarAsync($"SELECT COUNT(*) FROM {child} c LEFT JOIN {parent} p ON c.{column} = p.{principalColumn} WHERE c.{column} IS NOT NULL AND p.{principalColumn} IS NULL");
            if (orphans > 0)
                throw new InvalidOperationException($"Esquema não atualizado: {key.Name} possui {orphans} referência(s) órfã(s). Nenhum registro foi excluído; saneamento explícito é necessário.");
        }

        foreach (var key in keys)
        {
            var child = Table(key.Table);
            var parent = Table(key.PrincipalTable);
            var column = Quote(key.Columns[0].Name);
            var principalColumn = Quote(key.PrincipalColumns[0].Name);
            if (sqlite)
            {
                // SQLite cannot ADD CONSTRAINT without rebuilding tables. Preserve
                // old tables and use additive guards; fresh databases use native FKs.
                var native = await ScalarAsync($"SELECT COUNT(*) FROM pragma_foreign_key_list('{Literal(key.Table.Name)}') WHERE \"from\" = '{Literal(key.Columns[0].Name)}' AND \"table\" = '{Literal(key.PrincipalTable.Name)}' AND \"to\" = '{Literal(key.PrincipalColumns[0].Name)}'");
                var expectedDelete = key.OnDeleteAction switch
                {
                    ReferentialAction.Cascade => "'CASCADE'",
                    ReferentialAction.SetNull => "'SET NULL'",
                    _ => "'RESTRICT', 'NO ACTION'"
                };
                if (native > 0)
                {
                    var matching = await ScalarAsync($"SELECT COUNT(*) FROM pragma_foreign_key_list('{Literal(key.Table.Name)}') WHERE \"from\" = '{Literal(key.Columns[0].Name)}' AND \"table\" = '{Literal(key.PrincipalTable.Name)}' AND \"to\" = '{Literal(key.PrincipalColumns[0].Name)}' AND on_delete IN ({expectedDelete}) AND on_update = 'NO ACTION'");
                    if (matching != native) throw Incompatible(key.Name);
                    continue;
                }
                if (await ScalarAsync($"SELECT COUNT(*) FROM pragma_foreign_key_list('{Literal(key.Table.Name)}') WHERE \"from\" = '{Literal(key.Columns[0].Name)}'") > 0)
                    throw Incompatible(key.Name);
                var name = key.Name;
                var condition = $"NEW.{column} IS NOT NULL AND NOT EXISTS (SELECT 1 FROM {parent} WHERE {principalColumn} = NEW.{column})";
                await ExecuteAsync($"CREATE TRIGGER IF NOT EXISTS {Quote(name + "_Insert")} BEFORE INSERT ON {child} WHEN {condition} BEGIN SELECT RAISE(ABORT, '{Literal(name)}'); END;");
                await ExecuteAsync($"CREATE TRIGGER IF NOT EXISTS {Quote(name + "_Update")} BEFORE UPDATE OF {column} ON {child} WHEN {condition} BEGIN SELECT RAISE(ABORT, '{Literal(name)}'); END;");
                var dependent = $"EXISTS (SELECT 1 FROM {child} WHERE {column} = OLD.{principalColumn})";
                var deleteAction = key.OnDeleteAction switch
                {
                    ReferentialAction.Cascade => $"DELETE FROM {child} WHERE {column} = OLD.{principalColumn};",
                    ReferentialAction.SetNull => $"UPDATE {child} SET {column} = NULL WHERE {column} = OLD.{principalColumn};",
                    _ => $"SELECT RAISE(ABORT, '{Literal(name)}');"
                };
                await ExecuteAsync($"CREATE TRIGGER IF NOT EXISTS {Quote(name + "_Delete")} BEFORE DELETE ON {parent} WHEN {dependent} BEGIN {deleteAction} END;");
                await ExecuteAsync($"CREATE TRIGGER IF NOT EXISTS {Quote(name + "_ParentUpdate")} BEFORE UPDATE OF {principalColumn} ON {parent} WHEN NEW.{principalColumn} != OLD.{principalColumn} AND {dependent} BEGIN SELECT RAISE(ABORT, '{Literal(name)}'); END;");
                // REPLACE can implicitly delete a parent without firing DELETE
                // triggers when recursive_triggers is OFF. Refuse replacements of
                // referenced rows via the model's unique indexes before that happens.
                foreach (var index in key.PrincipalTable.Indexes.Where(i => i.IsUnique))
                {
                    if (await ScalarAsync($"SELECT COUNT(*) FROM pragma_index_list('{Literal(key.PrincipalTable.Name)}') WHERE name = '{Literal(index.Name)}' AND \"unique\" = 1") == 0)
                        continue;
                    var sameUniqueValue = string.Join(" AND ", index.Columns.Select(c => $"p.{Quote(c.Name)} = NEW.{Quote(c.Name)}"));
                    var filterOld = index.Filter ?? "1 = 1";
                    var filterNew = filterOld;
                    foreach (var parentColumn in key.PrincipalTable.Columns)
                    {
                        filterOld = filterOld.Replace(Quote(parentColumn.Name), "p." + Quote(parentColumn.Name), StringComparison.Ordinal);
                        filterNew = filterNew.Replace(Quote(parentColumn.Name), "NEW." + Quote(parentColumn.Name), StringComparison.Ordinal);
                    }
                    var replacement = $"({filterNew}) AND EXISTS (SELECT 1 FROM {parent} p WHERE {sameUniqueValue} AND ({filterOld}) AND p.{principalColumn} != NEW.{principalColumn} AND EXISTS (SELECT 1 FROM {child} c WHERE c.{column} = p.{principalColumn}))";
                    foreach (var operation in new[] { "INSERT", "UPDATE" })
                        await ExecuteAsync($"CREATE TRIGGER IF NOT EXISTS {Quote(name + "_Replace_" + index.Name + "_" + operation)} BEFORE {operation} ON {parent} WHEN {replacement} BEGIN SELECT RAISE(ABORT, '{Literal(name)}: REPLACE de registro referenciado'); END;");
                }
                continue;
            }

            var exists = sqlServer
                ? await ScalarAsync($"SELECT COUNT(*) FROM sys.foreign_keys WHERE name = N'{Literal(key.Name)}' AND parent_object_id = OBJECT_ID(N'{Literal(key.Table.Schema ?? "dbo")}.{Literal(key.Table.Name)}')")
                : await ScalarAsync($"SELECT COUNT(*) FROM information_schema.table_constraints WHERE constraint_type = 'FOREIGN KEY' AND constraint_name = '{Literal(key.Name)}' AND table_name = '{Literal(key.Table.Name)}' AND table_schema = current_schema()");
            var constraintName = key.Name;
            if (sqlServer && exists == 0)
            {
                // Legacy constraints may have a different name. Reuse an equivalent
                // definition instead of creating a duplicate relationship.
                var deleteCode = key.OnDeleteAction switch { ReferentialAction.Cascade => 1, ReferentialAction.SetNull => 2, _ => 0 };
                await using var command = connection.CreateCommand();
                command.Transaction = transaction;
                command.CommandText = $"SELECT TOP (1) f.name FROM sys.foreign_keys f JOIN sys.foreign_key_columns c ON c.constraint_object_id = f.object_id WHERE f.parent_object_id = OBJECT_ID(N'{Literal(key.Table.Schema ?? "dbo")}.{Literal(key.Table.Name)}') AND f.referenced_object_id = OBJECT_ID(N'{Literal(key.PrincipalTable.Schema ?? "dbo")}.{Literal(key.PrincipalTable.Name)}') AND COL_NAME(c.parent_object_id, c.parent_column_id) = N'{Literal(key.Columns[0].Name)}' AND COL_NAME(c.referenced_object_id, c.referenced_column_id) = N'{Literal(key.PrincipalColumns[0].Name)}' AND f.delete_referential_action = {deleteCode} AND f.update_referential_action = 0 AND (SELECT COUNT(*) FROM sys.foreign_key_columns k WHERE k.constraint_object_id = f.object_id) = 1 ORDER BY f.name";
                if (await command.ExecuteScalarAsync() is string equivalentName)
                {
                    constraintName = equivalentName;
                    exists = 1;
                }
            }
            if (exists == 0)
            {
                var action = key.OnDeleteAction switch
                {
                    ReferentialAction.Cascade => "CASCADE",
                    ReferentialAction.SetNull => "SET NULL",
                    _ => "NO ACTION"
                };
                await ExecuteAsync($"ALTER TABLE {child} {(sqlServer ? "WITH CHECK " : "")}ADD CONSTRAINT {Quote(key.Name)} FOREIGN KEY ({column}) REFERENCES {parent} ({principalColumn}) ON DELETE {action};");
            }
            if (sqlServer)
            {
                var deleteCode = key.OnDeleteAction switch { ReferentialAction.Cascade => 1, ReferentialAction.SetNull => 2, _ => 0 };
                var matching = await ScalarAsync($"SELECT COUNT(*) FROM sys.foreign_keys f JOIN sys.foreign_key_columns c ON c.constraint_object_id = f.object_id WHERE f.name = N'{Literal(constraintName)}' AND f.parent_object_id = OBJECT_ID(N'{Literal(key.Table.Schema ?? "dbo")}.{Literal(key.Table.Name)}') AND f.referenced_object_id = OBJECT_ID(N'{Literal(key.PrincipalTable.Schema ?? "dbo")}.{Literal(key.PrincipalTable.Name)}') AND COL_NAME(c.parent_object_id, c.parent_column_id) = N'{Literal(key.Columns[0].Name)}' AND COL_NAME(c.referenced_object_id, c.referenced_column_id) = N'{Literal(key.PrincipalColumns[0].Name)}' AND f.delete_referential_action = {deleteCode} AND f.update_referential_action = 0 AND (SELECT COUNT(*) FROM sys.foreign_key_columns k WHERE k.constraint_object_id = f.object_id) = 1");
                if (matching != 1) throw Incompatible(key.Name);
                await ExecuteAsync($"ALTER TABLE {child} WITH CHECK CHECK CONSTRAINT {Quote(constraintName)};");
            }
        }

        string Quote(string identifier) => sqlServer
            ? "[" + identifier.Replace("]", "]]", StringComparison.Ordinal) + "]"
            : "\"" + identifier.Replace("\"", "\"\"", StringComparison.Ordinal) + "\"";
        string Table(ITable table) => table.Schema is null
            ? Quote(table.Name) : $"{Quote(table.Schema)}.{Quote(table.Name)}";
        static string Literal(string value) => value.Replace("'", "''", StringComparison.Ordinal);
        static InvalidOperationException Incompatible(string name) => new(
            $"Constraint incompatível com o modelo: {name}. Nenhuma constraint foi removida; migração explícita é necessária.");
        async Task<long> ScalarAsync(string sql)
        {
            await using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = sql;
            return Convert.ToInt64(await command.ExecuteScalarAsync());
        }
        async Task ExecuteAsync(string sql)
        {
            await using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = sql;
            await command.ExecuteNonQueryAsync();
        }
    }
}
