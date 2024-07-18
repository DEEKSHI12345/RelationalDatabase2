using Microsoft.Data.SqlClient;
using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace AZURE_AI
{
    public static class DataService
    {
        public static List<TableSchema> GetDatabaseSchema()
        {
            var tables = new List<TableSchema>();

            using (SqlConnection connection = new SqlConnection("Server=SPCOKLAP-5527\\SQLEXPRESS;Database=Project_Management_System;Trusted_Connection=True;TrustServerCertificate=True;"))
            {
                connection.Open();
                string query = @"
                    SELECT 
                        TABLE_NAME, 
                        COLUMN_NAME 
                    FROM INFORMATION_SCHEMA.COLUMNS 
                    ORDER BY TABLE_NAME, ORDINAL_POSITION";

                using (SqlCommand command = new SqlCommand(query, connection))
                {
                    using (SqlDataReader reader = command.ExecuteReader())
                    {
                        TableSchema currentTable = null;

                        while (reader.Read())
                        {
                            string tableName = reader["TABLE_NAME"].ToString();
                            string columnName = reader["COLUMN_NAME"].ToString();

                            if (currentTable == null || currentTable.TableName != tableName)
                            {
                                if (currentTable != null)
                                {
                                    tables.Add(currentTable);
                                }

                                currentTable = new TableSchema
                                {
                                    TableName = tableName,
                                    Columns = new List<string>()
                                };
                            }

                            currentTable.Columns.Add(columnName);
                        }

                        if (currentTable != null)
                        {
                            tables.Add(currentTable);
                        }
                    }
                }
            }

            return tables;
        }
        public static string ExtractSourceTableName(string query)
        {
            
               
                var fromClauseMatch = Regex.Match(query, @"\bJOIN\b\s+([^\s]+)", RegexOptions.IgnoreCase);
                
                if(!fromClauseMatch.Success)
                {
                    var match = Regex.Match(query, @"\bFROM\b\s+([^\s]+)", RegexOptions.IgnoreCase);
                    return match.Groups[1].Value;
                }
            else
            {
                return fromClauseMatch.Groups[1].Value;
            }
            
           
            
            
        }
        
        public static List<List<string>> GetTable(string query)
        {
            var rows = new List<List<string>>();

            using (SqlConnection connection = new SqlConnection("Server=SPCOKLAP-5527\\SQLEXPRESS;Database=Project_Management_System;Trusted_Connection=True;TrustServerCertificate=True;"))
            {
                connection.Open();
                using (SqlCommand command = new SqlCommand(query, connection))
                {
                    using (SqlDataReader reader = command.ExecuteReader())
                    {
                        bool headerAdded = false;
                        while (reader.Read())
                        {
                            var cols = new List<string>();
                            var headerCols = new List<string>();
                            if (!headerAdded)
                            {
                                for (int i = 0; i < reader.FieldCount; i++)
                                {
                                    headerCols.Add(reader.GetName(i));
                                }
                                headerAdded = true;
                                rows.Add(headerCols);
                            }
                            for (int i = 0; i < reader.FieldCount; i++)
                            {
                                try
                                {
                                    cols.Add(reader.GetValue(i).ToString());
                                }
                                catch
                                {
                                    cols.Add("DataTypeConversionError");
                                }
                            }
                            rows.Add(cols);
                        }
                    }
                    return rows;
                }
            }
        }
        

        public static List<(string Table, List<string> Columns)> ParseQuery(string query)
        {
            var results = new List<(string Table, List<string> Columns)>();

            // Regex to match table names in FROM and JOIN clauses
            var tableRegex = new Regex(@"\bFROM\s+([^\s,]+)|\bJOIN\s+([^\s,]+)", RegexOptions.IgnoreCase);
            // Regex to match column names in SELECT clause
            var columnRegex = new Regex(@"\bSELECT\s+(.*?)\bFROM\b", RegexOptions.IgnoreCase | RegexOptions.Singleline);

            var tableMatches = tableRegex.Matches(query);
            var columnMatches = columnRegex.Match(query);

            var tables = new HashSet<string>();
            foreach (Match match in tableMatches)
            {
                if (match.Groups[1].Success)
                {
                    tables.Add(match.Groups[1].Value);
                }
                else if (match.Groups[2].Success)
                {
                    tables.Add(match.Groups[2].Value);
                }
            }

            var columns = new List<string>();
            if (columnMatches.Success)
            {
                var columnPart = columnMatches.Groups[1].Value.Trim();
                if (columnPart == "*")
                {
                    foreach (var table in tables)
                    {
                        var schemaTable = GetTableSchema(table);
                        if (schemaTable != null)
                        {
                            results.Add((schemaTable.TableName, schemaTable.Columns));
                        }
                    }
                    return results;
                }
                else
                {
                    var columnNames = columnPart.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries);
                    foreach (var columnName in columnNames)
                    {
                        columns.Add(columnName.Trim());
                    }
                }
            }

            var tableColumnMapping = new Dictionary<string, List<string>>();
            foreach (var table in tables)
            {
                tableColumnMapping[table] = new List<string>();
            }

            foreach (var column in columns)
            {
                var parts = column.Split('.');
                if (parts.Length == 2)
                {
                    var tableAlias = parts[0].Trim();
                    var columnName = parts[1].Trim();

                    foreach (var table in tables)
                    {
                        if (table.StartsWith(tableAlias, StringComparison.OrdinalIgnoreCase))
                        {
                            tableColumnMapping[table].Add(columnName);
                            break;
                        }
                    }
                }
            }

            foreach (var table in tableColumnMapping.Keys)
            {
                results.Add((table, tableColumnMapping[table]));
            }

            return results;
        }

        public class JoinCondition
        {
            public string SourceTable { get; set; }
            public string TargetTable { get; set; }
            public string ForeignKey { get; set; }
            public string PrimaryKey { get; set; } = "Id"; 
        }
        public class ForeignKeyRelationship
        {
            public string SourceTable { get; set; }
            public string TargetTable { get; set; }
            public string ForeignKey { get; set; }
            public string PrimaryKey { get; set; }
        }

        public static string UpdateQueryWithSelectedColumns(string originalQuery, List<string> selectedColumns, List<JoinCondition> joinConditions)
        {
            var fromClauseMatch = Regex.Match(originalQuery, @"\bFROM\b\s+[^\s]+", RegexOptions.IgnoreCase);
            if (!fromClauseMatch.Success)
            {
                throw new ArgumentException("FROM clause not found in the original query.");
            }
            var fromClause = fromClauseMatch.Value;

            var selectClauseMatch = Regex.Match(originalQuery, @"\bSELECT\b\s+(.+?)\bFROM\b", RegexOptions.IgnoreCase | RegexOptions.Singleline);
            if (!selectClauseMatch.Success)
            {
                throw new ArgumentException("SELECT clause not found in the original query.");
            }
            var selectClause = selectClauseMatch.Groups[1].Value.Trim();

            var updatedQuery = originalQuery;

            var newTables = new HashSet<string>(selectedColumns.Select(col => col.Split('.')[0]));

            var existingTables = new HashSet<string>(Regex.Matches(updatedQuery, @"\bFROM\b\s+([^\s]+)|\bJOIN\b\s+([^\s]+)", RegexOptions.IgnoreCase)
                .Cast<Match>()
                .SelectMany(match => new[] { match.Groups[1].Value, match.Groups[2].Value })
                .Where(value => !string.IsNullOrEmpty(value)));

            var existingColumns = selectClause.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(col => col.Trim())
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            foreach (var column in selectedColumns)
            {
                if (!existingColumns.Contains(column))
                {
                    selectClause += $", {column}";
                }
            }

            foreach (var newTable in newTables)
            {
                if (!existingTables.Contains(newTable))
                {
                    var joinCondition = joinConditions.FirstOrDefault(jc => jc.SourceTable == newTable || jc.TargetTable == newTable);
                    if (joinCondition != null)
                    {
                        updatedQuery += $" JOIN {joinCondition.TargetTable} ON {joinCondition.SourceTable}.{joinCondition.ForeignKey} = {joinCondition.TargetTable}.{joinCondition.PrimaryKey}";
                        existingTables.Add(newTable); 
                    }
                    else
                    {
                        throw new ArgumentException($"Join condition for table {newTable} is not specified.");
                    }
                }
            }

            updatedQuery = Regex.Replace(updatedQuery, @"\bSELECT\b\s+(.+?)\bFROM\b", $"SELECT {selectClause} FROM", RegexOptions.IgnoreCase | RegexOptions.Singleline);

            return updatedQuery;
        }

        public static List<JoinCondition> GetJoinConditions(string sourceTable, string targetTable)
        {
            var joinConditions = new List<JoinCondition>();

            using (SqlConnection connection = new SqlConnection("Server=SPCOKLAP-5527\\SQLEXPRESS;Database=Project_Management_System;Trusted_Connection=True;TrustServerCertificate=True;"))
            {
                connection.Open();

                string query = @"
                SELECT 
                    fk.name AS ForeignKeyName,
                    tp.name AS TargetTable,
                    cp.name AS PrimaryKeyColumn,
                    sp.name AS SourceTable,
                    cfk.name AS ForeignKeyColumn
                FROM 
                    sys.foreign_keys AS fk
                INNER JOIN 
                    sys.foreign_key_columns AS fkc ON fk.object_id = fkc.constraint_object_id
                INNER JOIN 
                    sys.tables AS tp ON fk.referenced_object_id = tp.object_id
                INNER JOIN 
                    sys.columns AS cp ON tp.object_id = cp.object_id AND fkc.referenced_column_id = cp.column_id
                INNER JOIN 
                    sys.tables AS sp ON fk.parent_object_id = sp.object_id
                INNER JOIN 
                    sys.columns AS cfk ON sp.object_id = cfk.object_id AND fkc.parent_column_id = cfk.column_id
                WHERE 
                    sp.name = @SourceTable AND tp.name = @TargetTable";

                using (SqlCommand command = new SqlCommand(query, connection))
                {
                    command.Parameters.AddWithValue("@SourceTable", sourceTable);
                    command.Parameters.AddWithValue("@TargetTable", targetTable);

                    using (SqlDataReader reader = command.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            joinConditions.Add(new JoinCondition
                            {
                                SourceTable = reader["SourceTable"].ToString(),
                                TargetTable = reader["TargetTable"].ToString(),
                                ForeignKey = reader["ForeignKeyColumn"].ToString(),
                                PrimaryKey = reader["PrimaryKeyColumn"].ToString()
                            });
                        }
                    }
                }
            }

            return joinConditions;
        }
        private static TableSchema GetTableSchema(string tableName)
        {
            using (SqlConnection connection = new SqlConnection("Server=SPCOKLAP-5522\\SQLEXPRESS;Database=EmployeeProjectAPI;Trusted_Connection=True;TrustServerCertificate=True"))
            {
                connection.Open();
                string query = @"
            SELECT 
                COLUMN_NAME 
            FROM INFORMATION_SCHEMA.COLUMNS 
            WHERE TABLE_NAME = @TableName
            ORDER BY ORDINAL_POSITION";

                using (SqlCommand command = new SqlCommand(query, connection))
                {
                    command.Parameters.AddWithValue("@TableName", tableName);

                    using (SqlDataReader reader = command.ExecuteReader())
                    {
                        var columns = new List<string>();
                        while (reader.Read())
                        {
                            columns.Add(reader["COLUMN_NAME"].ToString());
                        }

                        return new TableSchema
                        {
                            TableName = tableName,
                            Columns = columns
                        };
                    }
                }
            }
        }


        public static List<ForeignKeyRelationship> GetForeignKeyRelationships(string sourceTable)
        {
            var relationships = new List<ForeignKeyRelationship>();

            using (SqlConnection connection = new SqlConnection("Server=SPCOKLAP-5527\\SQLEXPRESS;Database=Project_Management_System;Trusted_Connection=True;TrustServerCertificate=True;"))
            {
                connection.Open();

                string query = @"
                SELECT 
                    fk.name AS ForeignKeyName,
                    tp.name AS TargetTable,
                    cp.name AS PrimaryKeyColumn,
                    sp.name AS SourceTable,
                    cfk.name AS ForeignKeyColumn
                FROM 
                    sys.foreign_keys AS fk
                INNER JOIN 
                    sys.foreign_key_columns AS fkc ON fk.object_id = fkc.constraint_object_id
                INNER JOIN 
                    sys.tables AS tp ON fk.referenced_object_id = tp.object_id
                INNER JOIN 
                    sys.columns AS cp ON tp.object_id = cp.object_id AND fkc.referenced_column_id = cp.column_id
                INNER JOIN 
                    sys.tables AS sp ON fk.parent_object_id = sp.object_id
                INNER JOIN 
                    sys.columns AS cfk ON sp.object_id = cfk.object_id AND fkc.parent_column_id = cfk.column_id
                WHERE 
                    sp.name = @SourceTable";

                using (SqlCommand command = new SqlCommand(query, connection))
                {
                    command.Parameters.AddWithValue("@SourceTable", sourceTable);

                    using (SqlDataReader reader = command.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            relationships.Add(new ForeignKeyRelationship
                            {
                                SourceTable = reader["SourceTable"].ToString(),
                                TargetTable = reader["TargetTable"].ToString(),
                                ForeignKey = reader["ForeignKeyColumn"].ToString(),
                                PrimaryKey = reader["PrimaryKeyColumn"].ToString()
                            });
                        }
                    }
                }
            }

            return relationships;
        }


    }
    public class TableSchema
    {
        public string TableName { get; set; }
        public List<string> Columns { get; set; }
    }
}