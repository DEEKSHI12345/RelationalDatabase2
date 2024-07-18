using Azure;
using Azure.AI.OpenAI;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Linq;
using System.Text.Json;
using static AZURE_AI.DataService;

namespace AZURE_AI.Pages
{
    public class IndexModel : PageModel
    {
        [BindProperty]
        public string UserPrompt { get; set; } = string.Empty;

        [BindProperty]
        public List<string> SelectedColumns { get; set; } = new List<string>();
        public List<List<string>> Data { get; set; } = new List<List<string>>();
        public string Summary { get; set; } = string.Empty;

        [BindProperty]
        public string Query { get; set; } = string.Empty;
        public string Error { get; set; } = string.Empty;
        [BindProperty]
        public string LastGeneratedQuery { get; set; } = string.Empty;

        public List<TableColumns> TableColumns { get; set; } = new List<TableColumns>();
        public List<TableSchema> DatabaseSchema { get; set; } = new List<TableSchema>();
        public string Source { get; set; } = "Projects";
        public List<string> Foreign { get; set; }

        public void OnGet()
        {
            LoadDatabaseSchema();
            TableColumns.Clear(); 
        }

        public void OnPost()
        {
            RunQuery(UserPrompt);
            List<ForeignKeyRelationship> relationships = DataService.GetForeignKeyRelationships(ExtractSourceTableName(Query));
            List<string> targetTables = relationships.Select(r => r.TargetTable).Distinct().ToList();
            Foreign = targetTables;

            
            LoadDatabaseSchema();
        }

        private void LoadDatabaseSchema()
        {
            var schema = DataService.GetDatabaseSchema();
            DatabaseSchema = schema.Select(s => new TableSchema
            {
                TableName = s.TableName,
                Columns = s.Columns
            }).ToList();
        }

        public void RunQuery(string prompt)
        {
            string OpenAIEndpoint = "https://magicvilla123.openai.azure.com/";
            string OpenAIKey = "1fcd028d8e744609be7ded7d8e4d63d2";
            string deploymentName = "magicvilla";

            OpenAIClient openAIClient = new(new Uri(OpenAIEndpoint), new AzureKeyCredential(OpenAIKey));
            string systemMessage = @"
            your are a helpful, cheerful database assistant. 
            use the following database schema when creating your answers:

            - projects (projectid,projectname,startdate,enddate,budget,projectmanagerid,statusid,createddate,updateddate,createdby,updatedby,isactive)
            - members (memberid,membername,email,contact,createddate,updateddate,createdby,updatedby,password,isactive)
            - comments (commentid,commenterid,postedon,comment,taskid,projectid,reply)
            - projectmembers (projectmemberid,projectid,memberid,isactive)
            - roles (roleid,role)
            - statuses (statusid,status)
            - taskmembers (id,taskid,memberid,isactive)
            - tasks (taskid,taskname,taskdetails,projectid,statusid,createddate,updateddate,createdby,updatedby,isactive)
            - userrefreshtokens (email,refreshtoken,id,createddate,createdby,updateddate,updatedby)

            include column name headers in the query results.

            always provide your answer in the json format below:
            { ""summary"": ""your-summary"", ""query"":  ""your-query"" }
            output only json.
            in the preceding json response, substitute ""your-query"" with microsoft sql server query to retrieve the requested data.
            in the preceding json response, substitute ""your-summary"" with a summary of the query.
            always include all columns in the table.
            if the resulting query is non-executable, replace ""your-query"" with na, but still substitute ""your-query"" with a summary of the query.
            ";

            if (string.IsNullOrWhiteSpace(systemMessage) || string.IsNullOrWhiteSpace(prompt))
            {
                Error = "System message or user prompt cannot be empty.";
                return;
            }

            ChatCompletionsOptions chatCompletionsOptions = new ChatCompletionsOptions()
            {
                Messages = {
                    new ChatRequestSystemMessage(systemMessage),
                    new ChatRequestUserMessage(prompt)
                },
                DeploymentName = deploymentName
            };

            try
            {
                ChatCompletions chatCompletionsResponse = openAIClient.GetChatCompletions(chatCompletionsOptions);

                var response = JsonSerializer.Deserialize<AIQuery>(chatCompletionsResponse.Choices[0].Message.Content
                    .Replace("```json", "").Replace("```", ""));

                Summary = response?.summary ?? string.Empty;
                Query = response?.query ?? string.Empty;

                


                if (SelectedColumns.Any())
                {
                    var tables = new HashSet<string>(SelectedColumns.Select(col => col.Split('.')[0]));
                    foreach (var table in tables)
                    {
                        List<JoinCondition> joinConditions = DataService.GetJoinConditions(ExtractSourceTableName(Query), table);
                        Query = DataService.UpdateQueryWithSelectedColumns(Query, SelectedColumns, joinConditions);
                    }
                    

                }

                if (Query.Contains("SELECT *"))
                {
                    foreach (var table in DatabaseSchema)
                    {
                        var columns = string.Join(", ", table.Columns);
                        Query = Query.Replace("SELECT *", $"SELECT {columns}");
                    }
                }
                Data = DataService.GetTable(Query);

                var parsedResult = DataService.ParseQuery(Query);
                TableColumns = parsedResult.GroupBy(pr => pr.Table).Select(g => new TableColumns
                {
                    Table = g.Key,
                    Columns = g.SelectMany(pr => pr.Columns)
                               .Distinct()
                               .Select(c => c.Contains('.') ? c.Split('.').Last() : c)
                               .ToList()
                }).ToList();
                LastGeneratedQuery = Query;
            }
            catch (Exception e)
            {
                Error = e.Message;
            }
        }



        public IActionResult OnPostUpdateQuery()
        {
            if (!string.IsNullOrEmpty(LastGeneratedQuery))
            {
                Query = LastGeneratedQuery;
            }
            if (Query != null && Query!="")
            {
                RunQuery(UserPrompt);
                //Data = DataService.GetTable(Query);

                List<ForeignKeyRelationship> relationships = DataService.GetForeignKeyRelationships(ExtractSourceTableName(Query));
                List<string> targetTables = relationships.Select(r => r.TargetTable).Distinct().ToList();
                Foreign = targetTables;
                LoadDatabaseSchema();
                return Page();
            }
            else
            {
                RunQuery(UserPrompt);
                List<ForeignKeyRelationship> relationships = DataService.GetForeignKeyRelationships(ExtractSourceTableName(Query));
                List<string> targetTables = relationships.Select(r => r.TargetTable).Distinct().ToList();
                Foreign = targetTables;
                LoadDatabaseSchema();
                return Page();
            }
            
            
        }
    }

    


    public class AIQuery
    {
        public string summary { get; set; } = string.Empty;
        public string query { get; set; } = string.Empty;
    }

    public class TableColumns
    {
        public string Table { get; set; } = string.Empty;
        public List<string> Columns { get; set; } = new List<string>();
    }

    public class TableSchema
    {
        public string TableName { get; set; } = string.Empty;
        public List<string> Columns { get; set; } = new List<string>();
    }

    public class ParsedResult
    {
        public string Table { get; set; } = string.Empty;
        public List<string> Columns { get; set; } = new List<string>();
    }
}
