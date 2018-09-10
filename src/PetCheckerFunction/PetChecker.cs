using Gremlin.Net.Driver;
using Gremlin.Net.Structure.IO.GraphSON;
using Microsoft.Azure.Documents;
using Microsoft.Azure.Documents.Client;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Host;
using Microsoft.ProjectOxford.Vision;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;


namespace PetCheckerFunction
{
    public static class PetChecker
    {
        [FunctionName("PetChecker")]
        public static async Task RunPetChecker(
            [CosmosDBTrigger("pets", "checks", ConnectionStringSetting = "constr",
                CreateLeaseCollectionIfNotExists=true)] IReadOnlyList<Document> document,
            TraceWriter log)
        {
            foreach (dynamic doc in document)
            {
                var isProcessed = doc.IsApproved != null;
                if (isProcessed)
                {
                    continue;
                }

                var url = doc.MediaUrl;
                var uploaded = (DateTime)doc.Created;
                log.Info($">>> Processing image in {url} upladed at {uploaded.ToString()}");

                using (var httpClient = new HttpClient())
                {
                    var kvService = new KeyVault(log);
                    var res = await httpClient.GetAsync(url);
                    var stream = await res.Content.ReadAsStreamAsync() as Stream;
                    log.Info($"--- Image succesfully downloaded from storage");
                    var (allowed, message, tags) = await PassesImageModerationAsync(stream, kvService, log);
                    log.Info($"--- Image analyzed. It was {(allowed ? string.Empty : "NOT")} approved");
                    doc.IsApproved = allowed;
                    doc.Message = message;
                    log.Info($"--- Updating CosmosDb document to have historical data");
                    await UpsertDocument(doc, kvService, log);
                    log.Info($"--- Updating Grapgh");
                    await InsertInGraph(tags, doc, kvService, log);
                    log.Info($"<<< Image in {url} processed!");
                }
            }

        }

        private static async Task InsertInGraph(IEnumerable<string> tags, dynamic doc, KeyVault kvService, TraceWriter log)
        {
            var hostname = await GetSecret("gremlin_endpoint", kvService);
            var port = await GetSecret("gremlin_port", kvService);
            var database = "pets";
            var collection = "checkins";
            var authKey = Environment.GetEnvironmentVariable("gremlin_key");
            var portToUse = 443;
            portToUse = int.TryParse(port, out portToUse) ? portToUse : 443;

            var gremlinServer = new GremlinServer(hostname, portToUse, enableSsl: true,
                                                username: "/dbs/" + database + "/colls/" + collection,
                                                password: authKey);
            var gremlinClient = new GremlinClient(gremlinServer, new GraphSON2Reader(), new GraphSON2Writer(), GremlinClient.GraphSON2MimeType);
            foreach (var tag in tags)
            {
                log.Info("--- --- Checking vertex for tag " + tag);
                await TryAddTag(gremlinClient, tag, log);
            }

            var queries = AddPetToGraphQueries(doc, tags);
            log.Info("--- --- Adding vertex for pet checkin ");
            foreach (string query in queries)
            {
                await gremlinClient.SubmitAsync<dynamic>(query);
            }
        }

        private static async Task TryAddTag(GremlinClient gremlinClient, string tag, TraceWriter log)
        {
            var query = $"g.V('{tag}')";
            var response = await gremlinClient.SubmitAsync<dynamic>(query);

            if (!response.Any())
            {
                log.Info("--- --- Adding vertex for tag " + tag);
                await gremlinClient.SubmitAsync<dynamic>(AddTagToGraphQuery(tag));
            }
        }

        private static IEnumerable<string> AddPetToGraphQueries(dynamic doc, IEnumerable<string> tags)
        {
            var id = doc.id.ToString();

            var msg = (doc.Message?.ToString() ?? "").Replace("'", "\'");

            yield return $"g.addV('checkin').property('id','{id}').property('description','{msg}')";
            foreach (var tag in tags)
            {
                yield return $"g.V('{id}').addE('seems').to(g.V('{tag}'))";
            }
        }

        private static string AddTagToGraphQuery(string tag) => $"g.addV('tag').property('id', '{tag}').property('value', '{tag}')";

        private static async Task UpsertDocument(dynamic doc, KeyVault kvService, TraceWriter log)
        {
            var endpoint = await GetSecret("cosmos_uri", kvService);
            var auth = await GetSecret("cosmos_key", kvService);

            var client = new DocumentClient(new Uri(endpoint), auth);
            var dbName = "pets";
            var colName = "checks";
            doc.Analyzed = DateTime.UtcNow;
            await client.UpsertDocumentAsync(
                UriFactory.CreateDocumentCollectionUri(dbName, colName), doc);
            log.Info($"--- CosmosDb document updated.");
        }

        private static async Task<string> GetSecret(string secretName, KeyVault kvService)
        {
            var secretValue = await kvService.GetSecretValue(secretName);
            return string.IsNullOrEmpty(secretValue) ?
                Environment.GetEnvironmentVariable(secretName) : secretValue;
        }

        public static async Task<(bool allowd, string message, string[] tags)> PassesImageModerationAsync(Stream image, KeyVault kvService, TraceWriter log)
        {
            try
            {
                log.Info("--- Creating VisionApi client and analyzing image");

                var key = await GetSecret("MicrosoftVisionApiKey", kvService);
                var endpoint = await GetSecret("MicrosoftVisionApiEndpoint", kvService);
                var numTags = await GetSecret("MicrosoftVisionNumTags", kvService);
                var client = new VisionServiceClient(key, endpoint);
                var features = new VisualFeature[] { VisualFeature.Description };
                var result = await client.AnalyzeImageAsync(image, features);

                log.Info($"--- Image analyzed with tags: {String.Join(",", result.Description.Tags)}");
                if (!int.TryParse(numTags, out var tagsToFetch))
                {
                    tagsToFetch = 5;
                }
                var fetchedTags = result?.Description?.Tags.Take(tagsToFetch).ToArray() ?? new string[0];
                bool isAllowed = fetchedTags.Contains("dog");
                string message = result?.Description?.Captions.FirstOrDefault()?.Text;
                return (isAllowed, message, fetchedTags);
            }
            catch (Exception ex)
            {
                return (false, "error " + ex.Message, new string[0]);
            }
        }
    }
}
